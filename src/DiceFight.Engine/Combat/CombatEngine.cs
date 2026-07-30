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

        // Keyword Call Out - scoped to one combat, not one turn (unlike
        // MustBlockThisTurn), so it starts fresh every time attackers are
        // declared rather than waiting for Clean Up.
        state.CallOutTargets.Clear();

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

        ValidateCallOuts(state, assignment);

        foreach (var id in blockerDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != inactiveId || die.Zone != Zone.FieldZone)
                throw new InvalidOperationException($"Die {id} is not an eligible blocker.");
            die.Zone = Zone.AttackZone;
        }

        RecordDeadlyEngagements(state, assignment);
        ResolveEnergyDrain(state, assignment);

        state.AttackSubStep = AttackSubStep.ActionAndGlobalWindow;
    }

    // Keyword Energy Drain X - "After blockers are assigned, spin each
    // Character die engaged with a Character die with Energy Drain down
    // [X] level(s)... Energy Drain does not target because it spins down
    // any Character die engaged" (clarification 2) - so, like Deadly,
    // this is fully automatic engine behavior, not something scripted via
    // AbilityDef/TargetSpec. Unlike Deadly, it resolves immediately here
    // rather than being deferred - the rule's own "After blockers are
    // assigned" is this exact moment, not Clean Up. Pairwise (same
    // engagement model as Deadly/Call Out): an Energy Drain attacker
    // spins down each of its blockers, and an Energy Drain blocker spins
    // down the attacker it's blocking. Multiple independent Energy Drain
    // sources engaged with the same die each apply their own spin-down
    // (they compound), same as any other independently-sourced effect
    // would - the rule text doesn't say otherwise, and "Character dice at
    // level 1 cannot be spun down" (DieStats.SpinLevel's own clamp)
    // already caps how far any of this can go regardless.
    private static void ResolveEnergyDrain(GameState state, CombatAssignment assignment)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList())
        {
            var attackerDrainAmount = DieStats.EnergyDrainAmount(state, attacker);
            foreach (var blockerId in assignment.BlockersOf(attacker.Id))
            {
                var blocker = FindDie(state, blockerId);
                if (attackerDrainAmount > 0)
                    DieStats.SpinLevel(state, blocker, -attackerDrainAmount);

                var blockerDrainAmount = DieStats.EnergyDrainAmount(state, blocker);
                if (blockerDrainAmount > 0)
                    DieStats.SpinLevel(state, attacker, -blockerDrainAmount);
            }
        }
    }

    // Keyword Deadly - "At the end of the turn, character dice that were
    // engaged with a Character die that has Deadly are KO'd (even if the
    // Character die with Deadly has been KO'd or leaves the Field Zone)."
    // Recorded now, at the moment of engagement, rather than checked
    // later at Clean Up - clarification (1) is explicit that Deadly
    // triggers off the engagement itself, not off anything that happens
    // afterward, so this has to be a snapshot rather than a live query.
    // Engagement is pairwise (rule 2.7.2.3: attacker<->each blocker
    // individually) - a Deadly blocker only marks the attacker it's
    // actually blocking, not that attacker's other co-blockers, and vice
    // versa; co-blockers of the same attacker are never engaged with
    // each other.
    private static void RecordDeadlyEngagements(GameState state, CombatAssignment assignment)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            var attackerIsDeadly = DieStats.HasKeyword(state, attacker, "Deadly");
            foreach (var blockerId in assignment.BlockersOf(attacker.Id))
            {
                if (attackerIsDeadly)
                    state.DeadlyEngagedDieIds.Add(blockerId);
                if (DieStats.HasKeyword(state, FindDie(state, blockerId), "Deadly"))
                    state.DeadlyEngagedDieIds.Add(attacker.Id);
            }
        }
    }

    // Keyword Call Out (wizkids.com/dicemasters/keywords) - "the targeted
    // die can only legally block the attacking die that applied Call Out
    // on it, and no other die can legally block the die that used Call
    // Out." Two directions to check, both against the SAME map: a Call
    // Out attacker may only be blocked by its own target, and a die that
    // IS someone's Call Out target may only block the attacker that
    // targeted it (not anyone else). Only ActiveCallOutTargets' entries
    // apply - anything cancelled imposes no restriction at all.
    private static void ValidateCallOuts(GameState state, CombatAssignment assignment)
    {
        var active = ActiveCallOutTargets(state);
        if (active.Count == 0) return;

        // Safe to invert 1:1 - ActiveCallOutTargets already excludes any
        // target claimed by more than one attacker (that pairing cancels
        // outright, per the keyword's own text).
        var owningAttackerOfTarget = active.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            foreach (var blockerId in assignment.BlockersOf(attacker.Id))
            {
                if (active.TryGetValue(attacker.Id, out var requiredTarget) && blockerId != requiredTarget)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName(state, attacker)} was Called Out - only its target may legally block it.");
                }

                if (owningAttackerOfTarget.TryGetValue(blockerId, out var owningAttackerId) && owningAttackerId != attacker.Id)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName(state, FindDie(state, blockerId))} was Called Out by another attacker and may only legally block that one.");
                }
            }
        }
    }

    // The keyword's own cancellation clause: "If the die that applied
    // Call Out cannot legally be blocked for any reason (an ability made
    // it unblockable, two different dice chose the same target for their
    // Call Out, the die targeted with Call Out was KO'd, etc.), then the
    // Call Out ability is cancelled." A cancelled Call Out imposes no
    // restriction at all - blocking legality for that attacker just
    // reverts to normal, it does NOT become unblockable itself. Only the
    // "target was KO'd/removed" and "duplicate target" cases are
    // modeled - "an ability made [the attacker] unblockable" has nothing
    // to check against yet, since Unblockable isn't a mechanic this
    // engine has built (see RULES_ENGINE_DESIGN.md).
    private static IReadOnlyDictionary<string, string> ActiveCallOutTargets(GameState state)
    {
        var duplicateTargets = state.CallOutTargets.Values
            .GroupBy(targetId => targetId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        return state.CallOutTargets
            .Where(kvp => !duplicateTargets.Contains(kvp.Value) && FindDie(state, kvp.Value).Zone is Zone.FieldZone or Zone.AttackZone)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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

        // Overcrush needs, per Overcrush attacker, its attack value, its
        // still-live blockers' total defense, and the *originally declared*
        // blocker list (to check afterward whether every one of them is
        // gone, however that happened) - captured now, before either Fast
        // phase below mutates (or Regenerate-resets) those same dice. This
        // total is a static fact about who was blocking at the start of
        // this sub-step - it doesn't depend on which Fast phase actually
        // lands the killing blow, so it's still computed once, upfront.
        var overcrushCandidates = new Dictionary<string, (int Attack, int BlockerDefenseTotal, IReadOnlyList<string> DeclaredBlockerIds)>();

        foreach (var attacker in attackers)
        {
            var declaredBlockerIds = assignment.BlockersOf(attacker.Id);
            var attack = DieStats.EffectiveAttack(state, attacker);

            if (declaredBlockerIds.Count == 0)
            {
                // Rule 2.7.4.3.1 - unblocked: hits the player directly and
                // leaves the Attack Zone before anything else can resolve.
                // Fast doesn't apply here at all - it's only about racing
                // against an opposing Character die, and an unblocked
                // attacker has none.
                inactivePlayer.Life -= attack;
                attacker.Zone = Zone.OutOfPlay;
                continue;
            }

            // wizkids.com/dicemasters/keywords - a blocker can be removed
            // from the Attack Zone before damage is assigned (KO'd or
            // otherwise removed by an ability used during the Action/Global
            // window, sub-step 3, which this engine already allows - see
            // TurnEngine.InMainOrAttackActionWindow). A blocker like that
            // takes no part here: no split entry needed for it, it deals no
            // damage back, and it contributes zero to Overcrush's "total
            // defense absorbed" below. The attacker is still *blocked*
            // though (declaredBlockerIds.Count > 0), so - Overcrush or not -
            // it does NOT fall into the unblocked branch above and does NOT
            // go to Zone.OutOfPlay; it stays in the Attack Zone and returns
            // to the Field Zone via the survivors sweep below, same as any
            // other blocked attacker.
            var liveBlockerIds = declaredBlockerIds.Where(id => FindDie(state, id).Zone == Zone.AttackZone).ToList();

            if (liveBlockerIds.Count > 0 &&
                (!attackerDamageSplits.TryGetValue(attacker.Id, out var split) || split.Values.Sum() != attack))
            {
                throw new InvalidOperationException(
                    $"Damage split for attacker {attacker.Id} must assign its full attack value ({attack}).");
            }
            // else if liveBlockerIds.Count == 0: every declared blocker was
            // already gone before this method ran - nothing to assign a
            // split against (rule 2.7.4.3.4's "assign in full" has nowhere
            // left to put it), and without Overcrush that portion of the
            // attack is just wasted, not redirected to the player.

            var blockerDefenseTotal = liveBlockerIds.Sum(id => DieStats.EffectiveDefense(state, FindDie(state, id)));
            if (DieStats.HasKeyword(state, attacker, "Overcrush"))
                overcrushCandidates[attacker.Id] = (attack, blockerDefenseTotal, declaredBlockerIds);
        }

        state.AttackSubStep = AttackSubStep.ResolveDamageAndWhenKOd;

        // Keyword Fast - "Characters with Fast deal combat damage before
        // other Character dice in the Attack Step. All Character dice
        // with Fast deal damage at the same time." Two full damage-then-KO
        // waves rather than rule 2.7.4.3's usual single simultaneous one:
        // every Fast-keyword die's damage lands (and is checked for KOs)
        // completely first, so a non-Fast die KO'd this way never gets to
        // deal its own damage back at all (the rule's own worked example -
        // a 4A/2D Fast attacker KOs a 5A/3D blocker before that blocker can
        // ever apply its damage). Dice with neither die in a pairing having
        // Fast simply resolve in the second wave - identical to the old
        // single-pass behavior when Fast isn't involved anywhere.
        var koDieIds = new List<string>();
        koDieIds.AddRange(ResolveFastOrSlowDamage(state, queue, assignment, attackerDamageSplits, fast: true, roller));
        koDieIds.AddRange(ResolveFastOrSlowDamage(state, queue, assignment, attackerDamageSplits, fast: false, roller));

        // Glossary/FAQ - Overcrush: "if this character die KO's or removes
        // all of its blockers, it deals any leftover damage to your
        // opponent" - "removes... for other reasons" as well as KO's via
        // this combat, per the keywords page. A blocker counts as gone if
        // it's no longer in the Attack Zone - it isn't blocking anymore
        // either way, whether that's because it was already removed before
        // this method ran, it was just KO'd above, or it Regenerated:
        // Regenerate's own text returns the die "to the field (but not the
        // Attack Zone)" - alive, but no longer a blocker, so it counts as
        // removed for Overcrush's purposes the same as an outright KO.
        foreach (var (attackerId, info) in overcrushCandidates)
        {
            if (!info.DeclaredBlockerIds.All(id => FindDie(state, id).Zone != Zone.AttackZone)) continue;
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

    // One wave of Keyword Fast's two-wave damage resolution (see
    // AssignCombatDamage's remarks). `fast` selects which side of the
    // Fast/non-Fast split this call resolves - a source die's own Fast
    // keyword decides which wave *its* damage lands in, independent of
    // whether the die on the other end of the engagement has Fast too.
    // Re-queries live attackers/blockers fresh (rather than working off a
    // snapshot) so the first wave's KOs are already reflected in what the
    // second wave sees - an attacker or blocker KO'd in the first wave
    // simply won't be found still in the Attack Zone here, so it never
    // gets to deal its own (slower) damage back.
    private static List<string> ResolveFastOrSlowDamage(
        GameState state,
        AbilityQueue queue,
        CombatAssignment assignment,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attackerDamageSplits,
        bool fast,
        IDiceRoller? roller)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList())
        {
            var liveBlockerIds = assignment.BlockersOf(attacker.Id)
                .Where(id => FindDie(state, id).Zone == Zone.AttackZone)
                .ToList();
            if (liveBlockerIds.Count == 0) continue;

            if (DieStats.HasKeyword(state, attacker, "Fast") == fast &&
                attackerDamageSplits.TryGetValue(attacker.Id, out var split))
            {
                foreach (var blockerId in liveBlockerIds)
                {
                    if (split.TryGetValue(blockerId, out var dealt) && dealt > 0)
                        FindDie(state, blockerId).Damage += dealt;
                }
            }

            // Rule 2.7.4.3.6/2.7.4.3.7 - each blocker deals its full attack
            // value to the (shared) attacker, timed by the blocker's own
            // Fast keyword rather than the attacker's.
            foreach (var blockerId in liveBlockerIds)
            {
                var blocker = FindDie(state, blockerId);
                if (DieStats.HasKeyword(state, blocker, "Fast") == fast)
                    attacker.Damage += DieStats.EffectiveAttack(state, blocker);
            }
        }

        // Rule 2.7.6.1 - simultaneous KO of anything at/over its defense
        // among whoever just took damage this wave (Regenerate, if the die
        // has it, intercepts inside TryResolveKO).
        var koIds = new List<string>();
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone).ToList())
        {
            if (!DieStats.TryResolveKO(state, die, roller)) continue;
            koIds.Add(die.Id);

            // Rule 2.7.6.5 - "when KO'd" fires for each die KO'd this way.
            TurnEngine.EnqueueTriggered(state, queue, die, TriggerType.WhenKOd);
        }
        return koIds;
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
