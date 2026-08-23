using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Rule 2.7 - Attack Step. V2_PLAN.md Phase 7 - ports v1 CombatEngine's
// core rules content (declare attackers -> blockers -> action/global
// window -> assign damage -> KO resolution) at the scope the plan
// actually asks for: every stat read goes through QueryEngine, every KO/
// damage goes through EffectInterpreter.ApplyDamage/KoDie (so
// DamageModifier/DieDamaged/DieKOd all apply to combat exactly like they
// do to ability damage), and every restriction goes through CombatFlags
// (Phase 5's per-die grants) + CombatRules (Phase 6's continuous grants) -
// both of which get their first real consumer here.
//
// Deliberately NOT ported: v1's Range/Infiltrate/Tag Out/Energy Drain/
// Deadly/Call Out/Obscure/Regenerate/Retaliation keywords, and every
// card-specific Grants* combat hook (Blob's Sidekick-return, Deathbird's
// damage-on-high-defense-KO, Lilandra's reroll-to-Prep-Area, etc.) - none
// of those are CombatFlag/CombatRule-shaped grants in the closed
// vocabulary (V2_PLAN.md ground rule 2), and Phase 0's audit never
// flagged the base combat loop itself as a fit problem. They go to
// V2_TAIL_POLICY.md if/when Phase 8's card migration needs them.
public static class CombatEngine
{
    // Rule 2.7.1 - move the chosen Field Zone dice into the Attack Zone.
    // OnlyBlocker (Part 1's own CombatFlagKind) is treated as an implicit
    // CantAttack for eligibility purposes - a judgment call, not specified
    // further anywhere in the frozen vocabulary (no authored card uses it
    // yet); revisit if a real migrated card needs a different reading.
    public static void DeclareAttackers(GameState state, AbilityQueue queue, IReadOnlyList<string> attackerDieIds)
    {
        RequireStep(state, StepIds.SelectAttackers);

        var forcedButOmitted = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Where(d => d.CombatFlags.Contains(CombatFlagKind.MustAttack) && !attackerDieIds.Contains(d.Id))
            .ToList();
        if (forcedButOmitted.Count > 0)
            throw new InvalidOperationException($"{string.Join(", ", forcedButOmitted.Select(d => d.Id))} must attack this turn.");

        foreach (var id in attackerDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.FieldZone || state.GetCurrentFace(die)?.Character is null)
                throw new InvalidOperationException($"Die '{id}' is not an eligible attacker.");
            if (die.CombatFlags.Contains(CombatFlagKind.CantAttack) || die.CombatFlags.Contains(CombatFlagKind.OnlyBlocker))
                throw new InvalidOperationException($"Die '{id}' cannot attack this turn.");

            die.Zone = Zone.AttackZone;
            // Rule 2.7.1.2 - "when attacks" fires for each attacking die.
            EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, die, die.ControllerId, state.CurrentStepId));
        }

        state.MoveToStep(StepIds.AssignBlockers);
    }

    // Rule 2.7.2 - the Inactive player assigns blockers (if any).
    public static void DeclareBlockers(GameState state, AbilityQueue queue, CombatAssignment assignment, IReadOnlyList<string> blockerDieIds)
    {
        RequireStep(state, StepIds.AssignBlockers);
        var inactiveId = state.OpponentOf(state.ActivePlayerId);

        var forcedButOmitted = state.DiceIn(inactiveId, Zone.FieldZone)
            .Where(d => d.CombatFlags.Contains(CombatFlagKind.MustBlock) && !blockerDieIds.Contains(d.Id))
            .ToList();
        if (forcedButOmitted.Count > 0)
            throw new InvalidOperationException($"{string.Join(", ", forcedButOmitted.Select(d => d.Id))} must block this turn.");

        ValidateUnblockable(state, assignment);
        ValidateMinBlockers(state, assignment);
        ValidateBlockerCapacity(state, assignment);

        foreach (var id in blockerDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != inactiveId || die.Zone != Zone.FieldZone || state.GetCurrentFace(die)?.Character is null)
                throw new InvalidOperationException($"Die '{id}' is not an eligible blocker.");
            if (die.CombatFlags.Contains(CombatFlagKind.CantBlock))
                throw new InvalidOperationException($"Die '{id}' cannot block this turn.");

            die.Zone = Zone.AttackZone;
            EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieBlocks, die, die.ControllerId, state.CurrentStepId));
        }

        state.MoveToStep(StepIds.ActionGlobalWindow);
    }

    // CombatFlagKind.Unblockable (Finding 14 - Falcon "Recon").
    private static void ValidateUnblockable(GameState state, CombatAssignment assignment)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            if (attacker.CombatFlags.Contains(CombatFlagKind.Unblockable) && assignment.BlockersOf(attacker.Id).Count > 0)
                throw new InvalidOperationException($"Die '{attacker.Id}' is unblockable this turn.");
        }
    }

    // CombatRuleKind.MinBlockers - "your [X] character dice can only be
    // blocked by N or more character dice" (Magneto "Visionary"). The
    // rule applies TO THE ATTACKER (CombatRule.Target resolves against
    // the granting card's own controller, same as every other continuous
    // template) - zero blockers is always legal regardless of any
    // minimum; only a nonzero count below it is rejected.
    private static void ValidateMinBlockers(GameState state, CombatAssignment assignment)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            var blockerCount = assignment.BlockersOf(attacker.Id).Count;
            if (blockerCount == 0) continue;

            var required = state.CombatRules
                .Where(r => r.Kind == CombatRuleKind.MinBlockers && r.AppliesTo(state, attacker))
                .Select(r => r.N ?? 1)
                .DefaultIfEmpty(0)
                .Max();

            if (blockerCount < required)
                throw new InvalidOperationException($"Die '{attacker.Id}' can only be blocked by {required} or more character dice.");
        }
    }

    // Rule 2.7.2.4 - "each Character die may block only one attacking
    // Character die, unless a card effect states otherwise"
    // (CombatRuleKind.BlocksN - Blob "Immovable"). Counts how many
    // DISTINCT attackers each blocker id appears against across the
    // whole assignment.
    private static void ValidateBlockerCapacity(GameState state, CombatAssignment assignment)
    {
        var attackerCountByBlocker = new Dictionary<string, int>();
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            foreach (var blockerId in assignment.BlockersOf(attacker.Id))
                attackerCountByBlocker[blockerId] = attackerCountByBlocker.GetValueOrDefault(blockerId) + 1;
        }

        foreach (var (blockerId, attackerCount) in attackerCountByBlocker)
        {
            if (attackerCount <= 1) continue;

            var blocker = FindDie(state, blockerId);
            var maxAttackers = state.CombatRules
                .Where(r => r.Kind == CombatRuleKind.BlocksN && r.AppliesTo(state, blocker))
                .Select(r => r.N ?? 1)
                .DefaultIfEmpty(1)
                .Max();

            if (attackerCount > maxAttackers)
                throw new InvalidOperationException($"Die '{blockerId}' can only block {maxAttackers} character die(s) at once.");
        }
    }

    // Rule 2.7.4 (assign) and 2.7.6 (resolve KOs, return survivors).
    // attackerDamageSplits: for each blocked attacker, how its full
    // attack value (rule 2.7.4.3.4 - must be assigned in full) is split
    // across its still-live blocker(s) - the active player's choice.
    public static CombatResult AssignCombatDamage(
        GameState state, AbilityQueue queue, CombatAssignment assignment,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attackerDamageSplits)
    {
        RequireStep(state, StepIds.ActionGlobalWindow);
        state.MoveToStep(StepIds.NormalDamage);

        var inactivePlayer = state.GetPlayer(state.OpponentOf(state.ActivePlayerId));
        var attackers = state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList();

        // Overcrush needs, per Overcrush attacker, its attack value, its
        // still-live blockers' total defense, and the ORIGINALLY declared
        // blocker list (to check afterward whether every one of them is
        // gone, however that happened) - captured now, before either Fast
        // wave below mutates anything. A static fact about who was
        // blocking at the start of this sub-step.
        var overcrushCandidates = new Dictionary<string, (int Attack, int BlockerDefenseTotal, IReadOnlyList<string> DeclaredBlockerIds)>();

        foreach (var attacker in attackers)
        {
            var declaredBlockerIds = assignment.BlockersOf(attacker.Id);
            var attack = QueryEngine.GetAttack(state, attacker);

            if (declaredBlockerIds.Count == 0)
            {
                // Rule 2.7.4.3.1 - unblocked: hits the player directly and
                // leaves the Attack Zone before anything else can resolve.
                inactivePlayer.Life -= attack;
                attacker.Zone = Zone.OutOfPlay;
                continue;
            }

            // "Once blocked, always blocked" - an attacker with a declared
            // blocker never falls through to the unblocked branch above,
            // even if every one of its blockers is later removed; any
            // damage that had nowhere live to land is simply wasted
            // (unless Overcrush redirects the leftover below).
            var liveBlockerIds = declaredBlockerIds.Where(id => FindDie(state, id).Zone == Zone.AttackZone).ToList();

            if (liveBlockerIds.Count > 0 &&
                (!attackerDamageSplits.TryGetValue(attacker.Id, out var split) || split.Values.Sum() != attack))
            {
                throw new InvalidOperationException(
                    $"Damage split for attacker '{attacker.Id}' must assign its full attack value ({attack}).");
            }

            var blockerDefenseTotal = liveBlockerIds.Sum(id => QueryEngine.GetDefense(state, FindDie(state, id)));
            if (QueryEngine.GetKeywords(state, attacker).Contains("Overcrush"))
                overcrushCandidates[attacker.Id] = (attack, blockerDefenseTotal, declaredBlockerIds);
        }

        // Keyword Fast - "Characters with Fast deal combat damage before
        // other Character dice in the Attack Step. All Character dice
        // with Fast deal damage at the same time." Two full waves rather
        // than one: every Fast die's damage lands (and can KO) completely
        // first, so a non-Fast die KO'd this way never deals its own
        // damage back at all (the rulebook's own worked example - see
        // the test suite).
        var koIds = new List<string>();
        koIds.AddRange(ResolveFastOrSlowDamage(state, queue, assignment, attackerDamageSplits, fast: true));
        koIds.AddRange(ResolveFastOrSlowDamage(state, queue, assignment, attackerDamageSplits, fast: false));

        // Glossary/FAQ - Overcrush: "if this character die KO's or removes
        // all of its blockers, it deals any leftover damage to your
        // opponent." A blocker counts as gone if it's no longer in the
        // Attack Zone, whether that's because it was already removed
        // before this method ran or was just KO'd above.
        foreach (var (_, info) in overcrushCandidates)
        {
            if (!info.DeclaredBlockerIds.All(id => FindDie(state, id).Zone != Zone.AttackZone)) continue;
            var leftover = info.Attack - info.BlockerDefenseTotal;
            if (leftover > 0) inactivePlayer.Life -= leftover;
        }

        // Rule 2.7.6.6 - survivors return to the Field Zone.
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone))
            die.Zone = Zone.FieldZone;

        state.MoveToStep(StepIds.ReturnToField);
        // Unlike v1, this does NOT also advance CurrentStep to CleanUp -
        // the caller calls TurnEngine.CleanUp explicitly afterward, same
        // as the skip-combat path already does; keeps CleanUp's own
        // RequireStep(Attack) contract the same regardless of whether
        // combat happened this turn.

        return new CombatResult(koIds);
    }

    // One wave of Keyword Fast's two-wave damage resolution. Two full
    // passes, matching v1's own DieStats.ApplyDamage/TryResolveKO split
    // exactly (rule 2.7.6.1 - "simultaneous KO... among whoever just took
    // damage this wave"): first MarkDamage lands on BOTH directions of
    // every still-live engagement in this wave (an attacker's split onto
    // its blockers, and each blocker's full attack back onto the shared
    // attacker) with nothing KO'd or moved out of the Attack Zone yet -
    // this is what lets a lethally-outmatched blocker still land its own
    // damage back in the SAME wave rather than being silently skipped
    // because it "already died." Only once every hit in the wave has
    // landed does the second pass check who actually crossed their
    // Defense threshold and KO them - so a wave where both sides deal
    // lethal damage really does KO both sides together, and a slower
    // (non-Fast) die KO'd by a Fast attacker's damage in THIS wave still
    // correctly never got to swing back, because its own damage was never
    // marked in the first place (the live-blocker/live-attacker checks
    // below already exclude it - it left the Attack Zone in an EARLIER
    // wave, not mid-way through this one).
    private static List<string> ResolveFastOrSlowDamage(
        GameState state, AbilityQueue queue, CombatAssignment assignment,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attackerDamageSplits, bool fast)
    {
        var wavedRecipients = new List<DieInstance>();

        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList())
        {
            var liveBlockerIds = assignment.BlockersOf(attacker.Id).Where(id => FindDie(state, id).Zone == Zone.AttackZone).ToList();
            if (liveBlockerIds.Count == 0) continue;

            // Rule 2.7.4.3.4-ish - the attacker's own split lands on its
            // still-live blockers, timed by the ATTACKER's own Fast.
            if (QueryEngine.GetKeywords(state, attacker).Contains("Fast") == fast && attackerDamageSplits.TryGetValue(attacker.Id, out var split))
            {
                foreach (var blockerId in liveBlockerIds)
                {
                    if (split.TryGetValue(blockerId, out var dealt) && dealt > 0 &&
                        EffectInterpreter.MarkDamage(state, queue, DamageSource.Combat, blockerId, dealt) is { } recipient)
                        wavedRecipients.Add(recipient);
                }
            }

            // Rule 2.7.4.3.6/2.7.4.3.7 - each still-live blocker deals its
            // full attack value back to the (shared) attacker, timed by
            // the BLOCKER's own Fast keyword rather than the attacker's -
            // unaffected by whether the attacker's own damage (marked
            // just above) would be lethal, since nothing's been KO'd yet.
            foreach (var blockerId in liveBlockerIds)
            {
                var blocker = FindDie(state, blockerId);
                if (QueryEngine.GetKeywords(state, blocker).Contains("Fast") == fast &&
                    EffectInterpreter.MarkDamage(state, queue, DamageSource.Combat, attacker.Id, QueryEngine.GetAttack(state, blocker)) is { } recipient)
                    wavedRecipients.Add(recipient);
            }
        }

        // Rule 2.7.6.1 - now resolve KOs, simultaneously, among everyone
        // still in the Attack Zone plus every redirect recipient (a
        // RedirectToSelf target isn't guaranteed to already be an
        // attacker/blocker in this engagement).
        var koIds = new List<string>();
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone).Concat(wavedRecipients).DistinctBy(d => d.Id).ToList())
        {
            if (EffectInterpreter.TryResolveKO(state, queue, die))
                koIds.Add(die.Id);
        }

        return koIds;
    }

    // Spike C - attack sub-steps are ordinary entries in the one flat
    // step list now, so this is just "are we standing on that step".
    private static void RequireStep(GameState state, string expectedStepId)
    {
        if (state.CurrentStepId != expectedStepId)
            throw new InvalidOperationException($"Expected the '{expectedStepId}' step, was '{state.CurrentStepId}'.");
    }

    private static DieInstance FindDie(GameState state, string id) =>
        state.Dice.FirstOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"No die with id '{id}'.");
}
