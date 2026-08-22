namespace DiceFight.V2.Model.Effects;

// The 7 condition kinds (V2_VOCABULARY.md Part 1), each its own record
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
public sealed record OnBurstFace(BurstLevel Level, string CheckBinding = "self") : Condition;

// Deliberately a single fixed comparator for now (V2_PLAN.md Appendix A's
// own note, carried forward: "extend comparators only w/ sign-off").
public enum LifeComparisonOperator { OwnLessThanOpponent }
public sealed record LifeComparison(LifeComparisonOperator Op = LifeComparisonOperator.OwnLessThanOpponent) : Condition;

public enum KoScope { Any, Own }
public sealed record NoKOsThisTurn(KoScope Scope) : Condition;

public enum TurnFactKind { PurchasedThisTurn, FieldedNoOtherCharacterThisTurn, PrepAreaEmpty }
public sealed record TurnFact(TurnFactKind Fact) : Condition;

// Finding 8 - branch on whether the checked die is currently on a
// character face or an energy face (Making the Team-style cards).
public enum FaceKind { CharacterFace, EnergyFace }
public sealed record OnFaceKind(FaceKind Kind, string CheckBinding = "self") : Condition;
