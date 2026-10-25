using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// Sets (2026-10-25): Set 1 is locked at the cards playable that day, and
// every collectible card is in exactly one set.
public class DiceKingdomSetsTests
{
    [Fact]
    public void Set_1_Is_101_Characters_An_Action_And_21_Basic_Actions()
    {
        var set1 = DiceKingdomSets.Sets[DiceKingdomSets.Set1];
        Assert.All(set1, id => Assert.True(DiceKingdomConfig.Catalog.ContainsKey(id), id));
        var types = set1.GroupBy(id => DiceKingdomConfig.Catalog[id].CardType).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(101, types[CardType.Character]);
        Assert.Equal(1, types[CardType.Action]);
        Assert.Equal(21, types[CardType.BasicAction]);
        Assert.Equal(123, set1.Count);
    }

    // A new card fails here until it's given a set - add "Set 2" to
    // DiceKingdomSets rather than growing the locked Set 1.
    [Fact]
    public void Every_Collectible_Card_Is_In_Exactly_One_Set()
    {
        foreach (var id in DiceKingdomPacks.Rarities.Keys.Concat(DiceKingdomPacks.BasicActions))
            Assert.True(DiceKingdomSets.Sets.Count(kv => kv.Value.Contains(id)) == 1,
                $"{DiceKingdomConfig.Catalog[id].Name} ({id}) is in no set - add it to the next set, not Set 1.");
    }

    [Fact]
    public void Every_Starter_Roster_Card_Is_In_Set_1()
    {
        foreach (var (champion, cards) in DiceKingdomConfig.CharactersByChampion)
            Assert.All(cards, id => Assert.Equal(DiceKingdomSets.Set1, DiceKingdomSets.SetOf(id)));
    }

    [Fact]
    public void Packs_Open_From_The_Current_Set()
    {
        var random = new Random(5);
        for (var i = 0; i < 200; i++)
            Assert.All(DiceKingdomPacks.Open(random.Next), id => Assert.Equal(DiceKingdomSets.Current, DiceKingdomSets.SetOf(id)));
    }
}
