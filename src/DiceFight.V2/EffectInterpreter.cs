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
// Known, documented simplification vs. v1: TargetFilters are resolved
// live, at the point their own node executes, not pre-resolved-and-cached
// against a single pre-execution snapshot the way v1's EffectInterpreter.
// Execute(node, ctx) does (rule 3.2.5 - see its own remarks, the "Casket
// of Ancient Winters" example). A rare multi-step ability where an early
// step's mutation shifts a LATER step's own live target pool would see
// that shift here; no currently-authored card needs this precision, and
// building a matching "collect every TargetFilter in tree order" pass
// (v1's CollectTargetSpecs, which v1's own comments already flag as a
// drift risk against Execute's real tree shape) is deferred rather than
// spent against Phase 5's budget speculatively - revisit if Phase 8's
// card migration finds a real card that needs it.
public static class EffectInterpreter
{
    // Entry point for a caller that already has an EffectContext built
    // (mostly tests exercising one template in isolation - Phase 5's own
    // acceptance bar, "one happy path + one no-legal-target case per
    // template"). Real ability resolution goes through DrainQueue below.
    public static void Execute(EffectNode node, EffectContext ctx) => Execute(node, ctx, () => { });

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
        var ctx = new EffectContext
        {
            State = state,
            Queue = queue,
            ControllerId = ability.ControllerId,
            Trigger = ability.Trigger,
            Roller = roller,
            Random = random,
        };
        // "self"/"event" are the two reserved binding names (V2_VOCABULARY.md
        // Part 1) - seeded here, before the tree runs, from the queue
        // entry EventBus.Fire/TurnEngine.UseGlobal already populated.
        if (ability.SourceDieId is { } sourceId) ctx.Bindings["self"] = sourceId;
        if (ability.EventSubjectDieId is { } eventId) ctx.Bindings["event"] = eventId;

