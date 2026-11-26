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
        var players = PlayersOf(record);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, players[0], players[1], record.FirstPlayerId);
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

    // A game the server forgot, rebuilt to carry on (2026-11-14, user: "No
    // V2 game with id" on a game left for half an hour): the same replay,
    // but under the game's own seat tokens, and lenient - a checksum that
    // drifts (the engine changed since the move was made) is reported, not
    // fatal, since the same moves on a slightly different engine beat losing
    // the game. It stops only where a move no longer goes through (or one
    // that was refused now would), and the record is cut back to match.
    public static (V2GameSession Session, string? Divergence) Restore(GameRecord record, LiveGame live)
    {
        var players = PlayersOf(record);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, players[0], players[1], record.FirstPlayerId);
        var store = new V2GameStore();
        var seats = record.Seats.Select(s => new Seat(s.PlayerId, live.SeatTokens[s.PlayerId])).ToList();
        var session = store.Create(state, record.GameId, record.Seed, live.BossId, seats);

        string? divergence = null;
        var replayed = 0;
        foreach (var action in record.Actions)
        {
            var error = Invoke(store, session, action);
            if ((error is null) != (action.Error is null))
            {
                divergence = $"Action {action.Seq} ({action.Action}): recorded {(action.Error is null ? "success" : "a refusal")}, replay {(error is null ? "succeeded" : $"failed: {error}")} - restored up to the move before.";
                break;
            }
            if (divergence is null && StateChecksum.Of(state) != action.Check)
                divergence = $"Action {action.Seq} ({action.Action}) and on: the state differs from the recording (engine changed?) - carried on.";
            replayed++;
        }
        if (replayed < record.Actions.Count) record.Actions.RemoveRange(replayed, record.Actions.Count - replayed);

        session.Record = record;
        session.RecordedActivePlayerId = state.ActivePlayerId;
        foreach (var (seat, owner) in live.SeatOwners) session.SeatOwners[seat] = owner;
        foreach (var seat in live.BotSeats) session.BotSeats.Add(seat);
        // Past any version a browser already saw, so every open tab reloads.
        session.RestoreVersion(Math.Max(live.Version, session.Version) + 1);
        return (session, divergence);
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

    // Both seats as dealt. A boss battle's boss (teamB) gets the boss's name
    // back - otherwise a game rebuilt after a restart showed it as "Custom
    // Team" (user, 2026-11-26: The Fire Ant Colony turned into Custom Team
    // mid-game).
    private static List<Player> PlayersOf(GameRecord record)
    {
        var bossName = DiceKingdomBosses.Find(record.BossId)?.Name;
        return record.Seats.Select((seat, i) => BuildPlayer(seat, i == 1 ? bossName : null)).ToList();
    }

    // The team exactly as it was dealt in the recorded game.
    private static Player BuildPlayer(RecordedSeat seat, string? name = null)
    {
        // A custom team - with or without a Champion of its own.
        if (seat.ChampionId is null || seat.CardDiceCounts is not null)
        {
            var custom = new Player
            {
                Id = seat.PlayerId,
                Name = name ?? (seat.ChampionId is { } id ? DiceKingdomConfig.Champions.First(c => c.Id == id).Name : "Custom Team"),
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
