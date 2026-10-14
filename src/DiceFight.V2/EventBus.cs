using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Fires a GameEvent against every active die's own TriggeredAbility list
// and enqueues the matches (V2_PLAN.md Phase 4 tasks 1+3) - the
// replacement for v1's TriggerType enum + three *DieMatch filter records
// (one event shape, one filter shape, per V2_VOCABULARY_HISTORY.md Part 1).
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
    // its zone at the moment the event fires: Energize reacts to a
    // die still in the Prep Area mid-roll, and a "when I
    // am KO'd" ability's own die has already left the Field/Attack Zone
    // by the time DieKOd fires. Found while testing DieFaceChanged, but
    // it's a real, general gap, not specific to that one event kind.
    public static void Fire(GameState state, AbilityQueue queue, GameEvent evt)
    {
        var firedCards = new HashSet<(string Controller, string Card, TriggeredAbility Ability)>();
        foreach (var controllerId in new[] { state.ActivePlayerId, state.OpponentOf(state.ActivePlayerId) })
        {
            var candidates = ActiveDice(state, controllerId).ToList();

            // Energize carve-out (2026-09-01): the rule text itself says
            // the ability "does not need to be active to trigger" - the
            // same "regardless of zone" shape the SubjectDie exception
            // above already established for self-only reactions. Scoped
            // narrowly to TurnStepEntered(Main), the one moment this
            // matters (v1's CheckEnergize precedent scans exactly
            // Zone.ReservePool, where a just-rolled die now sits after
            // FinishRoll) - NOT a general widening, so Colossus "Piotr"-
            // style "while active" TurnStepEntered abilities (CleanUp,
            // etc.) keep seeing only Field/Attack Zone dice.
            if (evt.Kind == TriggerKind.TurnStepEntered && evt.Step == StepIds.Main)
                candidates.AddRange(state.DiceIn(controllerId, Zone.ReservePool));

            if (evt.SubjectDie is { } subject && subject.ControllerId == controllerId && !candidates.Contains(subject))
                candidates.Add(subject);

            if (evt.Payload is PaymentPayload payment)
                candidates.AddRange(payment.Payment.Spent.Select(p => p.DieId).Distinct()
                    .Select(id => state.Dice.First(d => d.Id == id))
                    .Where(d => d.ControllerId == controllerId && !candidates.Contains(d)));

            foreach (var listener in candidates)
            {
                // No cardless-die skip here any more. A Sidekick has no
                // card and so no printed abilities, but it can be GRANTED
                // one (Lantern Ring), and AbilitiesOf already returns
                // nothing for a die that has neither.
                foreach (var ability in QueryEngine.AbilitiesOf(state, listener))
                {
                    // Spent dice may listen off-field only to explicitly payment-aware abilities.
                    if (listener.Zone is not (Zone.FieldZone or Zone.AttackZone) &&
                        listener != evt.SubjectDie &&
                        !(evt.Kind == TriggerKind.TurnStepEntered && evt.Step == StepIds.Main && listener.Zone == Zone.ReservePool) &&
                        ability.Filter?.Payment is null) continue;
                    if (Matches(state, evt, listener, ability) &&
                        (!ability.OncePerCard || firedCards.Add((listener.ControllerId, listener.CardId ?? listener.Id, ability))))
                        queue.Enqueue(listener.Id, listener.ControllerId, ability.Trigger, ability.Effect, evt.SubjectDie?.Id,
                            (evt.Payload as DamageDealtPayload)?.Amount);
                }
            }
            foreach (var grant in state.GrantedTriggeredAbilities.Where(g => g.ControllerId == controllerId))
            {
                var source = state.Dice.FirstOrDefault(d => d.Id == grant.SourceDieId);
                if (grant.Ability.Trigger == evt.Kind && (grant.Ability.Filter is { } filter
                    ? MatchesFilter(state, evt, source, grant.ControllerId, filter)
                    : source is null || evt.SubjectDie is null || evt.SubjectDie.Id == source.Id) &&
                    (!grant.Ability.OncePerCard || firedCards.Add((grant.ControllerId, source?.CardId ?? grant.SourceName, grant.Ability))))
                    queue.Enqueue(grant.SourceDieId, grant.ControllerId, grant.Ability.Trigger, grant.Ability.Effect,
                        evt.SubjectDie?.Id, (evt.Payload as DamageDealtPayload)?.Amount, grant.SourceName, isGranted: true);
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

        return MatchesFilter(state, evt, listener, listener.ControllerId, ability.Filter);
    }

    private static bool MatchesFilter(GameState state, GameEvent evt, DieInstance? listener, string controllerId, EventFilter filter)
    {
        if (filter.RequireActive && listener?.Zone is not (Zone.FieldZone or Zone.AttackZone)) return false;
        // Spike C - which timing window this listener wants. Checked
        // first: it's the cheapest test and the most likely to exclude.
        if (filter.Step is { } step && evt.Step != step) return false;
        if (filter.ExcludeStep is { } excluded && evt.Step == excluded) return false;

        if (filter.Ownership != TargetOwnership.Any)
        {
            var isOwn = evt.SubjectControllerId == controllerId;
            if ((filter.Ownership == TargetOwnership.Own) != isOwn) return false;
        }

        if (filter.Payment is { } requirement)
        {
            if (evt.Payload is not PaymentPayload paid) return false;
            if (requirement.Purpose is { } purpose && paid.Purpose != purpose) return false;
            if (requirement.SymbolId is { } symbol && !paid.Payment.Spent.Any(p => p.SymbolId == symbol && p.Count > 0)) return false;
            if (requirement.PaidForCardType is { } type &&
                (state.Dice.First(d => d.Id == paid.PaidForDieId).CardId is not { } paidCard ||
                 state.CardCatalog[paidCard].CardType != type)) return false;
            if (requirement.OnlyListenerCard)
            {
                if (listener?.CardId is null || paid.Payment.VirtualEnergy != 0 || paid.Payment.Spent.Count == 0 ||
                    paid.Payment.Spent.Any(p => p.CardId != listener?.CardId)) return false;
                // An exclusive-source purchase rule resolves once, even with several copies paying.
                if (paid.Payment.Spent[0].DieId != listener?.Id) return false;
            }
        }

        if (filter.SameCardAsListener && (listener is null || evt.SubjectDie?.CardId is null || evt.SubjectDie.CardId != listener?.CardId)) return false;

        if (filter.ExcludeSelf && evt.SubjectDie?.Id == listener?.Id) return false;

        // Part 30 - the general "this reaction is about ME" gate a filter
        // with other predicates set doesn't get for free (unlike a null
        // Filter's own shortcut in Matches, above).
        if (filter.RequireSelf && (listener is null || evt.SubjectDie?.Id != listener.Id)) return false;

        if (filter.ExcludeCause is { } excludeCause)
        {
            if (evt.Payload is DieFaceChangedPayload excludeFaceChange && excludeFaceChange.Cause == excludeCause) return false;
        }

        if (filter.LevelIncreased)
        {
            if (evt.Payload is not DieFaceChangedPayload faceChange) return false;
            // Awaken responds to a level increase caused by a spin/effect,
            // never a higher result from a roll or reroll (including one
            // performed by an ability).
            if (faceChange.Cause is FaceChangeCause.Roll or FaceChangeCause.Reroll) return false;
            // Both sides must be character faces: spinning from an energy
            // face has no "level before" to have gone up from, and rule
            // 1.6.6's spin-up is defined between levels. PriorFace is
            // nullable (Part 30) - a die's first-ever face has none, and
            // null-conditional here naturally refuses to fire, exactly
            // Awaken's own intent (no prior level to have risen from).
            if (faceChange.PriorFace?.Character is not { } before) return false;
            if (faceChange.NewFace.Character is not { } after) return false;
            if (after.Level <= before.Level) return false;
        }

        if (filter.SharesAffiliationWithListener)
        {
            if (listener is null || evt.SubjectDie is not { } subjectForAffiliation) return false;
            var listenerAffiliations = QueryEngine.GetAffiliations(state, listener);
            var subjectAffiliations = QueryEngine.GetAffiliations(state, subjectForAffiliation);
            if (!listenerAffiliations.Any(subjectAffiliations.Contains)) return false;
        }

        if (filter.Affiliations is { } affiliations)
        {
            if (evt.SubjectDie is not { } subjectForAff) return false;
            var subjectAffiliations = QueryEngine.GetAffiliations(state, subjectForAff);
            if (affiliations.AnyOf is { Count: > 0 } anyOfAff && !anyOfAff.Any(subjectAffiliations.Contains)) return false;
            if (affiliations.NoneOf is { Count: > 0 } noneOfAff && noneOfAff.Any(subjectAffiliations.Contains)) return false;
        }

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
