using DiceFight.V2.Data;

namespace DiceFight.V2.Tests;

// Random packs (2026-10-12): every Character has a rarity, packs follow
// their slots, and the odds come out near what DiceKingdomPacks says.
public class DiceKingdomPacksTests
{
    [Fact]
    public void Every_Character_Has_A_Rarity_And_No_Basic_Action_Does()
    {
        foreach (var card in DiceKingdomConfig.Catalog.Values)
            Assert.Equal(card.CardType == Model.CardType.Character, DiceKingdomPacks.Rarities.ContainsKey(card.Id));
        Assert.All(DiceKingdomPacks.Rarities.Keys, id => Assert.True(DiceKingdomConfig.Catalog.ContainsKey(id), id));
    }

    // "Decently competitive with just a common/uncommon team": every
    // starter roster is Common/Uncommon apart from at most two cards.
    [Fact]
    public void Starter_Rosters_Are_Mostly_Common_And_Uncommon()
    {
        foreach (var (champion, cards) in DiceKingdomConfig.CharactersByChampion)
            Assert.True(cards.Count(id => DiceKingdomPacks.Rarities[id] >= CardRarity.Rare) <= 2, champion);
    }

    [Fact]
    public void A_Pack_Is_Three_Different_Commons_An_Uncommon_And_A_Rare_Slot()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var pack = DiceKingdomPacks.Open(random.Next);
            var rarities = pack.Select(id => DiceKingdomPacks.Rarities[id]).ToList();
            Assert.Equal([CardRarity.Common, CardRarity.Common, CardRarity.Common, CardRarity.Uncommon], rarities.Take(4));
            Assert.NotEqual(CardRarity.Common, rarities[4]);
            Assert.Equal(5, pack.Distinct().Count());
        }
    }

    [Fact]
    public void The_Rare_Slot_Odds_Are_Near_55_40_5()
    {
        var random = new Random(11);
        var counts = new Dictionary<CardRarity, int>();
        const int packs = 20000;
        for (var i = 0; i < packs; i++)
        {
            var rarity = DiceKingdomPacks.Rarities[DiceKingdomPacks.Open(random.Next)[4]];
            counts[rarity] = counts.GetValueOrDefault(rarity) + 1;
        }
        Assert.InRange(counts[CardRarity.Uncommon] / (double)packs, 0.53, 0.57);
        Assert.InRange(counts[CardRarity.Rare] / (double)packs, 0.38, 0.42);
        Assert.InRange(counts[CardRarity.SuperRare] / (double)packs, 0.04, 0.06);
    }
}
