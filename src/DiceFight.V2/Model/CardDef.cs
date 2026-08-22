using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Model;

public enum CardType
{
    Character,
    BasicAction,
    Token,
}

// Appendix B. Affiliations/Keywords contribute to the die's tag set
// alongside its card name and (Finding 4) its EnergySymbolId - see
// V2_VOCABULARY.md Part 1's tag-unification note; there is no separate
// "affiliation filter" or "keyword filter" anywhere in the effect
// vocabulary, deliberately, because Tags already covers all of it.
public sealed record CardDef(
    string Id,
    string Name,
    string? Subtitle,
    string Set,
    CardType CardType,
    int PurchaseCost,
    string? EnergySymbolId,
    DieDefinition Die,
    int DieLimit,
    IReadOnlyList<string> Affiliations,
    IReadOnlyList<string> Keywords,
    string RawText,
    IReadOnlyList<TriggeredAbility> Abilities,
    IReadOnlyList<ContinuousDef> Continuous,
    bool IsImplemented = true);
