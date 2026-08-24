using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 7 acceptance: port v1's combat test scenarios (the
// rulebook worked examples are the oracle - tests/DiceFight.Engine.Tests/
// CombatEngineTests.cs), plus KO-triggers-fire-through-combat and
// aura-affects-combat-stats tests.
public class CombatEngineTests
{
    private static readonly Face Level1Char = new([], new CharacterFaceData(Level: 1, FieldingCost: 1, Attack: 3, Defense: 2));

    private static GameConfig BuildConfig() => new(
        Id: "test", Name: "Test",
        EnergySymbols: [new SymbolDef("Fist")],
        Keywords: [],
        Rules: new RulesConfig(StartingLife: 20, DrawCount: 3, MaxTeamCards: 4, MaxTeamDice: 4, BasicActionCount: 0),
        BasicDicePool: [],
        BasicActionSlots: 0);

    private static CardDef BuildCard(string id, IReadOnlyList<Face> faces, IReadOnlyList<string>? keywords = null, IReadOnlyList<ContinuousDef>? continuous = null, IReadOnlyList<TriggeredAbility>? abilities = null) => new(
        Id: id, Name: id, Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: new DieDefinition(id + "Die", faces),
        DieLimit: 4, Affiliations: [], Keywords: keywords ?? [], RawText: "",
        Abilities: abilities ?? [], Continuous: continuous ?? []);

    private static GameState BuildState(params CardDef[] cards)
    {
        var state = new GameState
        {
            Config = BuildConfig(),
            CardCatalog = cards.ToDictionary(c => c.Id),
            PlayerOne = new Player { Id = "p1", Name = "One", Life = 20 },
            PlayerTwo = new Player { Id = "p2", Name = "Two", Life = 20 },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Attack, // Spike C - parks the cursor on the Attack phase's first step
        };
        ContinuousRegistry.RegisterAll(state);
        return state;
    }

    private static DieInstance AddDie(GameState state, CardDef card, string controllerId, string? id = null) =>
        AddDieAt(state, card, controllerId, 0, id);

    private static DieInstance AddDieAt(GameState state, CardDef card, string controllerId, int faceIndex, string? id = null)
    {
        var die = new DieInstance { Id = id ?? card.Id + "-die", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.FieldZone, CurrentFaceIndex = faceIndex };
        state.Dice.Add(die);
        return die;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, int>> SoloSplit(string attackerId, string blockerId, int amount) =>
        new() { [attackerId] = new Dictionary<string, int> { [blockerId] = amount } };

    // --- Core loop ---

    [Fact]
    public void UnblockedAttacker_DealsDamageToPlayerAndLeavesPlay()
    {
        var bruiser = BuildCard("Bruiser", [Level1Char]); // 3A/2D
        var state = BuildState(bruiser);
        var attacker = AddDie(state, bruiser, "p1");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, queue, assignment, []);
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(17, state.PlayerTwo.Life);
        Assert.Equal(Zone.OutOfPlay, attacker.Zone);
        Assert.Empty(result.KOdDieIds);
    }

