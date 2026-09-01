using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Amount resolution (Fixed | PerMatch) - extracted in Phase 6 so
// EffectInterpreter (an ability's own Amount fields) and
// ContinuousRegistry (a StatAura's AtkDelta/DefDelta) share the identical
// Fixed/PerMatch/Distinct/Unit logic (V2_VOCABULARY_HISTORY.md Part 1) instead of
// each having their own copy. Bindings lets a PerMatch filter reference
// "self" (or any other bound name) the same way TargetFilter resolution
// does - a continuous aura seeds "self" with its own source die id, an
// ability seeds it from QueuedAbility.SourceDieId.
public static class AmountResolver
{
    private static readonly Dictionary<string, string> EmptyBindings = [];

    public static int Resolve(GameState state, string controllerId, Amount amount, IReadOnlyDictionary<string, string>? bindings = null, ProtectionFrom? protection = null, bool includeContinuous = true)
    {
        bindings ??= EmptyBindings;
        return amount switch
        {
            Fixed f => f.Value,
            PerMatch p => ResolvePerMatch(state, controllerId, p, bindings, protection, includeContinuous),
            _ => throw new NotSupportedException($"Unknown Amount type '{amount.GetType().Name}'."),
        };
    }

    private static int ResolvePerMatch(GameState state, string controllerId, PerMatch p, IReadOnlyDictionary<string, string> bindings, ProtectionFrom? protection, bool includeContinuous)
    {
        var matches = TargetResolver.Query(state, controllerId, p.Filter, bindings, protection, includeContinuous);
        var dieMatches = matches.Where(id => !state.IsPlayerId(id)).ToList();

        // Finding 14's two count units: EnergySymbols sums the pips
        // currently shown, Distinct counts different card names rather
        // than raw die count - both mutually meaningful with Dice, the
        // default (plain count of matching dice).
        var count = p.Unit == CountUnit.EnergySymbols
            ? dieMatches.Sum(id => state.GetCurrentFace(FindDie(state, id))?.Symbols.Sum(s => s.Count) ?? 0)
            : p.Distinct
                ? dieMatches.Select(id => FindDie(state, id).CardId).Where(cardId => cardId is not null).Distinct().Count()
                : dieMatches.Count;

        return count * p.Multiplier;
    }

    private static DieInstance FindDie(GameState state, string dieId) =>
        state.Dice.FirstOrDefault(d => d.Id == dieId)
        ?? throw new InvalidOperationException($"No die with id '{dieId}'.");
}
