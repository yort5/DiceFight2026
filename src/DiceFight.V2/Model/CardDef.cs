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
    bool IsImplemented = true,
    // Text that CANNOT be ignored (V2_VOCABULARY.md Part 21). 34 cards
    // print a clause immune to blanking - King Black Bolt's "you may not
    // use ? energy to purchase this die, this text may not be ignored",
    // Strahd's "doesn't count as an Adventurer, this text cannot be
    // ignored". Immunity is per CLAUSE, not per card: the rest of those
    // cards blanks normally.
    //
    // Separate collections rather than an immunity flag on each ability,
    // at the user's suggestion and because it is structurally safer: a
    // flag is something every filtering site must remember to check,
    // while a separate list is simply not in the blanking code path.
    // Nothing to forget.
    //
    // Usually empty. Read only through QueryEngine.AbilitiesOf /
    // ContinuousOf - never enumerate these directly.
    IReadOnlyList<TriggeredAbility>? PermanentAbilities = null,
    IReadOnlyList<ContinuousDef>? PermanentContinuous = null);
