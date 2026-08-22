namespace DiceFight.V2.Model.Effects;

// Relative to whoever controls the ability/query at resolution time -
// rule 3.1.4/3.1.5's "own"/"opposing" convention, ported from v1.
public enum TargetOwnership
{
    Any,
    Own,
    Opposing,
}

public enum TargetKind
{
    AnyDie,
    CharacterDie,
    ActionDie,
    Player,
    DieOrPlayer,
}

// The five stat kinds a TargetFilter/EventFilter can threshold on, plus
// Counter for reading a named per-card counter back (Finding 13 -
// V2_VOCABULARY.md Part 1/10). Counter entries must also set
// StatThreshold.CounterName; the other five ignore it.
public enum StatKind
{
    Attack,
    Defense,
    Level,
    PurchaseCost,
    FieldingCost,
    Counter,
}

// ONE threshold (V2_VOCABULARY.md Part 1 - "ONE threshold" is a closed-
// vocabulary decision, not an oversight: a card needing two simultaneous
// stat thresholds hasn't come up, and TargetFilter deliberately doesn't
// generalize to a list until one does).
public sealed record StatThreshold(StatKind Kind, string? CounterName = null, int? Min = null, int? Max = null);

public sealed record TagQuery(IReadOnlyList<string>? AnyOf = null, IReadOnlyList<string>? NoneOf = null);

// The single target-filter shape (V2_VOCABULARY.md Part 1, frozen 2026-08-22,
// Finding 14 gate). BindAs/Bound/AnsweredBy are deliberately on every
// TargetFilter rather than a separate "reactive target" type - the closed-
// vocabulary bet is that one shape covers active targeting, reactive-trigger
// subjects (Bound: "event"), and cross-player-answered offers alike.
public sealed record TargetFilter(
    TargetOwnership Ownership = TargetOwnership.Any,
    IReadOnlyList<Zone>? Zones = null,
    TargetKind Kind = TargetKind.CharacterDie,
    int Count = 1,
    TagQuery? Tags = null,
    StatThreshold? Stat = null,
    bool Optional = false,
    bool Self = false,
    string? BindAs = null,
    string? Bound = null,
    TargetOwnership AnsweredBy = TargetOwnership.Own)
{
    // Rule 3.3.4/3.3.5 - only Field Zone (which includes Attack Zone) is
    // targetable by default.
    public static readonly IReadOnlyList<Zone> DefaultZones = [Zone.FieldZone, Zone.AttackZone];
}

// Fixed(n): a literal amount (n may be negative - LifeChange's own signed-
// amount convention, positive = gain, negative = lose). PerMatch: a live
// count of TargetFilter matches at resolution time, times a multiplier;
// Distinct/Unit are Finding 14 (distinct-name counting, and counting energy
// symbols shown rather than dice). A live-value source ("this die's own
// current stat", "the triggering event's own amount") is deliberately NOT
// part of this closed set - that's the deferred live-value-Amounts spike
// (V2_VOCABULARY.md Part 4/11).
public abstract record Amount;
public sealed record Fixed(int Value) : Amount;
public sealed record PerMatch(TargetFilter Filter, int Multiplier, bool Distinct = false, CountUnit Unit = CountUnit.Dice) : Amount;

public enum CountUnit
{
    Dice,
    EnergySymbols,
}

// Effect/grant lifetime. UntilYourNextTurn is Finding 14 (Swords of
// Revealing Light/Vicious Struggle's "until your next turn" text, a real
// third duration alongside the original two).
public enum Duration
{
    EndOfTurn,
    UntilYourNextTurn,
    Permanent,
}
