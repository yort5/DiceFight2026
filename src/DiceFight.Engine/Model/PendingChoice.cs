namespace DiceFight.Engine.Model;

// A choice that can't be answered upfront through the normal TargetSpec/
// LegalTargets pipeline (see EffectInterpreter's own remarks on why) -
// its candidates either don't exist until a random draw happens mid-
// resolution (keyword Corrupt) or shouldn't be chosen blind before the
// player has actually seen the result of one (RedrawFromBag - Cosmic
// Cube "Infinite Possibilities", Rip Hunter). AbilityQueue.Drain stops
// the moment one of these gets set (see its own shouldStop param),
// leaving whatever else was still queued untouched on GameState.
// PendingQueue; GamesController.ResolvePendingChoice is the only legal
// next action until it's answered (every other action endpoint guards
// against this - see GamesController.RequireNoPendingChoice).
//
// Resolve is a closure built at the moment this was created, capturing
// whatever it needs (the already-drawn dice, a target zone, the
// original EffectContext) to finish applying the effect once an answer
// arrives - same "pass a closure" seam EffectContext.ResolveTargets
// already uses elsewhere in this engine. Never serialized itself (only
// PendingChoiceDto is, over the API) - GameState as a whole is always
// server-side in-memory only.
public sealed class PendingChoice
{
    public required string ControllerId { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> CandidateDieIds { get; init; }

    // false: exactly one choice (Corrupt - "choose one die"). true: any
    // subset, including none (RedrawFromBag - "you may send any number
    // of them").
    public required bool AllowMultiple { get; init; }

    public required Action<IReadOnlyList<string>> Resolve { get; init; }
}
