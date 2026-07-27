using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Executes an EffectNode tree against a GameState (see RULES_ENGINE_DESIGN.md
// - "Ability representation"). Intentionally a plain dispatch over a small,
// closed set of primitives rather than a general expression evaluator -
// that's the point of keeping the DSL small.
public static class EffectInterpreter
{
    // Rule 3.2.5 - an ability reacts to the game state as it existed when
    // it entered the queue, not a moving target as its own clauses
    // resolve. Casket of Ancient Winters is the case that makes this
    // concrete: its own first clause KOs 3 dice (which land in the Prep
    // Area, rule 1.5.3.2) before its third clause asks for "3 dice from
    // their Prep Area" - if that were resolved live, clause 1's own KOs
    // would inflate clause 3's candidate pool. So every TargetSpec in the
    // tree is resolved (legal set computed, caller's choice validated)
    // ONCE, upfront, against the pre-execution state, and cached; clauses
    // then just look up their already-resolved targets while running.
    // This also naturally covers a spec referenced twice within one
    // ability (Shocking Grasp's damage clause and its "if that character
    // is KO'd" check) - same spec, same cache entry, resolved once.
    // Cache key is TargetSpec's structural equality, so two calls building
    // an equivalent spec (e.g. two `TargetSpec.CharacterDie("target
    // character die")` invocations, which share the same default
    // EligibleZones array) hit the same entry. Caveat: two *array-literal*
    // EligibleZones for what's meant to be the same repeated target would
    // NOT share an entry (List/array equality is reference-based) - not
    // hit by anything in SampleCards today, but worth knowing.
    public static void Execute(EffectNode node, EffectContext ctx)
    {
        var cache = new Dictionary<TargetSpec, IReadOnlyList<string>>();
        foreach (var spec in CollectTargetSpecs(node).Distinct())
            Resolve(ctx, spec, cache);

        Execute(node, ctx, cache);
    }

    private static IEnumerable<TargetSpec> CollectTargetSpecs(EffectNode node)
    {
        switch (node)
        {
            case Sequence seq:
                foreach (var step in seq.Steps)
                foreach (var spec in CollectTargetSpecs(step))
                    yield return spec;
                break;
            case DealDamage n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Ko n: if (!n.Target.IsSelf) yield return n.Target; break;
            case MoveDie n: if (!n.Target.IsSelf) yield return n.Target; break;
            case ModifyStat n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Reroll n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Spin n: if (!n.Target.IsSelf) yield return n.Target; break;
            case PrepDie n: if (!n.Source.IsSelf) yield return n.Source; break;
            case FieldDie n: if (!n.Target.IsSelf) yield return n.Target; break;
            case Conditional n:
                if (!n.CheckTarget.IsSelf) yield return n.CheckTarget;
                foreach (var spec in CollectTargetSpecs(n.Then))
                    yield return spec;
                break;
        }
    }

    private static void Execute(EffectNode node, EffectContext ctx, Dictionary<TargetSpec, IReadOnlyList<string>> cache)
    {
        switch (node)
        {
            case Sequence seq:
                // Rule 3.1.7 - multiple effects in one ability resolve
                // sequentially, in the order the card text lists them.
                foreach (var step in seq.Steps)
                    Execute(step, ctx, cache);
                break;

            case DealDamage dealDamage:
                foreach (var id in Resolve(ctx, dealDamage.Target, cache))
                {
                    var die = FindDie(ctx, id);
                    die.Damage += dealDamage.Amount;
                    // Ability damage KOs immediately rather than waiting
                    // for a simultaneous batch check - abilities resolve
                    // one at a time (rule 3.2.2), unlike combat damage.
                    DieStats.TryResolveKO(ctx.State, die);
                }
                break;

            case Ko ko:
                foreach (var id in Resolve(ctx, ko.Target, cache))
                {
                    var die = FindDie(ctx, id);
                    die.Zone = Zone.PrepArea; // rule 1.5.3.2
                    die.Damage = 0;
                    die.Level = 1;
                    die.Status = DieStatus.Unrolled;
                    die.AppliedModifiers.Clear();
                }
                break;

            case MoveDie move:
                foreach (var id in Resolve(ctx, move.Target, cache))
                    FindDie(ctx, id).Zone = move.ToZone;
                break;

            case ModifyStat modify:
                foreach (var id in Resolve(ctx, modify.Target, cache))
                {
                    FindDie(ctx, id).AppliedModifiers.Add(
                        new Modifier(modify.AttackDelta ?? 0, modify.DefenseDelta ?? 0, ctx.SourceDieId ?? "ability"));
                }
                break;

            case Spin spin:
                foreach (var id in Resolve(ctx, spin.Target, cache))
                {
                    var die = FindDie(ctx, id);
                    var maxLevel = GetMaxLevel(ctx.State, die);
                    die.Level = Math.Clamp(die.Level + spin.LevelDelta, 1, maxLevel); // rule 3.7.4/3.7.5
                }
                break;

            case PrepDie prep:
                foreach (var id in Resolve(ctx, prep.Source, cache))
                    FindDie(ctx, id).Zone = Zone.PrepArea;
                break;

            case FieldDie field:
                foreach (var id in Resolve(ctx, field.Target, cache))
                {
                    // Rule 2.6.3 note - dice fielded by an ability are
                    // fielded for free on level 1 unless stated otherwise.
                    // Paying for a non-free ability-driven field isn't
                    // modeled - no currently-authored card needs it.
                    var die = FindDie(ctx, id);
                    die.Level = 1;
                    die.Zone = Zone.FieldZone;
                }
                break;

            case DrawDice draw:
                // Simplification: pulls straight from the Bag to the
                // Reserve Pool on an energy face, without the bag-refill-
                // from-Used-Pile behavior TurnEngine's draw implements.
                // Not exercised by any currently-authored card; revisit
                // together with a real IDiceRoller-backed draw+roll.
                for (var i = 0; i < draw.Count; i++)
                {
                    var bag = ctx.State.DiceIn(ctx.ControllerId, Zone.Bag).ToList();
                    if (bag.Count == 0) break;
                    var picked = ctx.Random is not null ? bag[ctx.Random.Next(bag.Count)] : bag[0];
                    picked.Zone = Zone.ReservePool;
                    picked.Status = DieStatus.Energy;
                }
                break;

            case Reroll:
                // Not exercised by any currently-authored card; needs an
                // IDiceRoller threaded through EffectContext to do anything.
                break;

            case GainLife gain:
                var gainingPlayer = ctx.State.GetPlayer(ctx.ControllerId);
                gainingPlayer.Life = Math.Min(Player.StartingLife, gainingPlayer.Life + gain.Amount); // rule 1.1.3
                break;

            case LoseLife lose:
                ctx.State.GetPlayer(ctx.ControllerId).Life -= lose.Amount;
                break;

            case SwapLife:
                var controller = ctx.State.GetPlayer(ctx.ControllerId);
                var opponent = ctx.State.GetPlayer(ctx.State.OpponentOf(ctx.ControllerId));
                (controller.Life, opponent.Life) = (opponent.Life, controller.Life);
                break;

            case Conditional conditional:
                if (Resolve(ctx, conditional.CheckTarget, cache).Any(id => CheckCondition(ctx, id, conditional.When)))
                    Execute(conditional.Then, ctx, cache);
                break;

            default:
                throw new NotSupportedException($"Unhandled effect node: {node.GetType().Name}");
        }
    }

    // TargetSpec.Self bypasses legal-target filtering entirely and
    // resolves straight to the ability's own source die (rule 3.1.15-style
    // self-reference). Everything else goes through LegalTargets (rule
    // 3.3) - the caller still picks WHICH legal die(s) to actually use
    // (that's a real player/AI decision this system doesn't make), but the
    // choice is validated against the real legal set rather than trusted
    // blindly, and rule 3.3.11's "as many as available, otherwise all of
    // them" count requirement is enforced here. Results are cached per
    // TargetSpec for the lifetime of one top-level Execute call - see the
    // class-level remarks on why a repeated reference to "the target"
    // shouldn't be re-validated against a board its own earlier clause
    // may have just changed.
    private static IReadOnlyList<string> Resolve(
        EffectContext ctx, TargetSpec spec, Dictionary<TargetSpec, IReadOnlyList<string>> cache)
    {
        if (spec.IsSelf)
            return ctx.SourceDieId is not null ? [ctx.SourceDieId] : [];

        if (cache.TryGetValue(spec, out var cached))
            return cached;

        var legal = LegalTargets.Query(ctx.State, ctx.ControllerId, spec);
        var chosen = ctx.ResolveTargets(spec);

        var illegal = chosen.Where(id => !legal.Contains(id)).ToList();
        if (illegal.Count > 0)
        {
            throw new InvalidOperationException(
                $"Chosen target(s) [{string.Join(", ", illegal)}] are not legal for '{spec.Description}'.");
        }

        IReadOnlyList<string> result;
        if (legal.Count == 0)
        {
            result = []; // rule 3.1.10 - no legal targets, nothing to apply
        }
        else
        {
            var required = Math.Min(spec.Count, legal.Count);
            if (chosen.Count < required)
            {
                throw new InvalidOperationException(
                    $"'{spec.Description}' needs {required} target(s) but only {chosen.Count} were chosen.");
            }

            result = chosen.Count > spec.Count ? chosen.Take(spec.Count).ToList() : chosen;
        }

        cache[spec] = result;
        return result;
    }

    private static bool CheckCondition(EffectContext ctx, string dieId, EffectCondition condition) => condition switch
    {
        EffectCondition.TargetWasKOd => FindDie(ctx, dieId) is { Zone: Zone.PrepArea, Status: DieStatus.Unrolled },
        _ => throw new NotSupportedException($"Unhandled effect condition: {condition}")
    };

    private static int GetMaxLevel(GameState state, DieInstance die) =>
        die.CardId is null ? 1 : Math.Max(1, state.CardCatalog[die.VirtualCardId ?? die.CardId].Levels.Count);

    private static DieInstance FindDie(EffectContext ctx, string id) =>
        ctx.State.Dice.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"No die with id '{id}'.");
}
