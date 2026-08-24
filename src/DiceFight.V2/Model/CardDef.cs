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
    // Rule 2.6.2.3 / the Crossover glossary entry - a card may carry
    // MORE THAN ONE energy type ("Crossover characters have two or more
    // types of energy"; some require all four), and purchasing one means
    // spending at least one energy of EACH. Widened from a single
    // `string?` on 2026-08-24 after the rules validation pass; v1's own
    // CardDef.EnergyTypes was a list all along. Empty for Basic Action
    // cards, which have no energy type (rule 1.2.4).
    IReadOnlyList<string> EnergySymbolIds,
    DieDefinition Die,
    int DieLimit,
    IReadOnlyList<string> Affiliations,
    IReadOnlyList<string> Keywords,
    string RawText,
    IReadOnlyList<TriggeredAbility> Abilities,
    IReadOnlyList<ContinuousDef> Continuous,
    bool IsImplemented = true);
