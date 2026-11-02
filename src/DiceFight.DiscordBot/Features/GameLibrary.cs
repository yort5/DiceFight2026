using System.Collections.Concurrent;
using System.Text.Json;

namespace DiceFight.DiscordBot.Features;

// A shared shelf of board games that members can borrow (2026-11-02, user:
// "both the church bot and the board game bot could use commands to list
// board games available or checkout"). The rules, apart from Discord: what
// can be added, borrowed and returned, and by whom.
public sealed class GameLibrary
{
    public List<LibraryGame> Games { get; set; } = [];

    public sealed record Borrower(ulong UserId, string Name);

    // The game a typed name means: an exact match, else the only one
    // containing it. Null with a reason otherwise.
    public (LibraryGame? Game, string? Problem) Find(string name)
    {
        name = name.Trim();
        var exact = Games.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return (exact, null);
        var partial = Games.Where(g => g.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count switch
        {
            1 => (partial[0], null),
            0 => (null, $"No game called \"{name}\" in the library."),
            _ => (null, $"\"{name}\" could be {string.Join(", ", partial.Take(5).Select(g => g.Name))}{(partial.Count > 5 ? "..." : "")} - which one?"),
        };
    }

    // Each change returns its message and whether the library changed (so is saved).
    public (string Message, bool Changed) Add(string name, string? notes, Borrower by, DateTime now)
    {
        name = name.Trim();
        if (name.Length == 0) return ("A game needs a name.", false);
        if (name.Length > 100) return ("That name is too long (100 characters at most).", false);
        if (Games.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            return ($"**{name}** is already in the library.", false);
        Games.Add(new LibraryGame { Name = name, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), AddedBy = by.Name, AddedUtc = now });
        return ($"Added **{name}** to the library.", true);
    }

    public (string Message, bool Changed) Remove(string name)
    {
        var (game, problem) = Find(name);
        if (game is null) return (problem!, false);
        Games.Remove(game);
        return ($"Removed **{game.Name}** from the library.", true);
    }

    public (string Message, bool Changed) CheckOut(string name, Borrower who, DateTime now)
    {
        var (game, problem) = Find(name);
        if (game is null) return (problem!, false);
        if (game.Loan is { } loan)
            return (loan.UserId == who.UserId
                ? $"You already have **{game.Name}**."
                : $"**{game.Name}** is out with {loan.Name} (since {loan.SinceUtc:MMM d}).", false);
        game.Loan = new GameLoan { UserId = who.UserId, Name = who.Name, SinceUtc = now };
        return ($"**{game.Name}** is checked out to {who.Name}. Enjoy!", true);
    }

    // The borrower returns it; a library manager can return anyone's.
    public (string Message, bool Changed) Return(string name, Borrower who, bool manager)
    {
        var (game, problem) = Find(name);
        if (game is null) return (problem!, false);
        if (game.Loan is not { } loan) return ($"**{game.Name}** isn't checked out.", false);
        if (loan.UserId != who.UserId && !manager)
            return ($"**{game.Name}** is out with {loan.Name} - only they (or a library manager) can return it.", false);
        game.Loan = null;
        return (loan.UserId == who.UserId ? $"**{game.Name}** is back on the shelf. Thanks!" : $"**{game.Name}** returned for {loan.Name}.", true);
    }

    // One line per game, available first. `search` narrows it; `mine` only that person's loans.
    public IReadOnlyList<string> Lines(string? search = null, ulong? mine = null) => Games
        .Where(g => string.IsNullOrWhiteSpace(search) || g.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(g => mine is null || g.Loan?.UserId == mine)
        .OrderBy(g => g.Loan is null ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.Loan is { } loan
            ? $"~~{g.Name}~~ - out with {loan.Name} since {loan.SinceUtc:MMM d}"
            : $"**{g.Name}**{(g.Notes is { } notes ? $" - {notes}" : "")}")
        .ToList();
}

public sealed class LibraryGame
{
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public string? AddedBy { get; set; }
    public DateTime AddedUtc { get; set; }
    public GameLoan? Loan { get; set; }
}

public sealed class GameLoan
{
    public ulong UserId { get; set; }
    public string Name { get; set; } = "";
    public DateTime SinceUtc { get; set; }
}

// Libraries by id, one JSON file each (discord/libraries/<id>.json in the
// bucket). Changes go one at a time per library and are saved straight away.
public sealed class GameLibraryStore(IBotDataStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<string, GameLibrary> _loaded = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public bool Durable => store.Durable;

    public static string FileName(string libraryId) => $"discord/libraries/{libraryId}.json";

    // Runs `change` on the library and saves it if it says so; returns its message.
    public async Task<T> UseAsync<T>(string libraryId, Func<GameLibrary, (T Result, bool Changed)> change)
    {
        var gate = _gates.GetOrAdd(libraryId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!_loaded.TryGetValue(libraryId, out var library))
            {
                var data = await store.ReadAsync(FileName(libraryId));
                library = data is null ? new GameLibrary() : JsonSerializer.Deserialize<GameLibrary>(data, Json) ?? new GameLibrary();
                _loaded[libraryId] = library;
            }
            var (result, changed) = change(library);
            if (changed) await store.WriteAsync(FileName(libraryId), JsonSerializer.SerializeToUtf8Bytes(library, Json));
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
