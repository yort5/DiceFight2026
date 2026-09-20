using DiceFight.V2.Data;
using DiceFight.V2.Model;
using Xunit;

namespace DiceFight.V2.Tests;

// Guardrails for the cost model in v3/COST_MODEL.md: a Character's raw
// stat value is judged by its Homash value, so a card whose stats
// outclass a cheaper card's can't sneak in without an ability to pay
// for it (the Elephant-vs-Greyhound problem that prompted the model).
public class DiceKingdomCostModelTests
{
    // Homash value: total ATK+DEF across the three levels, divided by
    // what it costs to actually use the die (purchase + fielding at
    // every level). https://dmunited.eu/what-in-the-world-are-homash-values/
    private static double Homash(CardDef card)
    {
        var levels = card.Die.Faces.Where(f => f.Character != null).Select(f => f.Character!).ToList();
        var stats = levels.Sum(l => l.Attack + l.Defense);
        return (double)stats / (card.PurchaseCost + levels.Sum(l => l.FieldingCost));
    }

    private static bool IsVanilla(CardDef c) => c.Abilities.Count == 0 && c.Continuous.Count == 0 && c.Keywords.Count == 0;

    [Fact]
    public void Every_Character_Sits_In_The_Sane_Homash_Band()
    {
        foreach (var card in DiceKingdomConfig.Catalog.Values)
        {
            var h = Homash(card);
            Assert.True(h is >= 1.0 and <= 2.6, $"{card.Name} has Homash {h:0.00} (expected 1.0-2.6).");
        }
    }

    [Fact]
    public void Vanilla_Characters_Get_The_Best_Stats_Per_Cost()
    {
        var vanilla = DiceKingdomConfig.Catalog.Values.Where(IsVanilla).ToList();
        Assert.NotEmpty(vanilla);
        foreach (var card in vanilla)
            Assert.True(Homash(card) >= 2.2, $"Vanilla {card.Name} has Homash {Homash(card):0.00}; with no ability it should be >= 2.2.");

        foreach (var card in DiceKingdomConfig.Catalog.Values.Where(c => !IsVanilla(c)))
            Assert.True(Homash(card) <= 2.2, $"{card.Name} has an ability but Homash {Homash(card):0.00}; expected <= 2.2 (its ability is paid for out of its stats).");
    }
}
