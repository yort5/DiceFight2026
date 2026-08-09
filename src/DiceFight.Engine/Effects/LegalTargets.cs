using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Rule 3.3 - Targeting, and rule 3.1.9's Legal Target definition. Computes
// which die ids a TargetSpec could legally apply to, given who controls
// the ability and the current game state. This is a first pass - it
// doesn't exclude captured dice (rule 3.8) or dice with a "cannot be
// targeted" ability applied to them, since neither Capturing nor per-die
// targeting restrictions are modeled yet (see RULES_ENGINE_DESIGN.md).
public static class LegalTargets
{
    public static IReadOnlyList<string> Query(GameState state, string requestingControllerId, TargetSpec spec)
    {
        if (spec.IsSelf)
        {
            throw new InvalidOperationException(
                "TargetSpec.Self resolves directly to the ability's source die and never queries LegalTargets.");
        }

        var zones = spec.EligibleZones ?? TargetSpec.DefaultZones;
        IEnumerable<DieInstance> candidates = state.Dice.Where(d => zones.Contains(d.Zone));

        candidates = spec.Ownership switch
        {
            TargetOwnership.Own => candidates.Where(d => d.ControllerId == requestingControllerId),
            TargetOwnership.Opposing => candidates.Where(d => d.ControllerId == state.OpponentOf(requestingControllerId)),
            _ => candidates
        };

        if (spec.CharacterDiceOnly)
            candidates = candidates.Where(d => d.Status is DieStatus.Character or DieStatus.SidekickCharacter);

        if (spec.MaxAttack is { } maxAttack)
            candidates = candidates.Where(d => DieStats.EffectiveAttack(state, d) <= maxAttack);

        if (spec.RequiredAffiliations is { } requiredAffiliations)
        {
            candidates = candidates.Where(d =>
            {
                var cardId = d.VirtualCardId ?? d.CardId;
                return cardId is not null
                    && state.CardCatalog.TryGetValue(cardId, out var card)
                    && card.Affiliations.Any(requiredAffiliations.Contains);
            });
        }

        if (spec.RequiredLevel is { } requiredLevel)
            candidates = candidates.Where(d => d.Level == requiredLevel);

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
