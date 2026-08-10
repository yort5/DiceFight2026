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

        // Rule-text "must attack" (Vulcan's Global) - same "if able" spirit
        // as DeclareBlockers' own forced-blocker check below: only
        // enforced against dice still actually eligible to attack
        // (controlled by the active player, still in the Field Zone) - a
        // forced die that got KO'd or moved elsewhere is just skipped.
        var forcedButOmitted = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Where(d => state.MustAttackThisTurn.Contains(d.Id) && !attackerDieIds.Contains(d.Id))
            .ToList();
        if (forcedButOmitted.Count > 0)
        {
            var names = string.Join(", ", forcedButOmitted.Select(d => DisplayName(state, d)));
            throw new InvalidOperationException($"{names} must attack this turn.");
        }

        foreach (var id in attackerDieIds)
        {
            var die = FindDie(state, id);
            // Rule 2.7's attackers are Character dice specifically -
            // Appendix 1's Continuous entry states outright "Continuous
            // dice cannot attack or block," and a Continuous Action die
            // now legitimately sits in the Field Zone too (see
            // TurnEngine.UseActionDie/ResolveContinuousDie), so the zone
            // check alone isn't enough to exclude it anymore.
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.FieldZone ||
                die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter))
                throw new InvalidOperationException($"Die {id} is not an eligible attacker.");
            die.Zone = Zone.AttackZone;

            // Rule 2.7.1.2 - "when attacks" fires for each attacking die.
            TurnEngine.EnqueueTriggered(state, queue, die, TriggerType.WhenAttacks);

            // Beast ("First Class", DPS058) - "when a character die with
            // Founder attacks, [...]" - a team-wide reactive scan, same
            // shape as TurnEngine.Field's own WhenAnotherDieFielded scan.
            TurnEngine.ResolveWhenAnotherDieAttacks(state, queue, die);
        }

        // Keyword Range - "When one or more Character dice with Range
        // attack, each active die with Range (on both sides)
        // simultaneously deals damage..." Triggered by an attacking die
        // specifically ("attack" - clarification 5, it doesn't matter
        // how many, only that at least one did), unlike Infiltrate/Tag
        // Out's "you may choose" windows this one isn't optional - but
        // it still needs a real sub-window since which opposing die each
        // Range die targets is a genuine choice. See ResolveRange.
        var hasRangeTrigger = attackerDieIds.Any(id => DieStats.HasKeyword(state, FindDie(state, id), "Range"));
        state.AttackSubStep = hasRangeTrigger ? AttackSubStep.RangeWindow : AttackSubStep.DeclareBlockers;
    }

    // Keyword Range X - Appendix 1, plus five numbered clarifications:
    // (1) each Range die may choose a different target, and a side's
    // Range dice still deal their own damage even if a Range die on
    // *that* side was KO'd by the other side's Range damage first - so
    // both sides' assignments are validated against the pre-damage state
    // up front, before either side's damage is applied, and never
    // re-validated once damage starts landing. (2) damage is applied
    // active-player-first, then inactive-player, even though it's
    // conceptually simultaneous - see ApplyRangeDamageAndResolveKOs.
    // (3)/(4) "when damaged" reactions aren't interrupted mid-Range and
    // only fire once all Range damage is resolved - moot today, since
    // WhenDamaged isn't wired to fire from anywhere yet (a pre-existing,
    // already-documented gap; no card needs it). (5) - the trigger check
    // above cares only whether *any* attacker has Range, not how many;
    // contribution below is every active Range die on a side, attacking
    // or not (the worked example - a lone Range 1 attacker still adds in
    // three more active Range 1 dice and two Range 2 dice sitting idle
    // in the Field Zone). Deliberately not modeled: the engine doesn't
    // verify every eligible Range die was actually included in a side's
    // assignment list (matches the trust level already extended to
    // Infiltrate/Tag Out's own caller-supplied choice lists), so a
    // caller that omits an eligible die simply doesn't deal its damage
    // rather than being rejected - flagged rather than silently assumed.
    public static void ResolveRange(
        GameState state, AbilityQueue queue,
        IReadOnlyList<(string RangeDieId, string TargetDieId)> activePlayerAssignments,
        IReadOnlyList<(string RangeDieId, string TargetDieId)> inactivePlayerAssignments,
        IDiceRoller? roller = null)
    {
        RequireSubStep(state, AttackSubStep.RangeWindow);

        var inactiveId = state.OpponentOf(state.ActivePlayerId);
        ValidateRangeAssignments(state, state.ActivePlayerId, activePlayerAssignments);
        ValidateRangeAssignments(state, inactiveId, inactivePlayerAssignments);

        ApplyRangeDamageAndResolveKOs(state, queue, activePlayerAssignments, roller);
        ApplyRangeDamageAndResolveKOs(state, queue, inactivePlayerAssignments, roller);

        state.AttackSubStep = AttackSubStep.DeclareBlockers;
    }

    private static void ValidateRangeAssignments(
        GameState state, string controllerId, IReadOnlyList<(string RangeDieId, string TargetDieId)> assignments)
    {
        foreach (var (rangeDieId, targetDieId) in assignments)
        {
            var rangeDie = FindDie(state, rangeDieId);
            if (rangeDie.ControllerId != controllerId || rangeDie.Zone is not (Zone.FieldZone or Zone.AttackZone))
                throw new InvalidOperationException($"{DisplayName(state, rangeDie)} is not an eligible Range die.");
            if (!DieStats.HasKeyword(state, rangeDie, "Range"))
                throw new InvalidOperationException($"{DisplayName(state, rangeDie)} does not have Range.");

            var target = FindDie(state, targetDieId);
            if (target.ControllerId == controllerId)
                throw new InvalidOperationException($"{DisplayName(state, target)} is not an opposing Character die.");
            if (target.Zone is not (Zone.FieldZone or Zone.AttackZone) || target.Status is not (DieStatus.Character or DieStatus.SidekickCharacter))
                throw new InvalidOperationException($"{DisplayName(state, target)} is not a legal Range target.");
        }
    }

    // Applies one side's whole batch of Range damage before checking any
    // KOs from it (clarification 2's "simultaneously" within a side),
    // then resolves KOs once per distinct target rather than once per
    // assignment (two Range dice hitting the same target stack their
    // damage into one KO check, not two).
    private static void ApplyRangeDamageAndResolveKOs(
        GameState state, AbilityQueue queue, IReadOnlyList<(string RangeDieId, string TargetDieId)> assignments, IDiceRoller? roller)
    {
        var recipients = new List<DieInstance>();
        foreach (var (rangeDieId, targetDieId) in assignments)
        {
            var amount = DieStats.RangeAmount(state, FindDie(state, rangeDieId));
            var recipient = DieStats.ApplyDamage(state, FindDie(state, targetDieId), amount);
            if (recipient is not null) recipients.Add(recipient);
        }

        var koIds = new List<string>();
        // Distinct by id, not by reference - two Range dice hitting the
        // same target (or redirecting to the same Colossus) still means
        // one KO check, not two.
        foreach (var recipient in recipients.DistinctBy(d => d.Id))
        {
            if (recipient.Zone is Zone.FieldZone or Zone.AttackZone && DieStats.TryResolveKO(state, recipient, roller))
                koIds.Add(recipient.Id);
        }
        // Previously this only fired WhenKOd, never Retaliation - a Range
        // KO is just as real a KO as a combat-damage one, and now goes
        // through the same shared reaction path (TurnEngine.
        // ResolveKOReactions) the combat-damage wave below already uses.
        TurnEngine.ResolveKOReactions(state, queue, koIds);
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
        ValidateObscure(state, assignment);
        ValidateMinimumBlockers(state, assignment);

        foreach (var id in blockerDieIds)
        {
            var die = FindDie(state, id);
            // Same Continuous exclusion as DeclareAttackers above.
            if (die.ControllerId != inactiveId || die.Zone != Zone.FieldZone ||
                die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter) ||
                state.CantBlockThisTurn.Contains(die.Id))
                throw new InvalidOperationException($"Die {id} is not an eligible blocker.");
            die.Zone = Zone.AttackZone;
        }

        RecordDeadlyEngagements(state, assignment);
        RecordVulcanTextBlanking(state, assignment);
        ResolveEnergyDrain(state, assignment);

        // Keyword Infiltrate carves out a real sub-window here, strictly
        // before the Action/Global window opens - but only when there's
        // actually a decision to make (rule text: "you may choose" - an
        // optional choice, not a forced effect). Skipping straight past a
        // window when nothing is eligible means every combat that never
        // touches Infiltrate (or Tag Out) proceeds exactly as it did
        // before either keyword existed - no caller has to learn about
        // (or explicitly no-op through) a sub-step that has nothing to
        // offer. Tag Out's own window is checked by NextSubStepAfterBlockers
        // below when Infiltrate's isn't reachable at all.
        var hasInfiltrateChoice = state.DiceIn(state.ActivePlayerId, Zone.AttackZone)
            .Any(d => assignment.BlockersOf(d.Id).Count == 0 && DieStats.HasKeyword(state, d, "Infiltrate"));

        state.AttackSubStep = hasInfiltrateChoice ? AttackSubStep.InfiltrateWindow : NextSubStepAfterBlockers(state);
    }

    // Shared by DeclareBlockers (when Infiltrate's own window isn't
    // reachable at all) and ResolveInfiltrate (once it's done resolving) -
    // both keywords carve out the identical "immediately after blockers
    // are declared... before Action dice or Global abilities may be
    // used" timing (Appendix 1), so this is where the chain of optional
    // post-blocker windows continues. Re-checked fresh each time rather
    // than decided once upfront, so e.g. an Infiltrate die returning to
    // the Field Zone can make it newly Tag-Out-eligible in the same combat.
    private static AttackSubStep NextSubStepAfterBlockers(GameState state)
    {
        var hasTagOutChoice = state.Dice.Any(d => d.Zone == Zone.FieldZone && DieStats.HasKeyword(state, d, "Tag Out"));
        return hasTagOutChoice ? AttackSubStep.TagOutWindow : AttackSubStep.ActionAndGlobalWindow;
    }

    // Keyword Infiltrate - "When a Character die with Infiltrate attacks
    // and is not blocked, you may choose to remove that die from combat
    // immediately after blockers are declared before Action dice or
    // Global abilities may be used. If you do, that die deals 1 damage to
    // your opponent, and the die remains in your Field Zone." A real
    // choice (unlike Deadly/Energy Drain, which are fully automatic), so
    // - like Declare Attackers/Blockers - it's the caller's job to say
    // which eligible dice actually use it. Only reachable when
    // DeclareBlockers found at least one real choice to offer - see its
    // own remarks; infiltratingDieIds may still be empty here (choosing
    // to use none of them is a legal choice too).
    public static void ResolveInfiltrate(
        GameState state, AbilityQueue queue, CombatAssignment assignment, IReadOnlyList<string> infiltratingDieIds)
    {
        RequireSubStep(state, AttackSubStep.InfiltrateWindow);

        foreach (var id in infiltratingDieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.AttackZone)
                throw new InvalidOperationException($"Die {id} is not an eligible Infiltrate candidate.");
            if (!DieStats.HasKeyword(state, die, "Infiltrate"))
                throw new InvalidOperationException($"{DisplayName(state, die)} does not have Infiltrate.");
            if (assignment.BlockersOf(id).Count > 0)
                throw new InvalidOperationException($"{DisplayName(state, die)} was blocked and cannot Infiltrate.");

            die.Zone = Zone.FieldZone; // "remains in your Field Zone" - removed from combat, not Out of Play
            state.GetPlayer(state.OpponentOf(state.ActivePlayerId)).Life -= 1;

            // Reactive "while active, each time one of your character dice
            // uses Infiltrate" abilities (e.g. Ricochet) aren't the
            // infiltrating die's own ability - they belong to whichever of
            // the controller's dice are active right now, same shape as
            // Attune reacting to "you use an Action die." The die that
            // just infiltrated is itself active again by this point (it's
            // back in the Field Zone above), so it's included here too.
            foreach (var reactor in state.DiceIn(die.ControllerId, Zone.FieldZone)
                .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone)))
            {
                TurnEngine.EnqueueTriggered(state, queue, reactor, TriggerType.WhenInfiltrates);
            }
        }

        state.AttackSubStep = NextSubStepAfterBlockers(state);
    }

    // Keyword Tag Out - "After blockers are declared, you may Prep this
    // die from the Field Zone to give target Character die +2A and +2D
    // until end of turn." Appendix 1's own clarification gives it the
    // exact same timing as Infiltrate ("immediately after blockers are
    // declared before Action dice or Global abilities may be used") - see
    // NextSubStepAfterBlockers. Fixed, card-invariant numbers (no card
    // overrides the +2A/+2D the way Black Manta overrides Retaliation's
    // base amount), so - like Infiltrate - this is built directly into
    // CombatEngine rather than via an AbilityDef/TriggerType; the target
    // buff is a genuine Applied modifier (rule 3.4.3 - "until end of
    // turn," cleared at Clean Up), not a Static one, so it goes through
    // the die's own AppliedModifiers exactly like Wasp's Attune buff does.
    // Usable by either player's own dice (the ability doesn't say "the
    // active player may," unlike Infiltrate which is inherently one-sided
    // since only an attacker can Infiltrate) - each use is validated
    // against its own Tag Out die's controller, not state.ActivePlayerId.
    // "Prep this die from the Field Zone" - a die currently attacking or
    // blocking (Attack Zone) isn't eligible; only one sitting at home.
    public static void ResolveTagOut(
        GameState state, AbilityQueue queue, IReadOnlyList<(string TagOutDieId, string TargetDieId)> uses)
    {
        RequireSubStep(state, AttackSubStep.TagOutWindow);

        foreach (var (tagOutDieId, targetDieId) in uses)
        {
            var tagOutDie = FindDie(state, tagOutDieId);
            if (tagOutDie.Zone != Zone.FieldZone)
                throw new InvalidOperationException($"{DisplayName(state, tagOutDie)} is not an eligible Tag Out candidate.");
            if (!DieStats.HasKeyword(state, tagOutDie, "Tag Out"))
                throw new InvalidOperationException($"{DisplayName(state, tagOutDie)} does not have Tag Out.");

            var target = FindDie(state, targetDieId);
            if (target.Zone is not (Zone.FieldZone or Zone.AttackZone) || target.Status is not (DieStatus.Character or DieStatus.SidekickCharacter))
                throw new InvalidOperationException($"{DisplayName(state, target)} is not a legal Tag Out target.");

            tagOutDie.Zone = Zone.PrepArea; // rule 1.5.3.2 - Prepped, not KO'd
            tagOutDie.ResetToUnrolled();
            target.AppliedModifiers.Add(new Modifier(AttackDelta: 2, DefenseDelta: 2, Source: tagOutDie.Id));
        }

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

    // Vulcan ("Power Suppression", DPS095) - "Ignore the abilities of
    // character dice blocking or blocked by Vulcan." Same per-engagement
    // scan shape as RecordDeadlyEngagements just above, populating
    // GameState.BlankedDieIds (see DieStats.GetCard) instead of
    // DeadlyEngagedDieIds - Vulcan must be directly engaged (an attacker,
    // or one of its blockers), not merely active elsewhere on the board.
    private static void RecordVulcanTextBlanking(GameState state, CombatAssignment assignment)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            var attackerBlanksOpponents = DieStats.GetCard(state, attacker)?.GrantsIgnoresAbilitiesWhileEngaged ?? false;
            foreach (var blockerId in assignment.BlockersOf(attacker.Id))
            {
                if (attackerBlanksOpponents) state.BlankedDieIds.Add(blockerId);
                if (DieStats.GetCard(state, FindDie(state, blockerId))?.GrantsIgnoresAbilitiesWhileEngaged ?? false)
                    state.BlankedDieIds.Add(attacker.Id);
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
    // reverts to normal, it does NOT become unblockable itself. The
    // "target was KO'd/removed", "duplicate target", and (now that Obscure
    // exists) "an ability made it unblockable" cases are all modeled.
    private static IReadOnlyDictionary<string, string> ActiveCallOutTargets(GameState state)
    {
        var duplicateTargets = state.CallOutTargets.Values
            .GroupBy(targetId => targetId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        return state.CallOutTargets
            .Where(kvp =>
                !duplicateTargets.Contains(kvp.Value)
                && FindDie(state, kvp.Value).Zone is Zone.FieldZone or Zone.AttackZone
                && !IsObscured(state, FindDie(state, kvp.Key)))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    // Keyword Obscure - "all dice from the applicable Character card are
    // unblockable until end of turn." Checked up front against the whole
    // assignment, same shape as ValidateCallOuts, so a rejected attempt
    // fails cleanly before any zone changes happen below.
    private static void ValidateObscure(GameState state, CombatAssignment assignment)
    {
        foreach (var attacker in state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
        {
            if (IsObscured(state, attacker) && assignment.BlockersOf(attacker.Id).Count > 0)
                throw new InvalidOperationException($"{DisplayName(state, attacker)} is unblockable this turn (Obscure).");
        }
    }

    private static bool IsObscured(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null && state.ObscuredCardIds.Contains(cardId);
    }

    // Magneto ("Visionary", DPS081) - "your Brotherhood of Mutants
    // character dice can only be blocked by 2 or more character dice."
    // Granter must be active on the ATTACKER's own side (the ability
    // says "your," i.e. the attacking player's), same active-granter
    // scan shape as every other continuous Grants* field - just
    // enforced here as a blocking-legality check instead of a stat/cost
    // computation. Zero blockers (unblocked) is always legal regardless
    // of any minimum - only a NONZERO count below the minimum is
    // rejected.
    private static void ValidateMinimumBlockers(GameState state, CombatAssignment assignment)
    {
        var attackerControllerId = state.ActivePlayerId;
        var granterCards = state.DiceIn(attackerControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(attackerControllerId, Zone.AttackZone))
            .Select(d => DieStats.GetCard(state, d))
            .Where(c => c is not null)
            .Distinct()
            .ToList();

        foreach (var attacker in state.DiceIn(attackerControllerId, Zone.AttackZone))
        {
            var attackerCard = DieStats.GetCard(state, attacker);
            var blockerCount = assignment.BlockersOf(attacker.Id).Count;
            if (blockerCount == 0) continue;

            foreach (var granterCard in granterCards)
            {
                if (granterCard!.GrantsMinimumBlockersRequirement is not { } requirement) continue;
                if (requirement.RequiredAffiliation is { } affiliation &&
                    !(attackerCard?.Affiliations.Contains(affiliation) ?? false)) continue;
                if (blockerCount < requirement.MinBlockers)
                {
                    throw new InvalidOperationException(
                        $"{DisplayName(state, attacker)} can only be blocked by {requirement.MinBlockers} or more character dice.");
                }
            }
        }
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
        // Colossus ("Organic Steel", DPS063) - a redirect target isn't
        // guaranteed to already be in the Attack Zone (it just has to be
        // active), so the KO scan below unions this with its own
        // Attack-Zone-wide scan rather than relying on that scan alone.
        var damagedRecipients = new List<DieInstance>();

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
                    {
                        var recipient = DieStats.ApplyDamage(state, FindDie(state, blockerId), dealt);
                        if (recipient is not null) damagedRecipients.Add(recipient);
                    }
                }
            }

            // Rule 2.7.4.3.6/2.7.4.3.7 - each blocker deals its full attack
            // value to the (shared) attacker, timed by the blocker's own
            // Fast keyword rather than the attacker's.
            foreach (var blockerId in liveBlockerIds)
            {
                var blocker = FindDie(state, blockerId);
                if (DieStats.HasKeyword(state, blocker, "Fast") == fast)
                {
                    var recipient = DieStats.ApplyDamage(state, attacker, DieStats.EffectiveAttack(state, blocker));
                    if (recipient is not null) damagedRecipients.Add(recipient);
                }
            }
        }

        // Rule 2.7.6.1 - simultaneous KO of anything at/over its defense
        // among whoever just took damage this wave (Regenerate, if the die
        // has it, intercepts inside TryResolveKO).
        var koIds = new List<string>();
        var koScanCandidates = state.Dice.Where(d => d.Zone == Zone.AttackZone)
            .Concat(damagedRecipients)
            .DistinctBy(d => d.Id)
            .ToList();
        foreach (var die in koScanCandidates)
        {
            if (DieStats.TryResolveKO(state, die, roller)) koIds.Add(die.Id);
        }

        // Rule 2.7.6.5 ("when KO'd" fires for each die KO'd this way) and
        // Retaliation - both go through the shared reactive-KO path now
        // (TurnEngine.ResolveKOReactions), called once for the whole
        // simultaneous wave so a Retaliation die that was ALSO KO'd in
        // this same wave is already gone from the active scan by the
        // time anyone's Retaliation is checked (clarification 1's
        // worked example - a Retaliation die and an affiliated ally
        // KO'd simultaneously by combat damage do not trigger each
        // other), regardless of which order the wave happened to
        // process dice in.
        TurnEngine.ResolveKOReactions(state, queue, koIds);

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
