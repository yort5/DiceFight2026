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
    // Mobile refresh (2026-09) - the Attack Zone is four fixed lanes.
    // attackerLanes carries each declared attacker's chosen lane (0-3);
    // several attackers may share one. A lane is the unit of combat
    // (2026-09-21): its attackers' Attack pools against its blockers -
    // see LaneFight and AssignCombatDamage.
    public const int LaneCount = 4;

    // Convenience overload for callers (tests, TurnEngine's skip-combat
    // path) that don't care which lane an attacker lands in - spreads
    // them round-robin across the four lanes so nothing has to reason
    // about lane assignment just to declare an attack.
    public static void DeclareAttackers(GameState state, AbilityQueue queue, IReadOnlyList<string> attackerDieIds) =>
        DeclareAttackers(state, queue, attackerDieIds.Select((id, i) => (id, lane: i % LaneCount)).ToDictionary(x => x.id, x => x.lane));

    public static void DeclareAttackers(GameState state, AbilityQueue queue, IReadOnlyDictionary<string, int> attackerLanes)
    {
        RequireStep(state, StepIds.SelectAttackers);
        var attackerDieIds = attackerLanes.Keys.ToList();

        var forcedButOmitted = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Where(d => d.CombatFlags.Contains(CombatFlagKind.MustAttack) && !attackerDieIds.Contains(d.Id))
            .ToList();
        if (forcedButOmitted.Count > 0)
            throw new InvalidOperationException($"{string.Join(", ", forcedButOmitted.Select(d => d.Id))} must attack this turn.");

        foreach (var (id, lane) in attackerLanes)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.FieldZone || state.GetCurrentFace(die)?.Character is null)
                throw new InvalidOperationException($"Die '{id}' is not an eligible attacker.");
            if (die.CombatFlags.Contains(CombatFlagKind.CantAttack) || die.CombatFlags.Contains(CombatFlagKind.OnlyBlocker))
                throw new InvalidOperationException($"Die '{id}' cannot attack this turn.");
            if (lane < 0 || lane >= LaneCount)
                throw new InvalidOperationException($"Lane {lane} is out of range for die '{id}'.");

            die.Zone = Zone.AttackZone;
            die.Lane = lane;
            // Rule 2.7.1.2 - "when attacks" fires for each attacking die.
            EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, die, die.ControllerId, state.CurrentStepId));
        }
        state.LogEvent(state.ActivePlayerId, attackerDieIds.Count == 0
            ? $"{state.NameOf(state.ActivePlayerId)} declares no attackers."
            : $"{state.NameOf(state.ActivePlayerId)} declares {attackerDieIds.Count} {(attackerDieIds.Count == 1 ? "attacker" : "attackers")}.");

        // "Select attackers. Resolve effects that occur due to attacking."
        EnterStep(state, queue, StepIds.AttackEffects);
        EnterStep(state, queue, StepIds.AssignBlockers);
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
        state.LogEvent(inactiveId, blockerDieIds.Count == 0
            ? $"{state.NameOf(inactiveId)} leaves every attacker unblocked."
            : $"{state.NameOf(inactiveId)} assigns {blockerDieIds.Count} {(blockerDieIds.Count == 1 ? "blocker" : "blockers")}.");

        // Keyword Deadly - record who is engaged with a Deadly die NOW,
        // not at damage: it counts even if either die is removed first.
        // Engagement is per lane, so every blocker of a lane is engaged
        // with every attacker in it.
        foreach (var laneGroup in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).GroupBy(a => a.Lane))
        {
            var laneBlockers = LaneBlockerIds(assignment, laneGroup).Select(id => FindDie(state, id)).ToList();
            foreach (var attacker in laneGroup)
            {
                var attackerDeadly = QueryEngine.GetKeywords(state, attacker).Contains("Deadly");
                foreach (var blocker in laneBlockers)
                {
                    if (attackerDeadly) RecordDeadlyEngagement(state, blocker.Id, attacker.Id);
                    if (QueryEngine.GetKeywords(state, blocker).Contains("Deadly")) RecordDeadlyEngagement(state, attacker.Id, blocker.Id);
                }
            }
        }

        // "Assign blockers. Resolve effects that occur due to blocking."
        EnterStep(state, queue, StepIds.BlockEffects);
        EnterStep(state, queue, StepIds.ActionGlobalWindow);
    }

    private static void RecordDeadlyEngagement(GameState state, string engagedId, string deadlyId)
    {
        if (!state.DeadlyEngagedDieIds.TryGetValue(engagedId, out var sources))
            state.DeadlyEngagedDieIds[engagedId] = sources = [];
        sources.Add(deadlyId);
    }

    // CombatFlagKind.Unblockable (Finding 14 - Falcon "Recon").
    private static void ValidateUnblockable(GameState state, CombatAssignment assignment)
    {
        foreach (var laneGroup in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).GroupBy(a => a.Lane))
        {
            var attackers = laneGroup.ToList();
            if (LaneBlockerIds(assignment, attackers).Count == 0) continue;
            var unblockable = attackers.FirstOrDefault(a => a.CombatFlags.Contains(CombatFlagKind.Unblockable));
            if (unblockable is not null)
                throw new InvalidOperationException($"Die '{unblockable.Id}' is unblockable this turn.");
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
        foreach (var laneGroup in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).GroupBy(a => a.Lane))
        {
            var attackers = laneGroup.ToList();
            var blockerCount = LaneBlockerIds(assignment, attackers).Count;
            if (blockerCount == 0) continue;

            foreach (var attacker in attackers)
            {
                var required = state.CombatRules
                    .Where(r => r.Kind == CombatRuleKind.MinBlockers && r.AppliesTo(state, attacker))
                    .Select(r => r.N ?? 1)
                    .DefaultIfEmpty(0)
                    .Max();

                if (blockerCount < required)
                    throw new InvalidOperationException($"Die '{attacker.Id}' can only be blocked by {required} or more character dice.");
            }
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

    // A lane is the unit of combat: every attacker in it fights together
    // (their Attack pools against the lane's blockers) and every blocker
    // assigned to ANY attacker in the lane defends the whole lane. The UI
    // attaches a lane's blockers to its first attacker only, so nothing
    // here trusts which attacker a blocker was assigned to.
    private sealed class LaneFight
    {
        public required List<DieInstance> Attackers { get; init; }
        public required List<string> DeclaredBlockerIds { get; init; }
        public required int TotalAttack { get; init; }
        public required int BlockerDefenseTotal { get; init; }
        public required bool Overcrush { get; init; }
        // attacker id -> blocker id -> damage, and the reverse (blocker
        // id -> attacker id -> damage), both fixed up front.
        public Dictionary<string, Dictionary<string, int>> AttackerSplits { get; } = [];
        public Dictionary<string, Dictionary<string, int>> BlockerSplits { get; } = [];
    }

    private static List<string> LaneBlockerIds(CombatAssignment assignment, IEnumerable<DieInstance> laneAttackers) =>
        laneAttackers.SelectMany(a => assignment.BlockersOf(a.Id)).Distinct().ToList();

    // Pools `sources` (id, damage) against `targets` (die, remaining
    // lethal): each source's damage goes lethal-first down the target
    // list, and whatever is left lands on the last target. Same rule the
    // controller's gang-block auto-split used, now shared across a lane.
    private static Dictionary<string, Dictionary<string, int>> AutoSplit(
        IReadOnlyList<(string Id, int Damage)> sources, IReadOnlyList<(string Id, int Lethal)> targets)
    {
        var lethalLeft = targets.ToDictionary(t => t.Id, t => t.Lethal);
        var result = new Dictionary<string, Dictionary<string, int>>();
        foreach (var (sourceId, damage) in sources)
        {
            var remaining = damage;
            var split = new Dictionary<string, int>();
            for (var i = 0; i < targets.Count; i++)
            {
                var id = targets[i].Id;
                var give = i == targets.Count - 1 ? remaining : Math.Min(remaining, lethalLeft[id]);
                split[id] = give;
                lethalLeft[id] = Math.Max(0, lethalLeft[id] - give);
                remaining -= give;
            }
            result[sourceId] = split;
        }
        return result;
    }

    // Rule 2.7.4 (assign) and 2.7.6 (resolve KOs, return survivors).
    // A lane's attackers deal their combined Attack to the lane's
    // blockers, and its blockers deal their combined Attack back across
    // the lane's attackers (both lethal-first, remainder on the last).
    // attackerDamageSplits is an optional override for a lane holding a
    // single attacker (the active player's own choice of how to divide
    // its full attack value, rule 2.7.4.3.4); anything else is split
    // automatically.
    public static CombatResult AssignCombatDamage(
        GameState state, AbilityQueue queue, CombatAssignment assignment,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> attackerDamageSplits)
    {
        RequireStep(state, StepIds.ActionGlobalWindow);
        EnterStep(state, queue, StepIds.FastDamage);

        var inactivePlayer = state.GetPlayer(state.OpponentOf(state.ActivePlayerId));
        var fights = new List<LaneFight>();

        foreach (var laneGroup in state.DiceIn(state.ActivePlayerId, Zone.AttackZone).GroupBy(a => a.Lane))
        {
            var laneAttackers = laneGroup.ToList();
            var declaredBlockerIds = LaneBlockerIds(assignment, laneAttackers);

            if (declaredBlockerIds.Count == 0)
            {
                // Rule 2.7.4.3.1 - unblocked: hits the player directly and
                // leaves the Attack Zone before anything else can resolve.
                foreach (var attacker in laneAttackers)
                {
                    var attack = QueryEngine.GetAttack(state, attacker);
                    inactivePlayer.Life -= attack;
                    attacker.Zone = Zone.OutOfPlay;
                    if (attack > 0)
                    {
                        var unblockedName = attacker.CardId is { } unblockedCardId ? state.CardCatalog[unblockedCardId].Name : "a Tardigrade";
                        state.LogEvent(attacker.ControllerId, $"{unblockedName} hits {inactivePlayer.Name} directly for {attack}.");
                    }
                }
                continue;
            }

            // "Once blocked, always blocked" - a lane with a declared
            // blocker never falls through to the unblocked branch above,
            // even if every blocker is later removed; damage with nowhere
            // live to land is wasted (unless Overcrush redirects it).
            var liveBlockers = declaredBlockerIds.Select(id => FindDie(state, id)).Where(b => b.Zone == Zone.AttackZone).ToList();
            var attacks = laneAttackers.Select(a => (a.Id, Damage: QueryEngine.GetAttack(state, a))).ToList();

            // Direct feedback (2026-09-18/21): a lane holding 2+ live
            // attackers grants EVERY attacker in it Overcrush, and any
            // attacker's own Overcrush counts for the whole lane. Read off
            // the lane as it stood before either wave runs, so an
            // attacker KO'd earlier in the Action/Global Window isn't
            // counted. Still deliberately isolated to this one condition.
            var overcrush = laneAttackers.Count >= 2 ||
                laneAttackers.Any(a => QueryEngine.GetKeywords(state, a).Contains("Overcrush"));

            var fight = new LaneFight
            {
                Attackers = laneAttackers,
                DeclaredBlockerIds = declaredBlockerIds,
                TotalAttack = attacks.Sum(a => a.Damage),
                BlockerDefenseTotal = liveBlockers.Sum(b => QueryEngine.GetDefense(state, b)),
                Overcrush = overcrush,
            };

            if (liveBlockers.Count > 0)
            {
                if (laneAttackers.Count == 1 && attackerDamageSplits.TryGetValue(laneAttackers[0].Id, out var provided))
                {
                    if (provided.Values.Sum() != attacks[0].Damage)
                        throw new InvalidOperationException(
                            $"Damage split for attacker '{laneAttackers[0].Id}' must assign its full attack value ({attacks[0].Damage}).");
                    fight.AttackerSplits[laneAttackers[0].Id] = provided.ToDictionary(kv => kv.Key, kv => kv.Value);
                }
                else
                {
                    var blockerTargets = liveBlockers.Select(b => (b.Id, Lethal: Math.Max(0, QueryEngine.GetDefense(state, b) - b.Damage))).ToList();
                    foreach (var (id, split) in AutoSplit(attacks, blockerTargets)) fight.AttackerSplits[id] = split;
                }

                var attackerTargets = laneAttackers.Select(a => (a.Id, Lethal: Math.Max(0, QueryEngine.GetDefense(state, a) - a.Damage))).ToList();
                var blockerAttacks = liveBlockers.Select(b => (b.Id, Damage: QueryEngine.GetAttack(state, b))).ToList();
                foreach (var (id, split) in AutoSplit(blockerAttacks, attackerTargets)) fight.BlockerSplits[id] = split;
            }

            fights.Add(fight);
        }

        // Keyword Fast - "Characters with Fast deal combat damage before
        // other Character dice in the Attack Step. All Character dice
        // with Fast deal damage at the same time." Two full waves rather
        // than one: every Fast die's damage lands (and can KO) completely
        // first, so a non-Fast die KO'd this way never deals its own
        // damage back at all (the rulebook's own worked example - see
        // the test suite).
        var koIds = new List<string>();
        koIds.AddRange(ResolveFastOrSlowDamage(state, queue, fights, fast: true));
        EnterStep(state, queue, StepIds.NormalDamage);
        koIds.AddRange(ResolveFastOrSlowDamage(state, queue, fights, fast: false));

        // "Resolve effects that occur due to damage or KO." The DieDamaged
        // and DieKOd abilities are already queued by the waves above; this
        // names the window they resolve in.
        foreach (var koId in koIds)
        {
            var koDie = FindDie(state, koId);
            var koName = koDie.CardId is { } koCardId ? state.CardCatalog[koCardId].Name : "A Tardigrade";
            state.LogEvent(koDie.ControllerId, $"{koName} is knocked out.");
        }
        EnterStep(state, queue, StepIds.DamageAndKoEffects);

        // Glossary/FAQ - Overcrush: "if this character die KO's or removes
        // all of its blockers, it deals any leftover damage to your
        // opponent." Per lane now: the lane's combined Attack minus its
        // blockers' combined Defense, once every declared blocker is gone.
        foreach (var fight in fights)
        {
            if (!fight.Overcrush) continue;
            if (!fight.DeclaredBlockerIds.All(id => FindDie(state, id).Zone != Zone.AttackZone)) continue;
            var leftover = fight.TotalAttack - fight.BlockerDefenseTotal;
            if (leftover <= 0) continue;
            inactivePlayer.Life -= leftover;
            state.LogEvent(state.ActivePlayerId, $"Overcrush: {leftover} excess damage carries through to {inactivePlayer.Name}.");
        }

        // Rule 2.7.6.6 - "Return remaining dice in the Attack Zone to the
        // Field Zone", the TURN SUMMARY's own final Attack Step entry.
        EnterStep(state, queue, StepIds.ReturnToField);
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone))
        {
            die.Zone = Zone.FieldZone;
            die.Lane = null;
        }
        // Unlike v1, this does NOT also advance CurrentStep to CleanUp -
        // the caller calls TurnEngine.CleanUp explicitly afterward, same
        // as the skip-combat path already does.

        return new CombatResult(koIds);
    }

    // One wave of Keyword Fast's two-wave damage resolution: first
    // MarkDamage lands on BOTH directions of every still-live lane
    // engagement in this wave (each attacker's split onto the lane's
    // blockers, each blocker's split back onto the lane's attackers), with
    // nothing KO'd yet - so a lethally-outmatched die still lands its own
    // damage in the SAME wave. Only then does the second pass KO whoever
    // crossed their Defense, simultaneously (rule 2.7.6.1). A slower die
    // KO'd by a Fast one in an EARLIER wave never got its damage marked.
    private static List<string> ResolveFastOrSlowDamage(GameState state, AbilityQueue queue, List<LaneFight> fights, bool fast)
    {
        var wavedRecipients = new List<DieInstance>();

        foreach (var fight in fights)
        {
            var liveAttackers = fight.Attackers.Where(a => a.Zone == Zone.AttackZone).ToList();
            var liveBlockers = fight.DeclaredBlockerIds.Select(id => FindDie(state, id)).Where(b => b.Zone == Zone.AttackZone).ToList();
            if (liveBlockers.Count == 0 || liveAttackers.Count == 0) continue;

            foreach (var attacker in liveAttackers)
            {
                if (QueryEngine.GetKeywords(state, attacker).Contains("Fast") != fast) continue;
                if (!fight.AttackerSplits.TryGetValue(attacker.Id, out var split)) continue;
                foreach (var blocker in liveBlockers)
                {
                    if (split.TryGetValue(blocker.Id, out var dealt) && dealt > 0 &&
                        EffectInterpreter.MarkDamage(state, queue, DamageSource.Combat, blocker.Id, dealt) is { } recipient)
                        wavedRecipients.Add(recipient);
                }
            }

            foreach (var blocker in liveBlockers)
            {
                if (QueryEngine.GetKeywords(state, blocker).Contains("Fast") != fast) continue;
                if (!fight.BlockerSplits.TryGetValue(blocker.Id, out var split)) continue;
                foreach (var attacker in liveAttackers)
                {
                    if (split.TryGetValue(attacker.Id, out var dealt) && dealt > 0 &&
                        EffectInterpreter.MarkDamage(state, queue, DamageSource.Combat, attacker.Id, dealt) is { } recipient)
                        wavedRecipients.Add(recipient);
                }
            }
        }

        // Rule 2.7.6.1 - resolve KOs simultaneously, among everyone still
        // in the Attack Zone plus every redirect recipient.
        var koIds = new List<string>();
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.AttackZone).Concat(wavedRecipients).DistinctBy(d => d.Id).ToList())
        {
            if (EffectInterpreter.TryResolveKO(state, queue, die))
                koIds.Add(die.Id);
        }

        return koIds;
    }

    // Moves onto a step and fires its window event, so every entry in
    // the Attack Step is addressable by an ability (EventFilter.Step).
    // Non-input steps are walked THROUGH by the method that precedes
    // them - the caller only ever has to act on NeedsInput steps, which
    // is what that flag is for.
    private static void EnterStep(GameState state, AbilityQueue queue, string stepId)
    {
        state.MoveToStep(stepId);
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, stepId));
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
