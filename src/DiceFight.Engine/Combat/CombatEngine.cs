using DiceFight.Engine.Model;

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
    public static void DeclareAttackers(GameState state, IReadOnlyList<string> attackerDieIds)
    {
        RequireSubStep(state, AttackSubStep.DeclareAttackers);

        foreach (var id in attackerDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.FieldZone)
                throw new InvalidOperationException($"Die {id} is not an eligible attacker.");
            die.Zone = Zone.AttackZone;
        }

        state.AttackSubStep = AttackSubStep.DeclareBlockers;
    }

    // Rule 2.7.2 - the Inactive player assigns blockers (if any).
    public static void DeclareBlockers(GameState state, CombatAssignment assignment, IReadOnlyList<string> blockerDieIds)
    {
        RequireSubStep(state, AttackSubStep.DeclareBlockers);

        var inactiveId = state.OpponentOf(state.ActivePlayerId);
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
    // its blocker(s) - the active player's choice per 2.7.4.3.5.
    public static CombatResult AssignCombatDamage(
        GameState state,
        CombatAssignment assignment,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attackerDamageSplits)
    {
        // Sub-steps 3 (Action/Global window) and 5 ("when damaged") are
        // folded in here for now - see class remarks.
        RequireSubStep(state, AttackSubStep.ActionAndGlobalWindow);
        state.AttackSubStep = AttackSubStep.AssignCombatDamage;

        var inactivePlayer = state.GetPlayer(state.OpponentOf(state.ActivePlayerId));
        var attackers = state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList();

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
            foreach (var blockerId in blockerIds)
            {
                var blocker = FindDie(state, blockerId);
                if (split.TryGetValue(blockerId, out var dealt) && dealt > 0)
                    blocker.Damage += dealt;

                // Rule 2.7.4.3.6/2.7.4.3.7 - each blocker deals its full
                // attack value to the (shared) attacker.
                totalFromBlockers += DieStats.EffectiveAttack(state, blocker);
            }

            attacker.Damage += totalFromBlockers;
        }

        state.AttackSubStep = AttackSubStep.ResolveDamageAndWhenKOd;

        // Rule 2.7.6.1 - simultaneous KO of anything at/over its defense.
        // (Overcrush, Regenerate, and other keyword interactions are not
        // yet implemented - deferred to the keyword-plugin work described
        // in the design doc.)
        var koDieIds = new List<string>();
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone).ToList())
        {
            if (die.Damage < DieStats.EffectiveDefense(state, die)) continue;

            koDieIds.Add(die.Id);
            die.Zone = Zone.PrepArea; // rule 1.5.3.2
            die.Damage = 0;
            die.Level = 1;
            die.Status = DieStatus.Unrolled;
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
}
