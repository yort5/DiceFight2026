namespace DiceFight.Api.Accounts;

// Who's playing (2026-10-10, user: "work without being logged in, taking
// some kind of unique identifier from the device ... if that user wanted to
// keep a record of their plays or play the same account on different
// devices, they could make an account ... no password"). Every browser
// gets one of these, anonymously, the first time it opens Dice Kingdom. A
// name makes it an account; a link code shown on one device and typed on
// another makes the second device the same player (no email yet - the
// user's pick; an Email field and a mailed code can come later and sign in
// the same way, by adding a Device).
//
// Kept as one JSON file per player (PlayerAccounts.cs).
public sealed class PlayerAccount
{
    public required string Id { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    // Set = an account; null = an anonymous device.
    public string? Name { get; set; }
    public List<PlayerDevice> Devices { get; set; } = [];
    public List<PlayedGame> Games { get; set; } = [];
    // Coins and the dice pulled from packs (2026-10-12). Null until the
    // player's first visit after packs arrived, which grants the starting
    // coins (PlayerAccounts.Wallet).
    public PlayerCollection? Collection { get; set; }
    // This player was linked into another one; its devices and games moved
    // there, and its old tokens resolve to that one.
    public string? MergedInto { get; set; }
}

public sealed class PlayerCollection
{
    public int Coins { get; set; }
    // The UTC day the daily coins were last given, "2026-10-12".
    public string? LastDailyDate { get; set; }
    // Card id -> dice of it owned, past its DieLimit too (for trade-ins).
    public Dictionary<string, int> Dice { get; set; } = [];
    public int PacksOpened { get; set; }
    public int TradeIns { get; set; }
    // Champions whose starter roster has been added (each card at its
    // DieLimit), the first time the player plays one.
    public List<string> Starters { get; set; } = [];
}

// A browser holding a token "{playerId}.{secret}"; only a hash of the secret is kept.
public sealed class PlayerDevice
{
    public required string Id { get; init; }
    public required string SecretHash { get; init; }
    public DateTime AddedUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    // "Chrome on Android" - for telling devices apart on the account page.
    public string? Label { get; set; }
}

// One Dice Kingdom game this player held a seat in. The seat tokens are
// kept so the game can be resumed from any of the player's devices.
public sealed class PlayedGame
{
    public required string GameId { get; init; }
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; set; }
    // "computer", "online" or "local" (both seats on one screen).
    public string Mode { get; set; } = "local";
    public List<PlayedSeat> Seats { get; set; } = [];
    // "win" / "loss", once over - only when this player had exactly one
    // human seat. A local game has no result of its own.
    public string? Result { get; set; }
    public string? WinnerSeatId { get; set; }
    // The other player's account name, in an online game.
    public string? OpponentName { get; set; }
}

public sealed class PlayedSeat
{
    public required string SeatId { get; init; }
    public string? ChampionId { get; set; }
    // Only on this player's own seats.
    public string? Token { get; set; }
    public bool Bot { get; set; }
}
