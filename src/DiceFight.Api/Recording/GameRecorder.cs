using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.Api.Recording;

// Keeps each live game's GameRecord (on its V2GameSession) up to date and
// writes it out - after every move, with its LiveGame beside it, so a game
// the server forgets can be restored (Restore below). Writes go through a
// queue drained in the background, so a slow bucket never holds up a move,
// and a file written again before its last write went out is written once,
// newest version (Cloud Storage allows about one write a second per object,
// and a computer turn makes several moves in a row). Registered only when
// GameRecordOptions names a sink (Program.cs); V2GameStore works without one.
public sealed class GameRecorder(IGameRecordSink sink, GameRecordOptions options, ILogger<GameRecorder> logger) : BackgroundService
{
    private readonly Channel<string> _writes = Channel.CreateUnbounded<string>();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _pending = new();

    private static readonly string? EngineVersion =
        Environment.GetEnvironmentVariable("K_REVISION") is { } revision
            ? $"{revision} ({Version()})"
            : Version();

    private static string? Version() =>
        typeof(GameRecorder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public string Describe() => sink.Describe();

    public void Start(V2GameSession session)
    {
        session.Record = new GameRecord
        {
            GameId = session.Id,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            EngineVersion = EngineVersion,
            Seed = session.Seed,
            Seats = [
                Seat(session.State.PlayerOne),
                Seat(session.State.PlayerTwo),
            ],
            Turns = 1,
            BossId = session.BossId,
            FirstPlayerId = session.State.ActivePlayerId,
        };
        if (session.BossId is not null) session.Record.Seats[1].Bot = true;
        session.RecordedActivePlayerId = session.State.ActivePlayerId;
        Save(session);
    }

    private static RecordedSeat Seat(Player player) => new()
    {
        PlayerId = player.Id,
        ChampionId = player.ChampionId,
        TeamCardIds = [.. player.TeamCardIds],
        CardDiceCounts = player.CustomCardDiceCounts is { } counts ? new(counts) : null,
        Tardigrades = player.CustomBasicDicePool?.ToDictionary(e => DiceKingdomConfig.TardigradeEnergyType(e.Die.Id), e => e.Count),
    };

    public static void MarkBot(V2GameSession session, string playerId)
    {
        if (session.Record?.Seats.FirstOrDefault(s => s.PlayerId == playerId) is { } seat) seat.Bot = true;
    }

    // One action, after it ran (or was rejected). `stepBefore` is where the
    // player was when they chose it.
    public void Record(V2GameSession session, string playerId, string action, object? request, string stepBefore, string? error, bool auto = false)
    {
        if (session.Record is not { } record || record.Result is { GameOver: true }) return;
        var state = session.State;
        record.Actions.Add(new RecordedAction
        {
            Seq = record.Actions.Count + 1,
            AtUtc = DateTime.UtcNow,
            PlayerId = playerId,
            Action = action,
            Request = request is null ? null : JsonSerializer.SerializeToElement(request, request.GetType(), GameRecordJson.Options),
            Turn = record.Turns,
            Step = stepBefore,
            Error = error,
            Auto = auto,
            Check = StateChecksum.Of(state),
        });

        var turnEnded = state.ActivePlayerId != session.RecordedActivePlayerId;
        if (turnEnded)
        {
            record.Turns++;
            session.RecordedActivePlayerId = state.ActivePlayerId;
        }
        Save(session);
    }

    public void Save(V2GameSession session)
    {
        if (session.Record is not { } record) return;
        var state = session.State;
        record.UpdatedUtc = DateTime.UtcNow;
        record.Result = new RecordedResult
        {
            GameOver = state.IsGameOver,
            WinnerId = state.WinnerId,
            Life = new() { [state.PlayerOne.Id] = state.PlayerOne.Life, [state.PlayerTwo.Id] = state.PlayerTwo.Life },
        };
        record.Log = state.Log.Select(l => l.PlayerId is { } p ? $"[{p}] {l.Text}" : l.Text).ToList();
        // Serialized now, on the request's thread - the record keeps changing.
        var name = RecordName(record);
        Queue(name, GameRecordJson.Serialize(record));
        Queue(LiveName(record.GameId), GameRecordJson.Serialize(new LiveGame
        {
            RecordName = name,
            SeatTokens = session.Seats.ToDictionary(s => s.PlayerId, s => s.Token),
            SeatOwners = new(session.SeatOwners),
            BotSeats = [.. session.BotSeats],
            BossId = session.BossId,
            Version = session.Version,
        }));
    }

    private string RecordName(GameRecord record) => $"{options.Prefix}{record.CreatedUtc:yyyy/MM/dd}/{record.GameId}.json";
    private string LiveName(string gameId) => $"{options.LivePrefix}{gameId}.json";

    private void Queue(string name, byte[] json)
    {
        _pending[name] = json;
        _writes.Writer.TryWrite(name);
    }

    // A game this server doesn't have in memory, rebuilt from its record, or
    // null when there's no record of it. Blocks on the bucket: it runs once
    // per forgotten game, on the first request that asks for it.
    public V2GameSession? Restore(string gameId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(gameId, "^[0-9a-f]{8}$")) return null;
        var liveJson = sink.ReadAsync(LiveName(gameId), CancellationToken.None).GetAwaiter().GetResult();
        if (liveJson is null) return null;
        var live = GameRecordJson.DeserializeLive(liveJson);
        var recordJson = sink.ReadAsync(live.RecordName, CancellationToken.None).GetAwaiter().GetResult();
        if (recordJson is null) return null;
        var record = GameRecordJson.Deserialize(System.Text.Encoding.UTF8.GetString(recordJson));
        var (session, divergence) = GameReplayer.Restore(record, live);
        if (divergence is not null)
            logger.LogWarning("Restored game {GameId} from its record, but the replay drifted: {Divergence}", gameId, divergence);
        else
            logger.LogInformation("Restored game {GameId} from its record ({Actions} actions).", gameId, record.Actions.Count);
        return session;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Recording Dice Kingdom games to {Sink}.", sink.Describe());
        try
        {
            // A write already under way finishes even as shutdown begins:
            // cancelling it would lose it, since it's already off the queue
            // StopAsync drains.
            await foreach (var name in _writes.Reader.ReadAllAsync(stoppingToken)) await WriteOne(name, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
    }

    // Shutdown (a redeploy, Cloud Run scaling in): flush what's queued.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _writes.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
        while (_writes.Reader.TryRead(out var name)) await WriteOne(name, cancellationToken);
    }

    public void LogRestoreFailure(string gameId, Exception ex) =>
        logger.LogWarning(ex, "Couldn't restore game {GameId} from its record.", gameId);

    private async Task WriteOne(string name, CancellationToken cancel)
    {
        // Already written by an earlier signal for the same file.
        if (!_pending.TryRemove(name, out var json)) return;
        try
        {
            await sink.WriteAsync(name, json, cancel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't write game record {Name}.", name);
        }
    }
}

// Builds the GameRecorder from configuration, or none, and runs it as a
// hosted service when there is one (Program.cs).
public sealed class GameRecorderSetup(GameRecorder? recorder) : IHostedService
{
    public GameRecorder? Recorder => recorder;

    public static GameRecorderSetup FromConfig(IServiceProvider services)
    {
        var options = Options(services);
        var sink = SinkFromConfig(services);
        return new(sink is null ? null : new GameRecorder(sink, options, services.GetRequiredService<ILogger<GameRecorder>>()));
    }

    private static GameRecordOptions Options(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>().GetSection("GameRecords").Get<GameRecordOptions>() ?? new GameRecordOptions();

    // Where GameRecords points, or null - also where players are kept (Accounts).
    public static IGameRecordSink? SinkFromConfig(IServiceProvider services)
    {
        var options = Options(services);
        return !string.IsNullOrWhiteSpace(options.Bucket) ? new CloudStorageSink(options.Bucket, new HttpClient())
            : !string.IsNullOrWhiteSpace(options.LocalPath) ? new LocalFolderSink(options.LocalPath)
            : null;
    }

    public Task StartAsync(CancellationToken cancel) => recorder?.StartAsync(cancel) ?? Task.CompletedTask;
    public Task StopAsync(CancellationToken cancel) => recorder?.StopAsync(cancel) ?? Task.CompletedTask;
}
