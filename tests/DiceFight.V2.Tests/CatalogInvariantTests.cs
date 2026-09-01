using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 8 task 5 - catalog-wide invariant tests, run over the
// UNION of every card source (the curated teams, the 145-card DPS set,
// and the one-off BonusCards), not each source in isolation. Each source
// already validates itself (CardCatalogTests, DpsCardsTests, BonusCardsTests),
// but nothing had checked them TOGETHER - which is where a cross-file
// collision or a card-authoring mistake with no in-file symptom would
// hide. This suite exists to keep finding that class of bug, not to
// re-cover what those per-source tests already do.
public class CatalogInvariantTests
{
    private static readonly IReadOnlyList<CardDef> AllCards =
        [.. CardCatalog.BuildCatalog().Values, .. DpsCards.All, .. BonusCards.All];

    [Fact]
    public void No_Card_Id_Is_Reused_Across_The_Curated_DPS_And_Bonus_Sources()
    {
        var duplicates = AllCards
            .GroupBy(c => c.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    // Each source's own test already runs ValidateCatalog on itself; this
    // reruns it over the union, which is how the "Loyalty" keyword gap
    // (Task 5's own motivating find - Jean Grey "Peaceful Coexistence"
    // used a keyword GameConfig never declared, and nothing checked a
    // card's Keywords against the declared list at all) was caught. Kept
    // as a standing regression guard now that the check exists.
    [Fact]
    public void The_Full_Merged_Catalog_Is_Valid_Against_The_Classic_Config()
    {
        var errors = DiceFightClassicConfig.Config.ValidateCatalog(AllCards);
        Assert.Empty(errors);
    }

    // GameConfig data drives real gameplay logic elsewhere (blanking's
    // tag-namespace rules, CombatEngine's Overcrush/Fast lookups) - a
    // card referencing an affiliation is free-form, but Set should always
    // be one of the sources this catalog actually draws from, catching a
    // copy-paste typo in a new card's Set field.
    [Fact]
    public void Every_Cards_Set_Is_One_Of_The_Known_Migration_Sources()
    {
        var knownSets = new HashSet<string> { "MSW", "DPS", "GOTG", "SKC", "TAG", "JLL", "CW", "XFO" };
        Assert.All(AllCards, c => Assert.Contains(c.Set, knownSets));
    }
}
