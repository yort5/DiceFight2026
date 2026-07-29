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
    // bag untouched (see EffectNode.Corrupt's remarks on why the "choose"
    // step bypasses the normal cached Resolve/LegalTargets pipeline).
    [Fact]
    public void Corrupt_DrawsDiceAndSendsTheChosenOneToUsedPile_ReturnsTheRestToTheBag()
    {
        var state = CreateState();
        Assert.Equal(8, state.DiceIn("p2", Zone.Bag).Count());

        DieInstance? chosen = null;
        IReadOnlyList<string> Resolve(TargetSpec spec)
        {
            if (spec.PlayersAllowed) return ["p2"];
            chosen = state.DiceIn("p2", Zone.DiceFromBag).First(); // whichever 2 actually got drawn
            return [chosen.Id];
        }

        EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null, Resolve, Random: new Random(1)));

        Assert.NotNull(chosen);
        Assert.Equal(Zone.UsedPile, chosen!.Zone);
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
            new EffectContext(state, "p1", SourceDieId: null,
                spec => spec.PlayersAllowed ? ["p2"] : [state.DiceIn("p2", Zone.DiceFromBag).First().Id],
                Random: new Random(1)));

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

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            new Corrupt(2, TargetSpec.Player("target player")),
            new EffectContext(state, "p1", SourceDieId: null,
                spec => spec.PlayersAllowed ? ["p2"] : ["not-a-real-die-id"], Random: new Random(1))));

        Assert.Contains("must be one of", ex.Message);
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
}
