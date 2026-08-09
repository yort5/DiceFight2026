using DiceFight.Engine;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// Exercises EffectInterpreter directly against the scripted sample cards'
// real AbilityDefs (SampleCards.Dazzler, .CosmicCube, .ShockingGrasp,
// .CasketOfAncientWinters), independent of the turn/combat engines.
public class EffectInterpreterTests
{
    // Lets a Regenerate test control exactly what a KO'd die rerolls to,
    // mirroring CombatEngineTests' FixedRoller.
    private sealed class FixedRoller(DieStatus status, int level) : IDiceRoller
    {
        public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level);
    }

    private static GameState CreateState(IReadOnlyDictionary<string, CardDef>? catalog = null) =>
        GameState.NewGame(
            catalog ?? SampleCards.BuildCatalog(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });

    // A legal target for most of these specs: in the Field Zone, on a
    // character face - Sidekicks satisfy the default TargetSpec.CharacterDie
    // (rule 3.3.4/1.6.6) but not one with a RequiredEnergyType, since
    // Sidekicks have no energy type at all (rule 1.3.10).
    private static DieInstance FieldSidekickTarget(GameState state, string playerId)
    {
        var die = state.DiceFor(playerId).First();
        die.Zone = Zone.FieldZone;
        die.Status = DieStatus.SidekickCharacter;
        return die;
    }

    [Fact]
    public void DealDamage_KOsDieWhenDamageReachesDefense()
    {
        var state = CreateState();
        var target = FieldSidekickTarget(state, "p2");

        EffectInterpreter.Execute(
            new DealDamage(4, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone);
        Assert.Equal(DieStatus.Unrolled, target.Status);
    }

    // Keyword Attune's "target player or Character die" - DealDamage has
    // to tell a resolved player id apart from a die id (see GameState.
    // IsPlayerId) since TargetSpec.CharacterDieOrPlayer can resolve to either.
    [Fact]
    public void DealDamage_ToAPlayerId_ReducesTheirLifeInsteadOfLookingForADie()
    {
        var state = CreateState();
        state.PlayerTwo.Life = 20;

        EffectInterpreter.Execute(
            new DealDamage(3, TargetSpec.CharacterDieOrPlayer("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => ["p2"]));

        Assert.Equal(17, state.PlayerTwo.Life);
    }

    [Fact]
    public void DealDamage_CharacterDieOrPlayer_StillDamagesADieChosenInstead()
    {
        var state = CreateState();
        var target = FieldSidekickTarget(state, "p2");

        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDieOrPlayer("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone); // 1 damage vs 1D - KO'd, not misread as a player id
    }

    // Black Manta's "Deep Sea Deviant" printing of Retaliation - "deal 1
    // damage to your opponent for each of your active Villains." The
    // amount is computed from the ability's own source die, not the
    // target: only the source's own controller's active dice sharing an
    // affiliation with the source's own card count, and unaffiliated or
    // opposing-controller dice don't.
    [Fact]
    public void DealDamagePerActiveAffiliate_CountsOnlyTheSourceControllersOwnAffiliatedActiveDice()
    {
        var state = CreateState();
        state.PlayerTwo.Life = 20;

        var source = new DieInstance
        {
            Id = "p1-manta-1", CardId = SampleCards.BlackMantaDeepSeaDeviant.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var sameAffiliationAlly = new DieInstance
        {
            Id = "p1-manta-2", CardId = SampleCards.BlackMantaDeepSeaDeviant.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var differentAffiliation = new DieInstance
        {
            Id = "p1-superman-1", CardId = SampleCards.SupermanKalEl.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var opposingControllerSameAffiliation = new DieInstance
        {
            Id = "p2-manta-1", CardId = SampleCards.BlackMantaDeepSeaDeviant.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.AddRange([source, sameAffiliationAlly, differentAffiliation, opposingControllerSameAffiliation]);

        EffectInterpreter.Execute(
            new DealDamagePerActiveAffiliate(TargetSpec.Player("t", TargetOwnership.Opposing)),
            new EffectContext(state, "p1", SourceDieId: source.Id, _ => ["p2"]));

        Assert.Equal(18, state.PlayerTwo.Life); // 2 active Villains on p1's side (source + sameAffiliationAlly)
    }

    [Fact]
    public void DealDamagePerActiveAffiliate_NoSourceDie_DealsNoDamage()
    {
        var state = CreateState();
        state.PlayerTwo.Life = 20;

        EffectInterpreter.Execute(
            new DealDamagePerActiveAffiliate(TargetSpec.Player("t", TargetOwnership.Opposing)),
            new EffectContext(state, "p1", SourceDieId: null, _ => ["p2"]));

        Assert.Equal(20, state.PlayerTwo.Life);
    }

    // Ability-driven KOs (DealDamage KO'ing its target, or a direct Ko node
    // like Casket of Ancient Winters) go through DieStats.ForceKO just like
    // combat KOs do, so a Regenerate target survives here too - locking in
    // that behavior independent of CombatEngine.
    [Fact]
    public void DealDamage_RespectsRegenerate_WhenRollerSuppliedAndFaceIsCharacter()
    {
        var regenCard = new CardDef
        {
            Id = "regen-target", Name = "Regen Target", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Keywords = [new KeywordInstance("Regenerate")],
        };
        var catalog = new Dictionary<string, CardDef> { [regenCard.Id] = regenCard };
        var state = CreateState(catalog);
        var target = new DieInstance
        {
            Id = "p2-regen-target-1", CardId = regenCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var roller = new FixedRoller(DieStatus.Character, 2);
        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Roller: roller));

        Assert.Equal(Zone.FieldZone, target.Zone); // regenerated, not KO'd
        Assert.Equal(2, target.Level);
        Assert.Equal(0, target.Damage);
    }

    // Keyword Experience's own "opposing Monster KO'd this turn"
    // condition (GameState.OpposingMonsterKOdThisTurn) - set inside
    // DieStats.ForceKO, the single choke point every real KO already
    // funnels through, so these exercise it via the simplest KO path
    // (DealDamage) rather than needing combat machinery. Token-granting
    // itself is TurnEngine.CleanUp's job, covered separately there.
    private static readonly CardDef MonsterCard = new()
    {
        Id = "test-monster", Name = "Test Monster", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
        Affiliations = ["Monster"],
    };

    private static readonly CardDef NonMonsterCard = new()
    {
        Id = "test-non-monster", Name = "Test Non-Monster", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
    };

    [Fact]
    public void ForceKO_OpposingMonsterKOd_SetsTheTurnFlag()
    {
        var catalog = new Dictionary<string, CardDef> { [MonsterCard.Id] = MonsterCard };
        var state = CreateState(catalog);
        state.ActivePlayerId = "p1";
        var target = new DieInstance
        {
            Id = "p2-monster-1", CardId = MonsterCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.True(state.OpposingMonsterKOdThisTurn);
    }

    // Keyword Loyalty's own "no character dice were KO'd that turn"
    // condition (GameState.AnyCharacterKOdThisTurn) - same choke point,
    // but unlike OpposingMonsterKOdThisTurn it's unscoped by controller
    // or affiliation: ANY character KO counts, either player's.
    [Fact]
    public void ForceKO_AnyCharacterKOd_SetsTheUnscopedFlag()
    {
        var catalog = new Dictionary<string, CardDef> { [NonMonsterCard.Id] = NonMonsterCard };
        var state = CreateState(catalog);
        state.ActivePlayerId = "p1";
        var target = new DieInstance
        {
            // Owned/controlled by the active player themself - unlike
            // OpposingMonsterKOdThisTurn, Loyalty's flag doesn't care whose.
            Id = "p1-nonmonster-1", CardId = NonMonsterCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.True(state.AnyCharacterKOdThisTurn);
    }

    [Fact]
    public void ForceKO_OpposingNonMonsterKOd_DoesNotSetTheFlag()
    {
        var catalog = new Dictionary<string, CardDef> { [NonMonsterCard.Id] = NonMonsterCard };
        var state = CreateState(catalog);
        state.ActivePlayerId = "p1";
        var target = new DieInstance
        {
            Id = "p2-nonmonster-1", CardId = NonMonsterCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.False(state.OpposingMonsterKOdThisTurn);
    }

    // Clarification 4 - "nor can you gain an Experience Token by KO'ing
    // one of your own Monsters."
    [Fact]
    public void ForceKO_OwnMonsterKOd_DoesNotSetTheFlag()
    {
        var catalog = new Dictionary<string, CardDef> { [MonsterCard.Id] = MonsterCard };
        var state = CreateState(catalog);
        state.ActivePlayerId = "p1";
        var target = new DieInstance
        {
            Id = "p1-monster-1", CardId = MonsterCard.Id, OwnerId = "p1", ControllerId = "p1", // p1's OWN Monster
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t", TargetOwnership.Own)),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.False(state.OpposingMonsterKOdThisTurn);
    }

    [Fact]
    public void ForceKO_RegenerateInterceptsAnOpposingMonster_DoesNotSetTheFlag()
    {
        var regenMonsterCard = new CardDef
        {
            Id = "test-regen-monster", Name = "Test Regen Monster", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Keywords = [new KeywordInstance("Regenerate")],
            Affiliations = ["Monster"],
        };
        var catalog = new Dictionary<string, CardDef> { [regenMonsterCard.Id] = regenMonsterCard };
        var state = CreateState(catalog);
        state.ActivePlayerId = "p1";
        var target = new DieInstance
        {
            Id = "p2-regenmonster-1", CardId = regenMonsterCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var roller = new FixedRoller(DieStatus.Character, 2);
        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Roller: roller));

        Assert.Equal(Zone.FieldZone, target.Zone); // regenerated, not actually KO'd
        Assert.False(state.OpposingMonsterKOdThisTurn); // so no flag either
    }

    // Keyword Sacrifice - "Sacrificed Character dice are moved from the
    // Field Zone to Out of Play or the Used Pile, as applicable."
    // Clarification 1 - Out of Play only on the sacrificed die's own
    // OWNER's turn.
    [Fact]
    public void Sacrifice_OnOwnersOwnTurn_MovesToOutOfPlay()
    {
        var state = CreateState();
        state.ActivePlayerId = "p1";
        var target = FieldSidekickTarget(state, "p1"); // owned by p1, sacrificed on p1's own turn

        EffectInterpreter.Execute(
            new Sacrifice(TargetSpec.CharacterDie("t", TargetOwnership.Own)),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(Zone.OutOfPlay, target.Zone);
        Assert.Equal(DieStatus.Unrolled, target.Status);
    }

    [Fact]
    public void Sacrifice_NotOnOwnersOwnTurn_MovesStraightToUsedPile()
    {
        var state = CreateState();
        state.ActivePlayerId = "p1"; // p1's turn, but the sacrificed die belongs to p2
        var target = FieldSidekickTarget(state, "p2");

        EffectInterpreter.Execute(
            new Sacrifice(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p2", SourceDieId: null, _ => [target.Id]));

        Assert.Equal(Zone.UsedPile, target.Zone);
    }

    // Clarification 3 - "will not trigger 'when KO'd' abilities." Modeled
    // by bypassing DieStats.ForceKO entirely, which also means a
    // Regenerate-keyword die never gets a chance to intercept - a
    // Regenerate check would return the die to the Field Zone even with
    // a roller supplied, so landing in Out of Play instead proves
    // Sacrifice never went through ForceKO at all.
    [Fact]
    public void Sacrifice_BypassesRegenerateEntirely_EvenWithARollerSupplied()
    {
        var regenCard = new CardDef
        {
            Id = "regen-sacrifice-target", Name = "Regen Sacrifice Target", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Keywords = [new KeywordInstance("Regenerate")],
        };
        var catalog = new Dictionary<string, CardDef> { [regenCard.Id] = regenCard };
        var state = CreateState(catalog);
        state.ActivePlayerId = "p1";
        var target = new DieInstance
        {
            Id = "p1-regen-1", CardId = regenCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var roller = new FixedRoller(DieStatus.Character, 2); // would regenerate it, if Sacrifice went through ForceKO
        EffectInterpreter.Execute(
            new Sacrifice(TargetSpec.CharacterDie("t", TargetOwnership.Own)),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Roller: roller));

        Assert.Equal(Zone.OutOfPlay, target.Zone); // not regenerated back to the Field Zone
        Assert.Equal(1, target.Level); // reset, not the roller's level 2
    }

    [Fact]
    public void Dazzler_WhenFielded_Deals4DamageToChosenTarget()
    {
        var state = CreateState();
        // Dazzler's spec requires Mask energy type, which Sidekicks don't
        // have (rule 1.3.10) - needs an opposing Mask-type character die,
        // e.g. any of the sample Characters (all placeholder Mask type).
        var target = new DieInstance
        {
            Id = "p2-captain-marvel-1", CardId = SampleCards.CaptainMarvel.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        state.Dice.Add(target);

        var ability = SampleCards.Dazzler.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-dazzler-1", _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone); // 4 damage vs 2D at level 1
    }

    [Fact]
    public void CosmicCube_SwapsLifeTotals()
    {
        var state = CreateState();
        state.PlayerOne.Life = 15;
        state.PlayerTwo.Life = 20;

        var ability = SampleCards.CosmicCube.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-cosmiccube-1", _ => []));

        Assert.Equal(20, state.PlayerOne.Life);
        Assert.Equal(15, state.PlayerTwo.Life);
    }

    [Fact]
    public void ShockingGrasp_KOingTarget_PrepsTheActionDieItself()
    {
        var state = CreateState();
        var target = FieldSidekickTarget(state, "p2"); // 1D - lethal to 1 damage
        var sourceDie = new DieInstance
        {
            Id = "p1-shockinggrasp-1", CardId = SampleCards.ShockingGrasp.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.OutOfPlay
        };
        state.Dice.Add(sourceDie);

        var ability = SampleCards.ShockingGrasp.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", sourceDie.Id, _ => [target.Id]));

        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd by the 1 damage
        Assert.Equal(Zone.PrepArea, sourceDie.Zone); // Conditional held -> Prep this die
    }

    [Fact]
    public void ShockingGrasp_NonLethalDamage_DoesNotPrepTheActionDie()
    {
        var toughCard = new CardDef
        {
            Id = "tough", Name = "Tough Guy", Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 5)]
        };
        var state = CreateState(new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [toughCard.Id] = toughCard });
        var target = new DieInstance
        {
            Id = "p2-tough-1", CardId = toughCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        };
        state.Dice.Add(target);
        var sourceDie = new DieInstance
        {
            Id = "p1-shockinggrasp-1", CardId = SampleCards.ShockingGrasp.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.OutOfPlay
        };
        state.Dice.Add(sourceDie);

        var ability = SampleCards.ShockingGrasp.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", sourceDie.Id, _ => [target.Id]));

        Assert.Equal(1, target.Damage); // damaged but not KO'd (1 vs 5D)
        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.Equal(Zone.OutOfPlay, sourceDie.Zone); // Conditional did not hold - not Prepped
    }

    [Fact]
    public void CasketOfAncientWinters_KOsAndMovesThreeDiceFromEachZone()
    {
        var state = CreateState();
        var opponentDice = state.DiceFor("p2").ToList(); // 8 Sidekicks
        var fieldTargets = opponentDice.Take(3).ToList();
        var reserveTargets = opponentDice.Skip(3).Take(3).ToList();
        var prepTargets = opponentDice.Skip(6).Take(2).ToList(); // only 2 left; exercises "as many as available"
        foreach (var die in fieldTargets)
        {
            die.Zone = Zone.FieldZone;
            die.Status = DieStatus.SidekickCharacter; // the Ko clause requires a character face (rule 1.6.6)
        }
        foreach (var die in reserveTargets) die.Zone = Zone.ReservePool;
        foreach (var die in prepTargets) die.Zone = Zone.PrepArea;

        var ability = SampleCards.CasketOfAncientWinters.Abilities.Single();
        IReadOnlyList<string> Resolve(TargetSpec spec) => spec.Description switch
        {
            "opponent's 3 character dice" => fieldTargets.Select(d => d.Id).ToList(),
            "opponent's 3 reserve pool dice" => reserveTargets.Select(d => d.Id).ToList(),
            "opponent's 3 prep area dice" => prepTargets.Select(d => d.Id).ToList(),
            _ => []
        };

        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-casket-1", Resolve));

        Assert.All(fieldTargets, d => Assert.Equal(Zone.PrepArea, d.Zone)); // KO'd
        Assert.All(reserveTargets, d => Assert.Equal(Zone.Bag, d.Zone));
        Assert.All(prepTargets, d => Assert.Equal(Zone.UsedPile, d.Zone));
    }

    // Keyword Call Out's targeting choice - CombatEngine.ValidateCallOuts/
    // ActiveCallOutTargets exercises the actual blocking-legality
    // enforcement; this just proves the effect itself records the right
    // (attacker, target) pair.
    [Fact]
    public void SetCallOutTarget_RecordsTheAttackerAndChosenTargetInState()
    {
        var state = CreateState();
        var attacker = new DieInstance
        {
            Id = "p1-attacker", CardId = null, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.AttackZone, Status = DieStatus.SidekickCharacter,
        };
        state.Dice.Add(attacker);
        var target = FieldSidekickTarget(state, "p2");

        EffectInterpreter.Execute(
            new SetCallOutTarget(TargetSpec.CharacterDie("t", TargetOwnership.Opposing)),
            new EffectContext(state, "p1", attacker.Id, _ => [target.Id]));

        Assert.Equal(target.Id, state.CallOutTargets[attacker.Id]);
    }

    [Fact]
    public void SetCallOutTarget_NoLegalTarget_RecordsNothing()
    {
        var state = CreateState(); // no opposing character die exists
        var attacker = new DieInstance
        {
            Id = "p1-attacker", CardId = null, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.AttackZone, Status = DieStatus.SidekickCharacter,
        };
        state.Dice.Add(attacker);

        EffectInterpreter.Execute(
            new SetCallOutTarget(TargetSpec.CharacterDie("t", TargetOwnership.Opposing)),
            new EffectContext(state, "p1", attacker.Id, _ => []));

        Assert.Empty(state.CallOutTargets);
    }

    // Keyword Corrupt X - draws X dice from the target player's bag
    // (random, refilling from the Used Pile if needed), then a real
    // choice of which ONE goes to the Used Pile; the rest return to the
    // bag untouched. That choice's candidates don't exist until the draw
    // actually happens mid-effect, so - unlike every other target in this
    // engine - there's no legitimate answer a caller could have supplied
    // upfront; Execute always pauses via GameState.PendingChoice when
    // there's a real choice (2+ drawn) rather than ever consulting
    // ctx.ResolveTargets for it (see EffectNode.Corrupt's own remarks).
    [Fact]
    public void Corrupt_DrawsDiceAndSendsTheChosenOneToUsedPile_ReturnsTheRestToTheBag()
    {
        var state = CreateState();
        Assert.Equal(8, state.DiceIn("p2", Zone.Bag).Count());

        EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null, spec => spec.PlayersAllowed ? ["p2"] : [],
                Random: new Random(1)));

        Assert.NotNull(state.PendingChoice);
        Assert.False(state.PendingChoice!.AllowMultiple);
        Assert.Equal(2, state.PendingChoice.CandidateDieIds.Count);
        Assert.Equal(2, state.DiceIn("p2", Zone.DiceFromBag).Count()); // drawn, but the choice hasn't resolved yet

        var chosenId = state.PendingChoice.CandidateDieIds[0];
        state.PendingChoice.Resolve([chosenId]);

        var chosen = state.Dice.Single(d => d.Id == chosenId);
        Assert.Equal(Zone.UsedPile, chosen.Zone);
        Assert.Equal(7, state.DiceIn("p2", Zone.Bag).Count()); // 1 sent to Used Pile, 1 returned
        Assert.Single(state.DiceIn("p2", Zone.UsedPile));
        Assert.Empty(state.DiceIn("p2", Zone.DiceFromBag)); // nothing left staged mid-effect
    }

    [Fact]
    public void Corrupt_DrawingOnlyOneDie_SkipsTheChoiceAndSendsItDirectlyToUsedPile()
    {
        var state = CreateState();
        // Leave p2 with exactly 1 die reachable at all (bag + Used Pile
        // combined) - Corrupt 2 can only actually draw 1.
        foreach (var d in state.DiceIn("p2", Zone.Bag).Skip(1).ToList())
            d.Zone = Zone.ReservePool;

        EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null, spec => spec.PlayersAllowed ? ["p2"] : [],
                Random: new Random(1)));

        Assert.Single(state.DiceIn("p2", Zone.UsedPile));
    }

    [Fact]
    public void Corrupt_RefillsFromTheUsedPileWhenTheBagRunsOut()
    {
        var state = CreateState();
        foreach (var d in state.DiceIn("p2", Zone.Bag).Skip(1).ToList())
        {
            d.Zone = Zone.UsedPile;
            d.ResetToUnrolled();
        }

        EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null, spec => spec.PlayersAllowed ? ["p2"] : [],
                Random: new Random(1)));

        Assert.NotNull(state.PendingChoice);
        state.PendingChoice!.Resolve([state.PendingChoice.CandidateDieIds[0]]);

        // Drew 1 from the original bag, then refilled all 7 Used Pile dice
        // into the bag to draw a 2nd - one of the 2 drawn ends up in the
        // Used Pile, the other (plus the 6 refilled-but-undrawn) sit in the bag.
        Assert.Single(state.DiceIn("p2", Zone.UsedPile));
        Assert.Equal(7, state.DiceIn("p2", Zone.Bag).Count());
    }

    [Fact]
    public void Corrupt_NoDiceAnywhereToDraw_NoOp()
    {
        var state = CreateState();
        foreach (var d in state.DiceFor("p2").ToList()) d.Zone = Zone.ReservePool; // neither Bag nor Used Pile has anything

        EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null, spec => spec.PlayersAllowed ? ["p2"] : [],
                Random: new Random(1)));

        Assert.Empty(state.DiceIn("p2", Zone.UsedPile));
    }

    [Fact]
    public void Corrupt_RejectsAChosenDieThatWasNotActuallyDrawn()
    {
        var state = CreateState();

        EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null, spec => spec.PlayersAllowed ? ["p2"] : [],
                Random: new Random(1)));

        // The friendly "must be a valid candidate" validation now lives
        // in GamesController.ResolvePendingChoice - the real trust
        // boundary, checked before Resolve is ever called. This just
        // confirms Resolve itself still fails safe (rather than silently
        // accepting a bogus id) if that guard were ever bypassed.
        Assert.NotNull(state.PendingChoice);
        Assert.Throws<InvalidOperationException>(() => state.PendingChoice!.Resolve(["not-a-real-die-id"]));
    }

    // DrawAndChooseOneToRoll - Gambit's own burst bonus ("draw 2 dice,
    // Roll one and return the other to your bag"), the same "draw N
    // random, pause for a real choice among what was actually drawn"
    // shape as Corrupt just above, just rolling-and-keeping the chosen
    // one instead of sending it to the Used Pile.
    [Fact]
    public void DrawAndChooseOneToRoll_DrawsTwoDice_RollsTheChosenOne_ReturnsTheOtherToTheBag()
    {
        var state = CreateState();
        Assert.Equal(8, state.DiceIn("p1", Zone.Bag).Count());

        EffectInterpreter.Execute(
            new DrawAndChooseOneToRoll(2),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Roller: new FixedRoller(DieStatus.SidekickCharacter, 1)));

        Assert.NotNull(state.PendingChoice);
        Assert.False(state.PendingChoice!.AllowMultiple);
        Assert.Equal(2, state.PendingChoice.CandidateDieIds.Count);

        var chosenId = state.PendingChoice.CandidateDieIds[0];
        state.PendingChoice.Resolve([chosenId]);

        var chosen = state.Dice.Single(d => d.Id == chosenId);
        Assert.Equal(Zone.ReservePool, chosen.Zone);
        Assert.Equal(DieStatus.SidekickCharacter, chosen.Status); // actually rolled, not left unrolled
        Assert.Equal(7, state.DiceIn("p1", Zone.Bag).Count()); // 8 - 2 drawn + 1 returned
        Assert.Empty(state.DiceIn("p1", Zone.DiceFromBag)); // nothing left staged mid-effect
    }

    [Fact]
    public void DrawAndChooseOneToRoll_DrawingOnlyOneDie_SkipsTheChoiceAndRollsItDirectly()
    {
        var state = CreateState();
        // Leave p1 with exactly 1 die reachable at all (bag + Used Pile
        // combined) - drawing 2 can only actually draw 1.
        foreach (var d in state.DiceIn("p1", Zone.Bag).Skip(1).ToList())
            d.Zone = Zone.ReservePool;

        EffectInterpreter.Execute(
            new DrawAndChooseOneToRoll(2),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Roller: new FixedRoller(DieStatus.SidekickCharacter, 1)));

        Assert.Null(state.PendingChoice); // no real choice among which
        Assert.Single(state.DiceIn("p1", Zone.ReservePool), d => d.Status == DieStatus.SidekickCharacter);
    }

    [Fact]
    public void DrawAndChooseOneToRoll_NoDiceAnywhereToDraw_NoOp()
    {
        var state = CreateState();
        foreach (var d in state.DiceFor("p1").ToList()) d.Zone = Zone.ReservePool; // neither Bag nor Used Pile has anything

        EffectInterpreter.Execute(
            new DrawAndChooseOneToRoll(2),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Roller: new FixedRoller(DieStatus.SidekickCharacter, 1)));

        Assert.Null(state.PendingChoice);
    }

    // Gambit end-to-end, both branches.
    [Fact]
    public void Gambit_WhenFieldedOnSingleBurstFace_RaisesTheRealChoice()
    {
        var state = CreateState();
        var gambit = new DieInstance
        {
            Id = "p1-gambit-1", CardId = SampleCards.GambitAceInTheHole.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1, // single-burst face
        };
        state.Dice.Add(gambit);

        var ability = SampleCards.GambitAceInTheHole.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "p1", gambit.Id, _ => [], Roller: new FixedRoller(DieStatus.SidekickCharacter, 1)));

        Assert.NotNull(state.PendingChoice);
        Assert.Equal(2, state.PendingChoice!.CandidateDieIds.Count);
    }

    [Fact]
    public void Gambit_WhenFieldedOnBlankFace_JustDrawsOneDie_NoChoice()
    {
        var state = CreateState();
        var gambit = new DieInstance
        {
            Id = "p1-gambit-1", CardId = SampleCards.GambitAceInTheHole.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 3, // blank face - not single-burst
        };
        state.Dice.Add(gambit);
        var bagCountBefore = state.DiceIn("p1", Zone.Bag).Count();

        var ability = SampleCards.GambitAceInTheHole.Abilities.Single(a => a.Trigger == TriggerType.WhenFielded);
        EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(state, "p1", gambit.Id, _ => [], Roller: new FixedRoller(DieStatus.SidekickCharacter, 1)));

        Assert.Null(state.PendingChoice);
        Assert.Equal(bagCountBefore - 1, state.DiceIn("p1", Zone.Bag).Count());
    }

    // The property that matters most about GameState.PendingQueue: if two
    // abilities are queued together and the first one pauses, the second
    // must NOT run until the pause is answered - and must still run
    // afterward, in its original order. Mirrors the shape
    // GamesController.Drain actually uses (a shouldStop check against
    // state.PendingChoice) rather than testing AbilityQueue in isolation.
    [Fact]
    public void TwoQueuedAbilities_WhenTheFirstPauses_TheSecondWaitsThenRunsAfterResolving()
    {
        var state = CreateState();
        var life = state.PlayerOne.Life; // "p1" - both abilities' controller
        var queue = new AbilityQueue();
        queue.Enqueue(null, "p1", TriggerType.Global, new Corrupt(2, TargetSpec.Player("target player")));
        queue.Enqueue(null, "p1", TriggerType.Global, new LoseLife(3));

        void Drain() => queue.Drain(
            ability => EffectInterpreter.Execute(
                ability.Effect,
                new EffectContext(state, ability.ControllerId, ability.SourceDieId,
                    spec => spec.PlayersAllowed ? ["p2"] : [], Random: new Random(1))),
            shouldStop: () => state.PendingChoice is not null);

        Drain();

        // Corrupt paused - LoseLife must not have run yet, and is still
        // sitting in the queue for later.
        Assert.NotNull(state.PendingChoice);
        Assert.Equal(life, state.GetPlayer("p1").Life);
        Assert.Equal(1, queue.Count);

        var pending = state.PendingChoice!;
        state.PendingChoice = null;
        pending.Resolve([pending.CandidateDieIds[0]]);
        Drain();

        Assert.Null(state.PendingChoice);
        Assert.True(queue.IsEmpty);
        Assert.Equal(life - 3, state.GetPlayer("p1").Life); // LoseLife finally ran
    }

    // Ronan the Accuser ("Treason!", DPS050) - "your opponent loses 1
    // life" needed LoseLife.Whose (every prior LoseLife card only ever
    // meant the ability's own controller, the default). Confirms Whose:
    // Opposing debits the other player, not the ability's controller.
    [Fact]
    public void LoseLife_WithWhoseOpposing_DebitsTheOpponentNotTheController()
    {
        var state = CreateState();
        var controllerLife = state.GetPlayer("p1").Life;
        var opponentLife = state.GetPlayer("p2").Life;

        EffectInterpreter.Execute(
            new LoseLife(1, TargetOwnership.Opposing),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));

        Assert.Equal(controllerLife, state.GetPlayer("p1").Life);
        Assert.Equal(opponentLife - 1, state.GetPlayer("p2").Life);
    }

    // Cosmic Cube "Infinite Possibilities" / Rip Hunter "Navigate the
    // Sands of Time" - unlike Corrupt (an "outside Clear and Draw" draw,
    // rule 2.3.13 - rolls immediately), this one replaces dice that were
    // already part of Clear and Draw's own draw, so its replacements land
    // unrolled in DiceFromBag rather than rolling right away. Unlike
    // Corrupt, the candidates here DO already exist in a real zone before
    // Execute runs - but the player still can't answer this in the same
    // request as the trigger (no round-trip to see what was drawn first),
    // so this also always pauses via PendingChoice, ignoring
    // ctx.ResolveTargets entirely (see EffectNode.RedrawFromBag's case in
    // EffectInterpreter for why).
    [Fact]
    public void RedrawFromBag_MovesChosenDiceAndDrawsAReplacementForEach()
    {
        var state = CreateState();
        var drawnThisTurn = state.DiceIn("p1", Zone.Bag).Take(3).ToList();
        foreach (var d in drawnThisTurn) d.Zone = Zone.DiceFromBag;
        var (keep, discard1, discard2) = (drawnThisTurn[0], drawnThisTurn[1], drawnThisTurn[2]);

        EffectInterpreter.Execute(
            new RedrawFromBag(
                TargetSpec.AnyDie(
                    "dice drawn this turn", TargetOwnership.Own, [Zone.DiceFromBag, Zone.DiceFromPrep], count: 10,
                    optional: true),
                Zone.OutOfPlay),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));

        Assert.NotNull(state.PendingChoice);
        Assert.True(state.PendingChoice!.AllowMultiple);
        Assert.Equal(3, state.PendingChoice.CandidateDieIds.Count);
        state.PendingChoice.Resolve([discard1.Id, discard2.Id]);

        Assert.Equal(Zone.OutOfPlay, discard1.Zone);
        Assert.Equal(Zone.OutOfPlay, discard2.Zone);
        Assert.Equal(Zone.DiceFromBag, keep.Zone); // not chosen - untouched
        // keep, plus one freshly-drawn replacement per die sent Out of Play.
        Assert.Equal(3, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void RedrawFromBag_ChoosingNone_DrawsNoReplacements()
    {
        var state = CreateState();
        var drawnThisTurn = state.DiceIn("p1", Zone.Bag).Take(2).ToList();
        foreach (var d in drawnThisTurn) d.Zone = Zone.DiceFromBag;
        var bagCountBefore = state.DiceIn("p1", Zone.Bag).Count();

        EffectInterpreter.Execute(
            new RedrawFromBag(
                TargetSpec.AnyDie(
                    "dice drawn this turn", TargetOwnership.Own, [Zone.DiceFromBag, Zone.DiceFromPrep], count: 10,
                    optional: true),
                Zone.OutOfPlay),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));

        Assert.NotNull(state.PendingChoice);
        state.PendingChoice!.Resolve([]); // "you may send any number of them" - zero is a legal choice

        Assert.Equal(2, state.DiceIn("p1", Zone.DiceFromBag).Count()); // both still there, untouched
        Assert.Equal(bagCountBefore, state.DiceIn("p1", Zone.Bag).Count()); // no draw happened
    }

    [Fact]
    public void RedrawFromBag_ToUsedPile_ResetsToUnrolled_ButOutOfPlayDoesNot()
    {
        var state = CreateState();
        var toOutOfPlay = state.DiceIn("p1", Zone.Bag).ElementAt(0);
        var toUsedPile = state.DiceIn("p1", Zone.Bag).ElementAt(1);
        toOutOfPlay.Zone = Zone.DiceFromBag;
        // Fake some stale rolled data to prove a reset actually clears it.
        toOutOfPlay.Status = DieStatus.Character;
        toOutOfPlay.Level = 3;
        toUsedPile.Status = DieStatus.Character;
        toUsedPile.Level = 3;

        // Staged one at a time so each Execute call's candidate set is
        // exactly the one die under test.
        var spec = TargetSpec.AnyDie(
            "x", TargetOwnership.Own, [Zone.DiceFromBag, Zone.DiceFromPrep], count: 10, optional: true);
        EffectInterpreter.Execute(
            new RedrawFromBag(spec, Zone.OutOfPlay),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));
        state.PendingChoice!.Resolve([toOutOfPlay.Id]);
        state.PendingChoice = null;

        toUsedPile.Zone = Zone.DiceFromBag;
        EffectInterpreter.Execute(
            new RedrawFromBag(spec, Zone.UsedPile),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));
        state.PendingChoice!.Resolve([toUsedPile.Id]);

        Assert.Equal(DieStatus.Character, toOutOfPlay.Status); // Out of Play isn't dormant - left alone
        Assert.Equal(DieStatus.Unrolled, toUsedPile.Status); // Used Pile is dormant - reset
    }

    // Keyword Intimidate - "remove target opposing Character die from the
    // Field Zone until end of turn." Just MoveDie targeting the new
    // Zone.Intimidated - see TurnEngine.CleanUp for the return half.
    [Fact]
    public void ScarletSpider_WhenFielded_MovesTheOpposingTargetToIntimidated()
    {
        var state = CreateState();
        var target = FieldSidekickTarget(state, "p2");

        var ability = SampleCards.ScarletSpider.Abilities.Single();
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", "p1-scarletspider-1", _ => [target.Id]));

        Assert.Equal(Zone.Intimidated, target.Zone);
        Assert.Equal(DieStatus.SidekickCharacter, target.Status); // untouched - not a dormant zone
    }

    [Fact]
    public void NeedsTarget_IsTrueForAGlobalWithARealTarget_FalseForOneWithout()
    {
        // Distraction's Global picks a specific attacking character die -
        // a real target the caller has to choose.
        var distractionGlobal = SampleCards.Distraction.Abilities.Single(a => a.Trigger == TriggerType.Global);
        Assert.True(EffectInterpreter.NeedsTarget(distractionGlobal.Effect));

        // Falcon's Global ("each player must field a Sidekick... if able")
        // has no chooser-selected target at all - see FieldSidekickForEachPlayer.
        var falconGlobal = SampleCards.Falcon.Abilities.Single(a => a.Trigger == TriggerType.Global);
        Assert.False(EffectInterpreter.NeedsTarget(falconGlobal.Effect));
    }

    private static readonly CardDef AwakenCard = new()
    {
        Id = "test-awaken", Name = "Test Awaken", Type = CardType.Character, PurchaseCost = 2, DieLimit = 4,
        Keywords = [new KeywordInstance("Awaken")],
        Abilities = [new AbilityDef(TriggerType.Awaken, Cost: null, Effect: new GainLife(1))],
        Levels = [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 3),
        ],
    };

    private static DieInstance AddAwakenDie(GameState state, string playerId, int level)
    {
        var die = new DieInstance
        {
            Id = $"{playerId}-awaken-1", CardId = AwakenCard.Id, OwnerId = playerId, ControllerId = playerId,
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = level,
        };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void Spin_UpOneLevel_MovesTheDieUpAndClampsAtCardMax()
    {
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [AwakenCard.Id] = AwakenCard };
        var state = CreateState(catalog);
        var die = AddAwakenDie(state, "p1", level: 3); // already at the card's max (3 levels)

        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "p1", die.Id, _ => []));

        Assert.Equal(3, die.Level); // "if able" - clamped, not an error
    }

    // The "character face" edge case: Level is only meaningful once a die
    // is actually on a character face (rule 1.6.8-adjacent) - spinning a
    // die that's currently on an energy/action face shouldn't silently
    // rewrite its stale Level, and can't sensibly trigger Awaken either.
    [Fact]
    public void Spin_OnADieNotOnACharacterFace_IsANoOpAndDoesNotTriggerAwaken()
    {
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [AwakenCard.Id] = AwakenCard };
        var state = CreateState(catalog);
        var die = AddAwakenDie(state, "p1", level: 1);
        die.Status = DieStatus.Energy; // rolled onto an energy face, not a character face

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "p1", die.Id, _ => [], Queue: queue));

        Assert.Equal(1, die.Level); // unchanged
        Assert.Equal(0, queue.Count); // Awaken never fires for a non-move
    }

    [Fact]
    public void Spin_TriggersAwaken_WhenDieActuallyMovesUpAndHasTheKeyword()
    {
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [AwakenCard.Id] = AwakenCard };
        var state = CreateState(catalog);
        var die = AddAwakenDie(state, "p1", level: 1);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "p1", die.Id, _ => [], Queue: queue));

        Assert.Equal(2, die.Level);
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Awaken, queue.Pending[0].Trigger);
        Assert.Equal(die.Id, queue.Pending[0].SourceDieId);
    }

    [Fact]
    public void Spin_DoesNotTriggerAwaken_WhenAlreadyAtMaxLevel()
    {
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [AwakenCard.Id] = AwakenCard };
        var state = CreateState(catalog);
        var die = AddAwakenDie(state, "p1", level: 3); // max level - spin up is a no-op

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "p1", die.Id, _ => [], Queue: queue));

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Spin_DoesNotTriggerAwaken_WhenSpinningDown()
    {
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [AwakenCard.Id] = AwakenCard };
        var state = CreateState(catalog);
        var die = AddAwakenDie(state, "p1", level: 3);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, -1),
            new EffectContext(state, "p1", die.Id, _ => [], Queue: queue));

        Assert.Equal(2, die.Level);
        Assert.Equal(0, queue.Count); // Awaken only reacts to spinning UP
    }

    [Fact]
    public void Cyclops_Awaken_DealsThreeDamage_WhenSpunUpAndDrained()
    {
        var state = CreateState();
        var cyclops = new DieInstance
        {
            Id = "p1-cyclops-1", CardId = SampleCards.Cyclops.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(cyclops);
        var target = FieldSidekickTarget(state, "p2"); // 1D - lethal to Cyclops's 3 damage

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "p1", cyclops.Id, _ => [], Queue: queue));

        Assert.Equal(1, queue.Count);
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [target.Id])));

        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd by Awaken's 3 damage
    }

    // TurnEngine.ResolveKOReactions - the shared choke point every KO
    // site now funnels reactions through. Before this existed, an
    // ability-driven KO (this Ko node) never fired WhenKOd/Retaliation
    // at all - only combat-damage KOs did, since CombatEngine had its
    // own hand-wired copy of that logic and nothing else did.
    private static readonly CardDef KOdReactorCard = new()
    {
        Id = "test-kod-reactor", Name = "Test WhenKOd Reactor", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 5)],
        Abilities = [new AbilityDef(TriggerType.WhenKOd, Cost: null, Effect: new LoseLife(1, TargetOwnership.Opposing))],
    };

    [Fact]
    public void Ko_NowFiresWhenKOdViaTheSharedReactionPath()
    {
        var catalog = new Dictionary<string, CardDef> { [KOdReactorCard.Id] = KOdReactorCard };
        var state = CreateState(catalog);
        var target = new DieInstance
        {
            Id = "p2-reactor-1", CardId = KOdReactorCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Queue: queue));

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.WhenKOd, queue.Pending[0].Trigger);
        Assert.Equal(target.Id, queue.Pending[0].SourceDieId);
    }

    // TriggerType.WhenAnotherDieKOd / KOdDieMatch - Magneto's own shape
    // ("when one of YOUR Mask character dice is KO'd") as the test
    // fixture: Ownership.Own + RequiredEnergyType.Mask.
    private static readonly CardDef MaskReactorCard = new()
    {
        Id = "test-mask-reactor", Name = "Test Mask Reactor", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
        Abilities = [new AbilityDef(
            TriggerType.WhenAnotherDieKOd, Cost: null, Effect: new GrantLoyaltyCounter(),
            KOdFilter: new KOdDieMatch(TargetOwnership.Own, RequiredEnergyType: EnergyType.Mask))],
    };

    private static readonly CardDef MaskCharacterCard = new()
    {
        Id = "test-mask-character", Name = "Test Mask Character", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4, EnergyTypes = [EnergyType.Mask],
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
    };

    private static readonly CardDef FistCharacterCard = new()
    {
        Id = "test-fist-character", Name = "Test Fist Character", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4, EnergyTypes = [EnergyType.Fist],
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
    };

    [Fact]
    public void WhenAnotherDieKOd_MatchingOwnMaskDieKOd_FiresAndGrantsLoyaltyCounter()
    {
        var catalog = new Dictionary<string, CardDef>
        {
            [MaskReactorCard.Id] = MaskReactorCard, [MaskCharacterCard.Id] = MaskCharacterCard,
        };
        var state = CreateState(catalog);
        var reactor = new DieInstance
        {
            Id = "p1-reactor-1", CardId = MaskReactorCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var target = new DieInstance
        {
            Id = "p1-mask-1", CardId = MaskCharacterCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(reactor);
        state.Dice.Add(target);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Queue: queue));

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.WhenAnotherDieKOd, queue.Pending[0].Trigger);
        Assert.Equal(reactor.Id, queue.Pending[0].SourceDieId);

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));
        Assert.Equal(1, state.LoyaltyCounters[MaskReactorCard.Id]);
    }

    [Fact]
    public void WhenAnotherDieKOd_WrongEnergyType_DoesNotFire()
    {
        var catalog = new Dictionary<string, CardDef>
        {
            [MaskReactorCard.Id] = MaskReactorCard, [FistCharacterCard.Id] = FistCharacterCard,
        };
        var state = CreateState(catalog);
        state.Dice.Add(new DieInstance
        {
            Id = "p1-reactor-1", CardId = MaskReactorCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        });
        var target = new DieInstance
        {
            Id = "p1-fist-1", CardId = FistCharacterCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Queue: queue));

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void WhenAnotherDieKOd_OpponentsMaskDieKOd_DoesNotFire_OwnershipIsOwn()
    {
        var catalog = new Dictionary<string, CardDef>
        {
            [MaskReactorCard.Id] = MaskReactorCard, [MaskCharacterCard.Id] = MaskCharacterCard,
        };
        var state = CreateState(catalog);
        state.Dice.Add(new DieInstance
        {
            Id = "p1-reactor-1", CardId = MaskReactorCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        });
        var target = new DieInstance
        {
            // p2's Mask die, not p1's - the reactor's Ownership.Own filter should exclude it.
            Id = "p2-mask-1", CardId = MaskCharacterCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Queue: queue));

        Assert.Equal(0, queue.Count);
    }

    // Madelyne Pryor's own shape - AffiliationContains + ExcludeSelf
    // together (the two KOdDieMatch fields Magneto's own fixture above
    // doesn't exercise).
    private static readonly CardDef BrotherhoodReactorCard = new()
    {
        Id = "test-brotherhood-reactor", Name = "Test Brotherhood Reactor", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4, Affiliations = ["Brotherhood of Mutants"],
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
        Abilities = [new AbilityDef(
            TriggerType.WhenAnotherDieKOd, Cost: null, Effect: new GrantLoyaltyCounter(),
            KOdFilter: new KOdDieMatch(TargetOwnership.Own, AffiliationContains: "Brotherhood of Mutants", ExcludeSelf: true))],
    };

    private static readonly CardDef SecondBrotherhoodCard = new()
    {
        Id = "test-brotherhood-ally", Name = "Test Brotherhood Ally", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4, Affiliations = ["Brotherhood of Mutants"],
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
    };

    [Fact]
    public void WhenAnotherDieKOd_ExcludeSelf_OwnDeathDoesNotTriggerItsOwnReaction()
    {
        var catalog = new Dictionary<string, CardDef> { [BrotherhoodReactorCard.Id] = BrotherhoodReactorCard };
        var state = CreateState(catalog);
        var reactor = new DieInstance
        {
            Id = "p1-reactor-1", CardId = BrotherhoodReactorCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(reactor);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [reactor.Id], Queue: queue));

        Assert.Equal(0, queue.Count); // "besides Madelyne Pryor" - its own death isn't "another die"
    }

    [Fact]
    public void WhenAnotherDieKOd_AffiliatedAllyKOd_Fires()
    {
        var catalog = new Dictionary<string, CardDef>
        {
            [BrotherhoodReactorCard.Id] = BrotherhoodReactorCard, [SecondBrotherhoodCard.Id] = SecondBrotherhoodCard,
        };
        var state = CreateState(catalog);
        var reactor = new DieInstance
        {
            Id = "p1-reactor-1", CardId = BrotherhoodReactorCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var ally = new DieInstance
        {
            Id = "p1-ally-1", CardId = SecondBrotherhoodCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(reactor);
        state.Dice.Add(ally);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [ally.Id], Queue: queue));

        Assert.Equal(1, queue.Count);
        Assert.Equal(reactor.Id, queue.Pending[0].SourceDieId);
    }

    // The three real WhenAnotherDieKOd cards, end-to-end against
    // SampleCards' own data (catches an authoring/wiring mistake the
    // synthetic-fixture tests above wouldn't, since those hand-build
    // their own KOdDieMatch rather than exercising a real CardDef).
    [Fact]
    public void Magneto_YourMaskCharacterDieKOd_GrantsLoyaltyCounter()
    {
        var state = CreateState();
        var magneto = new DieInstance
        {
            Id = "p1-magneto-1", CardId = SampleCards.Magneto.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var target = new DieInstance
        {
            Id = "p1-mask-1", CardId = SampleCards.KittyPrydeRightOfPassage.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        }; // Kitty Pryde "Right of Passage" - a real Mask-energy character die
        state.Dice.Add(magneto);
        state.Dice.Add(target);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Queue: queue));
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(1, state.LoyaltyCounters[SampleCards.Magneto.Id]);
    }

    [Fact]
    public void SupremeIntelligence_AnyCardNamedWithKree_GrantsLoyaltyCounter()
    {
        var kreeCard = new CardDef
        {
            Id = "test-kree-character", Name = "Kree Sentry", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
        };
        var catalog = new Dictionary<string, CardDef>(SampleCards.BuildCatalog()) { [kreeCard.Id] = kreeCard };
        var state = CreateState(catalog);
        var supremeIntelligence = new DieInstance
        {
            Id = "p1-si-1", CardId = SampleCards.SupremeIntelligence.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var target = new DieInstance
        {
            // owned by the OPPONENT - Supreme Intelligence's own filter has no Ownership restriction.
            Id = "p2-kree-1", CardId = kreeCard.Id, OwnerId = "p2", ControllerId = "p2",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(supremeIntelligence);
        state.Dice.Add(target);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id], Queue: queue));
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => [])));

        Assert.Equal(1, state.LoyaltyCounters[SampleCards.SupremeIntelligence.Id]);
    }

    [Fact]
    public void MadelynePryor_HerOwnDeath_DoesNotGrantHerOwnLoyaltyCounter()
    {
        var state = CreateState();
        var madelyne = new DieInstance
        {
            Id = "p1-madelyne-1", CardId = SampleCards.MadelynePryorSisterhood.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(madelyne);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("t")),
            new EffectContext(state, "p1", SourceDieId: null, _ => [madelyne.Id], Queue: queue));

        Assert.Equal(0, queue.Count);
        Assert.False(state.LoyaltyCounters.ContainsKey(SampleCards.MadelynePryorSisterhood.Id));
    }

    // Regression: TurnEngine.CheckAwaken/CheckEnergize both gate on
    // DieStats.HasKeyword(state, die, "Awaken"/"Energize") - Kitty Pryde
    // and Phoenix were both authored with a real Awaken/Energize
    // AbilityDef but no matching entry in Keywords, so neither would
    // ever actually fire through the real trigger path (only a test
    // that enqueues the trigger directly, bypassing the gate, would
    // have missed this - these two exercise the real gated path).
    [Fact]
    public void KittyPryde_SpinningUp_ActuallyTriggersAwaken_ViaTheRealKeywordGate()
    {
        var state = CreateState();
        var kittyPryde = new DieInstance
        {
            Id = "p1-kittypryde-1", CardId = SampleCards.KittyPrydeRightOfPassage.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(kittyPryde);

        var queue = new AbilityQueue();
        EffectInterpreter.Execute(
            new Spin(TargetSpec.Self, +1),
            new EffectContext(state, "p1", kittyPryde.Id, _ => [], Queue: queue));

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Awaken, queue.Pending[0].Trigger);
    }

    [Fact]
    public void Phoenix_RolledOnDoubleEnergy_ActuallyTriggersEnergize_ViaTheRealKeywordGate()
    {
        var state = CreateState();
        state.CurrentStep = TurnStep.RollAndReroll;
        var phoenix = new DieInstance
        {
            Id = "p1-phoenix-1", CardId = SampleCards.PhoenixFirepower.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Energy, EnergyKind = EnergyKind.Generic, EnergyAmount = 2,
        };
        state.Dice.Add(phoenix);

        var queue = new AbilityQueue();
        // Same shape as BlackPantherEnergize's own test - reroll nothing,
        // so Phoenix stays on its double-energy face and CheckEnergize
        // (invoked internally at the end of Roll and Reroll) sees it.
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);
    }

    // Blanket regression for the whole bug class Kitty Pryde/Phoenix
    // found: TriggerType.Energize/Awaken/Teamwatch are only ever fired
    // by TurnEngine.CheckEnergize/CheckAwaken/Field's Teamwatch scan,
    // ALL of which gate on DieStats.HasKeyword - an AbilityDef alone,
    // with no matching Keywords entry, silently never fires. Checks
    // every card in the real catalog at once rather than relying on a
    // hand-picked end-to-end test per card to catch the next one.
    [Theory]
    [InlineData(TriggerType.Energize, "Energize")]
    [InlineData(TriggerType.Awaken, "Awaken")]
    [InlineData(TriggerType.Teamwatch, "Teamwatch")]
    public void EveryCardWithThisTrigger_HasTheMatchingKeyword(TriggerType trigger, string keywordName)
    {
        var catalog = SampleCards.BuildCatalog();
        var missing = catalog.Values
            .Where(c => c.Abilities.Any(a => a.Trigger == trigger))
            .Where(c => !c.Keywords.Any(k => k.Name == keywordName))
            .Select(c => c.Id)
            .ToList();

        Assert.Empty(missing);
    }

    // Colossus's own Energize - the riskiest of this batch: FieldDie and
    // Spin are two SEPARATE EffectNodes in a Sequence, sharing one
    // TargetSpec instance (SampleCards.ColossusEnergizeTarget) so they
    // resolve to the same die. Confirms that actually holds - the
    // fielded die ends up at level 3 (1 from FieldDie's own always-
    // level-1 fielding, +2 from Spin), not some other Reserve Pool
    // character die and not left at level 1.
    [Fact]
    public void Colossus_Energize_FieldsAndSpinsTheSameDieToLevelThree()
    {
        var state = CreateState();
        var colossus = new DieInstance
        {
            Id = "p1-colossus-1", CardId = SampleCards.ColossusSkilledPainter.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        var target = new DieInstance
        {
            Id = "p1-target-1", CardId = SampleCards.ColossusSkilledPainter.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        // A second Reserve Pool character die - if the Sequence's two
        // clauses ever resolved their shared TargetSpec independently,
        // this is what would let them disagree on which die to use.
        var decoy = new DieInstance
        {
            Id = "p1-decoy-1", CardId = SampleCards.ColossusSkilledPainter.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(colossus);
        state.Dice.Add(target);
        state.Dice.Add(decoy);

        EffectInterpreter.Execute(
            new Sequence([
                new FieldDie(SampleCards.ColossusEnergizeTarget, Free: true),
                new Spin(SampleCards.ColossusEnergizeTarget, +2)
            ]),
            new EffectContext(state, "p1", colossus.Id, _ => [target.Id]));

        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.Equal(3, target.Level);
        Assert.Equal(Zone.ReservePool, decoy.Zone); // untouched
        Assert.Equal(1, decoy.Level);
    }

    // Toad's Teamwatch - via the real TurnEngine.Field path (which scans
    // for HasKeyword("Teamwatch"), not a direct enqueue), same "real
    // gate, not just a hand-fired trigger" standard as the Kitty Pryde/
    // Phoenix tests above.
    [Fact]
    public void Toad_FieldingAnotherBrotherhoodCharacter_TriggersTeamwatch_ViaTheRealField()
    {
        var state = CreateState();
        var toad = new DieInstance
        {
            Id = "p1-toad-1", CardId = SampleCards.ToadSecondaryMutation.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        // Madelyne Pryor - a real Brotherhood of Mutants character, ready
        // to field for free at level 1 (fielding cost 0).
        var fielded = new DieInstance
        {
            Id = "p1-madelyne-1", CardId = SampleCards.MadelynePryorSisterhood.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(toad);
        state.Dice.Add(fielded);
        state.CurrentStep = TurnStep.Main;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, fielded.Id, energyDieIdsToSpend: []);

        Assert.Contains(queue.Pending, a => a.Trigger == TriggerType.Teamwatch && a.SourceDieId == toad.Id);
    }

    // Lilandra's Global - the CharacterOnly narrowing actually matters:
    // purchasing a Basic Action shouldn't satisfy it, only a character die.
    [Fact]
    public void Lilandra_PurchasedOnlyABasicAction_GlobalConditionNotMet()
    {
        var state = CreateState();
        state.GetPlayer("p1").PurchasedDieThisTurn = true; // purchased *something* this turn...
        state.GetPlayer("p1").PurchasedCharacterDieThisTurn = false; // ...but not a character die

        var bagCountBefore = state.DiceIn("p1", Zone.Bag).Count();
        EffectInterpreter.Execute(
            new PrepFromBagIfPurchasedThisTurn(CharacterOnly: true),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));

        Assert.Equal(bagCountBefore, state.DiceIn("p1", Zone.Bag).Count()); // nothing drawn
    }

    [Fact]
    public void Lilandra_PurchasedACharacterDie_GlobalDrawsAndPreps()
    {
        var state = CreateState();
        state.GetPlayer("p1").PurchasedCharacterDieThisTurn = true;

        EffectInterpreter.Execute(
            new PrepFromBagIfPurchasedThisTurn(CharacterOnly: true),
            new EffectContext(state, "p1", SourceDieId: null, _ => [], Random: new Random(1)));

        Assert.Single(state.DiceIn("p1", Zone.PrepArea));
    }

    // Vulcan's Global itself, end-to-end - ForceAttack flags the target
    // in GameState.MustAttackThisTurn (enforcement is CombatEngine.
    // DeclareAttackers/TurnEngine.SkipAttackStep's job, covered there).
    [Fact]
    public void Vulcan_Global_FlagsTargetAsMustAttackThisTurn()
    {
        var state = CreateState();
        var target = new DieInstance
        {
            Id = "p1-target-1", CardId = SampleCards.VulcanRulerOfTheImperium.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(target);

        var ability = SampleCards.VulcanRulerOfTheImperium.Abilities.Single(a => a.Trigger == TriggerType.Global);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "p1", SourceDieId: null, _ => [target.Id]));

        Assert.Contains(target.Id, state.MustAttackThisTurn);
    }

    // Toad "Looking for Comradery" - the -2-always-reaches-level-1 trick
    // (see SampleCards.ToadLookingForComradery's own remarks). Checked
    // from two different starting levels to confirm it's a real clamp to
    // exactly 1, not just a fixed -2 offset.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ToadLookingForComradery_Energize_SpinsAnyStartingLevelDownToExactlyOne(int startingLevel)
    {
        var state = CreateState();
        var toad = new DieInstance
        {
            Id = "p1-toad-1", CardId = SampleCards.ToadLookingForComradery.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Energy, EnergyAmount = 2,
        };
        var target = new DieInstance
        {
            Id = "p1-target-1", CardId = SampleCards.ToadLookingForComradery.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = startingLevel,
        };
        state.Dice.Add(toad);
        state.Dice.Add(target);

        var ability = SampleCards.ToadLookingForComradery.Abilities.Single(a => a.Trigger == TriggerType.Energize);
        EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, "p1", toad.Id, _ => [target.Id]));

        Assert.Equal(1, target.Level);
    }

    // Blob "MGH Dependent" - two separate AbilityDefs on the same
    // WhenFielded trigger (the card's own "lose 1 life" plus Intimidate's
    // built-in effect) both actually enqueue off one real Field call.
    [Fact]
    public void Blob_WhenFielded_BothAbilitiesFire()
    {
        var state = CreateState();
        var blob = new DieInstance
        {
            Id = "p1-blob-1", CardId = SampleCards.BlobMGHDependent.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(blob);
        state.CurrentStep = TurnStep.Main;

        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, blob.Id, energyDieIdsToSpend: []);

        Assert.Equal(2, queue.Count);
        Assert.All(queue.Pending, a => Assert.Equal(TriggerType.WhenFielded, a.Trigger));
    }

    // EffectCondition.OnSingleBurstFace/OnDoubleBurstFace - the sheet's
    // "*"/"**" ability-text marks, now correctly understood as "some
    // abilities key off rolling that face" (a face-conditional bonus tied
    // to CharacterFace.BurstStars) rather than the earlier assumption
    // this session had (a different printing's separate text). Level 1 =
    // blank, level 2 = single burst, level 3 = double burst - a clean
    // fixture covering all three face states.
    private static readonly CardDef BurstFaceCard = new()
    {
        Id = "test-burst-face", Name = "Test Burst Face Character", Type = CardType.Character,
        PurchaseCost = 2, DieLimit = 4,
        Levels =
        [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1, BurstStars: null),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 3, BurstStars: 2),
        ],
    };

    [Theory]
    [InlineData(1, false)] // blank face
    [InlineData(2, true)] // single-burst face
    [InlineData(3, false)] // double-burst face
    public void OnSingleBurstFace_OnlyTrueOnTheSingleBurstLevel(int level, bool expected)
    {
        var state = CreateState(new Dictionary<string, CardDef> { [BurstFaceCard.Id] = BurstFaceCard });
        var die = new DieInstance
        {
            Id = "p1-burst-1", CardId = BurstFaceCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = level,
        };
        state.Dice.Add(die);

        // Rule 1.1.3 - life gain never exceeds StartingLife (20), so
        // GainLife needs headroom to actually move the needle here.
        state.GetPlayer("p1").Life = 10;
        var life = state.GetPlayer("p1").Life;
        EffectInterpreter.Execute(
            new Conditional(TargetSpec.Self, EffectCondition.OnSingleBurstFace, new GainLife(1)),
            new EffectContext(state, "p1", die.Id, _ => []));

        Assert.Equal(expected, state.GetPlayer("p1").Life > life);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)] // double-burst face only
    public void OnDoubleBurstFace_OnlyTrueOnTheDoubleBurstLevel(int level, bool expected)
    {
        var state = CreateState(new Dictionary<string, CardDef> { [BurstFaceCard.Id] = BurstFaceCard });
        var die = new DieInstance
        {
            Id = "p1-burst-1", CardId = BurstFaceCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = level,
        };
        state.Dice.Add(die);

        state.GetPlayer("p1").Life = 10;
        var life = state.GetPlayer("p1").Life;
        EffectInterpreter.Execute(
            new Conditional(TargetSpec.Self, EffectCondition.OnDoubleBurstFace, new GainLife(1)),
            new EffectContext(state, "p1", die.Id, _ => []));

        Assert.Equal(expected, state.GetPlayer("p1").Life > life);
    }

    // Conditional.Else - Gambit's own "Instead" shape ("you may draw and
    // roll a die. * Instead, [bonus]"), the first real use of the new
    // Else branch. GainLife(1) vs LoseLife(1) makes Then/Else trivially
    // distinguishable in a single assertion.
    [Fact]
    public void Conditional_WithElse_RunsThenWhenConditionHolds()
    {
        var state = CreateState(new Dictionary<string, CardDef> { [BurstFaceCard.Id] = BurstFaceCard });
        var die = new DieInstance
        {
            Id = "p1-burst-1", CardId = BurstFaceCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 2, // single-burst face
        };
        state.Dice.Add(die);

        state.GetPlayer("p1").Life = 10;
        var life = state.GetPlayer("p1").Life;
        EffectInterpreter.Execute(
            new Conditional(TargetSpec.Self, EffectCondition.OnSingleBurstFace, new GainLife(1), new LoseLife(1)),
            new EffectContext(state, "p1", die.Id, _ => []));

        Assert.Equal(life + 1, state.GetPlayer("p1").Life);
    }

    [Fact]
    public void Conditional_WithElse_RunsElseWhenConditionDoesNotHold()
    {
        var state = CreateState(new Dictionary<string, CardDef> { [BurstFaceCard.Id] = BurstFaceCard });
        var die = new DieInstance
        {
            Id = "p1-burst-1", CardId = BurstFaceCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1, // blank face - not single-burst
        };
        state.Dice.Add(die);

        var life = state.GetPlayer("p1").Life;
        EffectInterpreter.Execute(
            new Conditional(TargetSpec.Self, EffectCondition.OnSingleBurstFace, new GainLife(1), new LoseLife(1)),
            new EffectContext(state, "p1", die.Id, _ => []));

        Assert.Equal(life - 1, state.GetPlayer("p1").Life);
    }

    [Fact]
    public void Conditional_NoElseAndConditionDoesNotHold_DoesNothing()
    {
        var state = CreateState(new Dictionary<string, CardDef> { [BurstFaceCard.Id] = BurstFaceCard });
        var die = new DieInstance
        {
            Id = "p1-burst-1", CardId = BurstFaceCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(die);

        var life = state.GetPlayer("p1").Life;
        EffectInterpreter.Execute(
            new Conditional(TargetSpec.Self, EffectCondition.OnSingleBurstFace, new GainLife(1)), // no Else
            new EffectContext(state, "p1", die.Id, _ => []));

        Assert.Equal(life, state.GetPlayer("p1").Life);
    }

    // Rally end-to-end - the first burst-conditional Basic Action.
    // Rally's own die's BurstStars (not a Character die's Level-derived
    // face) drives which branch fires.
    private static (GameState state, DieInstance rally, List<DieInstance> sidekicks) CreateRallyGame(int? rallyBurstStars)
    {
        var state = CreateState();
        var rally = new DieInstance
        {
            Id = "p1-rally-1", CardId = SampleCards.Rally.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Action, BurstStars = rallyBurstStars,
        };
        state.Dice.Add(rally);
        var sidekicks = Enumerable.Range(0, 3).Select(i => new DieInstance
        {
            // "test-" prefix avoids colliding with GameState.NewGame's own
            // default "{playerId}-sidekick-{i}" starting-bag dice.
            Id = $"p1-test-sidekick-{i}", OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile,
        }).ToList();
        state.Dice.AddRange(sidekicks);
        return (state, rally, sidekicks);
    }

    [Fact]
    public void Rally_OnBlankFace_MovesAtMostTwoSidekicksEvenWithThreeAvailable()
    {
        var (state, rally, sidekicks) = CreateRallyGame(rallyBurstStars: null);
        var allThreeIds = sidekicks.Select(s => s.Id).ToList();

        var ability = SampleCards.Rally.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", rally.Id, _ => allThreeIds));

        Assert.Equal(2, state.DiceIn("p1", Zone.FieldZone).Count()); // capped at 2, not 3
    }

    [Fact]
    public void Rally_OnDoubleBurstFace_CanMoveAllThreeSidekicks()
    {
        var (state, rally, sidekicks) = CreateRallyGame(rallyBurstStars: 2);
        var allThreeIds = sidekicks.Select(s => s.Id).ToList();

        var ability = SampleCards.Rally.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", rally.Id, _ => allThreeIds));

        Assert.Equal(3, state.DiceIn("p1", Zone.FieldZone).Count());
    }

    // "Up to N" is a real voluntary choice (TargetSpec.Optional) - moving
    // zero is legal even when Sidekicks are available.
    [Fact]
    public void Rally_ChoosingNone_IsLegal()
    {
        var (state, rally, _) = CreateRallyGame(rallyBurstStars: null);

        var ability = SampleCards.Rally.Abilities.Single(a => a.Trigger == TriggerType.WhenUsed);
        EffectInterpreter.Execute(ability.Effect, new EffectContext(state, "p1", rally.Id, _ => []));

        Assert.Empty(state.DiceIn("p1", Zone.FieldZone));
    }
}
