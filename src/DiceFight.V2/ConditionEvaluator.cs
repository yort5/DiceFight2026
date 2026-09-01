using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// The 7 condition kinds (V2_VOCABULARY_HISTORY.md Part 1), each a pure read of
// current GameState - no PendingChoice involved anywhere here, matching
// V2_PLAN.md Phase 5's own framing ("conditions are pure state reads, so
// [live ActiveWhen evaluation] is safe"). CheckBinding names resolve
// against the same binding table TargetFilter.BindAs/Bound populate;
// "self" and "event" are always seeded before a condition ever runs.
public static class ConditionEvaluator
{
    // includeContinuous (Phase 6) - forwarded to CountAtLeast's own
    // TargetResolver.Query call; false when this Condition is a
    // continuous template's ActiveWhen gate (ContinuousRegistry), for
    // the identical self-referential-cycle reason TargetResolver.Query's
    // own includeContinuous param exists.
    public static bool Evaluate(GameState state, string controllerId, Condition condition, IReadOnlyDictionary<string, string> bindings, bool includeContinuous = true) => condition switch
    {
        CountAtLeast c => TargetResolver.Query(state, controllerId, c.Filter, bindings, includeContinuous: includeContinuous).Count >= c.N,

        // Rule 1.5.3.2 - a KO'd die lands in its owner's Prep Area,
        // unrolled. That's the only way a die legitimately ends up
        // dormant-in-Prep-Area mid-ability-resolution (a plain draw
        // lands rolled dice in the Reserve Pool, not this), so it's a
        // safe, faithful "was this bound die just KO'd" signal - same
        // predicate v1's own TargetWasKOd check uses.
        TargetWasKOd c => bindings.TryGetValue(c.CheckBinding, out var koId) &&
            state.Dice.FirstOrDefault(d => d.Id == koId) is { Zone: Zone.PrepArea, CurrentFaceIndex: null },

        OnBurstFace c => bindings.TryGetValue(c.CheckBinding, out var burstId) &&
            state.Dice.FirstOrDefault(d => d.Id == burstId) is { } burstDie &&
            state.GetCurrentFace(burstDie) is { } burstFace &&
            burstFace.Burst == (c.Level == BurstLevel.Double ? 2 : 1),

        LifeComparison c => c.Op switch
        {
            LifeComparisonOperator.OwnLessThanOpponent =>
                state.GetPlayer(controllerId).Life < state.GetPlayer(state.OpponentOf(controllerId)).Life,
            _ => false,
        },

        // No "this turn" bookkeeping exists yet to answer this precisely
        // (no KO has ever been tracked turn-scoped before Phase 5) - see
        // the class remarks below for the GameState.CharacterDiceKOdThisTurn
        // tracker this condition reads, populated by EffectInterpreter's
        // own KoDie helper.
        NoKOsThisTurn c => c.Scope switch
        {
            KoScope.Any => state.CharacterDiceKOdThisTurn.Count == 0,
            KoScope.Own => !state.CharacterDiceKOdThisTurn.Contains(controllerId),
            _ => true,
        },

        TurnFact c => c.Fact switch
        {
            TurnFactKind.PurchasedThisTurn => state.PurchasedThisTurn.Contains(controllerId),
            TurnFactKind.FieldedNoOtherCharacterThisTurn => !state.FieldedCharacterThisTurn.Contains(controllerId),
            TurnFactKind.PrepAreaEmpty => !state.DiceIn(controllerId, Zone.PrepArea).Any(),
            _ => false,
        },

        OnFaceKind c => bindings.TryGetValue(c.CheckBinding, out var faceId) &&
            state.Dice.FirstOrDefault(d => d.Id == faceId) is { } faceDie &&
            state.GetCurrentFace(faceDie) is { } face &&
            face.Kind == c.Kind,

        _ => false,
    };
}
