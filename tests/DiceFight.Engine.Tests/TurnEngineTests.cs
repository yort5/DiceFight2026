using DiceFight.Engine;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// A fake roller lets step tests be deterministic without modeling real
// physical die face tables yet (see TurnEngine.RolledFace remarks).
file sealed class FixedRoller(
    DieStatus status, int level, EnergyKind energyKind = EnergyKind.None,
    EnergyType? providedEnergyType = null, int EnergyAmount = 1)
    : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level, energyKind, providedEnergyType, EnergyAmount);
}

// Distinguishes "rerolled" from "kept as originally rolled" by giving each
// successive Roll() call, across the test's whole Roll+Reroll sequence, a
// strictly increasing Level - a die that gets rolled twice (Roll, then
// Reroll) ends up with a visibly different Level than one only ever
// rolled once.
file sealed class SequentialRoller : IDiceRoller
{
    private int _calls;
    public RolledFace Roll(DieInstance die, CardDef? card) => new(DieStatus.SidekickCharacter, ++_calls);
}

public class TurnEngineTests
{
    private static GameState CreateNewGame() =>
        GameState.NewGame(
            new Dictionary<string, CardDef>(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });

    [Fact]
    public void ClearAndDraw_FirstTurn_Draws3ToDiceFromBagAnd1OutOfPlay()
    {
        var state = CreateNewGame();

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(3, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Single(state.DiceIn("p1", Zone.OutOfPlay));
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count()); // 8 - 3 - 1
    }

