using DiceFight.V2.Model;

namespace DiceFight.V2.Bot;

// Carries out a BotDecision against a local GameState - one engine call
// per decision, mirroring what the matching V2GamesController endpoint
// does (priority checks, draining, Priority.Sync afterward), so a game
// driven here walks the same step/priority machinery a real browser game
// does. Used by tools/Simulator and tests; the web client carries out the
// same decisions through the API instead.
public sealed class BotDriver(GameState state, Random random)
{
    private readonly IDiceRoller _roller = new RandomDiceRoller(random);

    // The queue a PendingChoice interrupted (V2GameSession.PendingQueue's
    // local equivalent) - resumed when the choice is answered.
    private AbilityQueue? _pendingQueue;

    public GameState State => state;

    // Whoever has to decide next: the pending choice's owner, the priority
    // holder, the Inactive player in Assign Blockers, else the Active one.
    public string DecisionOwner() => DecisionOwner(state);

    public static string DecisionOwner(GameState state)
    {
        if (state.PendingChoice is { } pending) return pending.ControllerId;
        if (Priority.IsWindow(state)) return state.PriorityPlayerId ?? state.ActivePlayerId;
        return state.CurrentStepId == StepIds.AssignBlockers ? state.OpponentOf(state.ActivePlayerId) : state.ActivePlayerId;
    }

    public void Apply(string playerId, BotDecision decision)
    {
        if (state.IsGameOver) throw new InvalidOperationException("The game is over.");
        var queue = new AbilityQueue();
        switch (decision.Kind)
        {
            case BotActionKind.ResolvePendingChoice:
                queue = _pendingQueue ?? queue;
                _pendingQueue = null;
                EffectInterpreter.AnswerPendingChoice(state, decision.DieIds);
                break;
            case BotActionKind.ClearAndDraw: TurnEngine.ClearAndDraw(state, queue, random); break;
            case BotActionKind.Roll: TurnEngine.Roll(state, queue, _roller); break;
            case BotActionKind.Reroll: TurnEngine.RerollOwn(state, queue, _roller, decision.DieIds); break;
            case BotActionKind.FinishRoll: TurnEngine.FinishRoll(state, queue); break;
            case BotActionKind.Foresight:
                Priority.RequireHolder(state, playerId);
                TurnEngine.UseForesight(state, queue, _roller, playerId, decision.DieId!);
                break;
            case BotActionKind.Field:
                Priority.RequireHolder(state, playerId);
                TurnEngine.Field(state, queue, decision.DieId!, decision.EnergyDieIds, decision.Free);
                break;
            case BotActionKind.Purchase:
                Priority.RequireHolder(state, playerId);
                TurnEngine.Purchase(state, queue, decision.DieId!, decision.EnergyDieIds);
                break;
            case BotActionKind.UseAction:
                Priority.RequireHolder(state, playerId);
                TurnEngine.UseAction(state, queue, decision.DieId!);
                break;
            case BotActionKind.UseGlobal:
                Priority.RequireHolder(state, playerId);
                TurnEngine.UseGlobal(state, queue, decision.CardId!, playerId, decision.AbilityIndex, decision.EnergyDieIds);
                Drain(queue);
                Priority.AfterGlobal(state, playerId);
                Priority.Sync(state);
                return;
            case BotActionKind.UseChampionPower:
                ChampionPowers.Use(state, queue, playerId);
                Drain(queue);
                Priority.AfterGlobal(state, playerId);
                Priority.Sync(state);
                return;
            case BotActionKind.Pass: Priority.Pass(state, queue, playerId, decision.SkipAttack); break;
            case BotActionKind.DeclareAttackers: CombatEngine.DeclareAttackers(state, queue, decision.AttackerLanes); break;
            case BotActionKind.DeclareBlockers:
                var assignment = new CombatAssignment();
                foreach (var (attacker, blocker) in decision.Blocks) assignment.AssignBlocker(attacker, blocker);
                CombatEngine.DeclareBlockers(state, queue, assignment, decision.Blocks.Select(b => b.BlockerId).Distinct().ToList());
                break;
            case BotActionKind.CleanUp: TurnEngine.CleanUp(state, queue); break;
            default: throw new ArgumentOutOfRangeException(nameof(decision), decision.Kind, null);
        }
        Drain(queue);
        Priority.Sync(state);
    }

    private void Drain(AbilityQueue queue)
    {
        EffectInterpreter.DrainQueue(state, queue, _roller, random);
        _pendingQueue = state.PendingChoice is not null ? queue : null;
    }
}
