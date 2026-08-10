using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Rule 3.3 - Targeting, and rule 3.1.9's Legal Target definition. Computes
// which die ids a TargetSpec could legally apply to, given who controls
// the ability and the current game state. This is a first pass - it
// doesn't exclude captured dice (rule 3.8); CardDef.
// CannotBeTargetedByOpponentWhileNamedCardActive is the only per-die
// "cannot be targeted" shape modeled so far (see its own remarks for the
// narrower shapes still not covered).
public static class LegalTargets
{
    public static IReadOnlyList<string> Query(
        GameState state, string requestingControllerId, TargetSpec spec, TriggerType? currentTrigger = null)
    {
        if (spec.IsSelf)
        {
            throw new InvalidOperationException(
                "TargetSpec.Self resolves directly to the ability's source die and never queries LegalTargets.");
        }

        var zones = spec.EligibleZones ?? TargetSpec.DefaultZones;
        IEnumerable<DieInstance> candidates = state.Dice.Where(d => zones.Contains(d.Zone));

        candidates = candidates.Where(d => !DieStats.IsProtectedFromOpponentTargeting(state, d, requestingControllerId));

        if (currentTrigger is TriggerType.Global or TriggerType.WhenUsed)
        {
            var opponentId = state.OpponentOf(requestingControllerId);
            candidates = candidates.Where(d =>
                !(d.ControllerId == opponentId && state.ImmuneToActionAndGlobalTargetingControllerIds.Contains(opponentId)));
        }

        // Angel ("Xavier's Dream", DPS137) - "your opponent can't target
        // your Sidekick dice with Global Abilities." Global only (unlike
        // Gladiator's block above, this text doesn't mention Action
        // Dice), continuous rather than one-shot-activated - see
        // DieStats.SidekicksAreImmuneToOpponentGlobalTargeting.
        if (currentTrigger is TriggerType.Global)
        {
            var opponentId = state.OpponentOf(requestingControllerId);
            candidates = candidates.Where(d =>
                !(d.ControllerId == opponentId && DieStats.CountsAsSidekick(state, d) &&
                  DieStats.SidekicksAreImmuneToOpponentGlobalTargeting(state, opponentId)));
        }

        candidates = spec.Ownership switch
        {
            TargetOwnership.Own => candidates.Where(d => d.ControllerId == requestingControllerId),
            TargetOwnership.Opposing => candidates.Where(d => d.ControllerId == state.OpponentOf(requestingControllerId)),
            _ => candidates
        };

        if (spec.CharacterDiceOnly)
            candidates = candidates.Where(d => d.Status is DieStatus.Character or DieStatus.SidekickCharacter);

        if (spec.ActionDiceOnly)
            candidates = candidates.Where(d => d.Status == DieStatus.Action);

        if (spec.MaxAttack is { } maxAttack)
            candidates = candidates.Where(d => DieStats.EffectiveAttack(state, d) <= maxAttack);

        if (spec.MaxDefense is { } maxDefense)
            candidates = candidates.Where(d => DieStats.EffectiveDefense(state, d) <= maxDefense);

        // DieStats.HasAffiliation, not a raw CardCatalog lookup - Radicalization
        // (DPS012)'s own Global grants a temporary affiliation
        // (DieInstance.AppliedAffiliations) that a target filter like
        // this one needs to respect, same as everywhere else "does this
        // die have affiliation X" is asked.
        if (spec.RequiredAffiliations is { } requiredAffiliations)
            candidates = candidates.Where(d => requiredAffiliations.Any(a => DieStats.HasAffiliation(state, d, a)));

        if (spec.RequiredLevel is { } requiredLevel)
            candidates = candidates.Where(d => d.Level == requiredLevel);

        if (spec.MinLevel is { } minLevel)
            candidates = candidates.Where(d => d.Level >= minLevel);

        if (spec.RequiresLoyaltyCounter)
            candidates = candidates.Where(d => DieStats.LoyaltyBonus(state, d) > 0);

        if (spec.MatchesOwnTeamAffiliation)
        {
            var ownTeamAffiliations = state.GetPlayer(requestingControllerId).TeamCardIds
                .Where(id => state.CardCatalog.ContainsKey(id))
                .SelectMany(id => state.CardCatalog[id].Affiliations)
                .ToHashSet();
            candidates = candidates.Where(d => ownTeamAffiliations.Any(a => DieStats.HasAffiliation(state, d, a)));
        }

        if (spec.SidekicksOnly)
            candidates = candidates.Where(d => DieStats.CountsAsSidekick(state, d));

        if (spec.RequiredEnergyType is { } energyType)
        {
            candidates = candidates.Where(d =>
            {
                var cardId = d.VirtualCardId ?? d.CardId;
                return cardId is not null
                    && state.CardCatalog.TryGetValue(cardId, out var card)
                    && card.EnergyTypes.Contains(energyType);
            });
        }

        var ids = candidates.Select(d => d.Id);

        if (spec.PlayersAllowed)
        {
            IEnumerable<string> playerIds = spec.Ownership switch
            {
                TargetOwnership.Own => [requestingControllerId],
                TargetOwnership.Opposing => [state.OpponentOf(requestingControllerId)],
                _ => [requestingControllerId, state.OpponentOf(requestingControllerId)]
            };
            ids = ids.Concat(playerIds);
        }

        return ids.ToList();
    }
}
