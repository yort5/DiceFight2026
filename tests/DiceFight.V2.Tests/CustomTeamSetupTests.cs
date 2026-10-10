using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

public class CustomTeamSetupTests
{
    [Fact]
    public void CustomTeam_Seeds_Exactly_Selected_Dice_Without_A_Champion()
    {
        var cardA = DiceKingdomConfig.Mongoose.Id;
        var cardB = DiceKingdomConfig.HoneyBadger.Id;
        var custom = new Player
        {
            Id = "p1", Name = "Custom Team", ChampionId = null,
            CustomCardDiceCounts = new Dictionary<string, int> { [cardA] = 2, [cardB] = 1 },
            CustomBasicDicePool = [
                DiceKingdomConfig.CustomTardigrade("Claw", 3),
                DiceKingdomConfig.CustomTardigrade("Eye", 5),
            ],
        };
        custom.TeamCardIds.AddRange([cardA, cardB]);
        var standard = new Player { Id = "p2", Name = "Wolf", ChampionId = "Wolf" };
        standard.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Wolf"]);

        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, custom, standard);

        Assert.Null(state.PlayerOne.ChampionId);
        Assert.Equal(2, state.Dice.Count(d => d.OwnerId == "p1" && d.CardId == cardA));
        Assert.Single(state.Dice.Where(d => d.OwnerId == "p1" && d.CardId == cardB));
        Assert.Equal(3, state.Dice.Count(d => d.OwnerId == "p1" && d.PoolDieId == "TardigradeClaw"));
        Assert.Equal(5, state.Dice.Count(d => d.OwnerId == "p1" && d.PoolDieId == "TardigradeEye"));
        Assert.Equal(8, state.Dice.Count(d => d.OwnerId == "p2" && d.PoolDieId == "TardigradeClaw"));
        foreach (var die in state.Dice.Where(d => d.OwnerId == "p1" && d.PoolDieId is not null))
        {
            Assert.Equal(6, state.GetDieDefinition(die).Faces.Count);
        }
    }

    [Fact]
    public void CustomTeam_Is_Not_Silently_Clamped_To_DieLimit()
    {
        var cardId = DiceKingdomConfig.Mongoose.Id;
        var custom = new Player {
            Id = "p1", Name = "Custom", ChampionId = null,
            CustomCardDiceCounts = new Dictionary<string, int> { [cardId] = 5 },
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Wing", 2)],
        };
        custom.TeamCardIds.Add(cardId);
        var other = new Player { Id = "p2", Name = "Other", ChampionId = null,
            CustomCardDiceCounts = new Dictionary<string, int>(), CustomBasicDicePool = [] };
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, custom, other);
        Assert.Equal(5, state.Dice.Count(d => d.CardId == cardId));
        Assert.Equal(2, state.Dice.Count(d => d.PoolDieId == "TardigradeWing"));
        Assert.Empty(state.Dice.Where(d => d.OwnerId == "p2"));
    }
}
