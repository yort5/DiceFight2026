using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DiceFight.Api.Recording;
using DiceFight.V2.Data;

namespace DiceFight.Api.Accounts;

// Players (PlayerAccount.cs): registering a device, finding the player
// behind a token, naming, linking devices, and keeping each player's list
// of games current as they're played (V2GamesController calls the game
// hooks below).
//
// Stored as one JSON file per player in the same place game records go
// (GameRecords:Bucket or :LocalPath, under "dice-kingdom/players/"); with
// neither set, players live only in memory and are lost on a redeploy.
// Every player read is kept in memory - one Cloud Run instance, a hobby's
// worth of players. Writes queue and run in the background, like
// GameRecorder's. All changes to players happen under one lock.
public sealed class PlayerAccounts(IGameRecordSink? store, ILogger<PlayerAccounts> logger, TimeProvider? clock = null) : BackgroundService
{
    public const string TokenHeader = "X-Player-Token";
    public const string Prefix = "dice-kingdom/players/";

    private readonly ConcurrentDictionary<string, PlayerAccount> _players = new();
    private readonly Channel<(string Name, byte[] Json)> _writes = Channel.CreateUnbounded<(string, byte[])>();
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // --- Devices and tokens

    public (PlayerAccount Player, string Token) Register(string? label)
    {
        var player = new PlayerAccount { Id = RandomId(6) };
        var token = AddDevice(player, label);
        _players[player.Id] = player;
        // Not saved yet: an anonymous device with nothing played isn't worth
        // a file (ShouldKeep). If the server forgets it, the client just
        // registers again.
        return (player, token);
    }

