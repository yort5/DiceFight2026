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

    [Fact]
    public void Builder_Contains_72_Playable_And_44_Incomplete_Cards()
    {
        var builder = DiceKingdomConfig.TeamBuilderCatalog;
        Assert.Equal(116, builder.Count);
        Assert.Equal(72, DiceKingdomConfig.Catalog.Count);
        Assert.Equal(44, builder.Values.Count(card => !card.IsImplemented));
        Assert.Equal(65, builder.Values.Count(card => card.IncludeInTeamBuilder));
        Assert.All(DiceKingdomConfig.Catalog.Values, card => Assert.Same(card, builder[card.Id]));
        Assert.All(AllCards.Where(card => card.IncludeInTeamBuilder), source =>
        {
            var card = builder[source.Id];
            Assert.Equal(source.KingdomName, card.Name);
            Assert.Same(source.Affiliations, card.Affiliations);
            Assert.Equal(source.PurchaseCost, card.PurchaseCost);
            Assert.Equal(source.DieLimit, card.DieLimit);
            Assert.Equal(source.IsImplemented, card.IsImplemented);
            Assert.Equal(card.IsImplemented, DiceKingdomConfig.Catalog.ContainsKey(card.Id));
            var sourcePrinted = source.Die.Faces.Where(face => face.Kind != FaceKind.EnergyFace).ToArray();
            var importedPrinted = card.Die.Faces.Where(face => face.Kind != FaceKind.EnergyFace).ToArray();
            Assert.Equal(sourcePrinted.Select(face => face.Character), importedPrinted.Select(face => face.Character));
            Assert.Equal(sourcePrinted.Select(face => face.Burst), importedPrinted.Select(face => face.Burst));
            Assert.Equal(sourcePrinted.Select(face => face.Kind), importedPrinted.Select(face => face.Kind));
            Assert.Equal(sourcePrinted.Select(face => face.SymbolCount), importedPrinted.Select(face => face.SymbolCount));
            Assert.Equal(source.HasErrata, card.HasErrata);
            Assert.Same(source.ErrataNotes, card.ErrataNotes);
            Assert.All(card.Die.Faces.SelectMany(face => face.Symbols), symbol => Assert.Contains(symbol.SymbolId,
                new[] { source.KingdomEnergyType, "Wild" }));
            Assert.All(card.EnergySymbolIds, energy => Assert.Equal(source.KingdomEnergyType, energy));
        });
        Assert.Equal(116, builder.Values.Select(card => card.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(CardCatalog.CosmicCube.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.TightRanks.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.Radicalization.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.DampeningCollar.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.Archnemesis.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.BlobImmovable.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.EmmaFrostInfluential.Id, builder.Keys);
        Assert.DoesNotContain(DpsCards.DKenMKraanCrystal.Id, builder.Keys);
        Assert.Same(DiceKingdomConfig.Archnemesis, builder[DiceKingdomConfig.Archnemesis.Id]);
        Assert.DoesNotContain(BonusCards.DominoNotReallyAPartyGirl.Id, builder.Keys);
    }

    [Fact]
    public void Errata_Is_Individual_Card_Metadata_And_Not_A_Game_Keyword()
    {
        string[] expected =
        [
            "DPS003", "DPS004", "DPS005", "DPS006", "DPS010", "DPS015",
            "DPS027", "DPS029", "DPS033", "DPS044", "DPS045", "DPS056", "DPS063",
            "DPS072", "DPS078", "DPS081", "DPS086", "DPS095", "DPS107", "DPS113",
            "DPS145", "DPS152", "GOTG008", "GOTG105", "MSW019", "MSW020", "MSW027", "TAG003",
        ];
        Assert.Equal(expected, AllCards.Where(card => card.HasErrata).Select(card => card.Id).OrderBy(id => id).ToArray());
        Assert.All(AllCards, card => Assert.DoesNotContain("Errata", card.Keywords));
        Assert.All(DiceKingdomConfig.Catalog.Values.Where(card => !card.IncludeInTeamBuilder), card => Assert.False(card.HasErrata));
        Assert.All(AllCards, card => Assert.Equal(card.HasErrata, card.ErrataNotes is { Count: > 0 }));
    }

    [Fact]
    public void Every_Imported_Die_Uses_The_Native_Dice_Kingdom_Face_Pattern()
    {
        foreach (var card in DiceKingdomConfig.TeamBuilderCatalog.Values)
        {
            Assert.Equal(6, card.Die.Faces.Count);
            var printedKind = card.CardType.IsActionDie() ? FaceKind.ActionFace : FaceKind.CharacterFace;
            Assert.All(card.Die.Faces.Take(3), face => Assert.Equal(printedKind, face.Kind));
            Assert.All(card.Die.Faces.Skip(3), face => Assert.Equal(FaceKind.EnergyFace, face.Kind));
            Assert.Equal(new[] { 2, 2, 1 }, card.Die.Faces.Skip(3).Select(face => face.SymbolCount));
        }
    }

    [Fact]
    public void Imported_Actions_Change_Only_Their_Adapted_Dice_And_Document_The_Change()
    {
        var sources = AllCards.Where(card => card.IncludeInTeamBuilder && card.CardType.IsCommunity()).ToArray();
        Assert.Equal(7, sources.Length);
        foreach (var source in sources)
        {
            var imported = DiceKingdomConfig.TeamBuilderCatalog[source.Id];
            Assert.Equal(new[] { 2, 2, 2 }, source.Die.Faces.Take(3).Select(face => face.SymbolCount));
            Assert.Equal(new[] { 2, 2, 1 }, imported.Die.Faces.Skip(3).Select(face => face.SymbolCount));
            Assert.All(imported.Die.Faces.Skip(3), face => Assert.Equal(source.KingdomEnergyType, Assert.Single(face.Symbols).SymbolId));
            Assert.True(imported.HasErrata);
            Assert.Contains(imported.ErrataNotes!, note => note.Contains($"Generic 2 / Generic 2 / Generic 2 -> {source.KingdomEnergyType} 2 / {source.KingdomEnergyType} 2 / {source.KingdomEnergyType} 1"));
            Assert.Equal(source.PurchaseCost, imported.PurchaseCost);
            Assert.Equal(source.DieLimit, imported.DieLimit);
            Assert.Equal(source.Abilities.Count, imported.Abilities.Count);
        }
    }

    [Fact]
    public void Internal_Errata_Notes_Are_Excluded_Even_From_Direct_Card_Serialization()
    {
        var card = DiceKingdomConfig.TeamBuilderCatalog[DpsCards.OrganicSteelPreventDamage.Id];
        var json = System.Text.Json.JsonSerializer.Serialize(card, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("errataNotes", out _));
        Assert.True(document.RootElement.GetProperty("hasErrata").GetBoolean());
        Assert.DoesNotContain("rollinthunder_cards_with_dice", json);
    }

}
