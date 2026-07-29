namespace DiceFight.Engine.Effects;

// The effect DSL: a small, closed set of primitives that authored card
// abilities are composed from (see RULES_ENGINE_DESIGN.md - "Ability
// representation"). New card text is expressed by combining these, not by
// adding new C# code paths. TargetSpec/EffectNode are intentionally data
// (records), not behavior, so they can be authored, serialized, and
// round-tripped as JSON alongside CardDef.
public abstract record EffectNode;

// Rule 3.1.31 - which side of the requesting ability's controller a
// target must be on. "Own"/"Opposing" are relative to whoever controls
// the ability at resolution time (rule 3.1.4/3.1.5), not fixed players.
public enum TargetOwnership
{
    Any,
    Own,
    Opposing
}

// Rule 3.3 - Targeting. A structured filter over legal targets - zone,
// ownership, character-vs-any-die, required energy type, and how many -
// resolved by LegalTargets.Query against the current GameState rather
// than left for a caller to interpret from a free-text Description. This
// is the "shared legal-target predicate system" called out as an open
// question in the design doc; it still doesn't model captured dice
// (3.8) or per-die "cannot be targeted" abilities, since neither
// Capturing nor per-die targeting restrictions are implemented yet.
public sealed record TargetSpec(
    TargetOwnership Ownership,
    bool CharacterDiceOnly,
    IReadOnlyList<Model.Zone>? EligibleZones,
    Model.EnergyType? RequiredEnergyType,
    int Count,
    string Description,
    bool IsSelf = false,
    bool SidekicksOnly = false)
{
    // Rule 3.3.4/3.3.5 - only dice in the Field Zone (which includes the
    // Attack Zone) may be targeted, unless otherwise stated.
    public static readonly IReadOnlyList<Model.Zone> DefaultZones = [Model.Zone.FieldZone, Model.Zone.AttackZone];

    public static TargetSpec CharacterDie(
        string description,
        TargetOwnership ownership = TargetOwnership.Any,
        Model.EnergyType? energyType = null,
        int count = 1,
        IReadOnlyList<Model.Zone>? zones = null) =>
        new(ownership, CharacterDiceOnly: true, zones ?? DefaultZones, energyType, count, description);

    public static TargetSpec AnyDie(
        string description, TargetOwnership ownership, IReadOnlyList<Model.Zone> zones, int count = 1) =>
        new(ownership, CharacterDiceOnly: false, zones, RequiredEnergyType: null, count, description);

    // "target Sidekick" card text - matches real Sidekick dice, plus any
    // Ally-keyword Character die currently in the Field/Attack Zone (see
    // DieStats.CountsAsSidekick).
    public static TargetSpec Sidekick(
        string description,
        TargetOwnership ownership = TargetOwnership.Any,
        int count = 1,
        IReadOnlyList<Model.Zone>? zones = null) =>
        new(ownership, CharacterDiceOnly: false, zones ?? DefaultZones, RequiredEnergyType: null, count, description,
            SidekicksOnly: true);

    // Rule 3.1.15-style self-reference (e.g. Shocking Grasp's "you may
    // Prep this die"). Bypasses legal-target filtering entirely -
    // EffectInterpreter resolves it straight to the ability's source die.
    public static readonly TargetSpec Self =
        new(TargetOwnership.Any, CharacterDiceOnly: false, EligibleZones: null, RequiredEnergyType: null,
            Count: 1, Description: "self", IsSelf: true);
}

public sealed record DealDamage(int Amount, TargetSpec Target) : EffectNode;
public sealed record Ko(TargetSpec Target) : EffectNode;
// Invisible Woman's Global ("target character die must block this turn") -
// flags the target in GameState.MustBlockThisTurn; enforced by
// CombatEngine.DeclareBlockers, cleared at Clean Up.
public sealed record ForceBlock(TargetSpec Target) : EffectNode;
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

// Falcon's Global ("each player must field a Sidekick from their Used Pile
// if able") - a forced action on both players at once, not a chosen
// target. Sidekick dice are fungible, so "if able" is just "does one
// exist" - no real choice to make, so (like DrawDice) this bypasses the
// TargetSpec/ResolveTargets choice pipeline entirely rather than stretch
// TargetSpec to express "both players, no chooser, silently skip if none."
public sealed record FieldSidekickForEachPlayer : EffectNode;

// Starfire's Global ("if you purchased a die this turn, Prep a die from
// your bag") - the "if you..." check is against turn-scoped state
// (Player.PurchasedDieThisTurn), not a die's condition, so like
// FieldSidekickForEachPlayer this reads game state directly rather than
// going through Conditional/TargetSpec; the bag pick is fungible, same
// reasoning as DrawDice's.
public sealed record PrepFromBagIfPurchasedThisTurn : EffectNode;
