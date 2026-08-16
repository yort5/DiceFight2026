using DiceFight.Engine.Data;
using DiceFight.Engine.Model;
using DiceFight.Engine.TeamBuilding;
using Xunit;

namespace DiceFight.Engine.Tests;

// Covers RandomTeamBuilder (GamesController.Create's opponent-roster
// generator for a user-built Team A) against the real catalog, not a
// synthetic one - the interesting failure mode is running out of
// IsImplemented cards to draw from, which only a real-sized pool exercises.
public class RandomTeamBuilderTests
{
    private static readonly IReadOnlyDictionary<string, CardDef> Catalog = SampleCards.BuildCatalog();

    [Fact]
    public void Build_OnlyUsesImplementedNonTokenCards()
    {
        var team = RandomTeamBuilder.Build(Catalog, new Random(1));

        Assert.All(team, id =>
        {
            var card = Catalog[id];
            Assert.True(card.IsImplemented);
            Assert.NotEqual(CardType.Token, card.Type);
        });
    }

    [Fact]
    public void Build_RespectsTeamConstructionShape()
    {
        var team = RandomTeamBuilder.Build(Catalog, new Random(2));
        var cards = team.Select(id => Catalog[id]).ToList();

        var basicActions = cards.Where(c => c.Type is CardType.BasicAction or CardType.EpicBasicAction).ToList();
        var characters = cards.Where(c => c.Type is CardType.Character or CardType.Action).ToList();

        Assert.Equal(2, basicActions.Count);
        Assert.True(characters.Count <= 8);
        Assert.Equal(characters.Select(c => c.Name).Distinct().Count(), characters.Count); // no duplicate names
        Assert.True(characters.Sum(c => c.DieLimit) <= 20);
    }

    [Fact]
    public void Build_IsUsableAsATeamCardIdsList()
    {
        var teamA = new Player { Id = "teamA", Name = "Team A" };
        teamA.TeamCardIds.AddRange(RandomTeamBuilder.Build(Catalog, new Random(3)));
        var teamB = new Player { Id = "teamB", Name = "Team B" };
        teamB.TeamCardIds.AddRange(RandomTeamBuilder.Build(Catalog, new Random(4)));

        var state = GameState.NewGame(Catalog, teamA, teamB);

        Assert.True(state.DiceIn("teamA", Zone.Unpurchased).Any());
        Assert.True(state.DiceIn("teamB", Zone.Unpurchased).Any());
    }
}
