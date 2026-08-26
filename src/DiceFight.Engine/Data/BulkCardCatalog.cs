using System.Reflection;
using System.Text.Json;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Data;

// Loads the ~3,600 cards bulk-imported from the full reference sheet
// (scripts/import_bulk_cards.py, see DESIGN_LOG.md's "bulk-import the
// full reference sheet" status update for the methodology) - purely
// browsable/searchable data, distinct from the 55 hand-curated cards
// in SampleCards.cs that have real AbilityDefs where scriptable. Most
// get no AbilityDef; IsImplemented reflects whether a card's full text
// is exactly one of a small set of already engine-built keywords (see
// the script's PURE_KEYWORDS list), genuinely blank, or matches one of
// the small set of "ability templates" below (formulaic keywords that
// DO need an AbilityDef, but the same one every time) - everything
// else is real, searchable data flagged as not simulated, same meaning
// IsImplemented already has for the hand-curated cards.
public static class BulkCardCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // BuildCatalog() (and every test that calls it) can run many times
    // per process - parse the embedded JSON once and reuse the result
    // rather than re-deserializing ~3,600 rows on every call.
    private static readonly Lazy<IReadOnlyList<CardDef>> Cached = new(LoadUncached);

    public static IReadOnlyList<CardDef> Load() => Cached.Value;

    // Printed rarity for EVERY card on the reference sheet, hand-curated
    // ones included - which is why it is a separate map rather than a
    // field on BulkCardJson: the hand-curated cards are excluded from
    // BulkCards.json by design, but still need colour-coding in the Team
    // Builder. Display only; nothing in the rules engine reads it.
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CachedRarity =
        new(LoadRarityUncached);

    public static IReadOnlyDictionary<string, string> Rarities => CachedRarity.Value;

    // Keyed by normalized "name|subtitle", not by card id: the old tool
    // has no notion of our ids, and matching on the printed identity is
    // what lets a promo reprint resolve to its original set's code.
    private static readonly Lazy<OldCodeMaps> CachedOldCodes = new(LoadOldCodes);

    private sealed record OldCodeMaps(
        Dictionary<string, string> BySetAndCard, Dictionary<string, string> ByCard);

    private static OldCodeMaps LoadOldCodes()
    {
        using var stream = typeof(BulkCardCatalog).Assembly
            .GetManifestResourceStream("DiceFight.Engine.Data.OldTeamBuilderCodes.json")
            ?? throw new InvalidOperationException("OldTeamBuilderCodes.json embedded resource not found.");
        return JsonSerializer.Deserialize<OldCodeMaps>(stream, JsonOptions)
            ?? throw new InvalidOperationException("OldTeamBuilderCodes.json deserialized to null.");
    }

    // Prefer the card's OWN set, so a Basic Action reprinted across sets
    // resolves to the printing actually chosen (their art differs). The
    // unqualified fallback is what lets a promo reprint resolve to the
    // original set the old tool files it under.
    public static string? OldTeamBuilderCodeFor(string? set, string name, string? subtitle)
    {
        var maps = CachedOldCodes.Value;
        var key = NormalizeCardKey(name, subtitle);
        if (set is not null && maps.BySetAndCard.TryGetValue($"{set.ToLowerInvariant()}|{key}", out var bySet))
        {
            return bySet;
        }
        return maps.ByCard.TryGetValue(key, out var byCard) ? byCard : null;
    }

    public static string NormalizeCardKey(string name, string? subtitle) =>
        $"{Simplify(name)}|{NormalizeSubtitle(subtitle)}";

    // The two sources spell the Basic Action subtitle three ways between
    // them ("Basic Action", "Basic Action Card", "Basic Action Cards").
    // Must stay identical to normalize_subtitle() in extract_maxdice.py.
    private static string NormalizeSubtitle(string? subtitle)
    {
        var value = Simplify(subtitle);
        if (value.StartsWith("epicbasicaction", StringComparison.Ordinal)) return "epicbasicaction";
        if (value.StartsWith("basicaction", StringComparison.Ordinal)) return "basicaction";
        return value;
    }

    private static string Simplify(string? value) =>
        new(( value ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static IReadOnlyDictionary<string, string> LoadMap(string resource)
    {
        using var stream = typeof(BulkCardCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} embedded resource not found.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"{resource} deserialized to null.");
    }

    private static IReadOnlyDictionary<string, string> LoadRarityUncached()
    {
        using var stream = typeof(BulkCardCatalog).Assembly
            .GetManifestResourceStream("DiceFight.Engine.Data.CardRarity.json")
            ?? throw new InvalidOperationException("CardRarity.json embedded resource not found.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("CardRarity.json deserialized to null.");
    }

    private static IReadOnlyList<CardDef> LoadUncached()
    {
        using var stream = typeof(BulkCardCatalog).Assembly
            .GetManifestResourceStream("DiceFight.Engine.Data.BulkCards.json")
            ?? throw new InvalidOperationException("BulkCards.json embedded resource not found.");
        var rows = JsonSerializer.Deserialize<List<BulkCardJson>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("BulkCards.json deserialized to null.");
        return rows.Select(ToCardDef).ToList();
    }

    private static CardDef ToCardDef(BulkCardJson row)
    {
        var ability = BuildTemplatedAbility(row.AbilityTemplate);
        return new()
        {
            Id = row.Id,
            Name = row.Name,
            Subtitle = row.Subtitle,
            Type = Enum.Parse<CardType>(row.Type),
            PurchaseCost = row.PurchaseCost,
            // A list, not a single value: dual-energy characters ("Bolt/Mask")
            // are real printed cards, and CardDef.EnergyTypes has always been a
            // list to hold them. Basic Actions legitimately have none.
            EnergyTypes = row.EnergyTypes.Select(Enum.Parse<EnergyType>).ToList(),
            Affiliations = row.Affiliations,
            DieLimit = row.DieLimit,
            Levels = row.Levels.Select(f => new CharacterFace(f.FieldingCost, f.Attack, f.Defense, f.BurstStars)).ToList(),
            RawText = row.RawText,
            Keywords = row.Keywords.Select(k => new KeywordInstance(k)).ToList(),
            Abilities = ability is null ? [] : [ability],
            IsImplemented = row.IsImplemented,
            Set = row.Set,
        };
    }

    // The "which ability method to call" registry the JSON's
    // abilityTemplate field points into - each case reproduces the
    // exact AbilityDef shape a hand-curated card with that same
    // formulaic keyword already uses in SampleCards.cs (see that
    // file's BlackWidow/ScarletSpider/SupermanKalEl/Polaris for the
    // precedent each case below matches). Adding a 5th template means
    // one more case here plus one more matcher in import_bulk_cards.py -
    // nothing else changes.
    private static AbilityDef? BuildTemplatedAbility(AbilityTemplateJson? template)
    {
        if (template is null) return null;
        var trigger = Enum.Parse<TriggerType>(template.Trigger);
        EffectNode effect = template.Effect switch
        {
            // BlackWidow "Red Scare" precedent.
            "CallOut" => new SetCallOutTarget(TargetSpec.CharacterDie("target character die", TargetOwnership.Opposing)),
            // ScarletSpider "Former Villain" precedent.
            "Intimidate" => new MoveDie(
                TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Zone.Intimidated),
            // SupermanKalEl "Kal-El" precedent - base amount defaults to
            // 1 (rule text's own base value) when the card doesn't
            // override it; the bulk matcher only ever emits amount: 1
            // today since it requires exact "Retaliation" with nothing
            // else, but this stays param-driven for a future card that
            // states a different base amount.
            "Retaliation" => new DealDamage(
                GetAmount(template, 1), TargetSpec.Player("an opposing player", TargetOwnership.Opposing)),
            // Polaris "Lorna Dane" precedent.
            "Corrupt" => new Corrupt(GetAmount(template, 1), TargetSpec.Player("target player")),
            _ => throw new InvalidOperationException($"Unknown ability template '{template.Effect}'."),
        };
        return new AbilityDef(trigger, Cost: null, Effect: effect);
    }

    private static int GetAmount(AbilityTemplateJson template, int fallback) =>
        template.Params.TryGetValue("amount", out var amount) ? amount : fallback;

    private sealed record BulkCardJson(
        string Id, string Name, string? Subtitle, string Type, int PurchaseCost, List<string> EnergyTypes,
        int DieLimit, List<BulkFaceJson> Levels, string RawText, List<string> Keywords,
        List<string> Affiliations, bool IsImplemented, string Set, AbilityTemplateJson? AbilityTemplate);

    private sealed record BulkFaceJson(int FieldingCost, int Attack, int Defense, int? BurstStars);

    private sealed record AbilityTemplateJson(string Effect, string Trigger, Dictionary<string, int> Params);
}
