using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// A fake roller lets step tests be deterministic without modeling real
// physical die face tables yet (see TurnEngine.RolledFace remarks).
file sealed class FixedRoller(DieStatus status, int level) : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level);
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
        Assert.Equal(2, state.PlayerOne.VirtualGenericEnergy);
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
}
