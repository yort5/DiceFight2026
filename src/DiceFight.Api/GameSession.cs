using System.Security.Cryptography;
using DiceFight.Engine;

namespace DiceFight.Api;

/// <summary>
/// One of the two sides of a game, and the secret that proves you hold it.
/// </summary>
/// <param name="PlayerId">The engine's id for this side ("teamA"/"teamB").</param>
/// <param name="Token">Opaque bearer secret. Whoever has it holds the seat -
/// the same model as a shared document link, and the reason it must never
/// appear in anything shown to the other player.</param>
public sealed record Seat(string PlayerId, string Token);

// A game plus who is allowed to act in it.
//
// Before seats existed, every action took the acting player's id as a
// REQUEST PARAMETER, so any caller could act as either side. That is fine
// for one browser driving both halves and untenable the moment the two
// halves are different people, which is what this closes.
public sealed class GameSession
{
    public required string Id { get; init; }
    public required GameState State { get; init; }
    public required IReadOnlyList<Seat> Seats { get; init; }

    // Bumped on every action. The opponent's browser polls the game and
    // only re-renders when this changes, so a quiet game costs one cheap
    // comparison every couple of seconds rather than a constant redraw.
    // Interlocked because two seats can act in the same instant.
    private int _version;

    public int Version => Volatile.Read(ref _version);

    public void MarkChanged() => Interlocked.Increment(ref _version);

    // The Range window is the one place both players decide at once. Rule
    // 2.7.4.2 has every Range die fire simultaneously, but two browsers
    // cannot submit simultaneously - so the active player submits first
    // (rule 2.6.5.7 gives them priority, and priority is exactly what
    // settles a tie) and their assignments wait here until the opponent
    // answers. Null means nothing is waiting.
    //
    // Deliberately NOT in GameState: no rule has ever been half-resolved,
    // and an engine that could be caught mid-Range would have to answer
    // that question everywhere. This is the shape of the conversation, not
    // the shape of the game.
    public IReadOnlyList<(string RangeDieId, string TargetDieId)>? PendingRange { get; set; }

    /// <summary>Which side this token holds, or null if it holds none.</summary>
    public string? PlayerIdFor(string? token) =>
        token is null ? null : Seats.FirstOrDefault(s => FixedTimeEquals(s.Token, token))?.PlayerId;

    // Constant-time so a token cannot be guessed a character at a time.
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
