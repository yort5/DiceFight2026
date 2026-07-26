using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Executes an EffectNode tree against a GameState (see RULES_ENGINE_DESIGN.md
// - "Ability representation"). Intentionally a plain dispatch over a small,
// closed set of primitives rather than a general expression evaluator -
// that's the point of keeping the DSL small.
public static class EffectInterpreter
{
    public static void Execute(EffectNode node, EffectContext ctx)
    {
        switch (node)
        {
            case Sequence seq:
                // Rule 3.1.7 - multiple effects in one ability resolve
                // sequentially, in the order the card text lists them.
                foreach (var step in seq.Steps)
                    Execute(step, ctx);
                break;

            case DealDamage dealDamage:
                foreach (var id in Resolve(ctx, dealDamage.Target))
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
                foreach (var id in Resolve(ctx, ko.Target))
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
                foreach (var id in Resolve(ctx, move.Target))
                    FindDie(ctx, id).Zone = move.ToZone;
                break;

            case ModifyStat modify:
                foreach (var id in Resolve(ctx, modify.Target))
                {
                    FindDie(ctx, id).AppliedModifiers.Add(
                        new Modifier(modify.AttackDelta ?? 0, modify.DefenseDelta ?? 0, ctx.SourceDieId ?? "ability"));
                }
                break;

            case Spin spin:
                foreach (var id in Resolve(ctx, spin.Target))
                {
                    var die = FindDie(ctx, id);
                    var maxLevel = GetMaxLevel(ctx.State, die);
                    die.Level = Math.Clamp(die.Level + spin.LevelDelta, 1, maxLevel); // rule 3.7.4/3.7.5
                }
                break;

            case PrepDie prep:
                foreach (var id in Resolve(ctx, prep.Source))
                    FindDie(ctx, id).Zone = Zone.PrepArea;
                break;

            case FieldDie field:
                foreach (var id in Resolve(ctx, field.Target))
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
                if (Resolve(ctx, conditional.CheckTarget).Any(id => CheckCondition(ctx, id, conditional.When)))
                    Execute(conditional.Then, ctx);
                break;

            default:
                throw new NotSupportedException($"Unhandled effect node: {node.GetType().Name}");
        }
    }

    // "self" is a language-level convention (see TargetSpec remarks) so
    // individual resolvers don't each need to special-case it.
    private static IReadOnlyList<string> Resolve(EffectContext ctx, TargetSpec spec) =>
        spec.Description == "self" && ctx.SourceDieId is not null
            ? [ctx.SourceDieId]
            : ctx.ResolveTargets(spec);

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
