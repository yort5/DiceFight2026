using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Executes the 18 closed-vocabulary effect templates (V2_PLAN.md Phase 5,
// V2_VOCABULARY.md Part 1) against a GameState. Written continuation-
// passing style (`Action onComplete` threaded through every private
// Execute* helper) rather than v1's flat switch, because Phase 5 - unlike
// v1's own targeting seam (EffectContext.ResolveTargets, a caller-
// supplied function) - commits to routing EVERY real player decision
// through one mechanism: PendingChoice (Model/PendingChoice.cs). A choice
// pauses resolution (GameState.PendingChoice becomes non-null and the
// node's own Execute call returns without calling onComplete);
// AnswerPendingChoice's call into the captured Resolve closure is what
// finishes the rest of the tree. Sequence/Conditional/MayPay/
// DrawAndChooseOne/Distribute all fall out of this same continuation
// shape for free, rather than needing their own separate pause/resume
// bookkeeping.
//
// Rule 3.2.5 - "an ability reacts to the game state as it existed when
// it entered the queue" - is modeled as a PER-ABILITY snapshot (user
// decision, 2026-08-24, replacing Phase 5's documented resolve-live
// simplification once Casket of Ancient Winters' migration actually hit
// it): when one ability's resolution begins, the zone/face of every die
// is snapshotted, and every TargetFilter CANDIDATE POOL inside that one
// ability's tree resolves against the snapshot - so Casket's own Ko
// clause's KO'd dice (landing in the Prep Area, rule 1.5.3.2) never
// become candidates for its own later Prep-Area-targeting clause. The
// snapshot dissolves the moment that ability finishes: the next ability
// in the queue resolves completely against LIVE state, including
// everything the previous ability changed (its KO'd dice really are in
// the Prep Area now, a die it KO'd really is gone, and - once the
// ability-blanking spike lands - text it blanked really is blank even
// for an already-queued trigger, which still fires but does nothing).
//
// Deliberately snapshot-scoped to target ELIGIBILITY only:
// - Conditions always read live state - TargetWasKOd exists precisely to
//   observe what an earlier clause of the same ability just did.
// - PerMatch amounts count live matches at resolution (Part 1's own
//   wording: "a live count ... at resolution time").
// - Continuous templates (ContinuousRegistry) never see a snapshot at
//   all - they're query-time state, not queued abilities.
public static class EffectInterpreter
{
    // Entry point for a caller that already has an EffectContext built
    // (mostly tests exercising one template in isolation - Phase 5's own
    // acceptance bar, "one happy path + one no-legal-target case per
    // template"). Real ability resolution goes through DrainQueue below.
    // Captures the rule-3.2.5 snapshot here, unconditionally - a resumed
    // PendingChoice continuation never re-enters this method (it resumes
    // via its captured closure), so the snapshot taken here lives exactly
    // as long as one ability's own resolution, pauses included.
    public static void Execute(EffectNode node, EffectContext ctx)
    {
        ctx.Snapshot = ctx.State.Dice.ToDictionary(d => d.Id, d => new DieSnapshot(d.Zone, d.CurrentFaceIndex));
        Execute(node, ctx, () => { });
    }

    // Drains an AbilityQueue for real (V2_PLAN.md ground rule 6 - the
    // path Phase 4's own triggers-through-real-actions tests feed into).
    // Stops the instant a PendingChoice appears (AbilityQueue.Drain's own
    // shouldStop param), leaving whatever's still queued untouched;
    // AnswerPendingChoice resumes the interrupted ability, and the
    // caller is expected to call DrainQueue again afterward to pick up
    // anything left in the queue.
    public static void DrainQueue(GameState state, AbilityQueue queue, IDiceRoller roller, Random random) =>
        queue.Drain(qa => ResolveQueued(qa, state, queue, roller, random), () => state.PendingChoice is not null);

    private static void ResolveQueued(QueuedAbility ability, GameState state, AbilityQueue queue, IDiceRoller roller, Random random)
    {
        // Blanked between enqueue and resolution: the ability still fires,
        // it just has no text to do anything with (the Dwarf Wizard /
        // Shriek behaviour). This falls out of rule 3.2.5's per-ability
        // snapshots dissolving between queue entries, so it needs no
        // mechanism of its own - but it needs saying, or the check is the
        // one that gets missed (V2_VOCABULARY.md Part 19).
        //
        // A granted ability is exempt: it did not come from the blanked
        // card, so nothing severs it (Part 16).
        if (ability.SourceDieId is { } blankCheckId
            && state.Dice.FirstOrDefault(d => d.Id == blankCheckId) is { } source
            && !QueryEngine.AbilitiesActive(state, source)
            && !source.GrantedAbilities.Any(g => g.Ability.Effect == ability.Effect))
        {
            return;
        }

        var ctx = new EffectContext
        {
            State = state,
            Queue = queue,
            ControllerId = ability.ControllerId,
            Trigger = ability.Trigger,
            Roller = roller,
            Random = random,
            EventValue = ability.EventValue,
        };
        // "self"/"event" are the two reserved binding names (V2_VOCABULARY.md
        // Part 1) - seeded here, before the tree runs, from the queue
        // entry EventBus.Fire/TurnEngine.UseGlobal already populated.
        // Bound through Bind (not a raw dictionary write) so their stats
        // are captured too - Rogue "Mrs. X" swaps against `self`, which
        // must read its PRE-swap attack.
        if (ability.SourceDieId is { } sourceId) ctx.Bind("self", sourceId);
        if (ability.EventSubjectDieId is { } eventId) ctx.Bind("event", eventId);

        // The public Execute, not the private overload - it's what captures
        // this ability's own rule-3.2.5 snapshot (see the class remarks).
        Execute(ability.Effect, ctx);
    }

    // The only legal next action while GameState.PendingChoice is set.
    // Validates the answer against the choice's own Min/MaxCount and
    // candidate set, then hands off to its Resolve closure - which may
    // itself raise a brand new PendingChoice (Distribute's repeated
    // picks, a MayPay cost that itself needs a target) before this call
    // returns; that's fine, GameState.PendingChoice is cleared first.
    public static void AnswerPendingChoice(GameState state, IReadOnlyList<string> chosenIds)
    {
        var pending = state.PendingChoice ?? throw new InvalidOperationException("There is no pending choice to answer.");
        if (chosenIds.Count < pending.MinCount || chosenIds.Count > pending.MaxCount)
            throw new InvalidOperationException($"Expected between {pending.MinCount} and {pending.MaxCount} choices, got {chosenIds.Count}.");
        if (chosenIds.Any(id => !pending.CandidateIds.Contains(id)))
            throw new InvalidOperationException("A chosen id is not one of this choice's candidates.");

        state.PendingChoice = null;
        pending.Resolve(chosenIds);
    }

    private static void Execute(EffectNode node, EffectContext ctx, Action onComplete)
    {
        switch (node)
        {
            case Sequence n: ExecuteSequence(n.Steps, 0, ctx, onComplete); break;
            case DealDamage n: ExecuteDealDamage(n, ctx, onComplete); break;
            case Ko n: ExecuteKo(n, ctx, onComplete); break;
            case MoveDie n: ExecuteMoveDie(n, ctx, onComplete); break;
            case DrawToZone n: ExecuteDrawToZone(n, ctx, onComplete); break;
            case FieldDie n: ExecuteFieldDie(n, ctx, onComplete); break;
            case Reroll n: ExecuteReroll(n, ctx, onComplete); break;
            case Spin n: ExecuteSpin(n, ctx, onComplete); break;
            case SpinToEnergy n: ExecuteSpinToEnergy(n, ctx, onComplete); break;
            case ModifyStat n: ExecuteModifyStat(n, ctx, onComplete); break;
            case GrantTag n: ExecuteGrantTag(n, ctx, onComplete); break;
            case GrantAbility n: ExecuteGrantAbility(n, ctx, onComplete); break;
            case BlankText n: ExecuteBlankText(n, ctx, onComplete); break;
            case BlankCardText n: ExecuteBlankCardText(n, ctx, onComplete); break;
            case LifeChange n: ExecuteLifeChange(n, ctx, onComplete); break;
            case PurchaseModifier n: ExecutePurchaseModifier(n, ctx, onComplete); break;
            case CombatFlag n: ExecuteCombatFlag(n, ctx, onComplete); break;
            case MayPay n: ExecuteMayPay(n, ctx, onComplete); break;
            case Conditional n: ExecuteConditional(n, ctx, onComplete); break;
            case DrawAndChooseOne n: ExecuteDrawAndChooseOne(n, ctx, onComplete); break;
            case GrantCounter n: ExecuteGrantCounter(n, ctx, onComplete); break;
            default: throw new NotSupportedException($"Unknown effect node type '{node.GetType().Name}'.");
        }
    }

    // --- Sequence / Conditional / MayPay - the control-flow templates ---

    private static void ExecuteSequence(IReadOnlyList<EffectNode> steps, int index, EffectContext ctx, Action onComplete)
    {
        if (index >= steps.Count) { onComplete(); return; }
        Execute(steps[index], ctx, () => ExecuteSequence(steps, index + 1, ctx, onComplete));
    }

    private static void ExecuteConditional(Conditional n, EffectContext ctx, Action onComplete)
    {
        if (ConditionEvaluator.Evaluate(ctx.State, ctx.ControllerId, n.When, ctx.Bindings))
            Execute(n.Then, ctx, onComplete);
        else if (n.Else is not null)
            Execute(n.Else, ctx, onComplete);
        else
            onComplete();
    }

    // Ground rule 8 - EVERY "you may" routes through here, cost or not.
    // The stand-in-candidate trick (a PendingChoice whose sole candidate
    // is the ability's own source die, answered with 0 or 1 picks) is
    // ported directly from v1's MayPayLife - see its own remarks for why
    // this reuses the target-choice pipeline instead of a bespoke bool.
    private static void ExecuteMayPay(MayPay n, EffectContext ctx, Action onComplete)
    {
        var answeredBy = n.AnsweredBy == TargetOwnership.Own ? ctx.ControllerId : ctx.State.OpponentOf(ctx.ControllerId);
        // The stand-in is just a token for a yes/no answer, not a real
        // target - the ability's own source die where there is one, and
        // otherwise the answering player's id. The fallback matters for
        // card-scoped Globals (rule 2.6.5.2), which belong to a card
        // rather than to any die and so have no "self" binding at all.
        var standInId = ctx.Bindings.GetValueOrDefault("self") ?? answeredBy;

        ctx.State.PendingChoice = new PendingChoice
        {
            ControllerId = answeredBy,
            Description = "You may.",
            CandidateIds = [standInId],
            MinCount = 0,
            MaxCount = 1,
            Resolve = chosen =>
            {
                if (chosen.Count == 0) { onComplete(); return; } // declined
                if (n.Cost is not null)
                    Execute(n.Cost, ctx, () => Execute(n.Then, ctx, onComplete));
                else
                    Execute(n.Then, ctx, onComplete);
            },
        };
    }

    // --- Damage / KO ---

    private static void ExecuteDealDamage(DealDamage n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), targets =>
        {
            if (targets.Count == 0) { onComplete(); return; } // rule 3.1.10

            var amount = ResolveAmount(ctx, n.Amount);
            if (amount <= 0) { onComplete(); return; }

            if (n.Distribute && targets.Count > 1)
            {
                DistributeDamage(ctx, targets, amount, onComplete);
            }
            else
            {
                foreach (var id in targets) ApplyDamage(ctx.State, ctx.Queue, DamageSource.Ability, id, amount);
                onComplete();
            }
        });
    }

    // Finding 11 - N repeated 1-point PendingChoice picks instead of one
    // lump application; the same die may be chosen more than once. Stops
    // early if every remaining candidate has left play (a KO'd die is no
    // longer a legal damage recipient) even if points remain unspent.
    private static void DistributeDamage(EffectContext ctx, IReadOnlyList<string> targets, int remainingAmount, Action onComplete)
    {
        var alive = targets.Where(id => ctx.State.IsPlayerId(id) ||
            (ctx.State.Dice.FirstOrDefault(d => d.Id == id) is { } d && d.Zone is Zone.FieldZone or Zone.AttackZone)).ToList();

        if (remainingAmount <= 0 || alive.Count == 0) { onComplete(); return; }

        ctx.State.PendingChoice = new PendingChoice
        {
            ControllerId = ctx.ControllerId,
            Description = $"Assign 1 damage ({remainingAmount} remaining).",
            CandidateIds = alive,
            MinCount = 1,
            MaxCount = 1,
            Resolve = chosen =>
            {
                ApplyDamage(ctx.State, ctx.Queue, DamageSource.Ability, chosen[0], 1);
                DistributeDamage(ctx, targets, remainingAmount - 1, onComplete);
            },
        };
    }

    // Marks damage (interception + DieDamaged) WITHOUT resolving KO -
    // split out from ApplyDamage in Phase 7 because combat damage is
    // simultaneous within a wave (rule 2.7.6.1: "simultaneous KO of
    // anything at/over its defense among whoever just took damage this
    // wave") while ability damage resolves one instance at a time (rule
    // 3.2.2). CombatEngine calls this directly for BOTH directions of an
    // engagement before resolving either side's KO, so a lethal hit in
    // one direction can't stop the other die from still landing its own
    // damage back first - exactly the rulebook's own Fast worked example
    // (see CombatEngineTests) needs, and exactly what a single "mark AND
    // immediately KO" call would get wrong. Returns the actual recipient
    // (post-redirect) for the caller to run TryResolveKO against - null
    // if nothing was actually marked (player target, prevented, or the
    // amount reduced to 0).
    //
    // Walks GameState.DamageInterceptors (DamageModifier's registry):
    // PreventNonCombat blocks the instance outright (unless `source`
    // itself is Combat - it's a NON-combat preventer), multipliers apply
    // before flat reductions (V2_VOCABULARY.md Part 1/11's fixed
    // ordering rule), RedirectToSelf changes who actually takes the
    // (already-modified) hit and who DieDamaged/KO fire against.
    public static DieInstance? MarkDamage(GameState state, AbilityQueue queue, DamageSource source, string id, int amount)
    {
        if (amount <= 0) return null;
        if (state.IsPlayerId(id)) { state.GetPlayer(id).Life -= amount; return null; }

        var die = FindDie(state, id);
        var interceptors = state.DamageInterceptors.Where(m => m.AppliesTo(state, die, source)).ToList();

        if (interceptors.Any(m => m.Mode == DamageModifierMode.PreventNonCombat && source != DamageSource.Combat))
            return null;

        foreach (var m in interceptors.Where(m => m.Mode == DamageModifierMode.Double)) amount *= 2;
        foreach (var m in interceptors.Where(m => m.Mode == DamageModifierMode.Amplify)) amount += m.GetAmount(state, die);
        foreach (var m in interceptors.Where(m => m.Mode == DamageModifierMode.Reduce)) amount = Math.Max(0, amount - m.GetAmount(state, die));
        if (amount <= 0) return null;

        var recipient = interceptors
            .Where(m => m.Mode == DamageModifierMode.RedirectToSelf)
            .Select(m => m.RedirectTarget(state, die))
            .FirstOrDefault(d => d is not null) ?? die;

        recipient.Damage += amount;
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieDamaged, recipient, recipient.ControllerId, state.CurrentStepId, new DamageDealtPayload(amount)));
        return recipient;
    }

    // KOs `die` if its marked Damage has reached its current Defense
    // (rule ~1.5) - the second half of what ApplyDamage used to do in
    // one step. Public so CombatEngine can run this as its own pass,
    // once per wave, after MarkDamage has already landed on both sides
    // of every engagement in that wave.
    public static bool TryResolveKO(GameState state, AbilityQueue queue, DieInstance die)
    {
        if (QueryEngine.GetDefense(state, die) > die.Damage) return false;
        KoDie(state, queue, die, triggersKOAbilities: true);
        return true;
    }

    // The single choke point ability damage funnels through (matching
    // v1's own DieStats.ApplyDamage precedent) - marks damage, then
    // resolves KO immediately, since ability damage resolves one
    // instance at a time (rule 3.2.2). Combat damage (Phase 7) calls
    // MarkDamage/TryResolveKO directly instead - see MarkDamage's own
    // remarks for why the two can't be one atomic call there.
    public static void ApplyDamage(GameState state, AbilityQueue queue, DamageSource source, string id, int amount)
    {
        if (MarkDamage(state, queue, source, id, amount) is { } recipient)
            TryResolveKO(state, queue, recipient);
    }

    private static void ExecuteKo(Ko n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
                KoDie(ctx.State, ctx.Queue, FindDie(ctx.State, id), n.TriggersKOAbilities);
            onComplete();
        });
    }

    // Rule 1.5.3.2 - a KO'd die always lands in its owner's Prep Area,
    // unrolled (MoveToZone's own leaving-the-field reset covers the
    // rest: Damage/AppliedModifiers/GrantedTags/CombatFlags all clear).
    // TriggersKOAbilities: false is the Sacrifice shape (Part 2's
    // Spidey's Last Stand example) - same zone transition, DieKOd simply
    // doesn't fire, so nothing reacts to it as a KO. Unlike v1, this
    // doesn't distinguish Sacrifice's own OutOfPlay-during-owner's-turn
    // nuance (Appendix 1 clarification) - a deliberate simplification,
    // not modeled anywhere in the closed vocabulary's data shape (Ko has
    // no separate destination-zone param); revisit via the tail policy
    // if a migrated card actually needs that distinction. Public +
    // (state, queue) for the same Phase 7/CombatEngine reason ApplyDamage
    // is.
    public static void KoDie(GameState state, AbilityQueue queue, DieInstance die, bool triggersKOAbilities)
    {
        MoveToZone(state, die, Zone.PrepArea);
        state.CharacterDiceKOdThisTurn.Add(die.ControllerId);
        if (triggersKOAbilities)
            EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieKOd, die, die.ControllerId, state.CurrentStepId));
    }

    // --- Movement ---

    private static void ExecuteMoveDie(MoveDie n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids) MoveToZone(ctx.State, FindDie(ctx.State, id), n.ToZone);
            onComplete();
        });
    }

    private static void ExecuteDrawToZone(DrawToZone n, EffectContext ctx, Action onComplete)
    {
        var pool = ctx.State.DiceIn(ctx.ControllerId, n.FromZone).ToList();
        Shuffle(pool, ctx.Random);
        foreach (var die in pool.Take(Math.Max(0, n.Count)))
        {
            die.Zone = n.ToZone;
            if (n.ToZone == Zone.ReservePool)
            {
                // Landing in the Reserve Pool means rolled (DrawToZone's
                // own convention, mirrored from TurnEngine.Roll). A
                // freshly-drawn die has no prior face to report a change
                // FROM, so - same reasoning as Roll's own first-ever-roll
                // case - this doesn't fire DieFaceChanged.
                die.CurrentFaceIndex = ctx.Roller.Roll(ctx.State.GetDieDefinition(die));
            }
        }
        onComplete();
    }

    private static void ExecuteFieldDie(FieldDie n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                // Whether it already had a level to be fielded at, read
                // BEFORE the move (MoveToZone gives a dormant die a
                // default face on entering an active zone).
                var rolledCharacterFace = ctx.State.GetCurrentFace(die)?.Character is not null;
                MoveToZone(ctx.State, die, Zone.FieldZone);

                // Re-face only when the card names a level, or when the
                // die has no character face to be fielded on. Otherwise it
                // keeps the face it rolled - it already has a level, and
                // that is the level it is fielded at.
                //
                // Three cases, and all three must end on a character face:
                //  - rolled a character face, no override -> keep it;
                //  - a named level -> take that level's face;
                //  - dormant OR showing an ENERGY face -> lowest character
                //    face. The energy-face case is why this can't just
                //    lean on MoveToZone's dormant-die default: such a die
                //    has a CurrentFaceIndex already, so MoveToZone leaves
                //    it alone and it would otherwise be fielded on an
                //    energy face.
                if (n.Level is not null || !rolledCharacterFace)
                {
                    var definition = ctx.State.GetDieDefinition(die);
                    var characterFaces = definition.Faces
                        .Select((f, i) => (f, i))
                        .Where(x => x.f.Character is not null)
                        .ToList();
                    var chosen = n.Level is { } level
                        ? characterFaces.FirstOrDefault(x => x.f.Character!.Level == level)
                        : default;
                    if (chosen.f is null && characterFaces.Count > 0)
                        chosen = characterFaces.MinBy(x => x.f.Character!.Level);
                    if (chosen.f is not null) die.CurrentFaceIndex = chosen.i;
                }

                ctx.State.FieldedCharacterThisTurn.Add(die.ControllerId);

                // Rule 2.6.3.6 - "when fielded" fires immediately, same
                // as TurnEngine.Field's own real-action path. FieldDie
                // has no energy-dice source in the closed vocabulary
                // (Free isn't wired to a payment mechanism - there isn't
                // one here to pay through); every ability-driven field is
                // effectively free today.
                EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFielded, die, die.ControllerId, ctx.State.CurrentStepId));
            }
            onComplete();
        });
    }

    // --- Reroll / Spin / SpinToEnergy - the face-mutation templates.
    // Every one of these fires DieFaceChanged when the die had a prior
    // face (Part 1's own "every face-mutation site" mandate; skipped for
    // a die's first-ever face same as Roll/DrawToZone, since there's
    // nothing for a filter to compare a null PriorFace against). ---

    private static void ExecuteReroll(Reroll n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                var definition = ctx.State.GetDieDefinition(die);
                var priorFace = ctx.State.GetCurrentFace(die);
                var newIndex = ctx.Roller.Roll(definition);
                die.CurrentFaceIndex = newIndex;
                var newFace = definition.Faces[newIndex];

                if (priorFace is not null)
                {
                    var payload = new DieFaceChangedPayload(priorFace, newFace, FaceChangeCause.Reroll);
                    EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, ctx.State.CurrentStepId, payload));
                }

                // Finding 8 - the per-die multi-target Reroll pattern
                // (5 v1 users) folded into the node itself.
                if (newFace.Character is null && n.NonCharacterMoveTo is { } moveTo)
                {
                    MoveToZone(ctx.State, die, moveTo);
                    if (n.DamagePerMoved > 0)
                        ctx.State.GetPlayer(ctx.State.OpponentOf(ctx.ControllerId)).Life -= n.DamagePerMoved;
                }
            }
            onComplete();
        });
    }

    private static void ExecuteSpin(Spin n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                var definition = ctx.State.GetDieDefinition(die);
                var levels = definition.Faces.Where(f => f.Character is not null)
                    .Select(f => f.Character!.Level).Distinct().OrderBy(l => l).ToList();
                if (levels.Count == 0) continue; // no character face on this die at all - nothing to spin to

                var priorFace = ctx.State.GetCurrentFace(die);
                var currentLevel = priorFace?.Character?.Level ?? levels[0];
                // Finding 12 - SetLevel/LevelDelta are mutually exclusive
                // (an authoring concern, not re-validated at runtime here).
                var targetLevel = Math.Clamp(n.SetLevel ?? currentLevel + (n.LevelDelta ?? 0), levels[0], levels[^1]);

                var faceIndex = definition.Faces.Select((f, i) => (f, i)).First(x => x.f.Character?.Level == targetLevel).i;
                die.CurrentFaceIndex = faceIndex;

                if (priorFace is not null)
                {
                    var payload = new DieFaceChangedPayload(priorFace, definition.Faces[faceIndex], FaceChangeCause.Spin);
                    EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, ctx.State.CurrentStepId, payload));
                }
            }
            onComplete();
        });
    }

    private static void ExecuteSpinToEnergy(SpinToEnergy n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                var definition = ctx.State.GetDieDefinition(die);
                var energyFaces = definition.Faces.Select((f, i) => (f, i)).Where(x => x.f.Character is null).ToList();
                if (energyFaces.Count == 0)
                    throw new InvalidOperationException($"Die '{die.Id}' has no energy face to spin to.");

                // Prefer an exact match on Amount (the physical face this
                // effect is describing); fall back to the die's first
                // energy face if no face shows exactly that many symbols
                // - most basic dice only have one energy amount anyway.
                var chosen = energyFaces.FirstOrDefault(x => x.f.Symbols.Sum(s => s.Count) == n.Amount);
                var (face, index) = chosen.f is not null ? chosen : energyFaces[0];

                var priorFace = ctx.State.GetCurrentFace(die);
                die.CurrentFaceIndex = index;

                if (priorFace is not null)
                {
                    var payload = new DieFaceChangedPayload(priorFace, face, FaceChangeCause.Spin);
                    EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, ctx.State.CurrentStepId, payload));
                }
            }
            onComplete();
        });
    }

    // --- Stat / tag / life / combat-flag grants ---

    private static void ExecuteModifyStat(ModifyStat n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            var grantedDuring = n.Duration == Duration.UntilYourNextTurn ? ctx.State.ActivePlayerId : null;
            var source = ctx.Bindings.GetValueOrDefault("self", "ability");
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                // Finding 5 - SetAttack/SetDefense as a computed delta
                // modifier (v1's proven SetStat approach), mutually
                // exclusive with their matching delta field.
                //
                // Computed against the BASE queries (printed face +
                // AppliedModifiers), never the static-inclusive ones -
                // the game's own applied-vs-static distinction (user
                // ruling, 2026-08-24). A "set" replaces the die's OWN
                // value; conditional static auras then recompute on top
                // of the new value, because they depend on what the die
                // currently is, not on what it was when the aura started.
                //
                // Worked example that fixed this (was a real bug -
                // computing against GetAttack cancelled the aura out and
                // re-added it, landing a level too low): Lois Lane gives
                // other attacking SuperFriends +1A. An attacking 4A
                // SuperFriend shows 5A. Swap its attack with a 1A
                // Sidekick: the Sidekick becomes 4A, and the SuperFriend
                // becomes 2A - 1A swapped in, plus Lois's +1A again,
                // because it is still a SuperFriend and still attacking.
                // Against GetAttack it would have shown 1A.
                var atkDelta = n.SetAttack is { } setAtk ? ResolveAmount(ctx, setAtk) - QueryEngine.GetBaseAttack(ctx.State, die) : n.AtkDelta ?? 0;
                var defDelta = n.SetDefense is { } setDef ? ResolveAmount(ctx, setDef) - QueryEngine.GetBaseDefense(ctx.State, die) : n.DefDelta ?? 0;
                die.AppliedModifiers.Add(new AppliedModifier(atkDelta, defDelta, 0, source, n.Duration, grantedDuring));
            }
            onComplete();
        });
    }

    private static void ExecuteGrantTag(GrantTag n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            var grantedDuring = n.Duration == Duration.UntilYourNextTurn ? ctx.State.ActivePlayerId : null;
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                foreach (var tag in n.Tags)
                    die.GrantedTags.Add(new GrantedTag(tag, n.Duration, grantedDuring));
            }
            onComplete();
        });
    }

    // Mirrors ExecuteGrantTag exactly - same Duration handling, same
    // GrantedDuringPlayerId convention. The difference is only what lands
    // on the die, and that blanking never takes this back off again
    // (V2_VOCABULARY.md Part 16).
    private static void ExecuteGrantAbility(GrantAbility n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            var grantedDuring = n.Duration == Duration.UntilYourNextTurn ? ctx.State.ActivePlayerId : null;
            foreach (var id in ids)
                FindDie(ctx.State, id).GrantedAbilities.Add(new GrantedAbility(n.Ability, n.Duration, grantedDuring));
            onComplete();
        });
    }

    private static void ExecuteBlankText(BlankText n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            var grantedDuring = n.Duration == Duration.UntilYourNextTurn ? ctx.State.ActivePlayerId : null;
            foreach (var id in ids)
                FindDie(ctx.State, id).Suppressions.Add(new DieSuppression(n.Duration, grantedDuring));
            onComplete();
        });
    }

    private static void ExecuteBlankCardText(BlankCardText n, EffectContext ctx, Action onComplete)
    {
        var grantedDuring = n.Duration == Duration.UntilYourNextTurn ? ctx.State.ActivePlayerId : null;

        void Suppress(string playerId, string cardId)
        {
            // Suppressing twice is not stronger than once, and would leave
            // a second entry behind when the first expired.
            if (ctx.State.CardSuppressions.Any(s =>
                    s.PlayerId == playerId && s.CardId == cardId && s.Kind == SuppressionKind.TextIgnored))
            {
                return;
            }
            ctx.State.CardSuppressions.Add(
                new CardSuppression(playerId, cardId, SuppressionKind.TextIgnored, n.Duration, grantedDuring));
        }

        if (n.AllOpposing)
        {
            // Every card the opponent could play, not merely the ones with
            // a die on the board - which is the whole reason this effect
            // is card-scoped. Their own dice tell us which cards are
            // theirs; a card with no die of theirs anywhere was never
            // going to matter.
            var opponentId = ctx.State.OpponentOf(ctx.ControllerId);
            foreach (var cardId in ctx.State.Dice
                         .Where(d => d.OwnerId == opponentId && d.CardId is not null)
                         .Select(d => d.CardId!)
                         .Distinct()
                         .ToList())
            {
                Suppress(opponentId, cardId);
            }
            onComplete();
            return;
        }

        ResolveTarget(ctx, n.Target ?? new TargetFilter(), ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
            {
                var die = FindDie(ctx.State, id);
                if (die.CardId is { } cardId) Suppress(die.ControllerId, cardId);
            }
            onComplete();
        });
    }

    private static void ExecuteLifeChange(LifeChange n, EffectContext ctx, Action onComplete)
    {
        var amount = ResolveAmount(ctx, n.Amount); // signed - positive gains, negative loses (n.Amount's own convention)
        var playerId = n.Whose == TargetOwnership.Own ? ctx.ControllerId : ctx.State.OpponentOf(ctx.ControllerId);
        ctx.State.GetPlayer(playerId).Life += amount;
        onComplete();
    }

    // Appendix A: GrantNextPurchaseDiscount/GrantNextPurchaseGoesToBag
    // generalized into one one-shot per-controller offer, consumed by
    // the next matching TurnEngine.Purchase call (or discarded unused at
    // CleanUp) - see GameState.PendingPurchaseModifiers' own remarks.
    private static void ExecutePurchaseModifier(PurchaseModifier n, EffectContext ctx, Action onComplete)
    {
        ctx.State.PendingPurchaseModifiers.Add(new PendingPurchaseModifier(ctx.ControllerId, n.Delta, n.CardKind, n.GoesToZone));
        onComplete();
    }

    // No Duration param on CombatFlag itself (V2_VOCABULARY.md Part 1) -
    // every real use is "(this turn)," so DieInstance.CombatFlags is
    // always EndOfTurn-scoped, cleared at CleanUp alongside
    // AppliedModifiers/GrantedTags. Combat itself (reading these) is
    // Phase 7's concern - this template only records the grant.
    private static void ExecuteCombatFlag(CombatFlag n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids) FindDie(ctx.State, id).CombatFlags.Add(n.Flag);
            onComplete();
        });
    }

    // --- DrawAndChooseOne / GrantCounter ---

    // Finding 6 - covers v1's Corrupt (opponent's bag) and
    // DrawAndChooseOneToRoll (own bag) as one template. The ABILITY's
    // controller always makes the choice (Part 1's own note), regardless
    // of whose bag was drawn from.
    private static void ExecuteDrawAndChooseOne(DrawAndChooseOne n, EffectContext ctx, Action onComplete)
    {
        var targetPlayerId = n.PlayerTarget == TargetOwnership.Own ? ctx.ControllerId : ctx.State.OpponentOf(ctx.ControllerId);
        var pool = ctx.State.DiceIn(targetPlayerId, Zone.Bag).ToList();
        Shuffle(pool, ctx.Random);
        var drawn = pool.Take(Math.Max(0, n.Count)).ToList();

        if (drawn.Count == 0) { onComplete(); return; } // rule 3.1.10 - nothing drawn, nothing to choose

        ctx.State.PendingChoice = new PendingChoice
        {
            ControllerId = ctx.ControllerId,
            Description = "Choose one of the drawn dice.",
            CandidateIds = drawn.Select(d => d.Id).ToList(),
            MinCount = 1,
            MaxCount = 1,
            Resolve = chosen =>
            {
                var chosenId = chosen[0];
                MoveDrawnDie(ctx, FindDie(ctx.State, chosenId), n.ChosenToZone);
                foreach (var die in drawn.Where(d => d.Id != chosenId))
                    MoveDrawnDie(ctx, die, n.RestToZone);
                onComplete();
            },
        };
    }

    private static void MoveDrawnDie(EffectContext ctx, DieInstance die, Zone zone)
    {
        die.Zone = zone;
        // ReservePool destination = rolled (DrawToZone's own convention).
        if (zone == Zone.ReservePool)
            die.CurrentFaceIndex = ctx.Roller.Roll(ctx.State.GetDieDefinition(die));
    }

    // Finding 13 - counters live on the resolved target's own CARD (all
    // copies/dice of it share the count), not the die - Sidekick dice
    // (no CardId) and player ids can't hold one and are silently skipped.
    private static void ExecuteGrantCounter(GrantCounter n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
            {
                if (ctx.State.IsPlayerId(id)) continue;
                var die = FindDie(ctx.State, id);
                if (die.CardId is not { } cardId) continue;
                var key = (die.ControllerId, cardId, n.CounterName);
                ctx.State.Counters[key] = ctx.State.Counters.GetValueOrDefault(key) + n.Amount;
            }
            onComplete();
        });
    }

    // --- Shared plumbing ---

    // TargetFilter resolution's own choice/no-choice split (V2_PLAN.md
    // Phase 5 task 1+2): 0 candidates fizzles (rule 3.1.10); Count == 0
    // means "all matches, no choice" (Part 1's own note); a candidate
    // pool no bigger than a non-Optional Count auto-selects everything
    // (nothing to actually choose between); anything else is a real
    // PendingChoice, routed to AnsweredBy.
    private static void ResolveTarget(EffectContext ctx, TargetFilter filter, ProtectionFrom? protection, Action<IReadOnlyList<string>> onResolved)
    {
        var candidates = TargetResolver.Query(ctx.State, ctx.ControllerId, filter, ctx.Bindings, protection, snapshot: ctx.Snapshot);

        if (candidates.Count == 0) { onResolved([]); return; }

        if (filter.Count == 0)
        {
            BindIfNeeded(ctx, filter, candidates);
            onResolved(candidates);
            return;
        }

        var needsChoice = filter.Optional || candidates.Count > filter.Count;
        if (!needsChoice)
        {
            var auto = candidates.Take(filter.Count).ToList();
            BindIfNeeded(ctx, filter, auto);
            onResolved(auto);
            return;
        }

        var answeredBy = filter.AnsweredBy == TargetOwnership.Own ? ctx.ControllerId : ctx.State.OpponentOf(ctx.ControllerId);
        var maxCount = Math.Min(filter.Count, candidates.Count);
        ctx.State.PendingChoice = new PendingChoice
        {
            ControllerId = answeredBy,
            Description = $"Choose {(filter.Optional ? "up to " : "")}{maxCount} target(s).",
            CandidateIds = candidates,
            MinCount = filter.Optional ? 0 : maxCount,
            MaxCount = maxCount,
            Resolve = chosen =>
            {
                BindIfNeeded(ctx, filter, chosen);
                onResolved(chosen);
            },
        };
    }

    // Finding 9 - BindAs remembers the FIRST resolved die under a name
    // for later Bound/CheckBinding references within the same ability;
    // every real card needing this binds a single specific die (there's
    // no authored case binding a multi-die choice as one name), so "the
    // first one" rather than a list is the right shape here.
    private static void BindIfNeeded(EffectContext ctx, TargetFilter filter, IReadOnlyList<string> resolvedIds)
    {
        if (filter.BindAs is { } name && resolvedIds.Count > 0)
            ctx.Bind(name, resolvedIds[0]);
    }

    private static int ResolveAmount(EffectContext ctx, Amount amount) => amount switch
    {
        StatOf s => ctx.CapturedStats.TryGetValue(s.Binding, out var captured) && captured.TryGetValue(s.Stat, out var value)
            ? value
            : throw new InvalidOperationException($"StatOf references binding '{s.Binding}', which is not bound to a die in this ability."),
        EventValue => ctx.EventValue
            ?? throw new InvalidOperationException("EventValue was used by an ability whose triggering event carries no numeric payload."),
        _ => AmountResolver.Resolve(ctx.State, ctx.ControllerId, amount, ctx.Bindings, ProtectionFor(ctx.Trigger)),
    };

    // Global/DieUsed are the only two trigger kinds targeting protection
    // can gate (rule 3.8's own scope - a targeted reactive trigger isn't
    // "using" anything), ported from v1 LegalTargets.Query's identical
    // `currentTrigger is TriggerType.Global or TriggerType.WhenUsed` check.
    private static ProtectionFrom? ProtectionFor(TriggerKind trigger) => trigger switch
    {
        TriggerKind.Global => ProtectionFrom.Global,
        TriggerKind.DieUsed => ProtectionFrom.Action,
        _ => null,
    };

    // The single zone-transition choke point every movement template
    // (MoveDie/Ko/FieldDie/Reroll's NonCharacterMoveTo/DrawAndChooseOne)
    // funnels through - rule 3.4.5.4's "modifier lifetime ends the
    // moment a die leaves the Field/Attack Zone" (immediately, not just
    // at Clean Up), and the mirror-image "a dormant die entering play
    // needs SOME current face:" defaults to the die's own first (lowest-
    // level) character face; an ability needing a SPECIFIC level follows
    // up with Spin(SetLevel:n) against the same (bound) die - the
    // pattern V2_VOCABULARY.md Part 2's Mutation writeup (Finding 12)
    // uses, rather than this helper guessing at a level nothing told it.
    private static void MoveToZone(GameState state, DieInstance die, Zone toZone)
    {
        var wasActive = die.Zone is Zone.FieldZone or Zone.AttackZone;
        var enteringActive = toZone is Zone.FieldZone or Zone.AttackZone;
        die.Zone = toZone;

        if (wasActive && !enteringActive)
        {
            die.CurrentFaceIndex = null;
            die.Damage = 0;
            die.AppliedModifiers.Clear();
            die.GrantedTags.Clear();
            die.GrantedAbilities.Clear();
            die.Suppressions.Clear();
            die.CombatFlags.Clear();
        }
        else if (enteringActive && die.CurrentFaceIndex is null)
        {
            var definition = state.GetDieDefinition(die);
            var firstCharacter = definition.Faces.Select((f, i) => (f, i)).FirstOrDefault(x => x.f.Character is not null);
            die.CurrentFaceIndex = firstCharacter.f is not null ? firstCharacter.i : 0;
        }
    }

    private static DieInstance FindDie(GameState state, string dieId) =>
        state.Dice.FirstOrDefault(d => d.Id == dieId)
        ?? throw new InvalidOperationException($"No die with id '{dieId}'.");

    private static void Shuffle(List<DieInstance> dice, Random random)
    {
        for (var i = dice.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (dice[i], dice[j]) = (dice[j], dice[i]);
        }
    }
}
