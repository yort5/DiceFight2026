using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Queueing;

// Rule 3.2 - Timing and Resolution. Multiple simultaneously triggered
// abilities resolve one at a time, FIFO: "the first ability that enters
// the queue is the first to resolve, and the last ability that enters the
// queue is the last to be resolved" (3.2.2). Resolving one ability can
// itself enqueue new abilities (e.g. a "when attacks" ability damages a
// die with a "when damaged" ability) - those go to the back and are
// processed after everything already queued. This is exactly the FIFO
// queue the rulebook's own worked example (3.2.2) traces through, and
// AbilityQueueTests replicates that example directly.
//
// Only Prevent/Redirect-type effects may jump the queue (rule 3.2.8); that
// is modeled as Interrupt, which inserts at the front instead of the back.
public sealed class AbilityQueue
{
    private readonly Queue<QueuedAbility> _queue = new();
    private int _nextSequence;

    public int Count => _queue.Count;
    public bool IsEmpty => _queue.Count == 0;

    public IReadOnlyList<QueuedAbility> Pending => _queue.ToList();

    public QueuedAbility Enqueue(string? sourceDieId, string controllerId, TriggerType trigger, EffectNode effect)
    {
        var ability = new QueuedAbility(sourceDieId, controllerId, trigger, effect, _nextSequence++);
        _queue.Enqueue(ability);
        return ability;
    }

    // Rule 3.2.8 - an interrupting ability happens simultaneously with,
    // and ahead of, whatever is currently at the front of the queue.
    public QueuedAbility Interrupt(string? sourceDieId, string controllerId, TriggerType trigger, EffectNode effect)
    {
        var ability = new QueuedAbility(sourceDieId, controllerId, trigger, effect, _nextSequence++);
        var rest = _queue.ToArray();
        _queue.Clear();
        _queue.Enqueue(ability);
        foreach (var item in rest) _queue.Enqueue(item);
        return ability;
    }

    // Resolves abilities one at a time until the queue is empty. `resolve`
    // is responsible for actually applying the effect; if doing so triggers
    // further abilities, `resolve` should call Enqueue/Interrupt on this
    // same queue before returning, which Drain will then pick up.
    //
    // `shouldStop` (checked after each ability, not before - so the
    // ability that raised the stop condition still gets to finish its own
    // `resolve` call first) lets a caller pause mid-drain instead of
    // running to completion - see GameState.PendingChoice/PendingQueue's
    // own remarks for why (a mid-resolution choice, e.g. keyword
    // Corrupt, that can't be answered in the same call that triggered
    // it). Anything still left in the queue when this returns early is
    // simply still there for a later Drain call to pick back up -
    // AbilityQueue itself doesn't need to do anything special to
    // preserve it.
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