        Execute(ability.Effect, ctx, () => { });
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
        var standInId = ctx.Bindings.GetValueOrDefault("self")
            ?? throw new InvalidOperationException("MayPay needs a 'self' binding to stand in as its PendingChoice candidate.");
        var answeredBy = n.AnsweredBy == TargetOwnership.Own ? ctx.ControllerId : ctx.State.OpponentOf(ctx.ControllerId);

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
                foreach (var id in targets) ApplyDamage(ctx, id, amount);
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
                ApplyDamage(ctx, chosen[0], 1);
                DistributeDamage(ctx, targets, remainingAmount - 1, onComplete);
            },
        };
    }

    // The single choke point every damage instance funnels through
    // (matching v1's own DieStats.ApplyDamage precedent) - fires
    // DieDamaged, then checks the KO threshold (rule ~1.5: damage marked
    // >= current Defense) and KOs immediately if it's met, since ability
    // damage resolves one instance at a time (rule 3.2.2), unlike
    // simultaneous combat damage (Phase 7's own concern).
    // Phase 6 extension - walks GameState.DamageInterceptors (DamageModifier's
    // registry) before marking damage. Source is always Ability here (no
    // Combat damage source exists before Phase 7). PreventNonCombat
    // blocks the instance outright; multipliers apply before flat
    // reductions (V2_VOCABULARY.md Part 1/11's fixed ordering rule);
    // RedirectToSelf, if present, changes who actually takes the
    // (already-modified) hit and who DieDamaged/KO fire against.
    private static void ApplyDamage(EffectContext ctx, string id, int amount)
    {
        if (amount <= 0) return;
        if (ctx.State.IsPlayerId(id)) { ctx.State.GetPlayer(id).Life -= amount; return; }

        var die = FindDie(ctx.State, id);
        var interceptors = ctx.State.DamageInterceptors.Where(m => m.AppliesTo(ctx.State, die, DamageSource.Ability)).ToList();

        if (interceptors.Any(m => m.Mode == DamageModifierMode.PreventNonCombat))
            return;

        foreach (var m in interceptors.Where(m => m.Mode == DamageModifierMode.Double)) amount *= 2;
        foreach (var m in interceptors.Where(m => m.Mode == DamageModifierMode.Amplify)) amount += m.GetAmount(ctx.State, die);
        foreach (var m in interceptors.Where(m => m.Mode == DamageModifierMode.Reduce)) amount = Math.Max(0, amount - m.GetAmount(ctx.State, die));
        if (amount <= 0) return;

        var recipient = interceptors
            .Where(m => m.Mode == DamageModifierMode.RedirectToSelf)
            .Select(m => m.RedirectTarget(ctx.State, die))
            .FirstOrDefault(d => d is not null) ?? die;

        recipient.Damage += amount;
        EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieDamaged, recipient, recipient.ControllerId, ctx.State.CurrentStep, new DamageDealtPayload(amount)));

        if (QueryEngine.GetDefense(ctx.State, recipient) <= recipient.Damage)
            KoDie(ctx, recipient, triggersKOAbilities: true);
    }

    private static void ExecuteKo(Ko n, EffectContext ctx, Action onComplete)
    {
        ResolveTarget(ctx, n.Target, ProtectionFor(ctx.Trigger), ids =>
        {
            foreach (var id in ids)
                KoDie(ctx, FindDie(ctx.State, id), n.TriggersKOAbilities);
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
    // if a migrated card actually needs that distinction.
    private static void KoDie(EffectContext ctx, DieInstance die, bool triggersKOAbilities)
    {
        MoveToZone(ctx.State, die, Zone.PrepArea);
        ctx.State.CharacterDiceKOdThisTurn.Add(die.ControllerId);
        if (triggersKOAbilities)
            EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieKOd, die, die.ControllerId, ctx.State.CurrentStep));
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
                MoveToZone(ctx.State, die, Zone.FieldZone);

                var definition = ctx.State.GetDieDefinition(die);
                var levelFace = definition.Faces
                    .Select((f, i) => (f, i))
                    .FirstOrDefault(x => x.f.Character?.Level == n.Level);
                if (levelFace.f is not null) die.CurrentFaceIndex = levelFace.i;

                ctx.State.FieldedCharacterThisTurn.Add(die.ControllerId);

                // Rule 2.6.3.6 - "when fielded" fires immediately, same
                // as TurnEngine.Field's own real-action path. FieldDie
                // has no energy-dice source in the closed vocabulary
                // (Free isn't wired to a payment mechanism - there isn't
                // one here to pay through); every ability-driven field is
                // effectively free today.
                EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFielded, die, die.ControllerId, ctx.State.CurrentStep));
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
                    EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, ctx.State.CurrentStep, payload));
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
                    EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, ctx.State.CurrentStep, payload));
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
                    EventBus.Fire(ctx.State, ctx.Queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, ctx.State.CurrentStep, payload));
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
                var atkDelta = n.SetAttack is { } setAtk ? setAtk - QueryEngine.GetAttack(ctx.State, die) : n.AtkDelta ?? 0;
                var defDelta = n.SetDefense is { } setDef ? setDef - QueryEngine.GetDefense(ctx.State, die) : n.DefDelta ?? 0;
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
        var candidates = TargetResolver.Query(ctx.State, ctx.ControllerId, filter, ctx.Bindings, protection);

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
            ctx.Bindings[name] = resolvedIds[0];
    }

    // Phase 6 extracted this into AmountResolver (shared with
    // ContinuousRegistry's StatAura handling) - kept as a thin wrapper
    // here so every existing call site (ResolveAmount(ctx, amount))
    // didn't need touching.
    private static int ResolveAmount(EffectContext ctx, Amount amount) =>
        AmountResolver.Resolve(ctx.State, ctx.ControllerId, amount, ctx.Bindings, ProtectionFor(ctx.Trigger));

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
