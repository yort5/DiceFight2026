using DiceFight.V2.Model;

namespace DiceFight.V2.Model.Effects;

// The 10 events plus paid Global activation as its own trigger kind (not
// an event) - V2_VOCABULARY.md Part 1. Energize/Awaken are NOT their own
// kinds (unlike v1) - they're EventFilters over DieFaceChanged (Finding 1),
// since v1's own CheckAwaken comment is the design precedent: a face-
// change source that skips emission is the silently-never-fires bug
// class, and a roll-only event would have reintroduced exactly that bug.
public enum TriggerKind
{
    DieFielded,
    DieKOd,
    DieDamaged,
    DieAttacks,
    DieBlocks,
    DiceDrawn,
    PurchaseMade,
    TurnStepEntered,
    DieUsed,
    DieFaceChanged,
    Global,
}

public enum FaceChangeCause { Roll, Reroll, Spin, Effect }

// A shared marker so GameEvent (Phase 4's runtime type, DiceFight.V2
// namespace) can carry any event-specific payload without a loose
// `object?`, while still only two of the ten events actually needing one
// (Part 1: "DieDamaged carries the damage amount; DieFaceChanged carries
// {PriorFace, NewFace, Cause}").
public abstract record EventPayload;

// The event-specific payload for DieFaceChanged (V2_VOCABULARY.md Part 1) -
// carries full Face payloads, not just kinds, since Energize needs to
// check symbol count (>= 2) on the new face, not just "is it an energy
// face." Emitted from EVERY face-mutation site - see the class remarks.
public sealed record DieFaceChangedPayload(Face PriorFace, Face NewFace, FaceChangeCause Cause) : EventPayload;

// DieDamaged's own payload - the amount just dealt. Nothing fires
// DieDamaged yet (no damage-dealing mechanic exists before Phase 5's
// DealDamage interpreter / Phase 7's combat), but the payload TYPE is
// part of the already-frozen event design (Part 1), not new scope.
public sealed record DamageDealtPayload(int Amount) : EventPayload;

// Reactive-trigger filter - deliberately a different, smaller shape than
// TargetFilter (it filters ONE event's subject die, not a chosen set).
// Stat (Finding 14) mirrors TargetFilter.Stat, checked against the
// event's own subject die (Deathbird "Usurper" - "when you KO an
// opposing die with 3D or greater").
// Step (Spike C, V2_VOCABULARY.md Part 13) names WHICH timing window a
// TurnStepEntered listener cares about - a step id from the game's own
// ordered step list (StepIds). Without it a listener cannot tell
// TurnStepEntered(cleanup) from TurnStepEntered(select-attackers), which
// is what kept Colossus "Piotr" tailed through DPS batch 1. Null means
// "any step", the pre-Spike-C behavior; it is meaningless for the nine
// non-TurnStepEntered event kinds, which each fire at one point anyway.
public sealed record EventFilter(
    TargetOwnership Ownership = TargetOwnership.Any,
    TagQuery? Tags = null,
    bool ExcludeSelf = false,
    int? MinPurchaseCost = null,
    StatThreshold? Stat = null,
    string? Step = null);

// Rule 2.6.5.4 - a Global ability's energy price. RequiredSymbolId null
// means any energy (including generic) satisfies it.
public sealed record EnergyCost(int Amount, string? RequiredSymbolId = null);

// One authored ability on a card. Filter is only meaningful when Trigger
// is an event kind (null for Trigger == Global, where EnergyCost/
// OncePerTurn matter instead - the same "which fields matter depends on
// Trigger" shape v1's own AbilityDef had, kept because the alternative
// (a Trigger-specific record per kind) would fragment card authoring for
// no real benefit - Phase 5 is where firing logic actually branches).
public sealed record TriggeredAbility(
    TriggerKind Trigger,
    EffectNode Effect,
    EventFilter? Filter = null,
    EnergyCost? EnergyCost = null,
    bool OncePerTurn = false);
