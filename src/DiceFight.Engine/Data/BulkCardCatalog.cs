using System.Reflection;
using System.Text.Json;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Data;

// Loads the ~3,600 cards bulk-imported from the full reference sheet
// (scripts/import_bulk_cards.py, see DESIGN_LOG.md's "bulk-import the
// full reference sheet" status update for the methodology) - purely
// browsable/searchable data, distinct from the 55 hand-curated cards
// in SampleCards.cs that have real AbilityDefs where scriptable. None
// of these get an AbilityDef; IsImplemented reflects only whether a
// card's full text is exactly one of a small set of already
// engine-built keywords (see the script's PURE_KEYWORDS list) or
// genuinely blank - everything else is real, searchable data flagged
// as not simulated, same meaning IsImplemented already has for the
// hand-curated cards.
public static class BulkCardCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // BuildCatalog() (and every test that calls it) can run many times
    // per process - parse the embedded JSON once and reuse the result
    // rather than re-deserializing ~3,600 rows on every call.
    private static readonly Lazy<IReadOnlyList<CardDef>> Cached = new(LoadUncached);

    public static IReadOnlyList<CardDef> Load() => Cached.Value;

    private static IReadOnlyList<CardDef> LoadUncached()
    {
        using var stream = typeof(BulkCardCatalog).Assembly
            .GetManifestResourceStream("DiceFight.Engine.Data.BulkCards.json")
            ?? throw new InvalidOperationException("BulkCards.json embedded resource not found.");
        var rows = JsonSerializer.Deserialize<List<BulkCardJson>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("BulkCards.json deserialized to null.");
        return rows.Select(ToCardDef).ToList();
    }

    private static CardDef ToCardDef(BulkCardJson row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Subtitle = row.Subtitle,
        Type = Enum.Parse<CardType>(row.Type),
        PurchaseCost = row.PurchaseCost,
        EnergyTypes = row.EnergyType is { } e ? [Enum.Parse<EnergyType>(e)] : [],
        Affiliations = row.Affiliations,
        DieLimit = row.DieLimit,
        Levels = row.Levels.Select(f => new CharacterFace(f.FieldingCost, f.Attack, f.Defense, f.BurstStars)).ToList(),
        RawText = row.RawText,
        Keywords = row.Keywords.Select(k => new KeywordInstance(k)).ToList(),
        IsImplemented = row.IsImplemented,
        Set = row.Set,
    };

    private sealed record BulkCardJson(
        string Id, string Name, string? Subtitle, string Type, int PurchaseCost, string? EnergyType,
        int DieLimit, List<BulkFaceJson> Levels, string RawText, List<string> Keywords,
        List<string> Affiliations, bool IsImplemented, string Set);

    private sealed record BulkFaceJson(int FieldingCost, int Attack, int Defense, int? BurstStars);
}
