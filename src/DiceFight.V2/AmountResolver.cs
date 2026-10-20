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
            // MultipleOf's continuous-context resolution (StatAuraModifier
            // reaches this, unlike EffectInterpreter.ResolveAmount's own
            // ability-context copy, since a StatAura has no ability-
            // resolution EffectContext/CapturedStats to read from - see
            // MultipleOf's own remarks for why this reads the BASE stat).
            MultipleOf m => ResolveMultipleOf(state, bindings, m),
            StatOf s => ResolveStatOf(state, bindings, s, includeContinuous),
            _ => throw new NotSupportedException($"Unknown Amount type '{amount.GetType().Name}'."),
        };
    }

    private static int ResolveMultipleOf(GameState state, IReadOnlyDictionary<string, string> bindings, MultipleOf m)
    {
        if (!bindings.TryGetValue(m.Binding, out var dieId))
            throw new InvalidOperationException($"MultipleOf references binding '{m.Binding}', which is not bound to a die.");
        var die = FindDie(state, dieId);
        var baseStat = m.Stat switch
        {
            StatKind.Attack => QueryEngine.GetBaseAttack(state, die),
            StatKind.Defense => QueryEngine.GetBaseDefense(state, die),
            StatKind.PrintedAttack => state.GetCurrentFace(die)?.Character?.Attack ?? 0,
            StatKind.PrintedDefense => state.GetCurrentFace(die)?.Character?.Defense ?? 0,
            _ => throw new NotSupportedException($"MultipleOf doesn't support StatKind.{m.Stat}."),
        };
        return baseStat * m.Multiplier;
    }

    private static int ResolveStatOf(GameState state, IReadOnlyDictionary<string, string> bindings,
        StatOf stat, bool includeContinuous)
    {
        if (!bindings.TryGetValue(stat.Binding, out var id))
            throw new InvalidOperationException($"StatOf references unbound die '{stat.Binding}'.");
        var die = FindDie(state, id);
        var threshold = new StatThreshold(stat.Stat);
        return includeContinuous ? QueryEngine.GetStatValue(state, die, threshold)
            : QueryEngine.GetBaseStatValue(state, die, threshold);
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
            : p.Unit == CountUnit.EnergyTypes
            ? dieMatches.SelectMany(id => state.GetCurrentFace(FindDie(state, id))?.Symbols ?? [])
                .Select(s => s.SymbolId).Where(id => id is "Claw" or "Shell" or "Wing" or "Eye").Distinct().Count()
            : p.Distinct
                ? dieMatches.Select(id => FindDie(state, id).CardId).Where(cardId => cardId is not null).Distinct().Count()
                : dieMatches.Count;

        return count * p.Multiplier;
    }

    private static DieInstance FindDie(GameState state, string dieId) =>
        state.Dice.FirstOrDefault(d => d.Id == dieId)
        ?? throw new InvalidOperationException($"No die with id '{dieId}'.");
}