    [Fact]
    public void ClearAndDraw_SubsequentTurn_Draws4ToDiceFromBag()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Empty(state.DiceIn("p1", Zone.OutOfPlay));
    }

    [Fact]
    public void ClearAndDraw_RefillsBagFromUsedPile_WhenBagRunsDry()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        // Empty the bag into the Used Pile except for 2 dice.
        var toStash = state.DiceIn("p1", Zone.Bag).Take(6).ToList();
        foreach (var die in toStash) die.Zone = Zone.UsedPile;

        TurnEngine.ClearAndDraw(state, new Random(1));

        // Still draws the full 4 by refilling from the Used Pile mid-draw:
        // 2 from the bag, then a refill from the 6-die Used Pile for the
        // other 2, leaving 4 of that refill in the bag.
        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count());
    }

    [Fact]
    public void ClearAndDraw_Shortfall_LosesLifeAndGainsVirtualEnergy()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        // Remove all but 2 dice entirely (simulate nothing left to draw).
        var toRemove = state.DiceIn("p1", Zone.Bag).Skip(2).ToList();
        foreach (var die in toRemove) state.Dice.Remove(die);

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(2, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Equal(18, state.PlayerOne.Life); // 20 - 2 short

        // Rule 1.4.4 - represented as a real spendable die in the Reserve
        // Pool, not a separate counter (see TurnEngine.AddVirtualGenericEnergy).
        var virtualDie = Assert.Single(state.DiceIn("p1", Zone.ReservePool));
        Assert.True(virtualDie.IsVirtualEnergy);
        Assert.Equal(EnergyKind.Generic, virtualDie.EnergyKind);
        Assert.Equal(2, virtualDie.EnergyAmount);
    }

    [Fact]
    public void ClearAndDraw_TriggersWhenDrawnForADieActuallyDrawnThisTurn()
    {
        var card = new CardDef
        {
            Id = "test-when-drawn", Name = "Test WhenDrawn", Type = CardType.BasicAction,
            PurchaseCost = 2, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.WhenDrawn, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        // Move every real Sidekick out of reach (neither Bag nor Used
        // Pile) so the only die left to draw is guaranteed to be this one.
        foreach (var d in state.DiceIn("p1", Zone.Bag).ToList()) d.Zone = Zone.ReservePool;
        var whenDrawnDie = new DieInstance
        {
            Id = "p1-whendrawn-1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(whenDrawnDie);

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Contains(whenDrawnDie.Id, state.DiceIn("p1", Zone.DiceFromBag).Select(d => d.Id));
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.WhenDrawn, queue.Pending[0].Trigger);
        Assert.Equal(whenDrawnDie.Id, queue.Pending[0].SourceDieId);
    }

    [Fact]
    public void ClearAndDraw_DoesNotTriggerWhenDrawn_ForADieLeftInTheBag()
    {
        var card = new CardDef
        {
            Id = "test-when-drawn", Name = "Test WhenDrawn", Type = CardType.BasicAction,
            PurchaseCost = 2, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.WhenDrawn, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        // 9 dice total for p1 (8 real Sidekicks + this one), only 4 get
        // drawn - not guaranteed to be this one, so just assert the
        // invariant: this die only ever ends up queued if it was actually
        // among the ones drawn.
        var whenDrawnDie = new DieInstance
        {
            Id = "p1-whendrawn-1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(whenDrawnDie);

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        var wasDrawn = state.DiceIn("p1", Zone.DiceFromBag).Any(d => d.Id == whenDrawnDie.Id);
        Assert.Equal(wasDrawn, queue.Count == 1);
    }

    [Fact]
    public void ClearAndDraw_OmittingTheQueue_StillDrawsNormally()
    {
        var card = new CardDef
        {
            Id = "test-when-drawn", Name = "Test WhenDrawn", Type = CardType.BasicAction,
            PurchaseCost = 2, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.WhenDrawn, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;
        state.Dice.Add(new DieInstance
        {
            Id = "p1-whendrawn-1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        });

        TurnEngine.ClearAndDraw(state, new Random(1)); // no queue supplied

        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void CosmicCubeInfinitePossibilities_WhenDrawn_CanSendDrawnDiceOutOfPlayAndRedraw()
    {
        var cube = SampleCards.CosmicCubeInfinitePossibilities;
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [cube.Id] = cube },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        // Guarantee the Cosmic Cube die is drawn without depending on RNG
        // order: leave exactly 3 Sidekicks in the bag alongside it (4
        // total = this turn's draw count, so all 4 get picked regardless
        // of order), and stash the other 5 in the Used Pile - untouched
        // for now, but reachable for the replacement draws later.
        var sidekicks = state.DiceIn("p1", Zone.Bag).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.UsedPile;
        var cubeDie = new DieInstance
        {
            Id = "p1-cosmiccube-1", CardId = cube.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(cubeDie);

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Equal(1, queue.Count);
        var drawnThisTurn = state.DiceIn("p1", Zone.DiceFromBag).ToList();

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId,
                _ => drawnThisTurn.Select(d => d.Id).ToList(), // send everything drawn this turn Out of Play
                Random: new Random(2))));

        Assert.All(drawnThisTurn, d => Assert.Equal(Zone.OutOfPlay, d.Zone));
        // One replacement per die sent Out of Play, landing unrolled in
        // DiceFromBag (not immediately rolled - see RedrawFromBag's remarks).
        Assert.Equal(drawnThisTurn.Count, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.All(state.DiceIn("p1", Zone.DiceFromBag), d => Assert.Equal(DieStatus.Unrolled, d.Status));
    }

    [Fact]
    public void ClearAndDraw_SweepsExistingPrepAreaDiceIntoDiceFromPrep()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        // e.g. left there by a KO, or Shocking Grasp's own Conditional Prep,
        // sometime before this player's turn came back around.
        var leftover = state.DiceIn("p1", Zone.Bag).First();
        leftover.Zone = Zone.PrepArea;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
        Assert.Contains(leftover, state.DiceIn("p1", Zone.DiceFromPrep));
        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void Roll_RollsAndSettlesDiceStraightIntoReservePool()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        var leftover = state.DiceIn("p1", Zone.Bag).First();
        leftover.Zone = Zone.PrepArea;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state); // Main is skipped for this test's purposes; just move past ClearAndDraw
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.Roll(state, new FixedRoller(DieStatus.SidekickCharacter, 1));

        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.Equal(5, reserve.Count); // 4 fresh draws + the 1 carried over from the Prep Area
        Assert.Contains(leftover, reserve);
        Assert.All(reserve, d => Assert.Equal(DieStatus.SidekickCharacter, d.Status));
        Assert.Empty(state.DiceIn("p1", Zone.DiceFromBag));
        Assert.Empty(state.DiceIn("p1", Zone.DiceFromPrep));
    }

    [Fact]
    public void Roll_TrustsTheRollersEnergyKindAndType_ForEnergyFaces()
    {
        // A Sidekick rolling a specific-type energy face (not Wild) is
        // exactly the case ApplyRoll used to get wrong - it used to
        // hardcode every Sidekick energy face as Wild regardless of what
        // the roller actually rolled. Now it just trusts the roller.
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Bolt));

        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.All(reserve, d => Assert.Equal(EnergyKind.Specific, d.EnergyKind));
        Assert.All(reserve, d => Assert.Equal(EnergyType.Bolt, d.ProvidedEnergyType));
    }

    [Fact]
    public void Reroll_RerollsOnlySelectedDice_LeavesEveryoneElseAsRolled()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        var roller = new SequentialRoller();
        TurnEngine.Roll(state, roller);
        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        var toReroll = reserve[0];
        var levelsAfterRoll = reserve.ToDictionary(d => d.Id, d => d.Level);

        TurnEngine.Reroll(state, new AbilityQueue(), roller, [toReroll.Id]);

        Assert.NotEqual(levelsAfterRoll[toReroll.Id], toReroll.Level); // rerolled
        foreach (var die in reserve.Where(d => d.Id != toReroll.Id))
            Assert.Equal(levelsAfterRoll[die.Id], die.Level); // everyone else kept as originally rolled
        Assert.All(reserve, d => Assert.Equal(Zone.ReservePool, d.Zone)); // reroll doesn't move anyone

        // Rule 2.4.3/2.4.4 - the reroll decision is made once; nothing else
        // is legal in Roll & Reroll afterward, so it auto-advances to Main.
        Assert.Equal(TurnStep.Main, state.CurrentStep);
    }

    [Fact]
    public void Reroll_CanOnlyBeUsedOnce_EvenWithNoDiceSelected()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        var roller = new SequentialRoller();
        TurnEngine.Roll(state, roller);
        TurnEngine.Reroll(state, new AbilityQueue(), roller, []); // "I don't want to reroll anything" is still the one decision
        Assert.Equal(TurnStep.Main, state.CurrentStep);

        var ex = Assert.Throws<InvalidOperationException>(() => TurnEngine.Reroll(state, new AbilityQueue(), roller, []));
        Assert.Contains("RollAndReroll", ex.Message);
    }

    private static (GameState State, DieInstance Die) CreateEnergizeGame(int energyAmount)
    {
        var card = new CardDef
        {
            Id = "test-energize", Name = "Test Energize", Type = CardType.Character,
            PurchaseCost = 1, DieLimit = 1,
            Keywords = [new KeywordInstance("Energize")],
            Abilities = [new AbilityDef(TriggerType.Energize, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.RollAndReroll;

        var die = new DieInstance
        {
            Id = "energize-die", CardId = card.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Energy, EnergyKind = EnergyKind.Generic, EnergyAmount = energyAmount,
        };
        state.Dice.Add(die);
        return (state, die);
    }

    [Fact]
    public void Reroll_EnergizeDieLeftOnDoubleEnergy_TriggersOnceAtEndOfStep()
    {
        var (state, die) = CreateEnergizeGame(energyAmount: 2);
        var queue = new AbilityQueue();

        // Nothing selected to reroll - the die stays exactly as it was
        // rolled, so Energize should fire once the step closes.
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);
        Assert.Equal(die.Id, queue.Pending[0].SourceDieId);
    }

    [Fact]
    public void Reroll_EnergizeDieRerolledOffDoubleEnergy_DoesNotTrigger()
    {
        var (state, die) = CreateEnergizeGame(energyAmount: 2);
        var queue = new AbilityQueue();

        // Rerolling it lands on single energy this time - Energize checks
        // the step's final state, not the initial roll it's replacing.
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1, EnergyKind.Generic), [die.Id]);

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Reroll_EnergizeDieRerolledButStillDoubleEnergy_StillTriggersOnce()
    {
        var (state, die) = CreateEnergizeGame(energyAmount: 2);
        var queue = new AbilityQueue();

        var roller = new FixedRoller(DieStatus.Energy, 1, EnergyKind.Generic, EnergyAmount: 2);
        TurnEngine.Reroll(state, queue, roller, [die.Id]);

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Roll_LeavesDieSteppedIntoPrepAreaAfterThisSteps_ClearPhase_ForNextTurn()
    {
        // Models a card like Pepper Potts ("draw an extra die at the
        // beginning of your Clear and Draw Step... If it is a non-Sidekick
        // die, Prep it.") - a die that lands in the Prep Area *after* this
        // step's own Clear-phase sweep already ran must sit out this
        // turn's Roll & Reroll and only get rolled next turn.
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));
        var lateEntrant = state.DiceIn("p1", Zone.Bag).First();
        lateEntrant.Zone = Zone.PrepArea; // simulates a same-step Prep effect
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.Roll(state, new FixedRoller(DieStatus.SidekickCharacter, 1));

        Assert.Equal(4, state.DiceIn("p1", Zone.ReservePool).Count()); // just this turn's draw
        Assert.Equal(Zone.PrepArea, lateEntrant.Zone); // untouched - waits for next turn
    }

    [Fact]
    public void FullTurnCycle_SkippingAttack_HandsPriorityToOpponent()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0));
        TurnEngine.AdvanceStep(state);
        Assert.Equal(TurnStep.Main, state.CurrentStep);

        TurnEngine.SkipAttackStep(state);
        Assert.Equal(TurnStep.CleanUp, state.CurrentStep);

        TurnEngine.CleanUp(state);

        Assert.Equal("p2", state.ActivePlayerId);
        Assert.Equal(TurnStep.ClearAndDraw, state.CurrentStep);
        Assert.False(state.IsFirstTurn);
    }

    [Fact]
    public void AdvanceStep_PastCleanUp_Throws()
    {
        var state = CreateNewGame();
        state.CurrentStep = TurnStep.CleanUp;

        Assert.Throws<InvalidOperationException>(() => TurnEngine.AdvanceStep(state));
    }

    [Fact]
    public void ClearAndDraw_SweepsUnspentReservePoolDice_ResetsThemToUnrolled()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Mask));
        var rolled = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.All(rolled, d => Assert.Equal(DieStatus.Energy, d.Status)); // sanity - actually rolled

        // Nothing gets spent; the turn passes and it's p1's Clear & Draw again.
        state.CurrentStep = TurnStep.ClearAndDraw;
        TurnEngine.ClearAndDraw(state, new Random(2));

        // Rulebook's "More About Dice" - the Used Pile holds "unrolled
        // dice," and it doesn't matter what face happened to be showing.
        Assert.All(rolled, d => Assert.Equal(Zone.UsedPile, d.Zone));
        Assert.All(rolled, d => Assert.False(d.IsRolled));
        Assert.All(rolled, d => Assert.Equal(DieStatus.Unrolled, d.Status));
        Assert.All(rolled, d => Assert.Equal(EnergyKind.None, d.EnergyKind));
        Assert.All(rolled, d => Assert.Null(d.ProvidedEnergyType));
        Assert.All(rolled, d => Assert.Equal(1, d.EnergyAmount));
    }

    [Fact]
    public void ClearAndDraw_SweepsUnspentReservePoolDice_TwoDifferentRolledFacesBecomeIndistinguishable()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new SequentialRoller()); // each die gets a different Level
        var rolled = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.True(rolled.Select(d => d.Level).Distinct().Count() > 1); // sanity - genuinely different faces

        state.CurrentStep = TurnStep.ClearAndDraw;
        TurnEngine.ClearAndDraw(state, new Random(2));

        // Once dormant, dice that were on different faces are - correctly,
        // per the rulebook - indistinguishable from each other, which is
        // exactly what lets the web client collapse them into one "×N"
        // chip instead of listing each separately.
        var distinctStates = rolled
            .Select(d => (d.Status, d.Level, d.Damage, d.EnergyKind, d.ProvidedEnergyType, d.EnergyAmount))
            .Distinct()
            .Count();
        Assert.Equal(1, distinctStates);
    }

    [Fact]
    public void CleanUp_SweepsOutOfPlayAndUnusedActionDice_ResetsThemToUnrolled()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0, EnergyKind.Wild));
        TurnEngine.AdvanceStep(state); // Main

        // One energy die spent (-> Out of Play), one Action die rolled but
        // never used (-> stays in the Reserve Pool on its Action face).
        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        var spent = reserve[0];
        spent.Zone = Zone.OutOfPlay;
        var unusedAction = reserve[1];
        unusedAction.Status = DieStatus.Action;

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        foreach (var die in new[] { spent, unusedAction })
        {
            Assert.Equal(Zone.UsedPile, die.Zone);
            Assert.False(die.IsRolled);
            Assert.Equal(DieStatus.Unrolled, die.Status);
            Assert.Equal(EnergyKind.None, die.EnergyKind);
        }
    }
}
