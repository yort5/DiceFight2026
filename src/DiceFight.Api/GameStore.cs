using System.Collections.Concurrent;
using DiceFight.Engine;

namespace DiceFight.Api;

// In-memory only - games don't survive an API restart. Fine for local
// development; a real deployment would need persistence (and the
// login/auth layer the project's owner wants in front of the engine).
public sealed class GameStore
{
    private readonly ConcurrentDictionary<string, GameState> _games = new();

    public string Create(GameState state)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _games[id] = state;
        return id;
    }

    public GameState Get(string gameId) =>
        _games.TryGetValue(gameId, out var state)
            ? state
            : throw new KeyNotFoundException($"No game with id '{gameId}'.");
}
