namespace DiceFight.V2.Model;

// Rule 3.1's player-decision seam, ported from v1 (V2_PLAN.md Phase 5 task
// 2 - "this part of v1 is good"). Every player decision the interpreter
// needs - target selection with a real choice, MayPay yes/no, DrawAndChooseOne
// - creates one of these and stops; GameState.PendingChoice being non-null
// is itself the "paused" signal (nothing else drains the queue further
// until it's answered - see EffectInterpreter.DrainQueue). Resolve is a
// closure captured at creation time (same "pass a closure to finish later"
// seam v1's own PendingChoice used) that validates the answer against
// Min/MaxCount, applies whatever the choice was for, clears
// GameState.PendingChoice, and resumes the rest of the effect tree that
// was waiting on it.
public sealed class PendingChoice
{
    public required string ControllerId { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> CandidateIds { get; init; }
    public required int MinCount { get; init; }
    public required int MaxCount { get; init; }
    public required Action<IReadOnlyList<string>> Resolve { get; init; }
    // The candidate is an answer token, rather than a selectable target.
    public bool IsConfirmation { get; init; }
    // Whether the effect waiting on this choice helps or hurts whatever
    // gets picked - a hint for the computer opponent (Bot/), which
    // otherwise can't tell "deal 1 damage to a target" from "+3A to a
    // target" by candidate ids alone. Set where each effect resolves its
    // target (EffectInterpreter); Unknown leaves the bot to guess.
    public ChoiceIntent Intent { get; init; } = ChoiceIntent.Unknown;
    // The effect waiting on this pick, when the intent alone can't say
    // whether it helps or hurts - a "set D to A" (Archnemesis's Global) is
    // good or bad depending on the die's own stats. Bot use only.
    public Model.Effects.EffectNode? Effect { get; init; }
    // How much damage the waiting DealDamage will deal, when that's known
    // before the target is (a fixed amount, or a stat captured from an
    // earlier pick - Sacrifice's "damage equal to its ATK"). Bot use only.
    public int? DamageHint { get; init; }
}

// NameCard: the pick names a CARD, not a die (RememberCard - Blob/Drax-
// style lockouts): what matters is the card, not which of its dice.
public enum ChoiceIntent { Unknown, Harmful, Beneficial, NameCard }
