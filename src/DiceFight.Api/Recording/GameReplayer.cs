using System.Reflection;
using System.Text.Json;
using DiceFight.Api.Controllers;
using DiceFight.V2;
using DiceFight.V2.Model;
using DiceFight.V2.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Recording;

// Rebuilds a recorded game by running its actions, in order, through the
// same V2GamesController methods the players' requests went through, on the
// recorded seed. `onAction` sees the state BEFORE each action - the moment
// the player decided - which is what tuning and bot-teaching need ("what
// would the bot have done here?"). Each action's checksum is compared to
// the recording; the first mismatch is reported (an engine change since
// the game was played can do that) and the replay stops there.
public static class GameReplayer
{
    public sealed record Outcome(GameState State, int ActionsReplayed, string? Divergence);

    public static Outcome Replay(GameRecord record, Action<GameState, RecordedAction>? onAction = null)
    {
        var players = record.Seats.Select(BuildPlayer).ToList();
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, players[0], players[1]);
        var store = new V2GameStore();
        var session = store.Create(state, record.GameId, record.Seed);

        var replayed = 0;
        foreach (var action in record.Actions)
        {
            onAction?.Invoke(state, action);
            var error = Invoke(store, session, action);
            if ((error is null) != (action.Error is null))
                return new(state, replayed, $"Action {action.Seq} ({action.Action}): recorded {(action.Error is null ? "success" : $"error \"{action.Error}\"")}, replay {(error is null ? "succeeded" : $"failed: {error}")}.");
            var check = StateChecksum.Of(state);
            if (check != action.Check)
                return new(state, replayed, $"Action {action.Seq} ({action.Action}): state differs from the recording (checksum {check}, recorded {action.Check}).");
            replayed++;
        }
        return new(state, replayed, null);
    }

    private static string? Invoke(V2GameStore store, V2GameSession session, RecordedAction action)
    {
        var method = typeof(V2GamesController).GetMethod(action.Action, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidDataException($"Unknown action '{action.Action}' in the record.");
        var args = method.GetParameters().Select(p =>
            p.Name == "gameId" ? session.Id
            : p.ParameterType == typeof(string) ? null
            : action.Request is { } body ? body.Deserialize(p.ParameterType, GameRecordJson.Options)
            : null).ToArray();

        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Headers["X-Seat-Token"] = session.Seats.Single(s => s.PlayerId == action.PlayerId).Token;
        var controller = new V2GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };
        try
        {
            method.Invoke(controller, args);
            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException.Message;
        }
    }

    // The team exactly as it was dealt in the recorded game.
    private static Player BuildPlayer(RecordedSeat seat)
    {
        // A custom team - with or without a Champion of its own.
        if (seat.ChampionId is null || seat.CardDiceCounts is not null)
        {
            var custom = new Player
            {
                Id = seat.PlayerId,
                Name = seat.ChampionId is { } id ? DiceKingdomConfig.Champions.First(c => c.Id == id).Name : "Custom Team",
                ChampionId = seat.ChampionId,
                CustomCardDiceCounts = new(seat.CardDiceCounts ?? []),
                CustomBasicDicePool = [.. (seat.Tardigrades ?? []).Select(t => DiceKingdomConfig.CustomTardigrade(t.Key, t.Value))],
            };
            custom.TeamCardIds.AddRange(seat.TeamCardIds);
            return custom;
        }
        var champion = DiceKingdomConfig.Champions.First(c => c.Id == seat.ChampionId);
        var player = new Player { Id = seat.PlayerId, Name = champion.Name, ChampionId = champion.Id };
        player.TeamCardIds.AddRange(seat.TeamCardIds);
        return player;
    }
}
