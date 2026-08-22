namespace DiceFight.V2.Model.Effects;

// The 6 continuous templates (V2_VOCABULARY.md Part 1) - the replacement
// for v1's 39 one-per-card Grants* CardDef flags (ARCHITECTURE_REVIEW.md's
// central finding: zero reuse, per-card engine edits). ActiveWhen (Finding
// 2) is on the base type since every template gained it identically - a
// live board-state gate reusing the same 7 Condition kinds triggered
// abilities use, evaluated at query time; absent means "active whenever
// the source die itself is active," the same default v1's plain "while
// active" grants already had for free.
public abstract record ContinuousDef(Condition? ActiveWhen = null);

public sealed record StatAura(TargetFilter Target, Amount? AtkDelta = null, Amount? DefDelta = null, Condition? ActiveWhen = null)
    : ContinuousDef(ActiveWhen);

public enum CostKind
{
    Purchase,
    Fielding,
    GlobalEnergy,
    ActionDieUse, // Finding 14 - Lilandra "Freedom Fighter"
}
public enum Currency { Energy, Life } // Finding 14 - Lilandra "Majestrix", Jinzo "Trap Destroyer"
public sealed record CostModifier(CostKind Kind, TargetFilter Whose, int Delta, Currency Currency = Currency.Energy, Condition? ActiveWhen = null)
    : ContinuousDef(ActiveWhen);

public sealed record TagAura(TargetFilter Target, IReadOnlyList<string> Tags, Condition? ActiveWhen = null)
    : ContinuousDef(ActiveWhen);

public enum CombatRuleKind
{
    BlocksN,        // this die may block N attackers instead of 1 (N required)
    MinBlockers,    // must be blocked by >= N dice to be legally blocked at all (N required)
    CantSpinUp,
    CantFieldMore,
}
public sealed record CombatRule(CombatRuleKind Kind, TargetFilter Target, int? N = null, Condition? ActiveWhen = null)
    : ContinuousDef(ActiveWhen);

// Source (Finding 10) is the ability-vs-combat damage axis v1's
// DieStats.ApplyDamage already proved out as a real distinction, not a
// hypothetical one. Amplify/Double (Finding 14): see the fixed ordering
// rule in V2_VOCABULARY.md Part 1/11 - multipliers apply before flat
// reductions, decided once at adoption time rather than left for each
// card's interpreter case to relitigate.
public enum DamageModifierMode { Reduce, RedirectToSelf, PreventNonCombat, Amplify, Double }
public enum DamageSource { Ability, Combat, Any }
public sealed record DamageModifier(
    DamageModifierMode Mode,
    TargetFilter Target,
    int? Amount = null,
    DamageSource Source = DamageSource.Any,
    Condition? ActiveWhen = null) : ContinuousDef(ActiveWhen);

public enum ProtectionFrom { Global, Action, Both }
public sealed record TargetingProtection(TargetFilter Target, ProtectionFrom From, Condition? ActiveWhen = null)
    : ContinuousDef(ActiveWhen);
