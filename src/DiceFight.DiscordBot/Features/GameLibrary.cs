using System.Collections.Concurrent;
using System.Text.Json;

namespace DiceFight.DiscordBot.Features;

// Board games the members own, to lend and sell (2026-11-02, user: "add a
// board game of yours to the list of available board games; request to
// borrow a board game from someone; list the available board games that
// people own; list the available board games that aren't currently being
// borrowed; list a board game for sale"). Every game is one member's copy -
// several can own the same title. The rules, apart from Discord.
public sealed class GameLibrary
{
    public List<OwnedGame> Games { get; set; } = [];

    public sealed record Member(ulong UserId, string Name);

    // Each change returns its message and whether the library changed (so is saved).
    public readonly record struct Outcome(string Message, bool Changed, OwnedGame? Game = null)
    {
        public static Outcome No(string message) => new(message, false);
    }

    public OwnedGame? ById(string? id) => id is null ? null : Games.FirstOrDefault(g => g.Id == id);

    // The copy someone means: its id (autocomplete hands those back), else
    // a name - exact, or the only one containing it - among `candidates`.
    public (OwnedGame? Game, string? Problem) Find(string typed, IEnumerable<OwnedGame> candidates)
    {
        typed = typed.Trim();
        var pool = candidates.ToList();
        if (pool.FirstOrDefault(g => g.Id == typed) is { } byId) return (byId, null);
        var exact = pool.Where(g => string.Equals(g.Name, typed, StringComparison.OrdinalIgnoreCase)).ToList();
        var matches = exact.Count > 0 ? exact : pool.Where(g => g.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, $"No game matching \"{typed}\" here."),
            _ => (null, $"\"{typed}\" could be {string.Join(", ", matches.Take(5).Select(g => g.Label))}{(matches.Count > 5 ? "..." : "")} - pick one from the list as you type."),
        };
    }

    public IEnumerable<OwnedGame> OwnedBy(ulong userId) => Games.Where(g => g.Owner.UserId == userId);

    public Outcome Add(string name, string? notes, Member owner, DateTime now)
    {
        name = name.Trim();
        if (name.Length == 0) return Outcome.No("A game needs a name.");
        if (name.Length > 100) return Outcome.No("That name is too long (100 characters at most).");
        if (OwnedBy(owner.UserId).Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Outcome.No($"You already have **{name}** on the list.");
        var game = new OwnedGame { Id = NewId(), Name = name, Notes = Clean(notes), Owner = owner, AddedUtc = now };
        Games.Add(game);
        return new($"Added your **{name}** to the list.", true, game);
    }

    // The owner takes it off (sold, gone); a manager can take anyone's.
    public Outcome Remove(string typed, Member who, bool manager)
    {
        var (game, problem) = Find(typed, manager ? Games : OwnedBy(who.UserId));
        if (game is null) return Outcome.No(problem!);
        Games.Remove(game);
        return new($"Took {game.Label} off the list.", true, game);
    }

    // Asking the owner. The request stays until they lend it or decline.
    public Outcome RequestBorrow(string typed, Member who, DateTime now)
    {
        var (game, problem) = Find(typed, Games.Where(g => g.Owner.UserId != who.UserId));
        if (game is null)
            return Outcome.No(OwnedBy(who.UserId).Any(g => g.Name.Contains(typed.Trim(), StringComparison.OrdinalIgnoreCase)) && problem!.StartsWith("No ")
                ? "That's your own game." : problem!);
        if (game.Loan is { } loan)
            return Outcome.No(loan.Borrower.UserId == who.UserId ? $"You already have {game.Label}." : $"{game.Label} is out with {loan.Borrower.Name} right now.");
        if (game.Requests.Any(r => r.Requester.UserId == who.UserId))
            return Outcome.No($"You've already asked {game.Owner.Name} for **{game.Name}**.");
        game.Requests.Add(new BorrowRequest { Requester = who, SinceUtc = now });
        return new($"{who.Name} would like to borrow {game.Owner.Name}'s **{game.Name}**.", true, game);
    }

    // The owner answers a request.
    public Outcome Lend(string gameId, ulong requesterId, Member who, DateTime now)
    {
        if (ById(gameId) is not { } game) return Outcome.No("That game isn't on the list any more.");
        if (game.Owner.UserId != who.UserId) return Outcome.No($"Only {game.Owner.Name} can lend their **{game.Name}**.");
        if (game.Requests.FirstOrDefault(r => r.Requester.UserId == requesterId) is not { } request)
            return Outcome.No("That request has already been answered.");
        if (game.Loan is { } loan) return Outcome.No($"**{game.Name}** is still out with {loan.Borrower.Name} - mark it returned first.");
        game.Requests.Remove(request);
        game.Loan = new Loan { Borrower = request.Requester, SinceUtc = now };
        return new($"{game.Owner.Name} lent **{game.Name}** to {request.Requester.Name}. Enjoy!", true, game);
    }

    public Outcome Decline(string gameId, ulong requesterId, Member who)
    {
        if (ById(gameId) is not { } game) return Outcome.No("That game isn't on the list any more.");
        if (game.Owner.UserId != who.UserId) return Outcome.No($"Only {game.Owner.Name} can answer that.");
        if (game.Requests.RemoveAll(r => r.Requester.UserId == requesterId) == 0) return Outcome.No("That request has already been answered.");
        return new($"{game.Owner.Name} can't lend **{game.Name}** right now.", true, game);
    }

    // Back home: the borrower or the owner says so (or a manager).
    public Outcome Return(string typed, Member who, bool manager)
    {
        var (game, problem) = Find(typed, Games.Where(g => g.Loan is not null
            && (manager || g.Loan.Borrower.UserId == who.UserId || g.Owner.UserId == who.UserId)));
        if (game is null) return Outcome.No(problem!.StartsWith("No ") ? $"No game matching \"{typed.Trim()}\" is out with you or from you." : problem);
        var borrower = game.Loan!.Borrower;
        game.Loan = null;
        return new($"**{game.Name}** is back with {game.Owner.Name} (borrowed by {borrower.Name}).", true, game);
    }

    public Outcome Sell(string typed, string? price, string? notes, Member who, DateTime now)
    {
        var (game, problem) = Find(typed, OwnedBy(who.UserId));
        if (game is null) return Outcome.No(problem!.StartsWith("No ") ? $"You don't have a game matching \"{typed.Trim()}\" on the list - `/games add` it first." : problem);
        var update = game.Sale is not null;
        game.Sale = new SaleListing { Price = Clean(price), Notes = Clean(notes), ListedUtc = now };
        return new($"{(update ? "Updated" : "Listed")} your **{game.Name}** for sale{(game.Sale.Price is { } p ? $" ({p})" : "")}.", true, game);
    }

    public Outcome Unsell(string typed, Member who, bool manager)
    {
        var (game, problem) = Find(typed, Games.Where(g => g.Sale is not null && (manager || g.Owner.UserId == who.UserId)));
        if (game is null) return Outcome.No(problem!.StartsWith("No ") ? $"You have no game matching \"{typed.Trim()}\" for sale." : problem);
        game.Sale = null;
        return new($"**{game.Name}** is no longer for sale.", true, game);
    }

    public enum View { All, Available, ForSale }

    // One line per copy, sorted by name. `search` narrows by name.
    public IReadOnlyList<string> Lines(View view, string? search = null) => Games
        .Where(g => string.IsNullOrWhiteSpace(search) || g.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(g => view switch { View.Available => g.Loan is null, View.ForSale => g.Sale is not null, _ => true })
        .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Owner.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => view == View.ForSale ? SaleLine(g) : Line(g))
        .ToList();

    // The `/games mine` summary: what you own, have borrowed, and have asked for.
    public IReadOnlyList<(string Heading, IReadOnlyList<string> Lines)> Mine(ulong userId) =>
    [
        ("Your games", OwnedBy(userId).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).Select(g =>
            $"{Linked(g.Name)}{(g.Loan is { } l ? $" - lent to {l.Borrower.Name} since {l.SinceUtc:MMM d}" : "")}{(g.Sale is not null ? " - for sale" : "")}" +
            (g.Requests.Count > 0 ? $" - asked for by {string.Join(", ", g.Requests.Select(r => r.Requester.Name))}" : "")).ToList()),
        ("Borrowed", Games.Where(g => g.Loan?.Borrower.UserId == userId).Select(g => $"{g.Owner.Name}'s {Linked(g.Name)} since {g.Loan!.SinceUtc:MMM d}").ToList()),
        ("Waiting to hear", Games.Where(g => g.Requests.Any(r => r.Requester.UserId == userId)).Select(g => $"{g.Owner.Name}'s {Linked(g.Name)}").ToList()),
    ];

    private static string Line(OwnedGame g) =>
        $"{Linked(g.Name)} - {g.Owner.Name}" +
        (g.Loan is { } loan ? $" - *on loan to {loan.Borrower.Name}*" : "") +
        (g.Sale is not null ? " - for sale" : "") +
        (g.Notes is { } notes ? $" - {notes}" : "");

    private static string SaleLine(OwnedGame g) =>
        $"{Linked(g.Name)} - {g.Owner.Name}" + (g.Sale!.Price is { } price ? $" - **{price}**" : "") + (g.Sale.Notes is { } notes ? $" - {notes}" : "");

    // The name, linked to a BoardGameGeek search for it - player counts and
    // pictures one click away, without their (registration-only) API.
    public static string Linked(string name) =>
        $"[{name.Replace("[", "(").Replace("]", ")")}](https://boardgamegeek.com/geeksearch.php?action=search&objecttype=boardgame&q={Uri.EscapeDataString(name)})";

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string NewId() => Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()[..6]);
}

public sealed class OwnedGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Notes { get; set; }
    public GameLibrary.Member Owner { get; set; } = new(0, "");
    public DateTime AddedUtc { get; set; }
    public Loan? Loan { get; set; }
    public List<BorrowRequest> Requests { get; set; } = [];
    public SaleListing? Sale { get; set; }

    // "Wingspan (Ann's)" - which copy, where several own one.
    public string Label => $"{Owner.Name}'s **{Name}**";
}

public sealed class Loan
{
    public GameLibrary.Member Borrower { get; set; } = new(0, "");
    public DateTime SinceUtc { get; set; }
}

public sealed class BorrowRequest
{
    public GameLibrary.Member Requester { get; set; } = new(0, "");
    public DateTime SinceUtc { get; set; }
}

public sealed class SaleListing
{
    public string? Price { get; set; }
    public string? Notes { get; set; }
    public DateTime ListedUtc { get; set; }
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

    // Runs `change` on the library and saves it if it says so.
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

    public Task<GameLibrary.Outcome> ChangeAsync(string libraryId, Func<GameLibrary, GameLibrary.Outcome> change) =>
        UseAsync(libraryId, library => { var outcome = change(library); return (outcome, outcome.Changed); });
}