    // The player and device behind a token, or null for an unknown or
    // forged one. Follows MergedInto, so a linked device's old token works.
    public async Task<(PlayerAccount Player, PlayerDevice Device)?> ResolveAsync(string token, CancellationToken cancel = default)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0) return null;
        var player = await LoadAsync(token[..dot], cancel);
        for (var hops = 0; player?.MergedInto is { } next && hops < 8; hops++) player = await LoadAsync(next, cancel);
        if (player is null || player.MergedInto is not null) return null;

        var hash = Hash(token[(dot + 1)..]);
        lock (_gate)
        {
            var device = player.Devices.FirstOrDefault(d =>
                CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(d.SecretHash), Encoding.ASCII.GetBytes(hash)));
            if (device is null) return null;
            // Seen-at is only worth a write now and then.
            if (DateTime.UtcNow - device.LastSeenUtc > TimeSpan.FromHours(6))
            {
                device.LastSeenUtc = DateTime.UtcNow;
                Save(player);
            }
            return (player, device);
        }
    }

    public PlayerAccount? Get(string? id) => id is not null && _players.TryGetValue(id, out var p) ? p : null;

    private async Task<PlayerAccount?> LoadAsync(string id, CancellationToken cancel)
    {
        if (_players.TryGetValue(id, out var cached)) return cached;
        if (store is null || !IsId(id)) return null;
        byte[]? json;
        try
        {
            json = await store.ReadAsync(FileName(id), cancel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't read player {Id}.", id);
            return null;
        }
        if (json is null) return null;
        var loaded = JsonSerializer.Deserialize<PlayerAccount>(json, Json);
        return loaded is null ? null : _players.GetOrAdd(id, loaded);
    }

    public void SetName(PlayerAccount player, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) name = null;
        else if (name.Length > 24) throw new InvalidOperationException("Names are at most 24 characters.");
        lock (_gate)
        {
            player.Name = name;
            Save(player);
        }
    }

    public void RemoveDevice(PlayerAccount player, string deviceId)
    {
        lock (_gate)
        {
            if (player.Devices.RemoveAll(d => d.Id == deviceId) == 0)
                throw new KeyNotFoundException("No such device on this account.");
            Save(player);
        }
    }

    // --- Linking a device (no email yet): a signed-in device shows a code,
    // another device types it in and becomes the same player. Codes are
    // short-lived, single-use and only in memory.

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O, 1/I
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (string PlayerId, DateTime ExpiresUtc)> _codes = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTime SinceUtc)> _misses = new();

    public (string Code, DateTime ExpiresUtc) NewLinkCode(PlayerAccount player)
    {
        if (player.Name is null) throw new InvalidOperationException("Pick a name for your account first.");
        foreach (var (c, v) in _codes)
            if (v.PlayerId == player.Id || v.ExpiresUtc < DateTime.UtcNow) _codes.TryRemove(c, out _);
        var code = new string(Enumerable.Range(0, 6).Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)]).ToArray());
        var expires = DateTime.UtcNow + CodeLifetime;
        _codes[code] = (player.Id, expires);
        return (code, expires);
    }

    // `caller` and everything it played move into the code's player.
    // `sessions`/`lobbies`: live games, whose seat owners move too.
    public PlayerAccount RedeemLinkCode(PlayerAccount caller, string code, IEnumerable<V2GameSession> sessions, IEnumerable<V2Lobby> lobbies)
    {
        // Ten wrong codes in ten minutes and this player waits.
        var misses = _misses.GetValueOrDefault(caller.Id);
        if (DateTime.UtcNow - misses.SinceUtc > CodeLifetime) misses = (0, DateTime.UtcNow);
        if (misses.Count >= 10) throw new InvalidOperationException("Too many wrong codes - try again in a few minutes.");

        code = new string(code.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (!_codes.TryRemove(code, out var entry) || entry.ExpiresUtc < DateTime.UtcNow || Get(entry.PlayerId) is not { } target)
        {
            _misses[caller.Id] = (misses.Count + 1, misses.SinceUtc);
            throw new InvalidOperationException("That code isn't valid - codes last 10 minutes and work once.");
        }
        if (target.Id == caller.Id) throw new InvalidOperationException("This device is already on that account.");

        lock (_gate)
        {
            target.Devices.AddRange(caller.Devices);
            foreach (var game in caller.Games)
            {
                var mine = target.Games.FirstOrDefault(g => g.GameId == game.GameId);
                if (mine is null) target.Games.Add(game);
                else foreach (var seat in game.Seats.Where(s => s.Token is not null)) Own(mine, seat.SeatId).Token = seat.Token;
            }
            foreach (var session in sessions)
                foreach (var (seat, owner) in session.SeatOwners)
                    if (owner == caller.Id) session.SeatOwners[seat] = target.Id;
            foreach (var lobby in lobbies)
                if (lobby.HostOwnerId == caller.Id) lobby.HostOwnerId = target.Id;
            if (caller.Collection is { } theirs)
            {
                if (target.Collection is not { } mine) target.Collection = theirs;
                else
                {
                    // Both got the starting coins; the account keeps one
                    // grant's worth, or every throwaway device linked in
                    // would bring another 100.
                    mine.Coins = Math.Max(0, mine.Coins + theirs.Coins - DiceKingdomPacks.StartingCoins);
                    foreach (var (card, count) in theirs.Dice) mine.Dice[card] = mine.Dice.GetValueOrDefault(card) + count;
                    mine.PacksOpened += theirs.PacksOpened;
                    mine.TradeIns += theirs.TradeIns;
                    // A starter both had stays counted twice - those dice
                    // were really added to each.
                    foreach (var champion in theirs.Starters.Except(mine.Starters)) mine.Starters.Add(champion);
                    if (string.CompareOrdinal(theirs.LastDailyDate, mine.LastDailyDate) > 0) mine.LastDailyDate = theirs.LastDailyDate;
                }
            }
            if (caller.Bosses is { } theirBosses)
            {
                if (target.Bosses is not { } mine) target.Bosses = theirBosses;
                else
                {
                    // A boss both had beaten paid its first-win Points to
                    // each; the account keeps one of them.
                    mine.Points += theirBosses.Points;
                    foreach (var (bossId, other) in theirBosses.Records)
                    {
                        if (!mine.Records.TryGetValue(bossId, out var record)) { mine.Records[bossId] = other; continue; }
                        if (record.Wins > 0 && other.Wins > 0) mine.Points -= DiceKingdomBosses.Find(bossId)?.FirstWin.Points ?? 0;
                        record.Wins += other.Wins;
                        record.Losses += other.Losses;
                        if (other.FirstWinUtc < record.FirstWinUtc || record.FirstWinUtc is null) record.FirstWinUtc = other.FirstWinUtc;
                    }
                }
            }
            caller.Devices = [];
            caller.Games = [];
            caller.Collection = null;
            caller.Bosses = null;
            caller.MergedInto = target.Id;
            Save(target);
            Save(caller, force: true);
        }
        return target;
    }

    // --- Coins and packs (DiceKingdomPacks)

    // The player's collection, made with the starting coins on first use,
    // and today's coins if they haven't had them yet (DailyCoins - how many
    // were just given, for the client to mention).
    public (PlayerCollection Collection, int DailyCoins) Wallet(PlayerAccount player)
    {
        var today = DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd");
        lock (_gate)
        {
            var given = 0;
            if (player.Collection is null)
                // A new player's first day is covered by the starting coins.
                Collection(player, today);
            else if (player.Collection.LastDailyDate != today)
            {
                player.Collection.Coins += given = DiceKingdomPacks.DailyCoins;
                player.Collection.LastDailyDate = today;
            }
            Save(player);
            return (player.Collection, given);
        }
    }

    // Made on first use. A player who played before packs existed gets the
    // starter rosters of the Champions they finished games with. Under _gate.
    private PlayerCollection Collection(PlayerAccount player, string? today = null)
    {
        if (player.Collection is { } existing) return existing;
        var collection = player.Collection = new PlayerCollection
        {
            Coins = DiceKingdomPacks.StartingCoins,
            LastDailyDate = today ?? DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd"),
        };
        foreach (var seat in player.Games.Where(g => g.EndedUtc is not null).SelectMany(g => g.Seats).Where(s => s.Token is not null && !s.Bot))
            AddStarter(player, seat.ChampionId);
        return collection;
    }

    // A Champion's starter roster joins the collection the first time the
    // player finishes a game with it, win or lose (user, 2026-10-12: the
    // starter roster is added to your collection) - every card at its
    // DieLimit, the dice that team plays with. Under _gate.
    private void AddStarter(PlayerAccount player, string? championId)
    {
        if (championId is null || !DiceKingdomConfig.CharactersByChampion.TryGetValue(championId, out var cards)) return;
        var collection = Collection(player);
        if (collection.Starters.Contains(championId)) return;
        collection.Starters.Add(championId);
        foreach (var card in cards)
            collection.Dice[card] = collection.Dice.GetValueOrDefault(card) + DiceKingdomConfig.Catalog[card].DieLimit;
    }

    // Trades dice of one rarity (TradeInCost of them) for a random die of
    // the next. `give` names each die, repeats for several of one card.
    public string TradeIn(PlayerAccount player, IReadOnlyList<string> give, Func<int, int>? roll = null)
    {
        lock (_gate)
        {
            var collection = Collection(player);
            var rarities = give.Select(id => DiceKingdomPacks.Rarities.TryGetValue(id, out var r) ? r : (CardRarity?)null).Distinct().ToList();
            if (rarities is not [{ } rarity]) throw new InvalidOperationException("Trade in dice of one rarity at a time.");
            if (!DiceKingdomPacks.TradeInCost.TryGetValue(rarity, out var cost)) throw new InvalidOperationException("Super Rares can't be traded up.");
            if (give.Count != cost) throw new InvalidOperationException($"Trading up takes {cost} {rarity} dice.");
            foreach (var group in give.GroupBy(id => id))
                if (collection.Dice.GetValueOrDefault(group.Key) < group.Count())
                    throw new InvalidOperationException($"You don't have {group.Count()} {DiceKingdomConfig.Catalog[group.Key].Name} dice.");

            foreach (var id in give)
                if (--collection.Dice[id] == 0) collection.Dice.Remove(id);
            var got = DiceKingdomPacks.TradeIn(rarity, collection.Dice, roll ?? RandomNumberGenerator.GetInt32);
            collection.Dice[got] = collection.Dice.GetValueOrDefault(got) + 1;
            collection.TradeIns++;
            Save(player);
            return got;
        }
    }

    // Buys and opens one pack: the coins go and its dice join the
    // collection in the same step. `roll(n)` picks from 0..n-1.
    public IReadOnlyList<string> OpenPack(PlayerAccount player, Func<int, int>? roll = null)
    {
        var (collection, _) = Wallet(player);
        lock (_gate)
        {
            if (collection.Coins < DiceKingdomPacks.PackPrice)
                throw new InvalidOperationException($"A pack costs {DiceKingdomPacks.PackPrice} coins - you have {collection.Coins}.");
            var cards = DiceKingdomPacks.Open(roll ?? RandomNumberGenerator.GetInt32);
            collection.Coins -= DiceKingdomPacks.PackPrice;
            collection.PacksOpened++;
            foreach (var card in cards) collection.Dice[card] = collection.Dice.GetValueOrDefault(card) + 1;
            Save(player);
            return cards;
        }
    }

    // --- Boss battles (DiceKingdomBosses)

    public bool BossOpen(PlayerAccount player, string bossId)
    {
        lock (_gate)
            return DiceKingdomBosses.IsOpen(bossId, id => player.Bosses?.Records.GetValueOrDefault(id)?.Wins > 0);
    }

    // Counts a finished boss battle and pays its reward: FirstWin the first
    // time this boss is beaten, Rematch after. Under _gate.
    private BossResult SettleBoss(PlayerAccount player, string bossId, bool won)
    {
        var progress = player.Bosses ??= new BossProgress();
        if (!progress.Records.TryGetValue(bossId, out var record)) progress.Records[bossId] = record = new BossRecord();
        if (!won)
        {
            record.Losses++;
            return new BossResult();
        }
        var result = new BossResult { Won = true, FirstWin = record.Wins == 0 };
        record.Wins++;
        record.FirstWinUtc ??= (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var boss = DiceKingdomBosses.Find(bossId);
        if ((result.FirstWin ? boss?.FirstWin : boss?.Rematch) is not { } reward) return result;
        var collection = Collection(player);
        progress.Points += result.Points = reward.Points;
        collection.Coins += result.Coins = reward.Coins;
        foreach (var drop in reward.Drops ?? [])
            if (DiceKingdomBosses.Roll(drop, collection.Dice, RandomNumberGenerator.GetInt32) is { } card)
            {
                collection.Dice[card] = collection.Dice.GetValueOrDefault(card) + 1;
                result.Dice.Add(card);
            }
        return result;
    }

    // --- Games. Each owner's PlayedGame is updated as the game goes:
    // started (Create: both seats are the creator's), opened and joined
    // (an online game), a seat played from someone else's device (an
    // invite link - that seat changes hands), the computer asked to play a
    // seat, and the end.

    public void GameCreated(V2GameSession session, PlayerAccount? creator)
    {
        if (creator is null) return;
        lock (_gate)
        {
            foreach (var seat in session.Seats) session.SeatOwners[seat.PlayerId] = creator.Id;
            Touch(session, creator);
        }
    }

    public void LobbyOpened(V2Lobby lobby, PlayerAccount? host)
    {
        if (host is null) return;
        lock (_gate)
        {
            lobby.HostOwnerId = host.Id;
            var hostSeat = lobby.Seats[0];
            var game = Entry(host, lobby.Id);
            game.Mode = "online";
            var seat = Own(game, hostSeat.PlayerId);
            seat.ChampionId = lobby.HostChampionId;
            seat.Token = hostSeat.Token;
            Save(host);
        }
    }

    public void LobbyJoined(V2Lobby lobby, V2GameSession session, PlayerAccount? guest)
    {
        lock (_gate)
        {
            if (lobby.HostOwnerId is { } host) session.SeatOwners[lobby.Seats[0].PlayerId] = host;
            if (guest is not null) session.SeatOwners[lobby.Seats[1].PlayerId] = guest.Id;
            foreach (var owner in Owners(session)) Touch(session, owner);
        }
    }

    // Someone acted from a seat. If it's a different player than the one
    // holding it, the seat is theirs now (an invite link, or a player's own
    // link opened on an unlinked device).
    public void SeatUsed(V2GameSession session, string seatId, PlayerAccount? player)
    {
        if (player is null) return;
        if (session.SeatOwners.TryGetValue(seatId, out var owner) && owner == player.Id) return;
        lock (_gate)
        {
            if (owner is not null && Get(owner) is { } previous && previous.Games.FirstOrDefault(g => g.GameId == session.Id) is { } theirs)
            {
                if (theirs.Seats.FirstOrDefault(s => s.SeatId == seatId) is { } seat) seat.Token = null;
                if (theirs.Seats.All(s => s.Token is null)) previous.Games.Remove(theirs);
                Save(previous);
            }
            session.SeatOwners[seatId] = player.Id;
            foreach (var o in Owners(session)) Touch(session, o);
        }
    }

    public void BotSeat(V2GameSession session, string seatId)
    {
        // A boss battle's seats are fixed from the start: the boss is the
        // computer, the other seat the player, whoever asks for a hint.
        if (session.BossId is not null) return;
        lock (_gate)
        {
            if (!session.BotSeats.Add(seatId)) return;
            foreach (var owner in Owners(session)) Touch(session, owner);
        }
    }

    // After every move: records the result once the game is over.
    public void AfterMove(V2GameSession session)
    {
        if (!session.State.IsGameOver || session.ResultRecorded || session.SeatOwners.IsEmpty) return;
        lock (_gate)
        {
            if (session.ResultRecorded) return;
            session.ResultRecorded = true;
            foreach (var owner in Owners(session)) Touch(session, owner);
        }
    }

    private IEnumerable<PlayerAccount> Owners(V2GameSession session) =>
        session.SeatOwners.Values.Distinct().Select(Get).OfType<PlayerAccount>();

    // Brings `owner`'s PlayedGame in line with the live game. Under _gate.
    private void Touch(V2GameSession session, PlayerAccount owner)
    {
        var state = session.State;
        var game = Entry(owner, session.Id);
        var players = new[] { state.PlayerOne, state.PlayerTwo };
        foreach (var p in players)
        {
            var seat = game.Seats.FirstOrDefault(s => s.SeatId == p.Id);
            if (seat is null) game.Seats.Add(seat = new PlayedSeat { SeatId = p.Id });
            seat.ChampionId = p.ChampionId;
            seat.Bot = session.BotSeats.Contains(p.Id);
            var mine = session.SeatOwners.GetValueOrDefault(p.Id) == owner.Id;
            seat.Token = mine ? session.Seats.First(s => s.PlayerId == p.Id).Token : null;

        }
        var owners = session.SeatOwners.Values.Distinct().Count();
        game.Mode = session.BossId is not null ? "boss" : session.BotSeats.Count > 0 ? "computer" : owners > 1 ? "online" : "local";
        game.BossId = session.BossId;
        var others = session.SeatOwners.Where(kv => kv.Value != owner.Id).Select(kv => Get(kv.Value)?.Name).FirstOrDefault(n => n is not null);
        if (others is not null) game.OpponentName = others;

        if (state.IsGameOver)
        {
            game.EndedUtc ??= DateTime.UtcNow;
            game.WinnerSeatId = state.WinnerId;
            var human = game.Seats.Where(s => s.Token is not null && !s.Bot).ToList();
            game.Result = human.Count == 1 && state.WinnerId is not null ? (human[0].SeatId == state.WinnerId ? "win" : "loss") : null;
            // Only now: which seat the computer played is known for sure
            // once it has asked for a move (BotSeat), not at the start.
            foreach (var seat in human) AddStarter(owner, seat.ChampionId);
            // Settled once: Touch runs again on later visits to the game.
            if (session.BossId is { } bossId && game.Result is not null && game.BossResult is null)
                game.BossResult = SettleBoss(owner, bossId, game.Result == "win");
        }
        Save(owner);
    }

    private static PlayedGame Entry(PlayerAccount owner, string gameId)
    {
        var game = owner.Games.FirstOrDefault(g => g.GameId == gameId);
        if (game is null)
        {
            owner.Games.Add(game = new PlayedGame { GameId = gameId });
            // Keep the newest 500.
            if (owner.Games.Count > 500) owner.Games.RemoveRange(0, owner.Games.Count - 500);
        }
        return game;
    }

    private static PlayedSeat Own(PlayedGame game, string seatId)
    {
        var seat = game.Seats.FirstOrDefault(s => s.SeatId == seatId);
        if (seat is null) game.Seats.Add(seat = new PlayedSeat { SeatId = seatId });
        return seat;
    }

    // Unfinished games the server no longer has (idle too long, or a
    // redeploy) can't be resumed and have no result - dropped.
    public void Prune(PlayerAccount player, Func<string, bool> isLive)
    {
        lock (_gate)
            if (player.Games.RemoveAll(g => g.Result is null && g.EndedUtc is null && !isLive(g.GameId)) > 0) Save(player);
    }

    // --- Storage

    private static bool ShouldKeep(PlayerAccount p) =>
        p.Name is not null || p.Games.Count > 0 || p.Devices.Count > 1 || p.Collection is { PacksOpened: > 0 } || p.Bosses is not null;

    private void Save(PlayerAccount player, bool force = false)
    {
        if (store is null || (!force && !ShouldKeep(player))) return;
        // Serialized now, under the lock - the player keeps changing.
        _writes.Writer.TryWrite((FileName(player.Id), JsonSerializer.SerializeToUtf8Bytes(player, Json)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (store is null)
            logger.LogWarning("Players are kept in memory only - set GameRecords:Bucket (or :LocalPath) to keep them.");
        else
            logger.LogInformation("Keeping players in {Store}.", store.Describe());
        try
        {
            // A write already under way finishes even as shutdown begins:
            // cancelling it would lose it, since it's already off the queue
            // StopAsync drains.
            await foreach (var (name, json) in _writes.Reader.ReadAllAsync(stoppingToken)) await WriteOne(name, json, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
    }

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
            await store!.WriteAsync(name, json, cancel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't save player {Name}.", name);
        }
    }

    private static string FileName(string id) => $"{Prefix}{id}.json";

    // --- Helpers

    private static string AddDevice(PlayerAccount player, string? label)
    {
        var secret = Base64Url(RandomNumberGenerator.GetBytes(24));
        player.Devices.Add(new PlayerDevice { Id = RandomId(4), SecretHash = Hash(secret), Label = label });
        return $"{player.Id}.{secret}";
    }

    private static string RandomId(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));
    private static bool IsId(string id) => id.Length is > 0 and <= 32 && id.All(Uri.IsHexDigit);
    private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // "Chrome on Android" from a User-Agent - only for telling devices apart.
    public static string? DeviceLabel(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return null;
        string? browser =
            userAgent.Contains("Edg/") ? "Edge"
            : userAgent.Contains("Firefox/") || userAgent.Contains("FxiOS/") ? "Firefox"
            : userAgent.Contains("Chrome/") || userAgent.Contains("CriOS/") ? "Chrome"
            : userAgent.Contains("Safari/") ? "Safari"
            : null;
        string? os =
            userAgent.Contains("iPhone") ? "iPhone"
            : userAgent.Contains("iPad") ? "iPad"
            : userAgent.Contains("Android") ? "Android"
            : userAgent.Contains("Windows") ? "Windows"
            : userAgent.Contains("Mac OS X") ? "Mac"
            : userAgent.Contains("CrOS") ? "Chromebook"
            : userAgent.Contains("Linux") ? "Linux"
            : null;
        return browser is null && os is null ? null : browser is null ? os : os is null ? browser : $"{browser} on {os}";
    }
}

// Finds the player behind X-Player-Token on API requests, for the
// controllers (HttpContext.Player()). A missing or unknown token is not an
// error here - games work without a player; PlayersController's own
// endpoints answer 401.
public sealed class PlayerTokenMiddleware(RequestDelegate next, PlayerAccounts players)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && context.Request.Headers[PlayerAccounts.TokenHeader].ToString() is { Length: > 0 } token
            && await players.ResolveAsync(token, context.RequestAborted) is { } found)
        {
            context.Items[ItemKey] = found.Player;
            context.Items[DeviceKey] = found.Device;
        }
        await next(context);
    }

    internal const string ItemKey = "DiceFight.Player";
    internal const string DeviceKey = "DiceFight.PlayerDevice";
}

public static class PlayerHttpContextExtensions
{
    public static PlayerAccount? Player(this HttpContext context) =>
        context.Items.TryGetValue(PlayerTokenMiddleware.ItemKey, out var p) ? p as PlayerAccount : null;

    public static PlayerDevice? PlayerDevice(this HttpContext context) =>
        context.Items.TryGetValue(PlayerTokenMiddleware.DeviceKey, out var d) ? d as PlayerDevice : null;

    // For tests that drive controllers directly.
    public static void SetPlayer(this HttpContext context, PlayerAccount player, PlayerDevice? device = null)
    {
        context.Items[PlayerTokenMiddleware.ItemKey] = player;
        context.Items[PlayerTokenMiddleware.DeviceKey] = device ?? player.Devices.FirstOrDefault();
    }
}
