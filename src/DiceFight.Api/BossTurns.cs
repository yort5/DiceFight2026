using DiceFight.Api.Controllers;
using DiceFight.V2;
using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api;

// The server plays a boss battle's boss itself (2026-10-31, user: "I'd
// feel better with the full server version") - in other vs-computer games
// the browser holds both seats and carries out the bot's moves, so a
// player could make the boss play badly for its reward. Here the boss's
// seat token never leaves the server.
//
// One move per GET of the game - the browser's 2-second check-in - and no
// sooner than Pace after the last move, so the boss's turn plays out at a
// pace the player can follow and animate, the way an online opponent's
// does. No background timer: Cloud Run only gives the server CPU while it
// is answering a request, and a game nobody is looking at needn't move.
//
// Each move goes through the same V2GamesController method a player's
// request would (as GameReplayer does), so rules, recording and accounts
// are the same as a move from the browser. The caller holds the game's
// Gate (V2GamesController.OnActionExecuting).
public static class BossTurns
{
    // The least time between a move and the boss's next one (V2GameStore.BossPace - tests set it to zero).
    public static readonly TimeSpan Pace = TimeSpan.FromSeconds(1);

    // Makes the boss's next move if it's the boss's decision; true if one was made.
    public static bool Step(V2GameStore store, V2GameSession session)
    {
        var state = session.State;
        if (session.BossId is not { } bossId || state.IsGameOver) return false;
        var boss = state.PlayerTwo.Id;
        if (BotDriver.DecisionOwner(state) != boss) return false;
        if (DateTime.UtcNow - session.LastMoveUtc < store.BossPace) return false;

        // Dice the engine refused this turn (a rule the bot doesn't model),
        // never offered again until the turn changes - as the browser does.
        if (session.BossSkipTurn != state.ActivePlayerId)
        {
            session.BossSkip.Clear();
            session.BossSkipTurn = state.ActivePlayerId;
        }
        var style = DiceKingdomBosses.Find(bossId)?.Style;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (DiceKingdomBot.Decide(state, boss, session.BossSkip, style) is not { } decision) return false;
            if (Apply(store, session, decision) is null) return true;
            if (decision.DieId is { } die)
            {
                session.BossSkip.Add(die);
                continue;
            }
            // Nothing to rule out: fall back to doing nothing in this step.
            var fallback = decision.Kind switch
            {
                BotActionKind.DeclareAttackers when decision.AttackerLanes.Count > 0 => decision with { AttackerLanes = new Dictionary<string, int>() },
                BotActionKind.DeclareBlockers when decision.Blocks.Count > 0 => decision with { Blocks = [] },
                BotActionKind.Pass or BotActionKind.DeclareAttackers or BotActionKind.DeclareBlockers => null,
                _ => new BotDecision(BotActionKind.Pass, "fallback"),
            };
            return fallback is not null && Apply(store, session, fallback) is null;
        }
        return false;
    }

    // Carries out one decision as the boss; null if it went through, else the refusal.
    private static string? Apply(V2GameStore store, V2GameSession session, BotDecision d)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Headers["X-Seat-Token"] = session.Seats.Single(s => s.PlayerId == session.State.PlayerTwo.Id).Token;
        var games = new V2GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };
        var id = session.Id;
        var stepBefore = session.State.CurrentStepId;

        (string Action, object? Request, Action Run) move = d.Kind switch
        {
            BotActionKind.ClearAndDraw => (nameof(games.ClearAndDraw), null, () => games.ClearAndDraw(id)),
            BotActionKind.Roll => (nameof(games.Roll), null, () => games.Roll(id)),
            BotActionKind.Reroll => Request(nameof(games.Reroll), new V2RerollRequest(d.DieIds), r => games.Reroll(id, r)),
            BotActionKind.FinishRoll => (nameof(games.FinishRoll), null, () => games.FinishRoll(id)),
            BotActionKind.Foresight => Request(nameof(games.Foresight), new V2UseActionRequest(d.DieId!), r => games.Foresight(id, r)),
            BotActionKind.Field => Request(nameof(games.Field), new V2FieldRequest(d.DieId!, d.EnergyDieIds, d.Free), r => games.Field(id, r)),
            BotActionKind.Purchase => Request(nameof(games.Purchase), new V2PurchaseRequest(d.DieId!, d.EnergyDieIds), r => games.Purchase(id, r)),
            BotActionKind.UseAction => Request(nameof(games.UseAction), new V2UseActionRequest(d.DieId!), r => games.UseAction(id, r)),
            BotActionKind.UseGlobal => Request(nameof(games.UseGlobal), new V2UseGlobalRequest(d.CardId!, d.AbilityIndex, d.EnergyDieIds), r => games.UseGlobal(id, r)),
            BotActionKind.Pass when d.SkipAttack => (nameof(games.SkipAttackStep), null, () => games.SkipAttackStep(id)),
            BotActionKind.Pass => (nameof(games.Pass), null, () => games.Pass(id)),
            BotActionKind.DeclareAttackers => Request(nameof(games.DeclareAttackers),
                new V2DeclareAttackersRequest(d.AttackerLanes.Select(kv => new V2AttackerDeclaration(kv.Key, kv.Value)).ToList()), r => games.DeclareAttackers(id, r)),
            BotActionKind.DeclareBlockers => Request(nameof(games.DeclareBlockers),
                new V2DeclareBlockersRequest(d.Blocks.Select(b => new V2BlockAssignment(b.AttackerId, b.BlockerId)).ToList()), r => games.DeclareBlockers(id, r)),
            BotActionKind.ResolvePendingChoice => Request(nameof(games.ResolvePendingChoice), new V2ResolvePendingChoiceRequest(d.DieIds), r => games.ResolvePendingChoice(id, r)),
            BotActionKind.CleanUp => (nameof(games.CleanUp), null, () => games.CleanUp(id)),
            BotActionKind.UseChampionPower => (nameof(games.ChampionPower), null, () => games.ChampionPower(id)),
            _ => throw new InvalidOperationException($"No endpoint for {d.Kind}."),
        };

        string? error = null;
        try { move.Run(); }
        catch (Exception e) { error = e.Message; } // a refused boss move must never fail the player's request
        store.Recorder?.Record(session, session.State.PlayerTwo.Id, move.Action, move.Request, stepBefore, error);
        return error;
    }

    private static (string, object?, Action) Request<T>(string action, T request, Action<T> run) => (action, request, () => run(request));
}
