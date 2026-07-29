using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;

namespace DiceFight.Engine.Combat;

// Rule 2.7 - Attack Step. Implements the zone-move mechanics and damage
// math for sub-steps 1, 2, 4, and 6 (Declare Attackers/Blockers, Assign
// Combat Damage, Resolve Damage and "when KO'd"). Sub-steps 3 and 5 (the
// Action/Global window, and "when damaged" abilities) are represented only
// as AttackSubStep markers for now - they need the AbilityQueue wired to
// real per-card triggers before there's anything to execute there, which
// isn't built yet (see RULES_ENGINE_DESIGN.md).
public static class CombatEngine
{
    // Rule 2.7.1 - move the chosen Field Zone dice into the Attack Zone.
    public static void DeclareAttackers(GameState state, AbilityQueue queue, IReadOnlyList<string> attackerDieIds)
    {
        RequireSubStep(state, AttackSubStep.DeclareAttackers);

        foreach (var id in attackerDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.FieldZone)
                throw new InvalidOperationException($"Die {id} is not an eligible attacker.");
            die.Zone = Zone.AttackZone;

            // Rule 2.7.1.2 - "when attacks" fires for each attacking die.
            TurnEngine.EnqueueTriggered(state, queue, die, TriggerType.WhenAttacks);
        }

