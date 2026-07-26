using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// Covers rule 2.1 (Set Up): 20 starting life, 8 Sidekick dice per player,
// all starting in the bag.
public class GameSetupTests
{
    private static GameState CreateNewGame()
    {
        var catalog = new Dictionary<string, CardDef>();
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        return GameState.NewGame(catalog, p1, p2);
    }

    [Fact]
    public void NewGame_PlayersStartAt20Life()
    {
        var state = CreateNewGame();

        Assert.Equal(20, state.PlayerOne.Life);
        Assert.Equal(20, state.PlayerTwo.Life);
    }

    [Fact]
    public void NewGame_EachPlayerStartsWith8SidekickDiceInBag()
    {
        var state = CreateNewGame();

        var p1Bag = state.DiceIn("p1", Zone.Bag).ToList();
        var p2Bag = state.DiceIn("p2", Zone.Bag).ToList();

        Assert.Equal(8, p1Bag.Count);
        Assert.Equal(8, p2Bag.Count);
        Assert.All(p1Bag, d => Assert.True(d.IsSidekick));
        Assert.All(p2Bag, d => Assert.True(d.IsSidekick));
    }

    [Fact]
    public void NewGame_ActivePlayerIsPlayerOne()
    {
        var state = CreateNewGame();

        Assert.Equal("p1", state.ActivePlayerId);
        Assert.Equal(TurnStep.ClearAndDraw, state.CurrentStep);
    }
}
