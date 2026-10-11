using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.Api.Recording;

// Keeps each live game's GameRecord (on its V2GameSession) up to date and
// writes it out - at the end of every turn, so an abandoned game is still
// kept, and once more at game over. Writes go through a queue drained in
// the background, so a slow bucket never holds up a move. Registered only
// when GameRecordOptions names a sink (Program.cs); V2GameStore works
// without one.
public sealed class GameRecorder(IGameRecordSink sink, GameRecordOptions options, ILogger<GameRecorder> logger) : BackgroundService
{
    private readonly Channel<(string Name, byte[] Json)> _writes = Channel.CreateUnbounded<(string, byte[])>();

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
        };
        session.RecordedActivePlayerId = session.State.ActivePlayerId;
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
        if (state.IsGameOver || turnEnded) Save(session);
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
        var name = $"{options.Prefix}{record.CreatedUtc:yyyy/MM/dd}/{record.GameId}.json";
        _writes.Writer.TryWrite((name, GameRecordJson.Serialize(record)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Recording Dice Kingdom games to {Sink}.", sink.Describe());
        try
        {
            await foreach (var (name, json) in _writes.Reader.ReadAllAsync(stoppingToken)) await WriteOne(name, json, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    // Shutdown (a redeploy, Cloud Run scaling in): flush what's queued.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _writes.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
        while (_writes.Reader.TryRead(out var write)) await WriteOne(write.Name, write.Json, cancellationToken);
    }

    private async Task WriteOne(string name, byte[] json, CancellationToken cancel)
    {
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
