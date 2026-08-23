using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// EventSubjectDieId (Phase 5) is the "event" binding (V2_VOCABULARY.md
// Part 1's reserved binding name) - the triggering GameEvent's own
// SubjectDie, which is usually NOT the same die as SourceDieId (the
// listening die whose ability this is). Null for Global (using the
// ability IS the trigger - there's no separate event subject) and for
// the three die-less events (TurnStepEntered/PurchaseMade/DiceDrawn).
// EventValue (Spike B) is the triggering event's own numeric payload,
// carried alongside the subject die so an Amount can reference it after
// the event object itself is gone.
public sealed record QueuedAbility(string? SourceDieId, string ControllerId, TriggerKind Trigger, EffectNode Effect, int Sequence, string? EventSubjectDieId = null, int? EventValue = null);

// Rule 3.2 - Timing and Resolution. Ported from v1's AbilityQueue
// (V2_PLAN.md Phase 4 task 2 - "this part of v1 is good"), same FIFO +
// front-of-queue Interrupt shape, just over TriggerKind/EffectNode instead
// of v1's TriggerType/EffectNode. Multiple simultaneously triggered
// abilities resolve one at a time, FIFO (3.2.2); resolving one can itself
// enqueue more, which go to the back. Only Prevent/Redirect-type effects
// may jump the queue (3.2.8, modeled as Interrupt) - nothing calls
// Interrupt yet (that decision belongs to Phase 5's interpreter, which
// knows which effects are Prevent/Redirect-shaped), but the capability is
// part of the queue mechanism itself, faithfully ported now rather than
// bolted on later.
//
// Enqueue-vs-Drain are deliberately separate calls (matching v1's own
// TurnEngine/GamesController split - TurnEngine methods enqueue, the
// caller decides when to drain) rather than auto-draining inside
// TurnEngine: draining runs effects, which needs Phase 5's interpreter
// and can pause mid-resolution for a player choice - a concern this
// phase's mechanism should already accommodate even though nothing
// produces a real pause yet.
public sealed class AbilityQueue
{
    private readonly Queue<QueuedAbility> _queue = new();
    private int _nextSequence;

    public int Count => _queue.Count;
    public bool IsEmpty => _queue.Count == 0;
    public IReadOnlyList<QueuedAbility> Pending => _queue.ToList();

    public QueuedAbility Enqueue(string? sourceDieId, string controllerId, TriggerKind trigger, EffectNode effect, string? eventSubjectDieId = null, int? eventValue = null)
    {
        var ability = new QueuedAbility(sourceDieId, controllerId, trigger, effect, _nextSequence++, eventSubjectDieId, eventValue);
        _queue.Enqueue(ability);
        return ability;
    }

    // Rule 3.2.8 - an interrupting ability happens simultaneously with,
    // and ahead of, whatever is currently at the front of the queue.
    public QueuedAbility Interrupt(string? sourceDieId, string controllerId, TriggerKind trigger, EffectNode effect, string? eventSubjectDieId = null)
    {
        var ability = new QueuedAbility(sourceDieId, controllerId, trigger, effect, _nextSequence++, eventSubjectDieId);
        var rest = _queue.ToArray();
        _queue.Clear();
        _queue.Enqueue(ability);
        foreach (var item in rest) _queue.Enqueue(item);
        return ability;
    }

    // `resolve` actually applies the effect (Phase 5+); if doing so
    // enqueues further abilities, `resolve` should call Enqueue/Interrupt
    // on this same queue before returning, which Drain then picks up.
    // `shouldStop` is checked AFTER each ability (so the one that raised
    // the stop condition still finishes first) for a caller that needs to
    // pause mid-drain - same shape as v1's own Drain.
    public void Drain(Action<QueuedAbility> resolve, Func<bool>? shouldStop = null)
    {
        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            resolve(next);
            if (shouldStop?.Invoke() == true) break;
        }
    }
}
