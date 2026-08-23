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
}
