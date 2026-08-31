using System.Collections.Concurrent;
using DiceFight.Engine;

namespace DiceFight.Api;

// In-memory only - games don't survive an API restart. Fine for one
// browser driving both sides; a game two people are part-way through
// needs real persistence, which is the next piece.
public sealed class GameStore
{
    private readonly ConcurrentDictionary<string, GameSession> _games = new();

    public GameSession Create(GameState state)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var session = new GameSession
        {
            Id = id,
            State = state,
            // One seat per side, each with its own secret. The creator
            // keeps the first and hands the second out as an invite.
            Seats =
            [
                new Seat(state.PlayerOne.Id, GameSession.NewToken()),
                new Seat(state.PlayerTwo.Id, GameSession.NewToken()),
            ],
        };
        _games[id] = session;
        return session;
    }

    public GameSession GetSession(string gameId) =>
        _games.TryGetValue(gameId, out var session)
            ? session
            : throw new KeyNotFoundException($"No game with id '{gameId}'.");

    public GameState Get(string gameId) => GetSession(gameId).State;
}
