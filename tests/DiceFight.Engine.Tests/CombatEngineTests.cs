using DiceFight.Engine;
using DiceFight.Engine.Combat;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// A roller that always returns the same fixed face - lets Regenerate
// tests control exactly what a KO'd die rerolls to, without modeling real
// physical face tables (see PlaceholderDiceRoller remarks).
file sealed class FixedRoller(DieStatus status, int level) : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level);
}

public class CombatEngineTests
{
    private static (GameState state, DieInstance bruiser, DieInstance unblockedAttacker, DieInstance sidekickBlocker)
        CreateSkirmishState()
    {
        var bruiserCard = new CardDef
        {
            Id = "bruiser",
            Name = "Bruiser",
            Type = CardType.Character,
            PurchaseCost = 3,
            DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)]
        };

        var catalog = new Dictionary<string, CardDef> { [bruiserCard.Id] = bruiserCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var bruiser = new DieInstance
        {
            Id = "p1-bruiser-1", CardId = bruiserCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        var unblockedAttacker = state.DiceFor("p1").First(); // one of p1's sidekicks
        unblockedAttacker.Zone = Zone.FieldZone;
        unblockedAttacker.Status = DieStatus.SidekickCharacter;

        var sidekickBlocker = state.DiceFor("p2").First();
        sidekickBlocker.Zone = Zone.FieldZone;
        sidekickBlocker.Status = DieStatus.SidekickCharacter;

        state.Dice.Add(bruiser);

        return (state, bruiser, unblockedAttacker, sidekickBlocker);
    }

    [Fact]
    public void UnblockedAttacker_DealsDamageToPlayerAndLeavesPlay()
    {
        var (state, bruiser, unblocked, blocker) = CreateSkirmishState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id, unblocked.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id); // only Bruiser is blocked
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 3 } // Bruiser's full 3A
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        // Unblocked sidekick attacker (1A) hits the player for 1 and leaves play.
        Assert.Equal(19, state.PlayerTwo.Life);
        Assert.Equal(Zone.OutOfPlay, unblocked.Zone);
        Assert.Contains(blocker.Id, result.KOdDieIds); // 3 damage vs 1 defense
        Assert.Equal(Zone.PrepArea, blocker.Zone);
    }

    [Fact]
    public void BlockedAttacker_SurvivesIfBlockerDamageBelowDefense()
    {
        var (state, bruiser, unblocked, blocker) = CreateSkirmishState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 3 }
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        // Blocker's 1A doesn't reach Bruiser's 2D - Bruiser survives, returns to Field Zone.
        Assert.DoesNotContain(bruiser.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, bruiser.Zone);
        Assert.Equal(1, bruiser.Damage);
        Assert.Equal(20, state.PlayerTwo.Life); // blocked attacker never hits the player
    }

    [Fact]
    public void AssignCombatDamage_RejectsIncompleteDamageSplit()
    {
        var (state, bruiser, _, blocker) = CreateSkirmishState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var incompleteSplit = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 1 } // must be 3, not 1
        };

        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.AssignCombatDamage(state, queue, assignment, incompleteSplit));
    }

    [Fact]
    public void DeclareAttackers_RejectsDieNotInFieldZone()
    {
        var (state, bruiser, _, _) = CreateSkirmishState();
        bruiser.Zone = Zone.ReservePool;

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareAttackers(state, new AbilityQueue(), [bruiser.Id]));
    }

    // Same shape as CreateSkirmishState, but Bruiser has Overcrush and a
    // higher attack (5) so there's real "leftover beyond lethal" to work
    // with against a 1-defense Sidekick blocker.
    private static (GameState state, DieInstance bruiser, DieInstance blocker) CreateOvercrushSkirmishState()
    {
        var bruiserCard = new CardDef
        {
            Id = "overcrush-bruiser",
            Name = "Overcrush Bruiser",
            Type = CardType.Character,
            PurchaseCost = 3,
            DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2)],
            Keywords = [new KeywordInstance("Overcrush")],
        };

        var catalog = new Dictionary<string, CardDef> { [bruiserCard.Id] = bruiserCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var bruiser = new DieInstance
        {
            Id = "p1-overcrush-bruiser-1", CardId = bruiserCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bruiser);

        var blocker = state.DiceFor("p2").First(); // Sidekick, 1D
        blocker.Zone = Zone.FieldZone;
        blocker.Status = DieStatus.SidekickCharacter;

        return (state, bruiser, blocker);
    }

    [Fact]
    public void Overcrush_KillingAllBlockers_DealsLeftoverDamageToOpponent()
    {
        var (state, bruiser, blocker) = CreateOvercrushSkirmishState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        // Rule 2.7.4.3.4 - still have to assign the full 5A somewhere; all
        // of it goes to the one blocker, who only needed 1 to die.
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [blocker.Id] = 5 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(blocker.Id, result.KOdDieIds);
        // 5 attack - 1 defense (what was actually needed) = 4 leftover.
        Assert.Equal(16, state.PlayerTwo.Life);
    }

    [Fact]
    public void Overcrush_BlockerSurvives_DealsNoLeftoverDamage()
    {
        var bruiserCard = new CardDef
        {
            Id = "overcrush-bruiser", Name = "Overcrush Bruiser", Type = CardType.Character,
            PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2)],
            Keywords = [new KeywordInstance("Overcrush")],
        };
        var tankCard = new CardDef
        {
            Id = "tank", Name = "Tank", Type = CardType.Character,
            PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 10)], // survives even the full 5A
        };
        var catalog = new Dictionary<string, CardDef> { [bruiserCard.Id] = bruiserCard, [tankCard.Id] = tankCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var bruiser = new DieInstance
        {
            Id = "p1-overcrush-bruiser-1", CardId = bruiserCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var tank = new DieInstance
        {
            Id = "p2-tank-1", CardId = tankCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bruiser);
        state.Dice.Add(tank);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, tank.Id);
        CombatEngine.DeclareBlockers(state, assignment, [tank.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [tank.Id] = 5 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.DoesNotContain(tank.Id, result.KOdDieIds);
        Assert.Equal(20, state.PlayerTwo.Life); // no leftover - the blocker never died
    }

    // A Bruiser (Overcrush, 5A) attacking a Regenerate blocker (1D) - used
    // by both the Overcrush/Regenerate interaction test and the plain
    // Regenerate tests below.
    private static (GameState state, DieInstance bruiser, DieInstance regenBlocker) CreateOvercrushVsRegenerateState()
    {
        var bruiserCard = new CardDef
        {
            Id = "overcrush-bruiser", Name = "Overcrush Bruiser", Type = CardType.Character,
            PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2)],
            Keywords = [new KeywordInstance("Overcrush")],
        };
        var regenCard = new CardDef
        {
            Id = "regen-blocker", Name = "Regen Blocker", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Keywords = [new KeywordInstance("Regenerate")],
        };
        var catalog = new Dictionary<string, CardDef> { [bruiserCard.Id] = bruiserCard, [regenCard.Id] = regenCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var bruiser = new DieInstance
        {
            Id = "p1-overcrush-bruiser-1", CardId = bruiserCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var regenBlocker = new DieInstance
        {
            Id = "p2-regen-blocker-1", CardId = regenCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bruiser);
        state.Dice.Add(regenBlocker);

        return (state, bruiser, regenBlocker);
    }

    [Fact]
    public void Overcrush_InteractsWithRegenerate_LeftoverStillAppliesEvenThoughBlockerSurvives()
    {
        var (state, bruiser, regenBlocker) = CreateOvercrushVsRegenerateState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, regenBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [regenBlocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [regenBlocker.Id] = 5 },
        };
        // Regenerate rolls a character face - the blocker survives, but it
        // still ends up back in the Field Zone, not the Attack Zone (its
        // own glossary text: "return it to the field... but not the Attack
        // Zone"). It's alive, but no longer blocking - Overcrush's "removes
        // all of its blockers" condition doesn't require them dead, just
        // gone from the fight, so the leftover still carries through.
        var roller = new FixedRoller(DieStatus.Character, 1);
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits, roller);

        Assert.DoesNotContain(regenBlocker.Id, result.KOdDieIds); // never actually KO'd...
        Assert.Equal(Zone.FieldZone, regenBlocker.Zone);
        Assert.Equal(16, state.PlayerTwo.Life); // ...but Overcrush still triggers: 5 attack - 1 defense = 4 leftover
    }

    [Fact]
    public void Regenerate_RollingACharacterFace_ReturnsToFieldInsteadOfBeingKOd()
    {
        var (state, bruiser, regenBlocker) = CreateOvercrushVsRegenerateState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, regenBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [regenBlocker.Id]);
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [regenBlocker.Id] = 5 },
        };

        var roller = new FixedRoller(DieStatus.Character, 2); // rolls Level 2
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits, roller);

        Assert.DoesNotContain(regenBlocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, regenBlocker.Zone); // "back to the field... but not the Attack Zone"
        Assert.Equal(DieStatus.Character, regenBlocker.Status);
        Assert.Equal(2, regenBlocker.Level); // on the rolled face
        Assert.Equal(0, regenBlocker.Damage);
    }

    [Fact]
    public void Regenerate_RollingANonCharacterFace_FallsThroughToANormalKO()
    {
        var (state, bruiser, regenBlocker) = CreateOvercrushVsRegenerateState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, regenBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [regenBlocker.Id]);
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [regenBlocker.Id] = 5 },
        };

        var roller = new FixedRoller(DieStatus.Energy, 0); // rolls an energy face, not a character face
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits, roller);

        Assert.Contains(regenBlocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.PrepArea, regenBlocker.Zone); // "otherwise, move the die to your Prep Area"
        Assert.Equal(DieStatus.Unrolled, regenBlocker.Status);
    }

    [Fact]
    public void Regenerate_WithNoRollerSupplied_JustGetsKOdNormally()
    {
        var (state, bruiser, regenBlocker) = CreateOvercrushVsRegenerateState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, regenBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [regenBlocker.Id]);
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [regenBlocker.Id] = 5 },
        };

        // No roller passed (defaults to null) - Regenerate simply can't
        // trigger without one to roll with.
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(regenBlocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.PrepArea, regenBlocker.Zone);
    }

    // wizkids.com/dicemasters/keywords - Overcrush also triggers when a
    // blocker is "removed for other reasons", not just KO'd by this
    // combat's own damage - e.g. some other ability (a Basic Action used
    // during the Action/Global window, sub-step 3) KOs the blocker before
    // AssignCombatDamage is even called. Simulated here by calling
    // DieStats.ForceKO directly on the blocker after DeclareBlockers,
    // standing in for that ability resolving.
    [Fact]
    public void Overcrush_BlockerRemovedBeforeDamageResolves_DealsFullAttackToOpponent()
    {
        var (state, bruiser, blocker) = CreateOvercrushSkirmishState();

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        DieStats.ForceKO(state, blocker); // stand-in for a mid-combat ability KO'ing it

        // No live blockers left, so no split is needed at all.
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.DoesNotContain(bruiser.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, bruiser.Zone); // was blocked - NOT the unblocked/Out of Play path
        Assert.Equal(15, state.PlayerTwo.Life); // full 5 attack (no live blocker defense to subtract)
    }

    [Fact]
    public void BlockerRemovedBeforeDamageResolves_WithoutOvercrush_WastesTheDamage()
    {
        var (state, bruiser, _, blocker) = CreateSkirmishState(); // plain 3A/2D bruiser, no Overcrush

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        DieStats.ForceKO(state, blocker);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.DoesNotContain(bruiser.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, bruiser.Zone); // still blocked, still returns to the field
        Assert.Equal(20, state.PlayerTwo.Life); // no Overcrush - the wasted damage does not carry to the player
    }

    [Fact]
    public void Overcrush_OneOfTwoBlockersRemovedBeforeDamageResolves_OnlyLiveBlockerDefenseCounts()
    {
        var bruiserCard = new CardDef
        {
            Id = "overcrush-bruiser", Name = "Overcrush Bruiser", Type = CardType.Character,
            PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 8, Defense: 2)],
            Keywords = [new KeywordInstance("Overcrush")],
        };
        var catalog = new Dictionary<string, CardDef> { [bruiserCard.Id] = bruiserCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var bruiser = new DieInstance
        {
            Id = "p1-overcrush-bruiser-1", CardId = bruiserCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(bruiser);

        var sidekicks = state.DiceFor("p2").Take(2).ToList();
        foreach (var sk in sidekicks)
        {
            sk.Zone = Zone.FieldZone;
            sk.Status = DieStatus.SidekickCharacter;
        }
        var (removedBlocker, liveBlocker) = (sidekicks[0], sidekicks[1]);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [bruiser.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(bruiser.Id, removedBlocker.Id);
        assignment.AssignBlocker(bruiser.Id, liveBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [removedBlocker.Id, liveBlocker.Id]);

        DieStats.ForceKO(state, removedBlocker); // removed before damage resolves

        // Only the live blocker is left to receive the split - the full
        // attack value still has to go somewhere (rule 2.7.4.3.4).
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [bruiser.Id] = new Dictionary<string, int> { [liveBlocker.Id] = 8 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(liveBlocker.Id, result.KOdDieIds);
        Assert.Equal(13, state.PlayerTwo.Life); // 8 attack - 1 defense (only the live blocker's) = 7 leftover
    }

    // Keyword Call Out: p1's attacker targets one of two p2 Sidekicks when
    // it attacks - the target is the only die that may legally block it,
    // and it may not legally block anything else.
    private static (GameState state, DieInstance attacker, DieInstance target, DieInstance otherBlocker)
        CreateCallOutState()
    {
        var callOutCard = new CardDef
        {
            Id = "call-out-attacker", Name = "Call Out Attacker", Type = CardType.Character,
            PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)],
            Keywords = [new KeywordInstance("Call Out")],
            Abilities = [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
                Effect: new SetCallOutTarget(TargetSpec.CharacterDie("target character die", TargetOwnership.Opposing)))],
        };
        var catalog = new Dictionary<string, CardDef> { [callOutCard.Id] = callOutCard };
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var attacker = new DieInstance
        {
            Id = "p1-callout-1", CardId = callOutCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(attacker);

        var target = state.DiceFor("p2").ElementAt(0);
        target.Zone = Zone.FieldZone;
        target.Status = DieStatus.SidekickCharacter;

        var otherBlocker = state.DiceFor("p2").ElementAt(1);
        otherBlocker.Zone = Zone.FieldZone;
        otherBlocker.Status = DieStatus.SidekickCharacter;

        return (state, attacker, target, otherBlocker);
    }

    private static void DeclareAndResolveCallOuts(
        GameState state, AbilityQueue queue, IReadOnlyList<string> attackerIds, string chosenTargetId)
    {
        CombatEngine.DeclareAttackers(state, queue, attackerIds);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [chosenTargetId])));
    }

    [Fact]
    public void CallOut_RejectsABlockerThatIsNotTheChosenTarget()
    {
        var (state, attacker, target, otherBlocker) = CreateCallOutState();
        var queue = new AbilityQueue();
        DeclareAndResolveCallOuts(state, queue, [attacker.Id], target.Id);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, otherBlocker.Id); // not the Call Out target

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, assignment, [otherBlocker.Id]));
        Assert.Contains("Called Out", ex.Message);
    }

    [Fact]
    public void CallOut_TheChosenTargetCanLegallyBlockTheAttacker()
    {
        var (state, attacker, target, _) = CreateCallOutState();
        var queue = new AbilityQueue();
        DeclareAndResolveCallOuts(state, queue, [attacker.Id], target.Id);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, target.Id);

        CombatEngine.DeclareBlockers(state, assignment, [target.Id]); // does not throw
        Assert.Equal(Zone.AttackZone, target.Zone);
    }

    [Fact]
    public void CallOut_TargetCannotLegallyBlockADifferentAttacker()
    {
        var (state, attacker, target, _) = CreateCallOutState();
        var secondAttacker = state.DiceFor("p1").First(); // plain Sidekick, no Call Out
        secondAttacker.Zone = Zone.FieldZone;
        secondAttacker.Status = DieStatus.SidekickCharacter;

        var queue = new AbilityQueue();
        DeclareAndResolveCallOuts(state, queue, [attacker.Id, secondAttacker.Id], target.Id);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(secondAttacker.Id, target.Id); // the Call Out target tries to block the WRONG attacker

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, assignment, [target.Id]));
        Assert.Contains("Called Out", ex.Message);
    }

    [Fact]
    public void CallOut_CancelledWhenTargetLeavesPlayBeforeBlockersAreDeclared()
    {
        var (state, attacker, target, otherBlocker) = CreateCallOutState();
        var queue = new AbilityQueue();
        DeclareAndResolveCallOuts(state, queue, [attacker.Id], target.Id);

        target.Zone = Zone.PrepArea; // simulates being removed by some other effect mid-window

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, otherBlocker.Id); // would be illegal if Call Out were still active

        CombatEngine.DeclareBlockers(state, assignment, [otherBlocker.Id]); // no throw - cancelled, not unblockable
        Assert.Equal(Zone.AttackZone, otherBlocker.Zone);
    }

    [Fact]
    public void CallOut_CancelledWhenTwoAttackersChooseTheSameTarget()
    {
        var (state, attacker1, target, otherBlocker) = CreateCallOutState();
        var attacker2 = new DieInstance
        {
            Id = "p1-callout-2", CardId = attacker1.CardId, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(attacker2);

        var queue = new AbilityQueue();
        DeclareAndResolveCallOuts(state, queue, [attacker1.Id, attacker2.Id], target.Id);
        Assert.Equal(2, state.CallOutTargets.Count); // both recorded, both pointing at the same target

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker1.Id, otherBlocker.Id); // would be illegal if attacker1's Call Out still applied

        CombatEngine.DeclareBlockers(state, assignment, [otherBlocker.Id]); // no throw - both cancelled
        Assert.Equal(Zone.AttackZone, otherBlocker.Zone);
    }

    [Fact]
    public void CallOut_NoLegalTargetAvailable_RecordsNothing()
    {
        var (state, attacker, target, otherBlocker) = CreateCallOutState();
        target.Zone = Zone.Bag; // no opposing character die is actually eligible
        otherBlocker.Zone = Zone.Bag;

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Empty(state.CallOutTargets);
    }

    // Keyword Deadly: a catalog with one Deadly-keyword Character card
    // and one plain one, so any test can build whichever attacker/blocker
    // combination it needs. Engagement is recorded at Declare Blockers,
    // resolved later at Clean Up (see TurnEngineTests' own Deadly
    // coverage for that half).
    private static readonly CardDef DeadlyCard = new()
    {
        Id = "deadly-character", Name = "Deadly Character", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
        Keywords = [new KeywordInstance("Deadly")],
    };

    private static readonly CardDef PlainCharacterCard = new()
    {
        Id = "plain-character", Name = "Plain Character", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
    };

    private static GameState CreateDeadlyGame()
    {
        var catalog = new Dictionary<string, CardDef> { [DeadlyCard.Id] = DeadlyCard, [PlainCharacterCard.Id] = PlainCharacterCard };
        var state = GameState.NewGame(catalog, new Player { Id = "p1", Name = "Player One" }, new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        return state;
    }

    private static DieInstance AddCharacterDie(GameState state, string id, string playerId, string cardId, Zone zone)
    {
        var die = new DieInstance
        {
            Id = id, CardId = cardId, OwnerId = playerId, ControllerId = playerId,
            Zone = zone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void Deadly_AttackerEngagesBlocker_RecordsBlockerForCleanUp()
    {
        var state = CreateDeadlyGame();
        var deadlyAttacker = AddCharacterDie(state, "p1-deadly-1", "p1", DeadlyCard.Id, Zone.FieldZone);
        var blocker = AddCharacterDie(state, "p2-blocker-1", "p2", PlainCharacterCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [deadlyAttacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(deadlyAttacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Contains(blocker.Id, state.DeadlyEngagedDieIds);
        Assert.DoesNotContain(deadlyAttacker.Id, state.DeadlyEngagedDieIds); // the Deadly die itself isn't engaged with itself
    }

    [Fact]
    public void Deadly_BlockerEngagesAttacker_RecordsAttackerForCleanUp()
    {
        var state = CreateDeadlyGame();
        var plainAttacker = AddCharacterDie(state, "p1-plain-1", "p1", PlainCharacterCard.Id, Zone.FieldZone);
        var deadlyBlocker = AddCharacterDie(state, "p2-deadly-1", "p2", DeadlyCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [plainAttacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(plainAttacker.Id, deadlyBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [deadlyBlocker.Id]);

        Assert.Contains(plainAttacker.Id, state.DeadlyEngagedDieIds);
        Assert.DoesNotContain(deadlyBlocker.Id, state.DeadlyEngagedDieIds);
    }

    [Fact]
    public void Deadly_CoBlockerNotEngagedWithTheDeadlyDie_IsNotRecorded()
    {
        var state = CreateDeadlyGame();
        var plainAttacker = AddCharacterDie(state, "p1-plain-1", "p1", PlainCharacterCard.Id, Zone.FieldZone);
        var deadlyBlocker = AddCharacterDie(state, "p2-deadly-1", "p2", DeadlyCard.Id, Zone.FieldZone);
        var plainCoBlocker = AddCharacterDie(state, "p2-plain-1", "p2", PlainCharacterCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [plainAttacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(plainAttacker.Id, deadlyBlocker.Id);
        assignment.AssignBlocker(plainAttacker.Id, plainCoBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [deadlyBlocker.Id, plainCoBlocker.Id]);

        // The attacker is engaged with both blockers, so it's recorded
        // (via the Deadly one) - but the two blockers are never engaged
        // with each other (rule 2.7.2.3 is attacker<->blocker, not
        // blocker<->blocker), so the plain co-blocker is not.
        Assert.Contains(plainAttacker.Id, state.DeadlyEngagedDieIds);
        Assert.DoesNotContain(plainCoBlocker.Id, state.DeadlyEngagedDieIds);
    }

    [Fact]
    public void Deadly_UnblockedAttacker_RecordsNothing()
    {
        var state = CreateDeadlyGame();
        var deadlyAttacker = AddCharacterDie(state, "p1-deadly-1", "p1", DeadlyCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [deadlyAttacker.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []); // no blockers at all

        Assert.Empty(state.DeadlyEngagedDieIds);
    }

    // Clarification: "...even if the Character die with Deadly has been
    // KO'd or leaves the Field Zone" - the Deadly die dying to combat
    // damage itself (as the blocker, here) doesn't save the attacker it
    // was engaged with; the engagement was already locked in at Declare
    // Blockers, well before either die's fate is decided.
    [Fact]
    public void Deadly_BlockerKOdByCombatDamage_AttackerStillKOdAtCleanUpAfterward()
    {
        var strongCard = new CardDef
        {
            Id = "strong-attacker", Name = "Strong Attacker", Type = CardType.Character,
            PurchaseCost = 5, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 7, Defense: 7)],
        };
        var catalog = new Dictionary<string, CardDef> { [DeadlyCard.Id] = DeadlyCard, [strongCard.Id] = strongCard };
        var state = GameState.NewGame(catalog, new Player { Id = "p1", Name = "Player One" }, new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var attacker = new DieInstance
        {
            Id = "p1-strong-1", CardId = strongCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(attacker);
        var deadlyBlocker = new DieInstance
        {
            Id = "p2-deadly-1", CardId = DeadlyCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1, // 1A/1D
        };
        state.Dice.Add(deadlyBlocker);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, deadlyBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [deadlyBlocker.Id]);

        Assert.Contains(attacker.Id, state.DeadlyEngagedDieIds); // recorded already, before any damage

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [deadlyBlocker.Id] = 7 }, // full 7A, way over 1D
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        // The Deadly blocker dies outright to combat damage...
        Assert.Contains(deadlyBlocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.PrepArea, deadlyBlocker.Zone);
        // ...while the attacker (7D) shrugs off the blocker's paltry 1A
        // and survives combat cleanly.
        Assert.DoesNotContain(attacker.Id, result.KOdDieIds);
        Assert.Equal(Zone.FieldZone, attacker.Zone);

        TurnEngine.CleanUp(state);

        // Even with the Deadly die long gone, the attacker it was once
        // engaged with still dies at Clean Up.
        Assert.Equal(Zone.PrepArea, attacker.Zone);
    }

    // Clarification: an ability that removes the engaged die from combat
    // (e.g. Distraction's Global: "Remove target attacking character die
    // from combat," back to the Field Zone) doesn't save it either - the
    // engagement fact, not the die's current zone, is what Clean Up acts on.
    [Fact]
    public void Deadly_AttackerRemovedFromCombatByAnAbility_StillKOdAtCleanUp()
    {
        var state = CreateDeadlyGame();
        var attacker = AddCharacterDie(state, "p1-plain-1", "p1", PlainCharacterCard.Id, Zone.FieldZone);
        var deadlyBlocker = AddCharacterDie(state, "p2-deadly-1", "p2", DeadlyCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, deadlyBlocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [deadlyBlocker.Id]);

        Assert.Contains(attacker.Id, state.DeadlyEngagedDieIds);

        // Simulate some other ability pulling the attacker out of combat
        // entirely, well before Assign Combat Damage even runs.
        attacker.Zone = Zone.FieldZone;

        state.CurrentStep = TurnStep.CleanUp; // skip straight to Clean Up for this test
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.PrepArea, attacker.Zone); // still dies
    }

    // Keyword Fast - "Characters with Fast deal combat damage before
    // other Character dice in the Attack Step. All Character dice with
    // Fast deal damage at the same time." A fully parametrized 1-on-1
    // combat so each test can set up exactly the stats/Fast combination
    // it needs.
    private static (GameState state, DieInstance attacker, DieInstance blocker) CreateFastCombatState(
        int attackerAttack, int attackerDefense, bool attackerFast,
        int blockerAttack, int blockerDefense, bool blockerFast)
    {
        var attackerCard = new CardDef
        {
            Id = "fast-test-attacker", Name = "Attacker", Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: attackerAttack, Defense: attackerDefense)],
            Keywords = attackerFast ? [new KeywordInstance("Fast")] : [],
        };
        var blockerCard = new CardDef
        {
            Id = "fast-test-blocker", Name = "Blocker", Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: blockerAttack, Defense: blockerDefense)],
            Keywords = blockerFast ? [new KeywordInstance("Fast")] : [],
        };
        var catalog = new Dictionary<string, CardDef> { [attackerCard.Id] = attackerCard, [blockerCard.Id] = blockerCard };
        var state = GameState.NewGame(catalog, new Player { Id = "p1", Name = "Player One" }, new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var attacker = new DieInstance
        {
            Id = "p1-attacker-1", CardId = attackerCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var blocker = new DieInstance
        {
            Id = "p2-blocker-1", CardId = blockerCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(attacker);
        state.Dice.Add(blocker);
        return (state, attacker, blocker);
    }

    // The rulebook's own worked example, verbatim: "An attacker with
    // 4A/2D and Fast is blocked by a Character die with 5A/3D. The
    // attacker would deal its combat damage before the blocker because
    // of the Fast ability. This KOs the blocker before it can apply
    // damage to the attacker."
    [Fact]
    public void Fast_AttackerKOsBlockerBeforeBlockerCanRetaliate_MatchesTheRulebookExample()
    {
        var (state, attacker, blocker) = CreateFastCombatState(
            attackerAttack: 4, attackerDefense: 2, attackerFast: true,
            blockerAttack: 5, blockerDefense: 3, blockerFast: false);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blocker.Id] = 4 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(blocker.Id, result.KOdDieIds);
        Assert.Equal(Zone.PrepArea, blocker.Zone);
        Assert.DoesNotContain(attacker.Id, result.KOdDieIds);
        Assert.Equal(0, attacker.Damage); // never took the blocker's 5A - it was already dead
        Assert.Equal(Zone.FieldZone, attacker.Zone);
    }

    // The rulebook's own follow-up: "Had the attacker not had the Fast
    // ability, the blocker would also KO the attacker when combat damage
    // was resolved." Same stats, no Fast anywhere - ordinary simultaneous
    // combat, so both sides die together instead of just the blocker.
    [Fact]
    public void Fast_NeitherSideHasIt_SameMatchupKillsBothInstead()
    {
        var (state, attacker, blocker) = CreateFastCombatState(
            attackerAttack: 4, attackerDefense: 2, attackerFast: false,
            blockerAttack: 5, blockerDefense: 3, blockerFast: false);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blocker.Id] = 4 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(blocker.Id, result.KOdDieIds);
        Assert.Contains(attacker.Id, result.KOdDieIds); // dies too, unlike the Fast version above
    }

    [Fact]
    public void Fast_BlockerKOsAttackerBeforeAttackerCanDealDamage()
    {
        var (state, attacker, blocker) = CreateFastCombatState(
            attackerAttack: 1, attackerDefense: 2, attackerFast: false,
            blockerAttack: 5, blockerDefense: 3, blockerFast: true);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blocker.Id] = 1 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(attacker.Id, result.KOdDieIds);
        Assert.Equal(Zone.PrepArea, attacker.Zone);
        Assert.DoesNotContain(blocker.Id, result.KOdDieIds);
        Assert.Equal(0, blocker.Damage); // never took the attacker's 1A - attacker was already dead
    }

    // "All Character dice with Fast deal damage at the same time" - both
    // sides here deal exactly enough to KO the other; a sequential model
    // would let one side "win" by going first, but Fast's own text means
    // both actually die together.
    [Fact]
    public void Fast_BothSidesFast_ExchangeDamageSimultaneously()
    {
        var (state, attacker, blocker) = CreateFastCombatState(
            attackerAttack: 3, attackerDefense: 3, attackerFast: true,
            blockerAttack: 3, blockerDefense: 3, blockerFast: true);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blocker.Id] = 3 },
        };
        var result = CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Contains(attacker.Id, result.KOdDieIds);
        Assert.Contains(blocker.Id, result.KOdDieIds);
    }

    // A Fast die's damage not being lethal doesn't skip the second wave -
    // a blocker that survives Fast damage still gets to deal its own
    // damage back afterward, same as it always would have.
    [Fact]
    public void Fast_SurvivingTheFastWave_StillDealsDamageBackAfterward()
    {
        var (state, attacker, blocker) = CreateFastCombatState(
            attackerAttack: 1, attackerDefense: 5, attackerFast: true,
            blockerAttack: 3, blockerDefense: 10, blockerFast: false);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            [attacker.Id] = new Dictionary<string, int> { [blocker.Id] = 1 },
        };
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);

        Assert.Equal(1, blocker.Damage); // survives the Fast attacker's puny 1 damage (10D)
        Assert.Equal(3, attacker.Damage); // still gets its own 3 back afterward
        Assert.Equal(Zone.FieldZone, blocker.Zone);
        Assert.Equal(Zone.FieldZone, attacker.Zone); // 5D easily absorbs 3
    }

    // Keyword Energy Drain X - "After blockers are assigned, spin each
    // Character die engaged with a Character die with Energy Drain down
    // [X] level(s)." A 3-level catalog (so both starting level and
    // clamping-at-1 are exercisable) with a configurable Params amount.
    private static CardDef EnergyDrainCard(int amount) => new()
    {
        Id = "energy-drain-source", Name = "Energy Drain Source", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Keywords = [new KeywordInstance("Energy Drain", Params: [amount])],
        Levels =
        [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 3),
        ],
    };

    private static readonly CardDef PlainThreeLevelCard = new()
    {
        Id = "plain-three-level", Name = "Plain Three Level", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Levels =
        [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 3),
        ],
    };

    private static GameState CreateEnergyDrainGame(int drainAmount = 1)
    {
        var drainCard = EnergyDrainCard(drainAmount);
        var catalog = new Dictionary<string, CardDef> { [drainCard.Id] = drainCard, [PlainThreeLevelCard.Id] = PlainThreeLevelCard };
        var state = GameState.NewGame(catalog, new Player { Id = "p1", Name = "Player One" }, new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        return state;
    }

    private static DieInstance AddDrainDie(GameState state, string id, string playerId, string cardId, int level, Zone zone = Zone.FieldZone)
    {
        var die = new DieInstance
        {
            Id = id, CardId = cardId, OwnerId = playerId, ControllerId = playerId,
            Zone = zone, Status = DieStatus.Character, Level = level,
        };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void EnergyDrain_AttackerSpinsDownItsBlocker()
    {
        var state = CreateEnergyDrainGame();
        var attacker = AddDrainDie(state, "p1-drain-1", "p1", "energy-drain-source", level: 2);
        var blocker = AddDrainDie(state, "p2-plain-1", "p2", PlainThreeLevelCard.Id, level: 3);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Equal(2, blocker.Level); // spun down 1
        Assert.Equal(2, attacker.Level); // the Energy Drain die itself is untouched
    }

    [Fact]
    public void EnergyDrain_BlockerSpinsDownItsAttacker()
    {
        var state = CreateEnergyDrainGame();
        var attacker = AddDrainDie(state, "p1-plain-1", "p1", PlainThreeLevelCard.Id, level: 3);
        var blocker = AddDrainDie(state, "p2-drain-1", "p2", "energy-drain-source", level: 2);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Equal(2, attacker.Level); // spun down 1
        Assert.Equal(2, blocker.Level); // untouched
    }

    // Rule text: "Character dice at level 1 cannot be spun down."
    [Fact]
    public void EnergyDrain_ClampsAtLevel1()
    {
        var state = CreateEnergyDrainGame();
        var attacker = AddDrainDie(state, "p1-drain-1", "p1", "energy-drain-source", level: 1);
        var blocker = AddDrainDie(state, "p2-plain-1", "p2", PlainThreeLevelCard.Id, level: 1);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Equal(1, blocker.Level);
    }

    // Clarification (1): "Energy Drain 2... if on level 3 that die will be
    // spun down to level 1" - straight to the clamp, not just -1.
    [Fact]
    public void EnergyDrain_2_SpinsDownTwoLevels()
    {
        var state = CreateEnergyDrainGame(drainAmount: 2);
        var attacker = AddDrainDie(state, "p1-drain-1", "p1", "energy-drain-source", level: 1);
        var blocker = AddDrainDie(state, "p2-plain-1", "p2", PlainThreeLevelCard.Id, level: 3);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Equal(1, blocker.Level);
    }

    [Fact]
    public void EnergyDrain_UnblockedAttacker_NoEngagementNoEffect()
    {
        var state = CreateEnergyDrainGame();
        var attacker = AddDrainDie(state, "p1-drain-1", "p1", "energy-drain-source", level: 2);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []); // no blockers at all

        Assert.Equal(2, attacker.Level); // nothing to engage with, nothing spins
    }

    // Two independent Energy Drain sources engaged with the same
    // attacker (via co-blocking) each apply their own spin-down - they
    // compound, same as any other independently-sourced effect would.
    [Fact]
    public void EnergyDrain_TwoSourcesEngagedWithTheSameDie_Compound()
    {
        var state = CreateEnergyDrainGame();
        var attacker = AddDrainDie(state, "p1-plain-1", "p1", PlainThreeLevelCard.Id, level: 3);
        var drainBlocker1 = AddDrainDie(state, "p2-drain-1", "p2", "energy-drain-source", level: 2);
        var drainBlocker2 = AddDrainDie(state, "p2-drain-2", "p2", "energy-drain-source", level: 2);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, drainBlocker1.Id);
        assignment.AssignBlocker(attacker.Id, drainBlocker2.Id);
        CombatEngine.DeclareBlockers(state, assignment, [drainBlocker1.Id, drainBlocker2.Id]);

        Assert.Equal(1, attacker.Level); // 3 -> 2 -> 1, one spin-down per engaged Energy Drain blocker
    }

    // Keyword Infiltrate - "When a Character die with Infiltrate attacks
    // and is not blocked, you may choose to remove that die from combat
    // immediately after blockers are declared before Action dice or
    // Global abilities may be used. If you do, that die deals 1 damage
    // to your opponent, and the die remains in your Field Zone."
    private static readonly CardDef InfiltrateCard = new()
    {
        Id = "infiltrate-attacker", Name = "Infiltrate Attacker", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2)],
        Keywords = [new KeywordInstance("Infiltrate")],
    };

    private static GameState CreateInfiltrateGame()
    {
        var catalog = new Dictionary<string, CardDef> { [InfiltrateCard.Id] = InfiltrateCard, [PlainThreeLevelCard.Id] = PlainThreeLevelCard };
        var state = GameState.NewGame(catalog, new Player { Id = "p1", Name = "Player One" }, new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        return state;
    }

    private static DieInstance AddInfiltrateAttacker(GameState state, string id = "p1-infiltrate-1") =>
        AddCharacterDie(state, id, "p1", InfiltrateCard.Id, Zone.FieldZone);

    [Fact]
    public void Infiltrate_UnblockedEligibleDie_EntersInfiltrateWindow()
    {
        var state = CreateInfiltrateGame();
        var attacker = AddInfiltrateAttacker(state);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []);

        Assert.Equal(AttackSubStep.InfiltrateWindow, state.AttackSubStep);
    }

    // No Infiltrate-eligible dice at all - existing combats (the entire
    // rest of this test file) never touch the new sub-step.
    [Fact]
    public void Infiltrate_NoEligibleDice_SkipsStraightToActionAndGlobalWindow()
    {
        var state = CreateInfiltrateGame();
        var attacker = AddCharacterDie(state, "p1-plain-1", "p1", PlainThreeLevelCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        CombatEngine.DeclareBlockers(state, new CombatAssignment(), []);

        Assert.Equal(AttackSubStep.ActionAndGlobalWindow, state.AttackSubStep);
    }

    // A blocked Infiltrate attacker isn't eligible ("attacks and is not
    // blocked") - no choice to offer, so the window is skipped even
    // though an Infiltrate die is present in the combat.
    [Fact]
    public void Infiltrate_BlockedInfiltrateAttacker_SkipsWindow()
    {
        var state = CreateInfiltrateGame();
        var attacker = AddInfiltrateAttacker(state);
        var blocker = AddCharacterDie(state, "p2-plain-1", "p2", PlainThreeLevelCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Equal(AttackSubStep.ActionAndGlobalWindow, state.AttackSubStep);
    }

    [Fact]
    public void Infiltrate_ChoosingToInfiltrate_DealsDamageAndReturnsToFieldZone()
    {
        var state = CreateInfiltrateGame();
        var attacker = AddInfiltrateAttacker(state);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, assignment, []);

        CombatEngine.ResolveInfiltrate(state, queue, assignment, [attacker.Id]);

        Assert.Equal(Zone.FieldZone, attacker.Zone); // removed from combat, not Out of Play
        Assert.Equal(Player.StartingLife - 1, state.PlayerTwo.Life);
        Assert.Equal(AttackSubStep.ActionAndGlobalWindow, state.AttackSubStep);
    }

    // Choosing NOT to Infiltrate (empty list) leaves the die to resolve
    // as a perfectly ordinary unblocked attacker at Assign Combat Damage -
    // full attack value to the opponent, Out of Play afterward.
    [Fact]
    public void Infiltrate_DecliningToUseIt_ResolvesAsAnOrdinaryUnblockedAttacker()
    {
        var state = CreateInfiltrateGame();
        var attacker = AddInfiltrateAttacker(state);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, assignment, []);
        CombatEngine.ResolveInfiltrate(state, queue, assignment, []); // declines

        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(Player.StartingLife - 2, state.PlayerTwo.Life); // full 2A, not just Infiltrate's 1
        Assert.Equal(Zone.OutOfPlay, attacker.Zone);
    }

    [Fact]
    public void Infiltrate_RejectsABlockedDieAsACandidate()
    {
        var state = CreateInfiltrateGame();
        var attacker = AddInfiltrateAttacker(state);
        var other = AddCharacterDie(state, "p1-other-1", "p1", InfiltrateCard.Id, Zone.FieldZone);
        var blocker = AddCharacterDie(state, "p2-plain-1", "p2", PlainThreeLevelCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        // `other` stays unblocked so the window is actually reachable;
        // `attacker` gets blocked and is the one we try (illegally) to Infiltrate with.
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id, other.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, assignment, [blocker.Id]);

        Assert.Equal(AttackSubStep.InfiltrateWindow, state.AttackSubStep); // `other` keeps the window open

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.ResolveInfiltrate(state, queue, assignment, [attacker.Id]));
        Assert.Contains("blocked", ex.Message);
    }

    [Fact]
    public void Infiltrate_RejectsADieWithoutTheKeyword()
    {
        var state = CreateInfiltrateGame();
        var infiltrator = AddInfiltrateAttacker(state);
        var plain = AddCharacterDie(state, "p1-plain-1", "p1", PlainThreeLevelCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [infiltrator.Id, plain.Id]);
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, assignment, []);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.ResolveInfiltrate(state, queue, assignment, [plain.Id]));
        Assert.Contains("Infiltrate", ex.Message);
    }

    // Ricochet's own follow-up ("while active, each time one of your
    // character dice uses Infiltrate...") isn't the infiltrating die's
    // own ability - it's a reactive check against every active die the
    // controller has, same shape as Attune. A synthetic reactor card
    // proves the trigger fires independent of Infiltrate's own base card.
    [Fact]
    public void Infiltrate_TriggersWhenInfiltratesForEveryActiveReactorDie()
    {
        var reactorCard = new CardDef
        {
            Id = "infiltrate-reactor", Name = "Infiltrate Reactor", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Abilities = [new AbilityDef(TriggerType.WhenInfiltrates, Cost: null, Effect: new GainLife(1))],
        };
        var catalog = new Dictionary<string, CardDef>
        {
            [InfiltrateCard.Id] = InfiltrateCard, [reactorCard.Id] = reactorCard,
        };
        var state = GameState.NewGame(catalog, new Player { Id = "p1", Name = "Player One" }, new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var attacker = AddCharacterDie(state, "p1-infiltrate-1", "p1", InfiltrateCard.Id, Zone.FieldZone);
        var reactor = AddCharacterDie(state, "p1-reactor-1", "p1", reactorCard.Id, Zone.FieldZone);

        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]); // reactor doesn't attack, just sits active in the Field Zone
        var assignment = new CombatAssignment();
        CombatEngine.DeclareBlockers(state, assignment, []);
        CombatEngine.ResolveInfiltrate(state, queue, assignment, [attacker.Id]);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.WhenInfiltrates, queue.Pending[0].Trigger);
        Assert.Equal(reactor.Id, queue.Pending[0].SourceDieId);
    }
}