    [Fact]
    public void BlockedAttacker_SurvivesIfBlockerDamageBelowDefense()
    {
        var bruiser = BuildCard("Bruiser", [Level1Char]); // 3A/2D
        var weakling = BuildCard("Weakling", [new Face([], new CharacterFaceData(1, 1, 1, 5))]); // 1A/5D
        var state = BuildState(bruiser, weakling);
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, weakling, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 3));

        Assert.DoesNotContain(attacker.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, attacker.Zone);
        Assert.Equal(1, attacker.Damage); // blocker's 1A, below Bruiser's 2D
        Assert.Equal(20, state.PlayerTwo.Life); // blocked attacker never hits the player
    }

    [Fact]
    public void AssignCombatDamage_RejectsIncompleteDamageSplit()
    {
        var bruiser = BuildCard("Bruiser", [Level1Char]);
        var state = BuildState(bruiser, BuildCard("Weakling", [Level1Char]));
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, state.CardCatalog["Weakling"], "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 1))); // must be 3
    }

    [Fact]
    public void DeclareAttackers_RejectsDieNotInFieldZone()
    {
        var bruiser = BuildCard("Bruiser", [Level1Char]);
        var state = BuildState(bruiser);
        var attacker = AddDie(state, bruiser, "p1");
        attacker.Zone = Zone.ReservePool;

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareAttackers(state, new AbilityQueue(), [attacker.Id]));
    }

    [Fact]
    public void BlockerRemovedBeforeDamageResolves_WithoutOvercrush_WastesTheDamage()
    {
        var bruiser = BuildCard("Bruiser", [Level1Char]); // 3A, no Overcrush
        var state = BuildState(bruiser, BuildCard("Weakling", [Level1Char]));
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, state.CardCatalog["Weakling"], "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        EffectInterpreter.KoDie(state, queue, blocker, triggersKOAbilities: false); // stand-in for a mid-window ability KO'ing it

        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.DoesNotContain(attacker.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, attacker.Zone); // still blocked, still returns to the field
        Assert.Equal(20, state.PlayerTwo.Life); // no Overcrush - the wasted damage does not carry to the player
    }

    // --- Overcrush ---

    [Fact]
    public void Overcrush_KillingAllBlockers_DealsLeftoverDamageToOpponent()
    {
        var bruiser = BuildCard("Bruiser", [new Face([], new CharacterFaceData(1, 1, 5, 2))], keywords: ["Overcrush"]); // 5A
        var sidekickCard = BuildCard("Sidekick", [new Face([], new CharacterFaceData(1, 0, 1, 1))]); // 1D
        var state = BuildState(bruiser, sidekickCard);
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, sidekickCard, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        // Rule 2.7.4.3.4 - the full 5A must still be assigned somewhere.
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 5));

        Assert.Contains(blocker.Id, result.KOdDieIds);
        Assert.Equal(16, state.PlayerTwo.Life); // 5 attack - 1 defense actually needed = 4 leftover
    }

    [Fact]
    public void Overcrush_BlockerSurvives_DealsNoLeftoverDamage()
    {
        var bruiser = BuildCard("Bruiser", [new Face([], new CharacterFaceData(1, 1, 5, 2))], keywords: ["Overcrush"]);
        var tank = BuildCard("Tank", [new Face([], new CharacterFaceData(1, 1, 1, 10))]); // survives the full 5A
        var state = BuildState(bruiser, tank);
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, tank, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 5));

        Assert.Empty(result.KOdDieIds);
        Assert.Equal(20, state.PlayerTwo.Life);
    }

    [Fact]
    public void Overcrush_BlockerRemovedBeforeDamageResolves_DealsFullAttackToOpponent()
    {
        var bruiser = BuildCard("Bruiser", [new Face([], new CharacterFaceData(1, 1, 5, 2))], keywords: ["Overcrush"]);
        var sidekickCard = BuildCard("Sidekick", [new Face([], new CharacterFaceData(1, 0, 1, 1))]);
        var state = BuildState(bruiser, sidekickCard);
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, sidekickCard, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        EffectInterpreter.KoDie(state, queue, blocker, triggersKOAbilities: false); // removed before damage resolves

        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.DoesNotContain(attacker.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, attacker.Zone); // was blocked - not the unblocked/Out of Play path
        Assert.Equal(15, state.PlayerTwo.Life); // full 5 attack - no live blocker defense to subtract
    }

    // --- Fast (the rulebook's own worked example) ---

    private static (GameState state, DieInstance attacker, DieInstance blocker) CreateFastCombatState(
        int attackerAttack, int attackerDefense, bool attackerFast, int blockerAttack, int blockerDefense, bool blockerFast)
    {
        var attackerCard = BuildCard("Attacker", [new Face([], new CharacterFaceData(1, 1, attackerAttack, attackerDefense))], keywords: attackerFast ? ["Fast"] : []);
        var blockerCard = BuildCard("Blocker", [new Face([], new CharacterFaceData(1, 1, blockerAttack, blockerDefense))], keywords: blockerFast ? ["Fast"] : []);
        var state = BuildState(attackerCard, blockerCard);
        var attacker = AddDie(state, attackerCard, "p1");
        var blocker = AddDie(state, blockerCard, "p2");
        return (state, attacker, blocker);
    }

    // The rulebook's own worked example, verbatim: "An attacker with
    // 4A/2D and Fast is blocked by a Character die with 5A/3D. The
    // attacker would deal its combat damage before the blocker because of
    // the Fast ability. This KOs the blocker before it can apply damage
    // to the attacker."
    [Fact]
    public void Fast_AttackerKOsBlockerBeforeBlockerCanRetaliate_MatchesTheRulebookExample()
    {
        var (state, attacker, blocker) = CreateFastCombatState(4, 2, true, 5, 3, false);
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 4));

        Assert.Contains(blocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.PrepArea, blocker.Zone);
        Assert.DoesNotContain(attacker.Id, result.KOdDieIds);
        Assert.Equal(0, attacker.Damage); // never took the blocker's 5A - it was already dead
        Assert.Equal(Zone.FieldZone, attacker.Zone);
    }

    // The rulebook's own follow-up: "Had the attacker not had the Fast
    // ability, the blocker would also KO the attacker when combat damage
    // was resolved."
    [Fact]
    public void Fast_NeitherSideHasIt_SameMatchupKillsBothInstead()
    {
        var (state, attacker, blocker) = CreateFastCombatState(4, 2, false, 5, 3, false);
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 4));

        Assert.Contains(blocker.Id, result.KOdDieIds);
        Assert.Contains(attacker.Id, result.KOdDieIds); // dies too, unlike the Fast version above
    }

    [Fact]
    public void Fast_BothSidesFast_ExchangeDamageSimultaneously()
    {
        var (state, attacker, blocker) = CreateFastCombatState(3, 3, true, 3, 3, true);
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 3));

        Assert.Contains(attacker.Id, result.KOdDieIds);
        Assert.Contains(blocker.Id, result.KOdDieIds);
    }

    // --- KO triggers fire through combat, aura affects combat stats ---

    [Fact]
    public void KO_By_Combat_Damage_Fires_DieKOd_Through_The_Real_Event_Bus()
    {
        var watcherAbility = new TriggeredAbility(TriggerKind.DieKOd, new LifeChange(new Fixed(0)));
        var bruiser = BuildCard("Bruiser", [Level1Char]); // 3A/2D
        var victim = BuildCard("Victim", [new Face([], new CharacterFaceData(1, 1, 1, 1))], abilities: [watcherAbility]); // self-only "when KO'd"
        var state = BuildState(bruiser, victim);
        var attacker = AddDie(state, bruiser, "p1");
        var blocker = AddDie(state, victim, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 3));

        var pending = Assert.Single(queue.Pending);
        Assert.Equal(TriggerKind.DieKOd, pending.Trigger);
        Assert.Equal(blocker.Id, pending.SourceDieId);
    }

    [Fact]
    public void A_StatAura_Increases_Effective_Attack_Used_In_Combat()
    {
        var buffer = BuildCard("Buffer", [Level1Char], continuous:
            [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(2))]);
        var tank = BuildCard("Tank", [new Face([], new CharacterFaceData(1, 1, 1, 10))]);
        var state = BuildState(buffer, tank);
        var attacker = AddDie(state, buffer, "p1"); // 3A printed, +2 from its own aura = 5A
        var blocker = AddDie(state, tank, "p2");
        var queue = new AbilityQueue();

        Assert.Equal(5, QueryEngine.GetAttack(state, attacker));

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        // The split must assign the full AURA-INCLUSIVE attack (5), not
        // the printed value (3) - proves AssignCombatDamage reads
        // QueryEngine.GetAttack, not CardDef/Face data directly. (A
        // rejected split still advances AttackSubStep, same as v1's own
        // AssignCombatDamage - so this is asserted via a throw only,
        // not followed by a second real attempt in the same state.)
        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 3)));
    }

    [Fact]
    public void A_StatAura_Inclusive_Split_Is_Accepted_And_Applies_Full_Damage()
    {
        var buffer = BuildCard("Buffer", [Level1Char], continuous:
            [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(2))]);
        var tank = BuildCard("Tank", [new Face([], new CharacterFaceData(1, 1, 1, 10))]);
        var state = BuildState(buffer, tank);
        var attacker = AddDie(state, buffer, "p1"); // 3A printed, +2 from its own aura = 5A
        var blocker = AddDie(state, tank, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);

        CombatEngine.AssignCombatDamage(state, queue, assignment, SoloSplit(attacker.Id, blocker.Id, 5));
        Assert.Equal(5, blocker.Damage);
    }

    // --- CombatRule (BlocksN / MinBlockers) ---

    [Fact]
    public void CombatRule_BlocksN_Lets_One_Blocker_Take_Multiple_Attackers()
    {
        var tankGrant = BuildCard("Tank", [new Face([], new CharacterFaceData(1, 1, 1, 20))], continuous:
            [new CombatRule(CombatRuleKind.BlocksN, new TargetFilter(Self: true), N: 2)]);
        var attackerCard = BuildCard("Attacker", [Level1Char]);
        var state = BuildState(tankGrant, attackerCard);
        var blocker = AddDie(state, tankGrant, "p2");
        var a1 = AddDie(state, attackerCard, "p1", "a1");
        var a2 = AddDie(state, attackerCard, "p1", "a2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [a1.Id, a2.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(a1.Id, blocker.Id);
        assignment.AssignBlocker(a2.Id, blocker.Id);

        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]); // does not throw - BlocksN:2 covers it
        Assert.Equal(StepIds.ActionGlobalWindow, state.CurrentStepId);
    }

    [Fact]
    public void CombatRule_BlocksN_Default_Rejects_A_Second_Attacker()
    {
        var plainBlockerCard = BuildCard("PlainBlocker", [new Face([], new CharacterFaceData(1, 1, 1, 20))]);
        var attackerCard = BuildCard("Attacker", [Level1Char]);
        var state = BuildState(plainBlockerCard, attackerCard);
        var blocker = AddDie(state, plainBlockerCard, "p2");
        var a1 = AddDie(state, attackerCard, "p1", "a1");
        var a2 = AddDie(state, attackerCard, "p1", "a2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [a1.Id, a2.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(a1.Id, blocker.Id);
        assignment.AssignBlocker(a2.Id, blocker.Id);

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]));
    }

    [Fact]
    public void CombatRule_MinBlockers_Rejects_A_Single_Blocker_Below_The_Requirement()
    {
        var magnetoLike = BuildCard("MagnetoLike", [Level1Char], continuous:
            [new CombatRule(CombatRuleKind.MinBlockers, new TargetFilter(Self: true), N: 2)]);
        var blockerCard = BuildCard("Blocker", [new Face([], new CharacterFaceData(1, 1, 1, 20))]);
        var state = BuildState(magnetoLike, blockerCard);
        var attacker = AddDie(state, magnetoLike, "p1");
        var blocker = AddDie(state, blockerCard, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]));
    }

    // --- CombatFlag (MustAttack / CantBlock / Unblockable) ---

    [Fact]
    public void CombatFlag_MustAttack_Rejects_Omitting_A_Forced_Attacker()
    {
        var bruiser = BuildCard("Bruiser", [Level1Char]);
        var state = BuildState(bruiser);
        var attacker = AddDie(state, bruiser, "p1");
        attacker.CombatFlags.Add(CombatFlagKind.MustAttack);
        var queue = new AbilityQueue();

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareAttackers(state, queue, []));
    }

    [Fact]
    public void CombatFlag_Unblockable_Rejects_An_Assigned_Blocker()
    {
        var falcon = BuildCard("Falcon", [Level1Char]);
        var blockerCard = BuildCard("Blocker", [Level1Char]);
        var state = BuildState(falcon, blockerCard);
        var attacker = AddDie(state, falcon, "p1");
        attacker.CombatFlags.Add(CombatFlagKind.Unblockable);
        var blocker = AddDie(state, blockerCard, "p2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]));
    }
}
