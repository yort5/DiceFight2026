using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Rule 3.1.10 / 2.6.4.6 - a Global or Action die can't be initiated when it
// would have no result. Triggered abilities (Mountain Goat's On Attack
// draw) aren't gated; they just fizzle at resolution.
public class NoResultAbilityTests
{
    // Golden Eagle (Wing) with every Bag die on the Field - nothing left to
    // draw - and one Wing pip in the Reserve Pool, in its own Main Step.
    private static (GameState State, DieInstance Wing) EagleWithEmptyBag()
    {
        var p1 = new Player { Id = "p1", Name = "Eagle", ChampionId = "GoldenEagle" };
        p1.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["GoldenEagle"]);
        p1.TeamCardIds.Add(DiceKingdomConfig.ActionByChampion["GoldenEagle"]);
        var p2 = new Player { Id = "p2", Name = "Wolf", ChampionId = "Wolf" };
        p2.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Wolf"]);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, p1, p2);

        var bag = state.DiceIn("p1", Zone.Bag).ToList();
        foreach (var die in bag.Skip(1)) { die.Zone = Zone.FieldZone; die.CurrentFaceIndex = 0; }
        var wing = bag[0];
        wing.Zone = Zone.ReservePool;
        wing.CurrentFaceIndex = 2; // a Tardigrade's 1-energy face - one Wing pip
        state.MoveToStep(StepIds.Main);
        return (state, wing);
    }

    [Fact]
    public void A_Draw_Global_With_Nothing_To_Draw_Cant_Be_Used_And_Costs_Nothing()
    {
        var (state, wing) = EagleWithEmptyBag();
        var resurrection = DiceKingdomConfig.Resurrection;
        var globalIndex = resurrection.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobal(state, new AbilityQueue(), resurrection.Id, "p1", globalIndex, [wing.Id]));

        Assert.Contains("3.1.10", ex.Message);
        Assert.Equal(Zone.ReservePool, wing.Zone); // not spent
        Assert.False(Priority.CanUseAnyGlobal(state, "p1"));

        // One die in the Used Pile is enough - an empty Bag refills from it.
        state.DiceIn("p1", Zone.FieldZone).First().Zone = Zone.UsedPile;
        Assert.True(Priority.CanUseAnyGlobal(state, "p1"));
        TurnEngine.UseGlobal(state, new AbilityQueue(), resurrection.Id, "p1", globalIndex, [wing.Id]);
        Assert.Equal(Zone.OutOfPlay, wing.Zone);
    }

    [Fact]
    public void An_Action_Die_With_No_Possible_Result_Cant_Be_Used()
    {
        var (state, _) = EagleWithEmptyBag();
        var action = state.Dice.First(d => d.CardId == DiceKingdomConfig.Resurrection.Id && d.OwnerId == "p1");
        action.Zone = Zone.ReservePool;
        action.ControllerId = "p1";
        action.CurrentFaceIndex = 0; // action face - "choose a die in your Used Pile", which is empty

        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseAction(state, new AbilityQueue(), action.Id));
        Assert.Equal(Zone.ReservePool, action.Zone);
    }
}
