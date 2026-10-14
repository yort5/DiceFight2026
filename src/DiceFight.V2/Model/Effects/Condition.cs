namespace DiceFight.V2.Model.Effects;

// The 7 condition kinds (V2_VOCABULARY_HISTORY.md Part 1), each its own record
// rather than one bloated record with a pile of nullable per-kind params -
// v1's own Conditional/EffectCondition grew into exactly that shape and it
// was one of the audit's own complaints (ARCHITECTURE_REVIEW.md). Every
// condition that needs to examine a specific die does so via CheckBinding,
// a name resolved against the ability's binding table (TargetFilter.BindAs/
// Bound) - "self" and "event" are the two reserved binding names.
public abstract record Condition;

public sealed record CountAtLeast(TargetFilter Filter, int N) : Condition;

public sealed record TargetWasKOd(string CheckBinding = "event") : Condition;

public enum BurstLevel { Single, Double }
public sealed record OnBurstFace(BurstLevel Level, string CheckBinding = "self", bool AtLeast = false) : Condition;

// Deliberately a single fixed comparator for now (V2_PLAN.md Appendix A's
// own note, carried forward: "extend comparators only w/ sign-off").
public enum LifeComparisonOperator { OwnLessThanOpponent }
public sealed record LifeComparison(LifeComparisonOperator Op = LifeComparisonOperator.OwnLessThanOpponent) : Condition;

public enum KoScope { Any, Own }
public sealed record NoKOsThisTurn(KoScope Scope) : Condition;

public enum TurnFactKind { PurchasedThisTurn, PurchasedCharacterThisTurn, FieldedNoOtherCharacterThisTurn, PrepAreaEmpty }
public sealed record TurnFact(TurnFactKind Fact) : Condition;

// Finding 8 - branch on whether the checked die is currently on a
// character face or an energy face (Making the Team-style cards).
public sealed record OnFaceKind(FaceKind Kind, string CheckBinding = "self") : Condition;

// Added by user request (2026-09-28, Wolf finisher card) - "true when the
// checked die is CURRENTLY assigned 2+ blockers." Reads GameState.
// DeclaredBlocks directly rather than going through TargetFilter/
// CombatRule (neither shape has a "my own blocker count" predicate):
// DeclaredBlocks is only non-null between CombatEngine.DeclareBlockers
// and the ReturnToField step at the end of AssignCombatDamage (rule
// 2.7.2 onward), so this reads false everywhere outside that window -
// exactly right for a StatAura's ActiveWhen gate that should only apply
// while combat damage is actually being calculated. Deliberately checks
// CombatAssignment.BlockersOf(the checked die's own id) - a real gang-
// block against THIS die (rule 2.7.2.2), not every blocker anywhere in
// its lane; a lane-mate attacker's own blockers are a different, v3-only
// concept (CombatEngine's own LaneBlockerIds) this condition does not
// reach.
public sealed record BlockedByAtLeast(int N, string CheckBinding = "self") : Condition;

// Keyword Infiltrate (2026-10-03) - "is the checked die still in this
// zone." Self/Bound targets skip the zone scan entirely (TargetResolver),
// so nothing else can ask whether a queued offer's own die is still
// where the offer assumed it was - an On Block ping that resolves first
// can KO an Infiltrate attacker before its offer comes up.
public sealed record InZone(Zone Zone, string CheckBinding = "self") : Condition;
