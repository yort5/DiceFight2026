using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Fires a GameEvent against every active die's own TriggeredAbility list
// and enqueues the matches (V2_PLAN.md Phase 4 tasks 1+3) - the
// replacement for v1's TriggerType enum + three *DieMatch filter records
// (one event shape, one filter shape, per V2_VOCABULARY.md Part 1).
public static class EventBus
{
    // Rule 3.2.2's own ordering, ported from v1: the ACTIVE player's
    // matching triggers enqueue first, then the inactive player's; FIFO
    // within each side (enumeration order over each player's own active
    // dice). "Active dice" = Field Zone + Attack Zone, the scope every
    // OTHER-die reactive trigger in this codebase uses.
    //
    // The event's own SubjectDie is always added as an extra listener
    // candidate (for whichever controller it belongs to), even when it
    // isn't currently "active" by that definition - self-only abilities
    // need to fire from the exact die the event is about regardless of
    // its zone at the moment the event fires: Energize/Awaken react to a
    // die still in the Prep Area mid-roll (v1's CheckEnergize/CheckAwaken
    // have no zone gate at all, for exactly this reason), and a "when I
    // am KO'd" ability's own die has already left the Field/Attack Zone
    // by the time DieKOd fires. Found while testing DieFaceChanged, but
    // it's a real, general gap, not specific to that one event kind.
    public static void Fire(GameState state, AbilityQueue queue, GameEvent evt)
    {
        foreach (var controllerId in new[] { state.ActivePlayerId, state.OpponentOf(state.ActivePlayerId) })
        {
            var candidates = ActiveDice(state, controllerId).ToList();
            if (evt.SubjectDie is { } subject && subject.ControllerId == controllerId && !candidates.Contains(subject))
                candidates.Add(subject);

            foreach (var listener in candidates)
            {
                if (listener.CardId is not { } cardId) continue;
                var card = state.CardCatalog[cardId];

                foreach (var ability in card.Abilities)
                {
                    if (Matches(state, evt, listener, ability))
                        queue.Enqueue(listener.Id, listener.ControllerId, ability.Trigger, ability.Effect, evt.SubjectDie?.Id);
                }
            }
        }
    }

    private static IEnumerable<DieInstance> ActiveDice(GameState state, string controllerId) =>
        state.DiceIn(controllerId, Zone.FieldZone).Concat(state.DiceIn(controllerId, Zone.AttackZone));

    private static bool Matches(GameState state, GameEvent evt, DieInstance listener, TriggeredAbility ability)
    {
        if (ability.Trigger != evt.Kind) return false;

        if (ability.Filter is null)
        {
            // No filter = "this is about me" - the common "When [this
            // card] is fielded/attacks/KO'd/..." pattern that covers the
            // overwhelming majority of real card text. For the three
            // events with no single subject die (TurnStepEntered,
            // PurchaseMade, DiceDrawn), there's no die to compare against,
            // so null instead means unconstrained - "whenever this
            // happens, regardless of whose."
            return evt.SubjectDie is null || evt.SubjectDie.Id == listener.Id;
        }

        return MatchesFilter(state, evt, listener, ability.Filter);
    }

    private static bool MatchesFilter(GameState state, GameEvent evt, DieInstance listener, EventFilter filter)
    {
        if (filter.Ownership != TargetOwnership.Any)
        {
            var isOwn = evt.SubjectControllerId == listener.ControllerId;
            if ((filter.Ownership == TargetOwnership.Own) != isOwn) return false;
        }

        if (filter.ExcludeSelf && evt.SubjectDie?.Id == listener.Id) return false;

        if (filter.Tags is { } tags)
        {
            if (evt.SubjectDie is not { } subjectForTags) return false; // a tag filter needs a die to read tags from
            var subjectTags = QueryEngine.GetTags(state, subjectForTags);
            if (tags.AnyOf is { Count: > 0 } anyOf && !anyOf.Any(subjectTags.Contains)) return false;
            if (tags.NoneOf is { Count: > 0 } noneOf && noneOf.Any(subjectTags.Contains)) return false;
        }

        if (filter.MinPurchaseCost is { } minCost)
        {
            var cost = evt.SubjectDie?.CardId is { } cid ? state.CardCatalog[cid].PurchaseCost : (int?)null;
            if (cost is null || cost < minCost) return false;
        }

        if (filter.Stat is { } stat)
        {
            if (evt.SubjectDie is not { } subjectForStat) return false;
            var value = QueryEngine.GetStatValue(state, subjectForStat, stat);
            if (stat.Min is { } min && value < min) return false;
            if (stat.Max is { } max && value > max) return false;
        }

        return true;
    }
}
