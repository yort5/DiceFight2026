namespace DiceFight.Engine.Effects;

// The effect DSL: a small, closed set of primitives that authored card
// abilities are composed from (see RULES_ENGINE_DESIGN.md - "Ability
// representation"). New card text is expressed by combining these, not by
// adding new C# code paths. TargetSpec/EffectNode are intentionally data
// (records), not behavior, so they can be authored, serialized, and
// round-tripped as JSON alongside CardDef.
public abstract record EffectNode;

// Rule 3.3 - a targeting spec. Concrete selectors (opposing/own,
// zone, attribute filters) will be layered on top of this as the shared
// "legal target" predicate system called out as an open question in the
// design doc; kept minimal here as a placeholder shape. Until that system
// exists, EffectInterpreter resolves a TargetSpec via a caller-supplied
// callback (see EffectContext), keyed by convention on Description - the
// literal string "self" always means the ability's own source die.
public sealed record TargetSpec(string Description);

public sealed record DealDamage(int Amount, TargetSpec Target) : EffectNode;
public sealed record Ko(TargetSpec Target) : EffectNode;
public sealed record MoveDie(TargetSpec Target, Model.Zone ToZone) : EffectNode;
public sealed record ModifyStat(TargetSpec Target, int? AttackDelta, int? DefenseDelta) : EffectNode;
public sealed record Reroll(TargetSpec Target) : EffectNode;
public sealed record Spin(TargetSpec Target, int LevelDelta) : EffectNode;
public sealed record DrawDice(int Count) : EffectNode;
public sealed record PrepDie(TargetSpec Source) : EffectNode;
public sealed record FieldDie(TargetSpec Target, bool Free) : EffectNode;
public sealed record GainLife(int Amount) : EffectNode;
public sealed record LoseLife(int Amount) : EffectNode;

// Rule 2.9.1's "switch life totals" card text (e.g. Cosmic Cube) - simple
// enough to be its own primitive rather than a generic "set stat" node.
public sealed record SwapLife : EffectNode;

// An ordered sequence, per rule 3.1.7 - non-global abilities with multiple
// effects resolve sequentially in text-box order.
public sealed record Sequence(IReadOnlyList<EffectNode> Steps) : EffectNode;

// Rule 3.1.17's "if you do" / "if [x], then [y]" pattern (e.g. Shocking
// Grasp: "if that character is KO'd by this damage, you may Prep this
// die"). CheckTarget is evaluated against When; Then only runs if it holds
// for at least one resolved die.
public enum EffectCondition
{
    TargetWasKOd
}

public sealed record Conditional(TargetSpec CheckTarget, EffectCondition When, EffectNode Then) : EffectNode;
