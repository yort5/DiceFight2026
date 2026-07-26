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

public class TurnEngineTests
{
    private static GameState CreateNewGame() =>
        GameState.NewGame(
            new Dictionary<string, CardDef>(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });

    [Fact]
    public void ClearAndDraw_FirstTurn_Draws3ToPrepAnd1OutOfPlay()
    {
        var state = CreateNewGame();

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(3, state.DiceIn("p1", Zone.PrepArea).Count());
        Assert.Single(state.DiceIn("p1", Zone.OutOfPlay));
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count()); // 8 - 3 - 1
    }

    [Fact]
    public void ClearAndDraw_SubsequentTurn_Draws4ToPrepArea()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(4, state.DiceIn("p1", Zone.PrepArea).Count());
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
        Assert.Equal(4, state.DiceIn("p1", Zone.PrepArea).Count());
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

        Assert.Equal(2, state.DiceIn("p1", Zone.PrepArea).Count());
        Assert.Equal(18, state.PlayerOne.Life); // 20 - 2 short
        Assert.Equal(2, state.PlayerOne.VirtualGenericEnergy);
    }

    [Fact]
    public void RollAndReroll_MovesPrepAreaDiceToReservePoolOnRolledFace()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state); // Main is skipped for this test's purposes; just move past ClearAndDraw
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.RollAndReroll(state, new FixedRoller(DieStatus.SidekickCharacter, 1), _ => []);

        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.Equal(4, reserve.Count);
        Assert.All(reserve, d => Assert.Equal(DieStatus.SidekickCharacter, d.Status));
        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void FullTurnCycle_SkippingAttack_HandsPriorityToOpponent()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.RollAndReroll(state, new FixedRoller(DieStatus.Energy, 0), _ => []);
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
