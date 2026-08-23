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

    // Spike B - each binding's stats AS OF THE MOMENT IT WAS BOUND.
    // Kept as a parallel dictionary rather than folded into Bindings so
    // that TargetResolver/ConditionEvaluator, which only ever want a die
    // id, need no changes at all. Populated by EffectInterpreter.Bind.
    public Dictionary<string, IReadOnlyDictionary<StatKind, int>> CapturedStats { get; } = [];

    // The triggering event's numeric payload, if it had one
    // (DamageDealtPayload.Amount today). Null for events that carry no
    // number and for directly-invoked effects.
    public int? EventValue { get; init; }

    // Rule 3.2.5's per-ability snapshot (see EffectInterpreter's class
    // remarks) - every die's zone/face as of the moment THIS ability's
    // resolution began. Set by EffectInterpreter.Execute, consulted by
    // TargetFilter candidate-pool resolution only; carried through
    // PendingChoice pauses by the continuation closures holding this
    // same context. Null only before Execute has run.
    public IReadOnlyDictionary<string, DieSnapshot>? Snapshot { get; set; }

    // Binds a name to a die AND snapshots that die's base stats (Spike
    // B). The snapshot is the point: an Amount referencing this binding
    // later reads the value as of NOW, before any later clause of the
    // same ability modifies it - which is what makes a two-way stat swap
    // actually swap. Lives here rather than on EffectInterpreter so that
    // no caller can seed a binding without also capturing it.
    //
    // Player ids are bindable (Kind: Player filters) and simply have no
    // stats to capture. Base stats, not static-inclusive - see StatOf.
    public void Bind(string name, string id)
    {
        Bindings[name] = id;
        if (State.IsPlayerId(id)) return;
        if (State.Dice.FirstOrDefault(d => d.Id == id) is not { } die) return;

        CapturedStats[name] = new Dictionary<StatKind, int>
        {
            [StatKind.Attack] = QueryEngine.GetBaseAttack(State, die),
            [StatKind.Defense] = QueryEngine.GetBaseDefense(State, die),
            [StatKind.Level] = State.GetCurrentFace(die)?.Character?.Level ?? 0,
            [StatKind.FieldingCost] = QueryEngine.GetBaseFieldingCost(State, die),
            [StatKind.PurchaseCost] = die.CardId is { } cardId ? QueryEngine.GetBasePurchaseCost(State.CardCatalog[cardId]) : 0,
        };
    }
}

public sealed record DieSnapshot(Model.Zone Zone, int? FaceIndex);
