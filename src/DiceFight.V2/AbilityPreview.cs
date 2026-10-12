using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Rule 3.1.10 - "Energy cannot be spent, or an ability initiated, under
// any of the following conditions: (1) there would be no result or
// potential result that changes the game state; (2) there are no legal
// targets available" (2.6.4.6 applies it to Action dice). The interpreter
// already FIZZLES an effect with nothing to act on at resolution; this is
// the up-front half, for abilities a player chooses to initiate (a Global,
// using an Action die) - so a Resurrection Global can't be paid for with
// nothing left to draw (user, 2026-09-28). Triggered abilities (Mountain
// Goat's On Attack draw) are NOT gated by this: the die still attacks and
// the draw just fizzles.
//
// Deliberately permissive: anything it can't cheaply judge counts as
// "could have a result", so this only ever rules out the clear cases
// (an empty draw, a target filter matching nothing).
public static class AbilityPreview
{
    public static bool CouldHaveResult(GameState state, string controllerId, EffectNode effect, ProtectionFrom? protection) => effect switch
    {
        // A step bound to an earlier step's pick can't be judged alone, so
        // a sequence needs one independently judgeable step with a result.
        Sequence s => s.Steps.Any(step => IndependentlyUseful(state, controllerId, step, protection)),
        _ => IndependentlyUseful(state, controllerId, effect, protection) || TargetOf(effect) is { Bound: not null },
    };

    private static bool IndependentlyUseful(GameState state, string controllerId, EffectNode effect, ProtectionFrom? protection)
    {
        if (effect is Conditional conditional)
            return ConditionEvaluator.Evaluate(state, controllerId, conditional.When, new Dictionary<string, string>())
                ? CouldHaveResult(state, controllerId, conditional.Then, protection)
                : conditional.Else is { } alternative && CouldHaveResult(state, controllerId, alternative, protection);
        if (effect is MayPay offer)
            return CouldHaveResult(state, controllerId, offer.Then, protection);
        if (effect is Sequence) return CouldHaveResult(state, controllerId, effect, protection);
        if (effect is DrawToZone draw)
            return draw.FromZone == Zone.Bag
                ? state.DiceIn(controllerId, Zone.Bag).Any() || state.DiceIn(controllerId, Zone.UsedPile).Any() // an empty Bag refills from the Used Pile
                : state.DiceIn(controllerId, draw.FromZone).Any();
        // A purchase discount only matters to the player who can still buy
        // this turn - it's discarded at Clean Up.
        if (effect is PurchaseModifier)
            return controllerId == state.ActivePlayerId && state.CurrentStep == TurnStep.Main;
        if (TargetOf(effect) is not { } filter) return true;
        if (filter.Bound is not null) return false;
        if (filter.Self) return true;
        return TargetResolver.Query(state, controllerId, filter, new Dictionary<string, string>(), protection).Count > 0;
    }

    private static TargetFilter? TargetOf(EffectNode effect) => effect switch
    {
        DealDamage n => n.Target,
        Ko n => n.Target,
        MoveDie n => n.Target,
        Sacrifice n => n.Target,
        SpinByOwner n => n.Target,
        FieldDie n => n.Target,
        Reroll n => n.Target,
        Spin n => n.Target,
        SpinToEnergy n => n.Target,
        ModifyStat n => n.Target,
        GrantTag n => n.Target,
        GrantAbility n => n.Target,
        BlankText n => n.Target,
        CombatFlag n => n.Target,
        _ => null,
    };
}