        state.AttackSubStep = AttackSubStep.DeclareBlockers;
    }

    // Rule 2.7.2 - the Inactive player assigns blockers (if any).
    public static void DeclareBlockers(GameState state, CombatAssignment assignment, IReadOnlyList<string> blockerDieIds)
    {
        RequireSubStep(state, AttackSubStep.DeclareBlockers);

        var inactiveId = state.OpponentOf(state.ActivePlayerId);

        // Rule-text "must block" (e.g. Invisible Woman's Global) - checked
        // before any zone changes below, so a missing forced blocker
        // aborts cleanly instead of leaving other chosen blockers already
        // moved. Only enforced against dice still actually eligible to
        // block (controlled by the inactive player, still in the Field
        // Zone) - a forced die that got KO'd or moved elsewhere is just
        // skipped, matching the rule text's "if able" spirit even though
        // this ability's own printed text doesn't say "if able".
        var forcedButOmitted = state.DiceIn(inactiveId, Zone.FieldZone)
            .Where(d => state.MustBlockThisTurn.Contains(d.Id) && !blockerDieIds.Contains(d.Id))
            .ToList();
        if (forcedButOmitted.Count > 0)
        {
            var names = string.Join(", ", forcedButOmitted.Select(d => DisplayName(state, d)));
            throw new InvalidOperationException($"{names} must block this turn.");
        }

        foreach (var id in blockerDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != inactiveId || die.Zone != Zone.FieldZone)
                throw new InvalidOperationException($"Die {id} is not an eligible blocker.");
            die.Zone = Zone.AttackZone;
        }

        state.AttackSubStep = AttackSubStep.ActionAndGlobalWindow;
    }

    // Rule 2.7.4 (assign) and 2.7.6 (resolve KOs, return survivors).
    // attackerDamageSplits: for each blocked attacker, how its total attack
    // value (which must be assigned in full - 2.7.4.3.4) is split across
    // its blocker(s) - the active player's choice per 2.7.4.3.5. roller is
    // optional (null in test call sites that don't care) - it's what lets
    // a Regenerate die reroll instead of actually being KO'd here.
    public static CombatResult AssignCombatDamage(
        GameState state,
        AbilityQueue queue,
        CombatAssignment assignment,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attackerDamageSplits,
        IDiceRoller? roller = null)
    {
        // Sub-steps 3 (Action/Global window) and 5 ("when damaged") are
        // folded in here for now - see class remarks.
        RequireSubStep(state, AttackSubStep.ActionAndGlobalWindow);
        state.AttackSubStep = AttackSubStep.AssignCombatDamage;

        var inactivePlayer = state.GetPlayer(state.OpponentOf(state.ActivePlayerId));
        var attackers = state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList();

        // Overcrush needs, per blocked Overcrush attacker, its attack value
        // and its blockers' total defense - captured now, before the KO
        // pass below mutates (or Regenerate-resets) those same dice.
        var overcrushCandidates = new Dictionary<string, (int Attack, int BlockerDefenseTotal)>();

        foreach (var attacker in attackers)
        {
            var blockerIds = assignment.BlockersOf(attacker.Id);
            var attack = DieStats.EffectiveAttack(state, attacker);

            if (blockerIds.Count == 0)
            {
                // Rule 2.7.4.3.1 - unblocked: hits the player directly and
                // leaves the Attack Zone before anything else can resolve.
                inactivePlayer.Life -= attack;
                attacker.Zone = Zone.OutOfPlay;
                continue;
            }

            if (!attackerDamageSplits.TryGetValue(attacker.Id, out var split) || split.Values.Sum() != attack)
            {
                throw new InvalidOperationException(
                    $"Damage split for attacker {attacker.Id} must assign its full attack value ({attack}).");
            }

            var totalFromBlockers = 0;
            var blockerDefenseTotal = 0;
            foreach (var blockerId in blockerIds)
            {
                var blocker = FindDie(state, blockerId);
                if (split.TryGetValue(blockerId, out var dealt) && dealt > 0)
                    blocker.Damage += dealt;

                // Rule 2.7.4.3.6/2.7.4.3.7 - each blocker deals its full
                // attack value to the (shared) attacker.
                totalFromBlockers += DieStats.EffectiveAttack(state, blocker);
                blockerDefenseTotal += DieStats.EffectiveDefense(state, blocker);
            }

            attacker.Damage += totalFromBlockers;
            if (DieStats.HasKeyword(state, attacker, "Overcrush"))
                overcrushCandidates[attacker.Id] = (attack, blockerDefenseTotal);
        }

        state.AttackSubStep = AttackSubStep.ResolveDamageAndWhenKOd;

        // Rule 2.7.6.1 - simultaneous KO of anything at/over its defense
        // (Regenerate, if the die has it, intercepts inside TryResolveKO).
        var koDieIds = new List<string>();
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone).ToList())
        {
            if (!DieStats.TryResolveKO(state, die, roller)) continue;
            koDieIds.Add(die.Id);

            // Rule 2.7.6.5 - "when KO'd" fires for each die KO'd this way.
            TurnEngine.EnqueueTriggered(state, queue, die, TriggerType.WhenKOd);
        }

        // Glossary - Overcrush: "if this character die KO's or removes all
        // of its blockers, it deals any leftover damage to your opponent."
        // The active player already had to assign the attacker's full
        // value across its blockers (rule 2.7.4.3.4); Overcrush redirects
        // whatever would otherwise be wasted overkill - but only once
        // every blocker actually died (a Regenerate'd blocker surviving
        // means the condition isn't met, so nothing carries through).
        foreach (var (attackerId, info) in overcrushCandidates)
        {
            var blockerIds = assignment.BlockersOf(attackerId);
            if (!blockerIds.All(koDieIds.Contains)) continue;
            var leftover = info.Attack - info.BlockerDefenseTotal;
            if (leftover > 0) inactivePlayer.Life -= leftover;
        }

        // Rule 2.7.6.6 - survivors return to the Field Zone.
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone))
            die.Zone = Zone.FieldZone;

        state.AttackSubStep = AttackSubStep.Done;
        state.CurrentStep = TurnStep.CleanUp;

        return new CombatResult(koDieIds);
    }

    private static void RequireSubStep(GameState state, AttackSubStep expected)
    {
        if (state.CurrentStep != TurnStep.Attack)
            throw new InvalidOperationException($"Not in the Attack Step (currently {state.CurrentStep}).");
        if (state.AttackSubStep != expected)
            throw new InvalidOperationException($"Expected Attack sub-step {expected}, was {state.AttackSubStep}.");
    }

    private static DieInstance FindDie(GameState state, string id) =>
        state.Dice.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"No die with id '{id}'.");

    private static string DisplayName(GameState state, DieInstance die) =>
        die.CardId is { } cardId && state.CardCatalog.TryGetValue(cardId, out var card) ? card.Name : die.Id;
}
