using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// Deck-out (user-supplied rule, 2026-09-28): each die a player can't draw
// in Clear and Draw costs 1 life and grants 1 generic Virtual energy that
// lasts through that Main Step (GameState.VirtualEnergy).
public class DeckOutTests
{
    private static GameState NewGame()
    {
        Player Build(string id, string champion)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[champion]);
            return p;
        }
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, Build("p1", "Wolf"), Build("p2", "Armadillo"));
        state.IsFirstTurn = false;
        return state;
    }

    // Everything but one Tardigrade is on the Field: 1 drawn, 3 short.
    private static GameState DeckedOutGame()
    {
        var state = NewGame();
        foreach (var die in state.DiceIn("p1", Zone.Bag).Skip(1).ToList())
        {
            die.Zone = Zone.FieldZone;
            die.CurrentFaceIndex = 0;
        }
        TurnEngine.ClearAndDraw(state, new AbilityQueue(), new Random(1));
        return state;
    }

    [Fact]
    public void Each_Die_Short_Of_The_Draw_Costs_One_Life_And_Grants_One_Virtual_Energy()
    {
        var state = DeckedOutGame();

        Assert.Single(state.DiceIn("p1", Zone.DiceFromBag));
        Assert.Equal(17, state.PlayerOne.Life);
        Assert.Equal(3, state.VirtualEnergyOf("p1"));
        Assert.Contains(state.Log, e => e.Text.Contains("can't draw 3 dice"));
    }

    [Fact]
    public void A_Full_Draw_Costs_Nothing()
    {
        var state = NewGame();
        TurnEngine.ClearAndDraw(state, new AbilityQueue(), new Random(1));

        Assert.Equal(20, state.PlayerOne.Life);
        Assert.Equal(0, state.VirtualEnergyOf("p1"));
    }

    [Fact]
    public void Virtual_Energy_Pays_Fielding_Automatically_And_Expires_When_Main_Ends()
    {
        var state = DeckedOutGame();
        state.MoveToStep(StepIds.Main);
        var badger = state.Dice.First(d => d.OwnerId == "p1" && d.CardId == DiceKingdomConfig.HoneyBadger.Id);
        badger.Zone = Zone.ReservePool;
        badger.ControllerId = "p1";
        badger.CurrentFaceIndex = 2; // level 3 - fielding cost 1

        TurnEngine.Field(state, new AbilityQueue(), badger.Id, []);

        Assert.Equal(Zone.FieldZone, badger.Zone);
        Assert.Equal(2, state.VirtualEnergyOf("p1"));

        TurnEngine.EnterAttackStep(state, new AbilityQueue());
        Assert.Equal(0, state.VirtualEnergyOf("p1"));
    }

    // Virtual energy is generic: it pays toward the amount but never the
    // card's type, so a typed purchase still needs one matching pip.
    [Fact]
    public void Virtual_Energy_Cant_Satisfy_A_Purchase_Type_On_Its_Own()
    {
        var state = DeckedOutGame();
        state.MoveToStep(StepIds.Main);
        var badger = state.Dice.First(d => d.OwnerId == "p1" && d.CardId == DiceKingdomConfig.HoneyBadger.Id && d.Zone == Zone.Unpurchased);

        Assert.Throws<InvalidOperationException>(() => TurnEngine.Purchase(state, new AbilityQueue(), badger.Id, []));
        Assert.Equal(3, state.VirtualEnergyOf("p1"));

        var claw = state.DiceIn("p1", Zone.DiceFromBag).Single();
        claw.Zone = Zone.ReservePool;
        claw.CurrentFaceIndex = 2; // a Tardigrade's 1-energy face - one Claw pip
        TurnEngine.Purchase(state, new AbilityQueue(), badger.Id, [claw.Id]);

        Assert.Equal(Zone.UsedPile, badger.Zone);
        Assert.Equal(Zone.OutOfPlay, claw.Zone);
        Assert.Equal(2, state.VirtualEnergyOf("p1")); // 2-cost: 1 Claw + 1 Virtual
    }

    // Offered more dice than needed (a client that doesn't know about
    // Virtual energy): Virtual still goes first, surplus dice stay put.
    [Fact]
    public void Surplus_Offered_Dice_Are_Left_Untouched_When_Virtual_Covers_The_Cost()
    {
        var state = DeckedOutGame();
        state.MoveToStep(StepIds.Main);
        var badger = state.Dice.First(d => d.OwnerId == "p1" && d.CardId == DiceKingdomConfig.HoneyBadger.Id);
        badger.Zone = Zone.ReservePool;
        badger.ControllerId = "p1";
        badger.CurrentFaceIndex = 2; // fielding cost 1
        var claw = state.DiceIn("p1", Zone.DiceFromBag).Single();
        claw.Zone = Zone.ReservePool;
        claw.CurrentFaceIndex = 0; // two Claw pips

        TurnEngine.Field(state, new AbilityQueue(), badger.Id, [claw.Id]);

        Assert.Equal(Zone.ReservePool, claw.Zone);
        Assert.Equal(0, claw.CurrentFaceIndex);
        Assert.Equal(2, state.VirtualEnergyOf("p1"));
    }
}
