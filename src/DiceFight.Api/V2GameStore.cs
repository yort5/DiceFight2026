using System.Collections.Concurrent;
using DiceFight.V2;

namespace DiceFight.Api;

// v2 counterpart to GameStore.cs - kept as a genuinely separate class
// (not a shared generic store) per the mellow-sparking-comet plan's own
// reasoning: "keeps v1 untouched" matters more here than avoiding a
// little duplication. In-memory only, same caveat as GameStore.
//
// `recorder` (2026-10-04): when game recording is configured, every game
// started here gets a GameRecord (Recording/GameRecorder.cs).
//
// `accounts` (2026-10-10): players, and who holds each seat - the controller
// keeps each player's list of games through it.
public sealed class V2GameStore(Recording.GameRecorder? recorder = null, Accounts.PlayerAccounts? accounts = null)
{
    private readonly ConcurrentDictionary<string, V2GameSession> _games = new();

    public Recording.GameRecorder? Recorder => recorder;
    public Accounts.PlayerAccounts? Accounts => accounts;

    public IEnumerable<V2GameSession> Sessions => _games.Values;
    public IEnumerable<V2Lobby> Lobbies => _lobbies.Values;

    // `id`/`seed` are given only when replaying a recorded game.
    public V2GameSession Create(GameState state, string? id = null, int? seed = null, string? bossId = null)
    {
        id ??= Guid.NewGuid().ToString("N")[..8];
        var session = new V2GameSession
        {
            Id = id,
            State = state,
            Seats =
            [
                new Seat(state.PlayerOne.Id, GameSession.NewToken()),
                new Seat(state.PlayerTwo.Id, GameSession.NewToken()),
            ],
            Seed = seed ?? Random.Shared.Next(),
            BossId = bossId,
        };
        if (bossId is not null) session.BotSeats.Add(state.PlayerTwo.Id);
        _games[id] = session;
        recorder?.Start(session);
        return session;
    }

    public V2GameSession GetSession(string gameId) =>
        _games.TryGetValue(gameId, out var session)
            ? session
            : _forgotten.ContainsKey(gameId)
                ? throw new KeyNotFoundException("This game has expired - nobody moved for a long while. Start a new one from the menu.")
                : throw new KeyNotFoundException($"No V2 game with id '{gameId}'.");

    // --- Forgetting idle games (2026-10-04). Games and open invites only
    // live in memory, so ones nobody will come back to are dropped by
    // GameSweeper: an unfinished game with no move for a day, a finished
    // one a couple of hours after it ended (time to look at the final
    // board), an invite never joined after a day. An unfinished game's
    // record is saved one last time first, which also keeps the moves of
    // the turn it was abandoned in. Forgotten ids are remembered for a
    // week so a late visitor hears "expired", not "no such game".

    private readonly ConcurrentDictionary<string, DateTime> _forgotten = new();

    public int ForgetIdle(DateTime nowUtc, GameExpiryOptions expiry)
    {
        var forgotten = 0;
        foreach (var (id, session) in _games)
        {
            var finished = session.State.IsGameOver;
            var idle = nowUtc - session.LastMoveUtc;
            if (idle < TimeSpan.FromHours(finished ? expiry.FinishedHours : expiry.UnfinishedHours)) continue;
            if (!finished) recorder?.Save(session);
            if (_games.TryRemove(id, out _)) { _forgotten[id] = nowUtc; forgotten++; }
        }
        foreach (var (id, lobby) in _lobbies)
            if (nowUtc - lobby.CreatedUtc >= TimeSpan.FromHours(expiry.LobbyHours) && _lobbies.TryRemove(id, out _))
            {
                _forgotten[id] = nowUtc;
                forgotten++;
            }
        foreach (var (id, at) in _forgotten)
            if (nowUtc - at > TimeSpan.FromDays(7)) _forgotten.TryRemove(id, out _);
        return forgotten;
    }

    public int GameCount => _games.Count;

    // --- Open games (2026-09-30): the host picks only their own Champion
    // and gets an invite link straight away; the game itself is built when
    // the invited player picks theirs (V2GamesController.Join). The lobby's
    // id and seat tokens carry over unchanged, so the link keeps working.

    private readonly ConcurrentDictionary<string, V2Lobby> _lobbies = new();

    public V2Lobby OpenLobby(string? hostChampionId, CustomV2TeamRequest? hostTeam, string hostLabel, string hostSeatId, string guestSeatId)
    {
        var lobby = new V2Lobby
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            HostChampionId = hostChampionId,
            HostTeam = hostTeam,
            HostLabel = hostLabel,
            Seats = [new Seat(hostSeatId, GameSession.NewToken()), new Seat(guestSeatId, GameSession.NewToken())],
        };
        _lobbies[lobby.Id] = lobby;
        return lobby;
    }

    public V2Lobby? GetLobby(string gameId) => _lobbies.TryGetValue(gameId, out var lobby) ? lobby : null;

    public bool HasGame(string gameId) => _games.ContainsKey(gameId);

    // Builds the real game under the lobby's id and seats, exactly once -
    // a second join (a double tap, two tabs) finds the lobby already gone.
    public V2GameSession StartLobby(V2Lobby lobby, GameState state)
    {
        if (!_lobbies.TryRemove(lobby.Id, out _))
            throw new InvalidOperationException("That game has already started.");
        var session = new V2GameSession { Id = lobby.Id, State = state, Seats = lobby.Seats, Seed = Random.Shared.Next() };
        _games[lobby.Id] = session;
        recorder?.Start(session);
        return session;
    }
}

public sealed class V2Lobby
{
    public required string Id { get; init; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    // The host's Champion - a preset's, or a custom team's own (null when
    // the custom team has none).
    public string? HostChampionId { get; init; }
    // The host's custom team, or null for a Champion preset.
    public CustomV2TeamRequest? HostTeam { get; init; }
    public required string HostLabel { get; init; }
    public required IReadOnlyList<Seat> Seats { get; init; }
    // The host's player (Accounts), when they had one.
    public string? HostOwnerId { get; set; }
}

// Config section "GameExpiry" - how long idle games stay in memory.
public sealed class GameExpiryOptions
{
    public double UnfinishedHours { get; set; } = 24;
    public double FinishedHours { get; set; } = 2;
    public double LobbyHours { get; set; } = 24;
}

// Runs V2GameStore.ForgetIdle every half hour.
public sealed class GameSweeper(V2GameStore store, Microsoft.Extensions.Options.IOptions<GameExpiryOptions> expiry, ILogger<GameSweeper> logger)
    : Microsoft.Extensions.Hosting.BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var forgotten = store.ForgetIdle(DateTime.UtcNow, expiry.Value);
                if (forgotten > 0) logger.LogInformation("Forgot {Count} idle games/invites; {Live} games still in memory.", forgotten, store.GameCount);
            }
        }
        catch (OperationCanceledException) { }
    }
}
