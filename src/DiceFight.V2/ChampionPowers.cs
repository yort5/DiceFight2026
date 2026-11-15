using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Champion once-per-turn powers (2026-10-04, the user's redesign of the
// always-on passives - v3/DESIGN_NOTES.md). Three are used like a free,
// owner-only Global: using one queues an effect whose target goes through
// the ordinary pending-choice flow. Golden Eagle's is an option on Field
// itself (TurnEngine.Field's `free`). Each is once per turn, reset at every
// Clear and Draw.
public static class ChampionPowers
{
    public static ChampionDef? Of(GameState state, string playerId) =>
        state.GetPlayer(playerId).ChampionId is { } id ? state.Config.Champions.FirstOrDefault(c => c.Id == id) : null;

    // No Champion power during either player's first turn (user,
    // 2026-11-15: going first, Wolf could field an early Tardigrade and pump
    // it +3). From game turn 3 on - the first player's second turn.
    public const int FirstPowerTurn = 3;

    private static bool Unused(GameState state, string playerId) =>
        state.TurnNumber >= FirstPowerTurn && !state.ChampionPowerUsedThisTurn.Contains(playerId);

    // Wolf / Armadillo / Owl: usable right now by this player? Also gates
    // whether the Inactive player is offered priority (Priority.Pass), so
    // Armadillo's only counts when it could matter - after blocks, with one
    // of their dice in combat.
    public static bool CanUse(GameState state, string playerId)
    {
        if (Of(state, playerId) is not { } champion || !Unused(state, playerId)) return false;
        var mine = state.DiceIn(playerId, Zone.FieldZone).Concat(state.DiceIn(playerId, Zone.AttackZone))
            .Where(d => state.GetCurrentFace(d)?.Character is not null);
        return champion.PassiveKind switch
        {
            ChampionPassiveKind.PumpOneAttack =>
                playerId == state.ActivePlayerId && Priority.IsWindow(state) && mine.Any()
                && (!champion.PowerBeforeBlocksOnly || state.CurrentStepId == StepIds.Main),
            ChampionPassiveKind.SpinOne =>
                playerId == state.ActivePlayerId && Priority.IsWindow(state)
                && state.Dice.Any(d => d.Zone is Zone.FieldZone or Zone.AttackZone && state.GetCurrentFace(d)?.Character is not null),
            ChampionPassiveKind.ShieldOneFromCombat =>
                state.CurrentStepId == StepIds.ActionGlobalWindow && InAFight(state, playerId).Any(),
            _ => false,
        };
    }

    // This player's dice in a fight after blocks: its blockers, or its
    // attackers in a lane someone blocked. An unblocked attacker takes no
    // combat damage, so shielding it would do nothing.
    private static IEnumerable<DieInstance> InAFight(GameState state, string playerId)
    {
        if (state.DeclaredBlocks is not { } blocks) return [];
        if (playerId != state.ActivePlayerId) return state.DiceIn(playerId, Zone.AttackZone);
        var attackers = state.DiceIn(playerId, Zone.AttackZone).ToList();
        var blockedLanes = attackers.Where(a => blocks.BlockersOf(a.Id).Count > 0).Select(a => a.Lane).ToHashSet();
        return attackers.Where(a => blockedLanes.Contains(a.Lane));
    }

    // Golden Eagle: field one die free, once per your turn, in Main.
    public static bool CanFieldFree(GameState state, string playerId) =>
        Of(state, playerId)?.PassiveKind == ChampionPassiveKind.FieldOneFree
        && Unused(state, playerId) && playerId == state.ActivePlayerId && state.CurrentStepId == StepIds.Main;

    public static void Use(GameState state, AbilityQueue queue, string playerId)
    {
        Priority.RequireHolder(state, playerId);
        if (state.TurnNumber < FirstPowerTurn)
            throw new InvalidOperationException("Champion powers can't be used during either player's first turn.");
        if (!CanUse(state, playerId) || Of(state, playerId) is not { } champion)
            throw new InvalidOperationException("Your Champion's power can't be used right now.");

        EffectNode effect = champion.PassiveKind switch
        {
            ChampionPassiveKind.PumpOneAttack => new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own,
                Prompt: $"choose one of your creatures to get +{champion.Amount} ATK this turn."), AtkDelta: champion.Amount),
            ChampionPassiveKind.ShieldOneFromCombat => new CombatFlag(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.AttackZone],
                Prompt: "choose one of your creatures in combat - combat damage to it is prevented this turn."), CombatFlagKind.PreventCombatDamage),
            ChampionPassiveKind.SpinOne => new SpinByOwner(new TargetFilter(
                Prompt: champion.Amount == 1
                    ? "choose a creature - yours spins up a level, theirs spins down a level."
                    : $"choose a creature - yours spins up {champion.Amount} levels, theirs spins down {champion.Amount} levels."), Math.Max(1, champion.Amount)),
            _ => throw new InvalidOperationException("That Champion has no power to use."),
        };
        state.ChampionPowerUsedThisTurn.Add(playerId);
        state.LogEvent(playerId, $"{state.NameOf(playerId)} uses their Champion power.");
        queue.Enqueue(null, playerId, TriggerKind.Global, effect, sourceName: champion.Name);
    }

    // Plain-language text for each kind - shared by the API and the card reference.
    // A once-per-turn power says it's locked at the start (FirstPowerTurn).
    public static string Describe(ChampionDef c) => c.PassiveKind is ChampionPassiveKind.PumpOneAttack
        or ChampionPassiveKind.ShieldOneFromCombat or ChampionPassiveKind.FieldOneFree or ChampionPassiveKind.SpinOne
        ? DescribeKind(c) + " (not during either player's first turn)"
        : DescribeKind(c);

    private static string DescribeKind(ChampionDef c) => c.PassiveKind switch
    {
        ChampionPassiveKind.AttackBuff => $"+{c.Amount} ATK to all your dice",
        ChampionPassiveKind.DefenseBuff => $"+{c.Amount} DEF to all your dice",
        ChampionPassiveKind.FieldingCostDiscount => $"Your dice cost {c.Amount} less to field (min 0)",
        ChampionPassiveKind.PurchaseCostDiscount => $"Your Character purchases cost {c.Amount} less (min 1)",
        ChampionPassiveKind.Foresight => "Foresight: once per turn, in your Main Step, reroll one die in your Reserve Pool",
        ChampionPassiveKind.PumpOneAttack => $"Once per your turn: one of your creatures gets +{c.Amount} ATK this turn",
        ChampionPassiveKind.ShieldOneFromCombat => "Once per turn, after blocks: prevent all combat damage to one of your creatures in combat",
        ChampionPassiveKind.FieldOneFree => "Once per your turn: field one creature without paying its fielding cost",
        ChampionPassiveKind.SpinOne => c.Amount <= 1
            ? "Once per your turn: spin one creature a level - yours up, theirs down"
            : $"Once per your turn: spin one creature {c.Amount} levels - yours up, theirs down",
        _ => c.PassiveKind.ToString(),
    };
}
