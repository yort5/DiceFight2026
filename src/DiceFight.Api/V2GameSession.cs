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

    // Every roll, draw and random pick in this game comes from one seeded
    // generator (2026-10-04, game recording) - so the same actions on the
    // same seed rebuild the same game (Recording/GameReplayer.cs).
    public required int Seed { get; init; }
    public Random Rng => _rng ??= new Random(Seed);
    private Random? _rng;

    // The game as played so far, when recording is on (Recording/GameRecorder.cs).
    public Recording.GameRecord? Record { get; set; }
    public string? RecordedActivePlayerId { get; set; }

    private int _version;
    public int Version => Volatile.Read(ref _version);
    public void MarkChanged()
    {
        Interlocked.Increment(ref _version);
        LastMoveUtc = DateTime.UtcNow;
    }

    // When a move last changed this game (creation counts) - what
    // V2GameStore.ForgetIdle goes by. Polling doesn't count: an open tab
    // polls forever.
    public DateTime LastMoveUtc { get; private set; } = DateTime.UtcNow;

    // Held here, not on GameState (v2 has no such field - deliberately,
    // per its own EffectInterpreter remarks: "no rule has ever been
    // half-resolved," the same reasoning v1's own GameSession.PendingRange
    // already documents). Non-null exactly when a PendingChoice interrupted
    // a Drain mid-ability; ResolvePendingChoice picks this queue back up to
    // finish whatever was still behind the choice, matching v1's
    // AnswerPendingChoice/queue-resumption pattern.
    public AbilityQueue? PendingQueue { get; set; }

    // Which player (Accounts/PlayerAccount) holds each seat, by seat id -
    // and which seats the computer plays. Kept by PlayerAccounts.
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> SeatOwners { get; } = new();
    public HashSet<string> BotSeats { get; } = [];
    public bool ResultRecorded { get; set; }
    // A boss battle: the boss (DiceKingdomBosses) is teamB, the computer's
    // seat from the start, playing in the boss's Style.
    public string? BossId { get; init; }
    // The server plays the boss (BossTurns): dice the engine refused it
    // this turn, and whose turn that was.
    public HashSet<string> BossSkip { get; } = [];
    public string? BossSkipTurn { get; set; }

    // One request at a time per game (2026-10-31): the server moves a boss
    // while answering a GET, which mustn't overlap the player's own move.
    // V2GamesController takes it around every action on this game.
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public string? PlayerIdFor(string? token) =>
        token is null ? null : Seats.FirstOrDefault(s => FixedTimeEquals(s.Token, token))?.PlayerId;

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
