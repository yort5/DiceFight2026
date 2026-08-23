using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Everything EffectInterpreter needs to run one ability (V2_PLAN.md Phase
// 5, the v2 counterpart to v1's EffectContext record). Bindings is
// mutable (a class, not a record) because resolving one TargetFilter with
// BindAs mid-tree must be visible to every later node in the same
// ability - "self" and "event" are seeded before Execute ever runs
// (EffectInterpreter.Resolve, from QueuedAbility.SourceDieId/
// EventSubjectDieId). Roller/Random are both required, unlike v1's
// optional versions, since the closed vocabulary's Reroll/DrawToZone/
// DrawAndChooseOne templates always need them - no call site resolves an
// ability without a real (or test-fake) source for either.
public sealed class EffectContext
{
    public required GameState State { get; init; }
    public required AbilityQueue Queue { get; init; }
    public required string ControllerId { get; init; }
    public required TriggerKind Trigger { get; init; }
    public required IDiceRoller Roller { get; init; }
    public required Random Random { get; init; }
    public Dictionary<string, string> Bindings { get; } = [];

    // Rule 3.2.5's per-ability snapshot (see EffectInterpreter's class
    // remarks) - every die's zone/face as of the moment THIS ability's
    // resolution began. Set by EffectInterpreter.Execute, consulted by
    // TargetFilter candidate-pool resolution only; carried through
    // PendingChoice pauses by the continuation closures holding this
    // same context. Null only before Execute has run.
    public IReadOnlyDictionary<string, DieSnapshot>? Snapshot { get; set; }
}

public sealed record DieSnapshot(Model.Zone Zone, int? FaceIndex);
