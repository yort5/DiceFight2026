using System.Collections.Concurrent;
using DiceFight.V2;

namespace DiceFight.Api;

// v2 counterpart to GameStore.cs - kept as a genuinely separate class
// (not a shared generic store) per the mellow-sparking-comet plan's own
// reasoning: "keeps v1 untouched" matters more here than avoiding a
// little duplication. In-memory only, same caveat as GameStore.
public sealed class V2GameStore
{
    private readonly ConcurrentDictionary<string, V2GameSession> _games = new();

    public V2GameSession Create(GameState state)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var session = new V2GameSession
        {
            Id = id,
            State = state,
            Seats =
            [
                new Seat(state.PlayerOne.Id, GameSession.NewToken()),
                new Seat(state.PlayerTwo.Id, GameSession.NewToken()),
            ],
        };
        _games[id] = session;
        return session;
    }

    public V2GameSession GetSession(string gameId) =>
        _games.TryGetValue(gameId, out var session)
            ? session
            : throw new KeyNotFoundException($"No V2 game with id '{gameId}'.");
}
