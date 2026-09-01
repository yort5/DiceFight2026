using DiceFight.V2.Model;

namespace DiceFight.V2.Model.Effects;

// The 10 events plus paid Global activation as its own trigger kind (not
// an event) - V2_VOCABULARY_HISTORY.md Part 1. Energize/Awaken are NOT their own
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

// The event-specific payload for DieFaceChanged (V2_VOCABULARY_HISTORY.md Part 1) -
// carries full Face payloads, not just kinds, since Energize needs to
// check symbol count (>= 2) on the new face, not just "is it an energy
// face." Emitted from EVERY face-mutation site - see the class remarks.
//
// PriorFace is nullable (2026-09-01, Energize correction, Part 30): a
// die's first-ever face assignment (drawn from the Bag, rolled for the
// first time) has none to report a change FROM, which is fine for
// LevelIncreased (Awaken genuinely cannot fire without a prior level to
// have risen from - EventBus's own check already null-checks it) but
// was WRONG for Energize's Stat-threshold check, which only cares about
// NewFace and needs nothing to compare against. The old design skipped
// emission entirely on a null prior face (v1's Awaken/Energize gate bug
// precedent, ported forward) - which silently meant a card drawn-and-
// rolled by an ability (Mutant Research Program, Groot) could never
// Energize even landing on double energy, since no event fired at all.
// Now every face-mutation site fires unconditionally, prior face or not.
public sealed record DieFaceChangedPayload(Face? PriorFace, Face NewFace, FaceChangeCause Cause) : EventPayload;

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
// Step (Spike C, V2_VOCABULARY_HISTORY.md Part 13) names WHICH timing window a
// TurnStepEntered listener cares about - a step id from the game's own
// ordered step list (StepIds). Without it a listener cannot tell
// TurnStepEntered(cleanup) from TurnStepEntered(select-attackers), which
// is what kept Colossus "Piotr" tailed through DPS batch 1. Null means
// "any step", the pre-Spike-C behavior; it is meaningless for the nine
// non-TurnStepEntered event kinds, which each fire at one point anyway.
public sealed record EventFilter(
    TargetOwnership Ownership = TargetOwnership.Any,
    TagQuery? Tags = null,
    // Same split as TargetFilter's - see its own remarks.
    TagQuery? Affiliations = null,
    bool ExcludeSelf = false,
    // Keyword Awaken: "every time this die spins UP one or more levels,
    // regardless of what caused the spin". A DieFaceChanged whose payload
    // shows a higher character level than before - which is why this is a
    // predicate on the existing event rather than a trigger kind of its
    // own. Cause is deliberately not checked: an Amplify, an ability's
    // Spin and a reroll that lands higher all count.
    bool LevelIncreased = false,
    // Keyword Teamwatch: "when a character with Teamwatch is active and
    // you field a DIFFERENT character die with the SAME AFFILIATION, use
    // their Teamwatch ability". The affiliation is not a fixed value - it
    // is whatever the LISTENING die has - so it cannot be written as an
    // Affiliations TagQuery, which is why this is its own flag.
    //
    // Pair it with ExcludeSelf; "a different character die" is the other
    // half of the same sentence.
    bool SharesAffiliationWithListener = false,
    int? MinPurchaseCost = null,
    StatThreshold? Stat = null,
    string? Step = null,
    // 2026-09-01, Energize correction (Part 30) - a filter with OTHER
    // predicates set (LevelIncreased, ExcludeCause, ...) is no longer
    // automatically self-only the way a null Filter is (Matches' own
    // shortcut only applies when Filter is null at all). Awaken's own
    // LevelIncreased flag had exactly this gap - proven by a direct test,
    // not theorized: an unrelated die also carrying an Awaken-shaped
    // ability would incorrectly react to a DIFFERENT die's spin-up,
    // since LevelIncreased only inspects the payload, never the
    // listener's own identity. RequireSelf is the general fix - true
    // wherever a filtered reaction is conceptually "about ME," same as
    // ExcludeSelf is the general fix for "about someone ELSE."
    bool RequireSelf = false,
    // Energize's own carve-out: "During the Roll and Reroll Step, only
    // check at the end of the Step" (deferred - TurnStepEntered(Main) -
    // see DpsCards.Energize) is scoped to that ONE step's own roll, not
    // to every face change. Excluding Cause.Roll is how "not that step's
    // own initial roll" is expressed - nothing in the engine can cause a
    // Reroll/Spin/Effect face change WHILE mid-Roll-and-Reroll today (no
    // interactive reroll is wired up, no ability resolves mid-step), so
    // excluding Cause.Roll and excluding "during Roll and Reroll" are the
    // same set for now. Revisit if interactive rerolling changes that.
    FaceChangeCause? ExcludeCause = null);

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
