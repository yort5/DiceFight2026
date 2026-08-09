using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Rule 2.6.5.4 - a Global ability's energy price, e.g. "Pay 1 Mask".
// RequiredType null means any energy (including generic) satisfies it -
// none of the currently-scripted Globals need that case, but Basic
// Action cards themselves have no energy type (rule 1.2.4), so a Global
// printed on one would need this.
public sealed record EnergyCost(int Amount, EnergyType? RequiredType = null);

// TriggerType.WhenAnotherDieKOd's own filter - "which KO'd die actually
// matches" isn't engine code (unlike Retaliation/Teamwatch's hardcoded
// affiliation-sharing checks), it's per-card data, since different cards
// filter completely differently (energy type, name substring, or both
// combined with an ownership/self-exclusion check). Ownership is always
// relative to the REACTING die's own controller (same convention as
// TargetSpec.Ownership) - Own means the KO'd die shares the reactor's
// controller, matching card text like Magneto's "one of YOUR Mask
// character dice." All non-null fields must match (an implicit AND) -
// no currently-authored card needs an OR of alternatives.
public sealed record KOdDieMatch(
    TargetOwnership Ownership = TargetOwnership.Any,
    EnergyType? RequiredEnergyType = null,
    // Substring match against the KO'd die's card Name (case-insensitive) -
    // covers both "a card with Kree in its name" (Supreme Intelligence,
    // a real substring check) and "When Lilandra is KO'd" (Gladiator, a
    // specific card referenced by name) - the latter just happens to be
    // a substring match against exactly one real name.
    string? NameContains = null,
    // Affiliation "contains" rather than "equals" - CardDef.Affiliations
    // is a list (a Crossover-style card can have more than one), so this
    // matches if any entry equals the given string.
    string? AffiliationContains = null,
    // Madelyne Pryor's "besides Madelyne Pryor" - the KO'd die must not
    // be the reacting die itself.
    bool ExcludeSelf = false);

// TriggerType.WhenAnotherDieFielded's own filter - same shape as
// KOdDieMatch just above, for "when [a die matching this] is fielded"
// instead of "is KO'd". RequiredKeyword (Cyclops "First Class"/DPS025's
// "a character die with Founder") checks DieStats.HasKeyword rather than
// a printed-only lookup, since keywords can be granted as well as
// printed and this filter shouldn't care which.
public sealed record FieldedDieMatch(
    TargetOwnership Ownership = TargetOwnership.Any,
    string? RequiredKeyword = null,
    string? AffiliationContains = null,
    bool ExcludeSelf = false);

// A single authored ability on a card: when it fires, what it costs
// (rule 3.1.16 - Ability Cost, distinct from energy cost), and what it does.
// Cost and Effects are both effect trees so a cost like "KO one of your
// dice" reuses the same primitives as an effect. EnergyCost is only
// meaningful for Trigger == Global (rule 2.6.5.4); Non-global abilities
// don't have an energy price of their own.
public sealed record AbilityDef(
    TriggerType Trigger,
    IReadOnlyList<EffectNode>? Cost,
    EffectNode Effect,
    EnergyCost? EnergyCost = null,
    // Card-text limiters like Falcon's "Once during your turn" (Global) -
    // only meaningful for Trigger == Global today. Tracked per-cardId in
    // GameState.GlobalsUsedThisTurn, reset every Clean Up.
    bool OncePerTurn = false,
    // Only meaningful for Trigger == WhenAnotherDieKOd - required (never
    // null) for that trigger, since a card without any real filter would
    // fire for every single KO in the game, which no real card means.
    KOdDieMatch? KOdFilter = null,
    // Only meaningful for Trigger == WhenAnotherDieFielded - same
    // "always required" reasoning as KOdFilter above.
    FieldedDieMatch? FieldedFilter = null);
