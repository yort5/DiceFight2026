using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// The match log (GameState.Log). Written by the engine at the points a
// player actually does something, so one action that cascades through
// several abilities is still one line and both players read the same
// account.
public class MatchLogTests
{
    private static GameState NewGame()
    {
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        return GameState.NewGame(new Dictionary<string, CardDef>(), p1, p2);
    }

    [Fact]
    public void LogEvent_NumbersLinesFromOne()
    {
        var state = NewGame();

        state.LogEvent("p1", "first");
        state.LogEvent(null, "second");

        Assert.Equal([1, 2], state.Log.Select(e => e.Seq));
        Assert.Equal("p1", state.Log[0].PlayerId);
        Assert.Null(state.Log[1].PlayerId);
    }

    // The log rides on every API response, so it cannot grow without
    // bound. The OLDEST lines go, and Seq keeps counting so the numbering
    // stays honest about what was dropped.
    [Fact]
    public void LogEvent_CapsTheOldestLinesButKeepsNumbering()
    {
        var state = NewGame();

        for (var i = 1; i <= 250; i++) state.LogEvent("p1", $"line {i}");

        Assert.Equal(200, state.Log.Count);
        Assert.Equal("line 51", state.Log[0].Text);
        Assert.Equal(51, state.Log[0].Seq);
        Assert.Equal(250, state.Log[^1].Seq);
    }

    // Rolling is the one action whose result is worth spelling out - the
    // faces are what the player is about to make decisions from.
    [Fact]
    public void Roll_LogsTheFacesItLandedOn()
    {
        var state = NewGame();
        state.CurrentStep = TurnStep.RollAndReroll;
        foreach (var die in state.DiceIn("p1", Zone.Bag).Take(2)) die.Zone = Zone.DiceFromBag;

        TurnEngine.Roll(state, new FixedRoller(new RolledFace(DieStatus.Energy, 0, EnergyKind.Wild)));

        var line = Assert.Single(state.Log);
        Assert.Equal("p1", line.PlayerId);
        Assert.Equal("Player One rolls 2 dice: Wild, Wild.", line.Text);
    }

    // A Sidekick die has no card, so it has to be named explicitly rather
    // than falling through to its die id.
    [Fact]
    public void Roll_NamesSidekickCharacterFacesRatherThanDieIds()
    {
        var state = NewGame();
        state.CurrentStep = TurnStep.RollAndReroll;
        state.DiceIn("p1", Zone.Bag).First().Zone = Zone.DiceFromBag;

        TurnEngine.Roll(state, new FixedRoller(new RolledFace(DieStatus.SidekickCharacter, 1)));

        Assert.Contains("Sidekick L1", Assert.Single(state.Log).Text);
    }

    private sealed class FixedRoller(RolledFace face) : IDiceRoller
    {
        public RolledFace Roll(DieInstance die, CardDef? card) => face;
    }
}
