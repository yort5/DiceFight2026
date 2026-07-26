using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Queueing;

// One ability that has entered the queue (rule 3.2). SequenceNumber is
// assignment order, not necessarily resolution order once interrupts are
// involved - kept mainly for debugging/inspection in tests. SourceDieId is
// null for Global abilities, which are sourced from the paying player
// rather than a specific die (rule 3.1.5).
public sealed record QueuedAbility(
    string? SourceDieId,
    string ControllerId,
    TriggerType Trigger,
    EffectNode Effect,
    int SequenceNumber);
