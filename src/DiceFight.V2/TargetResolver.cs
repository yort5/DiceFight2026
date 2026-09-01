using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// TargetFilter -> candidate id resolution (V2_PLAN.md Phase 5 task 1, a
// port of v1 LegalTargets.Query's good ideas onto the closed 11-field
// filter shape - V2_VOCABULARY_HISTORY.md Part 1). Self/Bound bypass the query
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
    //
    // snapshot (rule 3.2.5, see EffectInterpreter's class remarks) - when
    // present, ZONE and FACE-KIND eligibility read the resolving ability's
    // own start-of-resolution snapshot instead of live state, so an
    // earlier clause's own mutations (a KO landing dice in the Prep Area)
    // can't grow or shrink a later clause's candidate pool mid-ability.
    // Tag/Stat/protection checks stay live - a die's identity-tags don't
    // move mid-ability, and stats are the queries' own concern. Null for
    // every non-ability caller (conditions, PerMatch amounts, continuous
    // templates), which all deliberately read live state.
    public static IReadOnlyList<string> Query(
        GameState state, string requestingControllerId, TargetFilter filter,
        IReadOnlyDictionary<string, string> bindings, ProtectionFrom? protection = null, bool includeContinuous = true,
        IReadOnlyDictionary<string, DieSnapshot>? snapshot = null)
    {
        Zone ZoneOf(DieInstance d) => snapshot is not null && snapshot.TryGetValue(d.Id, out var s) ? s.Zone : d.Zone;
        Face? FaceOf(DieInstance d) => snapshot is not null && snapshot.TryGetValue(d.Id, out var s)
            ? (s.FaceIndex is { } i ? state.GetDieDefinition(d).Faces[i] : null)
            : state.GetCurrentFace(d);
        // Self/Bound resolve to ONE already-known id rather than scanning
        // the board - that's the whole point (Finding 9). But "already
        // known" only fixes WHICH die; an author-attached Tags/
        // Affiliations/Stat filter is still a real question about that
        // die's CURRENT state (Energize: "is the die I already am showing
        // a double-energy face right now" - CountAtLeast(Self, Stat:
        // SymbolCount>=2), which needs Self to be able to fail, not just
        // echo back unconditionally). Zones/Kind/Ownership stay bypassed -
        // meaningless for a die whose identity is already fixed.
        string? fixedId = null;
        if (filter.Self)
        {
            fixedId = bindings.TryGetValue("self", out var selfId)
                ? selfId
                : throw new InvalidOperationException("TargetFilter.Self has no 'self' binding in this context.");
        }
        else if (filter.Bound is { } boundName)
        {
            if (!bindings.TryGetValue(boundName, out var fixedBoundId)) return [];
            fixedId = fixedBoundId;
        }

        if (fixedId is not null)
        {
            if (state.IsPlayerId(fixedId)) return [fixedId]; // Tags/Affiliations/Stat are die-only questions
            var fixedDie = state.Dice.First(d => d.Id == fixedId);

            if (filter.Affiliations is { } fixedAffiliations)
            {
                var dieAffiliations = QueryEngine.GetAffiliations(state, fixedDie);
                if (fixedAffiliations.AnyOf is { Count: > 0 } anyOfAff && !anyOfAff.Any(dieAffiliations.Contains)) return [];
                if (fixedAffiliations.NoneOf is { Count: > 0 } noneOfAff && noneOfAff.Any(dieAffiliations.Contains)) return [];
            }

            if (filter.Tags is { } fixedTags)
            {
                var dieTags = includeContinuous ? QueryEngine.GetTags(state, fixedDie) : QueryEngine.GetBaseTags(state, fixedDie);
                if (fixedTags.AnyOf is { Count: > 0 } anyOf && !anyOf.Any(dieTags.Contains)) return [];
                if (fixedTags.NoneOf is { Count: > 0 } noneOf && noneOf.Any(dieTags.Contains)) return [];
            }

            if (filter.Stat is { } fixedStat)
            {
                var value = includeContinuous ? QueryEngine.GetStatValue(state, fixedDie, fixedStat) : QueryEngine.GetBaseStatValue(state, fixedDie, fixedStat);
                if (fixedStat.Min is { } min && value < min) return [];
                if (fixedStat.Max is { } max && value > max) return [];
            }

            return [fixedId];
        }

        var zones = filter.Zones ?? TargetFilter.DefaultZones;
        IEnumerable<DieInstance> dice = filter.Kind == TargetKind.Player
            ? []
            : state.Dice.Where(d => zones.Contains(ZoneOf(d)));

        dice = filter.Ownership switch
        {
            TargetOwnership.Own => dice.Where(d => d.ControllerId == requestingControllerId),
            TargetOwnership.Opposing => dice.Where(d => d.ControllerId == state.OpponentOf(requestingControllerId)),
            _ => dice,
        };

        if (filter.Kind == TargetKind.CharacterDie)
            dice = dice.Where(d => FaceOf(d)?.Character is not null);
        else if (filter.Kind == TargetKind.ActionDie)
            dice = dice.Where(d => d.CardId is { } cid && state.CardCatalog[cid].CardType.IsActionDie());
        else if (filter.Kind == TargetKind.BasicActionDie)
            dice = dice.Where(d => d.CardId is { } cid && state.CardCatalog[cid].CardType.IsCommunity());

        if (protection is { } p)
            dice = dice.Where(d => QueryEngine.CanBeTargeted(state, d, requestingControllerId, p));

        if (filter.Affiliations is { } affiliations)
        {
            dice = dice.Where(d =>
            {
                // No includeContinuous split here, unlike Tags: nothing
                // grants an affiliation continuously (see GetAffiliations'
                // own remarks), so there is no self-referential aura to
                // break the way TagAuras needed GetBaseTags.
                var dieAffiliations = QueryEngine.GetAffiliations(state, d);
                if (affiliations.AnyOf is { Count: > 0 } anyOf && !anyOf.Any(dieAffiliations.Contains)) return false;
                if (affiliations.NoneOf is { Count: > 0 } noneOf && noneOf.Any(dieAffiliations.Contains)) return false;
                return true;
            });
        }

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
