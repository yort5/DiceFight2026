using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiceFight.Api.Recording;

// One Dice Kingdom game as played (2026-10-04, user: record human games to
// tune teams and teach the bot). A header, then every action in order -
// rejected ones too - with the game's random seed, so GameReplayer can
// rebuild every state the players saw by re-running the actions through
// the real endpoints. `Check` (StateChecksum) after each action catches a
// replay that has drifted, e.g. across an engine change.
//
// Written as JSON to a local folder or a Cloud Storage bucket
// (GameRecordSinks.cs) at the end of every turn and at game over.
public sealed class GameRecord
{
    public const int CurrentFormat = 1;

    public int Format { get; init; } = CurrentFormat;
    public required string GameId { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; set; }
    // What was running: the Cloud Run revision (K_REVISION) and/or the
    // assembly's informational version. A replay on a different engine
    // may drift - the checksums say where.
    public string? EngineVersion { get; init; }
    public required int Seed { get; init; }
    public required List<RecordedSeat> Seats { get; init; }
    public List<RecordedAction> Actions { get; init; } = [];
    public int Turns { get; set; }
    public RecordedResult? Result { get; set; }
    // The engine's own play-by-play, as of the last write.
    public List<string> Log { get; set; } = [];
}

// `Bot`: this seat asked for the computer's move (GET bot-decision) at least
// once - the web client's "vs computer" seat. A human seat never does.
public sealed class RecordedSeat
{
    public required string PlayerId { get; init; }
    // Null for a custom team (Team Builder), which has no Champion.
    public string? ChampionId { get; init; }
    // The team as dealt - the roster changes; a replay uses this.
    public required List<string> TeamCardIds { get; init; }
    // Custom teams only: dice per card, and Tardigrades per energy type.
    // Null means the Champion's own pack and pool.
    public Dictionary<string, int>? CardDiceCounts { get; init; }
    public Dictionary<string, int>? Tardigrades { get; init; }
    public bool Bot { get; set; }
}

public sealed class RecordedAction
{
    public required int Seq { get; init; }
    public required DateTime AtUtc { get; init; }
    public required string PlayerId { get; init; }
    // The V2GamesController method name (Purchase, DeclareBlockers, ...).
    public required string Action { get; init; }
    // The request body as sent, or null for body-less actions.
    public JsonElement? Request { get; init; }
    public required int Turn { get; init; }
    // Step and priority holder BEFORE the action - where the player was.
    public required string Step { get; init; }
    // Set when the server rejected the action (an illegal move).
    public string? Error { get; init; }
    // The client made this move on the player's behalf (X-Auto-Move: no
    // attackers to block, nothing left to do in the attack window) - not a
    // decision.
    public bool Auto { get; init; }
    // StateChecksum after the action.
    public required string Check { get; init; }
}

public sealed class RecordedResult
{
    public bool GameOver { get; init; }
    public string? WinnerId { get; init; }
    public required Dictionary<string, int> Life { get; init; }
}

public static class GameRecordJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Serialize(GameRecord record) => JsonSerializer.SerializeToUtf8Bytes(record, Options);

    public static GameRecord Deserialize(string json) =>
        JsonSerializer.Deserialize<GameRecord>(json, Options) ?? throw new InvalidDataException("Empty game record.");
}
