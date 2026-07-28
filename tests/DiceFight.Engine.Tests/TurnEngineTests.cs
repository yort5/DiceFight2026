using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// A fake roller lets step tests be deterministic without modeling real
// physical die face tables yet (see TurnEngine.RolledFace remarks).
file sealed class FixedRoller(
    DieStatus status, int level, EnergyKind energyKind = EnergyKind.None, EnergyType? providedEnergyType = null)
    : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level, energyKind, providedEnergyType);
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

        TurnEngine.Reroll(state, roller, [toReroll.Id]);

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
        TurnEngine.Reroll(state, roller, []); // "I don't want to reroll anything" is still the one decision
        Assert.Equal(TurnStep.Main, state.CurrentStep);

        var ex = Assert.Throws<InvalidOperationException>(() => TurnEngine.Reroll(state, roller, []));
        Assert.Contains("RollAndReroll", ex.Message);
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
