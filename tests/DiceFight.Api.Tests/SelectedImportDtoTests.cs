using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.Api.Tests;

public class SelectedImportDtoTests
{
    [Fact]
    public void Personal_Actions_And_Shared_Actions_Keep_Their_Types_And_Die_Metadata()
    {
        var personal = V2CardDefDto.From(DiceKingdomConfig.Catalog["MYST002"]);
        Assert.True(personal.IsAction); Assert.Equal("Action", personal.Type);
        Assert.Equal(new[] { "Wing" }, personal.EnergyTypes);
        Assert.Equal("Wing", personal.DieEnergyType); Assert.Equal("Common", personal.Rarity);
        var shared = V2CardDefDto.From(DiceKingdomConfig.Catalog["JUS069"]);
        Assert.True(shared.IsAction); Assert.Equal("BasicAction", shared.Type);
        Assert.Empty(shared.EnergyTypes); Assert.Equal("Claw", shared.DieEnergyType);
        Assert.Null(shared.Rarity); Assert.Equal(new[] { 0, 1, 2 }, shared.ActionFaceBursts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rolled_Action_Bursts_Are_Exposed_To_The_Shared_Renderer(int burst)
    {
        var p = new Player { Id = "p1", Name = "One", CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Claw", 8)] };
        var opponent = new Player { Id = "p2", Name = "Two", CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Claw", 8)] };
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, p, opponent);
        var die = new DieInstance { Id = "thorn", CardId = "JUS069", OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = burst };
        Assert.True(V2DieDto.From(state, die).IsActionFace);
        Assert.Equal(burst, V2DieDto.From(state, die).Burst);
    }
}
