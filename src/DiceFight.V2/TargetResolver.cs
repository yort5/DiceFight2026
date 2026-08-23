using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// TargetFilter -> candidate id resolution (V2_PLAN.md Phase 5 task 1, a
// port of v1 LegalTargets.Query's good ideas onto the closed 11-field
// filter shape - V2_VOCABULARY.md Part 1). Self/Bound bypass the query
// entirely and resolve straight from the ability's own binding table
// (BindAs/Bound, Finding 9) instead - "self" and "event" are the two
// reserved names (EffectInterpreter seeds them from QueuedAbility.
// SourceDieId/EventSubjectDieId before running anything).
//
// Candidates can mix die ids and player ids when Kind is Player/
// DieOrPlayer - GameState.IsPlayerId is how callers tell them apart (the
// two id spaces don't collide, same convention v1 used).
public static class TargetResolver
{
    // includeContinuous (Phase 6) - false when resolving a CONTINUOUS
    // template's own Target/Whose filter (ContinuousRegistry), so Tags/
    // Stat checks read GetBaseTags/GetBaseStatValue instead of the
    // continuous-inclusive versions; otherwise a self-referential aura
    // (its own Target filtering on a stat/tag it itself grants) would
    // recurse into evaluating itself to answer whether it's active - a
    // real StackOverflow found while building this phase, not a
    // hypothetical. Every other caller (ordinary ability targeting,
    // Amount/Condition resolution) keeps the default `true`.
    public static IReadOnlyList<string> Query(
        GameState state, string requestingControllerId, TargetFilter filter,
        IReadOnlyDictionary<string, string> bindings, ProtectionFrom? protection = null, bool includeContinuous = true)
    {
        if (filter.Self)
        {
            return bindings.TryGetValue("self", out var selfId)
                ? [selfId]
                : throw new InvalidOperationException("TargetFilter.Self has no 'self' binding in this context.");
        }

        if (filter.Bound is { } boundName)
        {
            return bindings.TryGetValue(boundName, out var boundId) ? [boundId] : [];
        }

        var zones = filter.Zones ?? TargetFilter.DefaultZones;
        IEnumerable<DieInstance> dice = filter.Kind == TargetKind.Player
            ? []
            : state.Dice.Where(d => zones.Contains(d.Zone));

        dice = filter.Ownership switch
        {
            TargetOwnership.Own => dice.Where(d => d.ControllerId == requestingControllerId),
            TargetOwnership.Opposing => dice.Where(d => d.ControllerId == state.OpponentOf(requestingControllerId)),
            _ => dice,
        };

        if (filter.Kind == TargetKind.CharacterDie)
            dice = dice.Where(d => state.GetCurrentFace(d)?.Character is not null);
        else if (filter.Kind == TargetKind.ActionDie)
            dice = dice.Where(d => d.CardId is { } cid && state.CardCatalog[cid].CardType == CardType.BasicAction);

        if (protection is { } p)
            dice = dice.Where(d => QueryEngine.CanBeTargeted(state, d, requestingControllerId, p));

        if (filter.Tags is { } tags)
        {
            dice = dice.Where(d =>
            {
                var dieTags = includeContinuous ? QueryEngine.GetTags(state, d) : QueryEngine.GetBaseTags(state, d);
                if (tags.AnyOf is { Count: > 0 } anyOf && !anyOf.Any(dieTags.Contains)) return false;
                if (tags.NoneOf is { Count: > 0 } noneOf && noneOf.Any(dieTags.Contains)) return false;
                return true;
            });
        }

        if (filter.Stat is { } stat)
        {
            dice = dice.Where(d =>
            {
                var value = includeContinuous ? QueryEngine.GetStatValue(state, d, stat) : QueryEngine.GetBaseStatValue(state, d, stat);
                if (stat.Min is { } min && value < min) return false;
                if (stat.Max is { } max && value > max) return false;
                return true;
            });
        }

        var ids = dice.Select(d => d.Id).ToList();

        if (filter.Kind is TargetKind.Player or TargetKind.DieOrPlayer)
        {
            IEnumerable<string> playerIds = filter.Ownership switch
            {
                TargetOwnership.Own => [requestingControllerId],
                TargetOwnership.Opposing => [state.OpponentOf(requestingControllerId)],
                _ => [requestingControllerId, state.OpponentOf(requestingControllerId)],
            };
            ids.AddRange(playerIds);
        }

        return ids;
    }
}
