using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// Covers rule 2.1 (Set Up): 20 starting life, 8 Sidekick dice per player
// all starting in the bag, and how team dice are provisioned onto their
// cards - including Basic Actions being community property.
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

    private static CardDef Character(string id) => new()
    {
        Id = id, Name = id, Type = CardType.Character, PurchaseCost = 3, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)]
    };

    private static CardDef BasicAction(string id, int dieLimit = 3) => new()
    {
        Id = id, Name = id, Type = CardType.BasicAction, PurchaseCost = 2, DieLimit = dieLimit
    };

    private static GameState NewGameWith(IEnumerable<string> teamOne, IEnumerable<string> teamTwo, params CardDef[] cards)
    {
        var catalog = cards.ToDictionary(c => c.Id);
        var p1 = new Player { Id = "p1", Name = "Player One" };
        var p2 = new Player { Id = "p2", Name = "Player Two" };
        p1.TeamCardIds.AddRange(teamOne);
        p2.TeamCardIds.AddRange(teamTwo);
        return GameState.NewGame(catalog, p1, p2);
    }

    private static int DiceOf(GameState state, string cardId) =>
        state.Dice.Count(d => d.CardId == cardId);

    [Fact]
    public void Setup_CharacterDice_AreProvisionedPerPlayerAtTheirDieLimit()
    {
        var state = NewGameWith(["hero"], ["hero"], Character("hero"));

        // Both players brought the same Character card, so there are two
        // independent sets - each may only purchase their own (rule 2.6.2.2).
        Assert.Equal(8, DiceOf(state, "hero"));
        Assert.Equal(4, state.Dice.Count(d => d.CardId == "hero" && d.OwnerId == "p1"));
        Assert.Equal(4, state.Dice.Count(d => d.CardId == "hero" && d.OwnerId == "p2"));
    }

    // Rule 2.1.2 - Basic Action cards are community property in the centre
    // of the table, so two players bringing the same one share ONE set of
    // dice rather than getting a pile each.
    [Fact]
    public void Setup_BasicActionBroughtByBothPlayers_IsOneSharedSetOfDice()
    {
        var state = NewGameWith(["boom"], ["boom"], BasicAction("boom"));

        Assert.Equal(TeamSetup.BasicActionDiceCount, DiceOf(state, "boom"));
    }

    [Fact]
    public void Setup_DifferentBasicActions_EachGetTheirOwnDice()
    {
        var state = NewGameWith(["boom"], ["zap"], BasicAction("boom"), BasicAction("zap"));

        Assert.Equal(TeamSetup.BasicActionDiceCount, DiceOf(state, "boom"));
        Assert.Equal(TeamSetup.BasicActionDiceCount, DiceOf(state, "zap"));
    }

    // Rule 1.2.11 - every Basic Action card uses exactly 3 dice, whatever
    // the imported reference data happens to say its die limit is.
    [Fact]
    public void Setup_BasicActionDiceCount_IsAlwaysThreeRegardlessOfDieLimit()
    {
        var state = NewGameWith(["boom"], [], BasicAction("boom", dieLimit: 1));

        Assert.Equal(3, DiceOf(state, "boom"));
    }
}
