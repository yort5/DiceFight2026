using System.Security.Cryptography;
using DiceFight.V2;

namespace DiceFight.Api;

// v2 counterpart to GameSession.cs - same shape (seats/version), wrapping
// DiceFight.V2.GameState instead of DiceFight.Engine.GameState. Reuses
// the existing Seat record (Dtos.cs's Seat carries no engine-specific
// type, just two strings) rather than duplicating it.
public sealed class V2GameSession
{
    public required string Id { get; init; }
    public required GameState State { get; init; }
    public required IReadOnlyList<Seat> Seats { get; init; }

    private int _version;
    public int Version => Volatile.Read(ref _version);
    public void MarkChanged() => Interlocked.Increment(ref _version);

    // Held here, not on GameState (v2 has no such field - deliberately,
    // per its own EffectInterpreter remarks: "no rule has ever been
    // half-resolved," the same reasoning v1's own GameSession.PendingRange
    // already documents). Non-null exactly when a PendingChoice interrupted
    // a Drain mid-ability; ResolvePendingChoice picks this queue back up to
    // finish whatever was still behind the choice, matching v1's
    // AnswerPendingChoice/queue-resumption pattern.
    public AbilityQueue? PendingQueue { get; set; }

    public string? PlayerIdFor(string? token) =>
        token is null ? null : Seats.FirstOrDefault(s => FixedTimeEquals(s.Token, token))?.PlayerId;

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
