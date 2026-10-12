using DiceFight.Api.Accounts;
using DiceFight.V2.Data;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Controllers;

// Players (Accounts/PlayerAccount.cs): a browser registers once and sends
// its token as X-Player-Token from then on. "me" is whoever that token
// belongs to - an anonymous device, or a named account on several devices.
[ApiController]
[Route("api/players")]
public sealed class PlayersController(V2GameStore store) : ControllerBase
{
    private PlayerAccounts Accounts => store.Accounts ?? throw new InvalidOperationException("Players aren't set up on this server.");

    [HttpPost]
    public ActionResult<RegisteredPlayerDto> Register()
    {
        var (player, token) = Accounts.Register(PlayerAccounts.DeviceLabel(Request.Headers.UserAgent.ToString()));
        return Ok(new RegisteredPlayerDto(player.Id, token));
    }

    [HttpGet("me")]
    public ActionResult<PlayerDto> Me()
    {
        var player = RequirePlayer();
        Accounts.Prune(player, IsLive);
        return Ok(Describe(player));
    }

    [HttpPut("me/name")]
    public ActionResult<PlayerDto> SetName([FromBody] SetPlayerNameRequest request)
    {
        var player = RequirePlayer();
        Accounts.SetName(player, request.Name);
        return Ok(Describe(player));
    }

    [HttpPost("me/link-code")]
    public ActionResult<LinkCodeDto> LinkCode()
    {
        var (code, expires) = Accounts.NewLinkCode(RequirePlayer());
        return Ok(new LinkCodeDto(code, expires));
    }

    // This device joins the account that showed the code.
    [HttpPost("me/link")]
    public ActionResult<PlayerDto> Link([FromBody] LinkDeviceRequest request)
    {
        var target = Accounts.RedeemLinkCode(RequirePlayer(), request.Code, store.Sessions, store.Lobbies);
        HttpContext.SetPlayer(target, HttpContext.PlayerDevice());
        Accounts.Prune(target, IsLive);
        return Ok(Describe(target));
    }

    // Signing a device out, this one included.
    [HttpDelete("me/devices/{deviceId}")]
    public ActionResult<PlayerDto> RemoveDevice(string deviceId)
    {
        var player = RequirePlayer();
        Accounts.RemoveDevice(player, deviceId);
        return Ok(Describe(player));
    }

    // Coins and dice from packs (V2/Data/DiceKingdomPacks.cs). Asking is
    // also what gives the day's coins, like /me.
    [HttpGet("me/collection")]
    public ActionResult<CollectionDto> Collection()
    {
        var (collection, daily) = Accounts.Wallet(RequirePlayer());
        return Ok(DescribeCollection(collection, daily));
    }

    [HttpPost("me/packs")]
    public ActionResult<OpenedPackDto> OpenPack()
    {
        var player = RequirePlayer();
        var cards = Accounts.OpenPack(player);
        var collection = player.Collection!;
        // A card is new if this pack brought every die of it the player has.
        var pulled = cards.Select(id => new PulledCardDto(
            id, DiceKingdomPacks.Rarities[id].ToString(), collection.Dice[id], collection.Dice[id] == cards.Count(c => c == id))).ToList();
        return Ok(new OpenedPackDto(pulled, DescribeCollection(collection, 0)));
    }

    private static CollectionDto DescribeCollection(PlayerCollection c, int daily) => new(
        c.Coins, daily, DiceKingdomPacks.DailyCoins, DiceKingdomPacks.PackPrice, c.PacksOpened, c.Dice,
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private PlayerAccount RequirePlayer() =>
        HttpContext.Player() ?? throw new SeatRequiredException($"A valid {PlayerAccounts.TokenHeader} is required.");

    private bool IsLive(string gameId) => store.HasGame(gameId) || store.GetLobby(gameId) is not null;

    private PlayerDto Describe(PlayerAccount player)
    {
        var thisDevice = HttpContext.PlayerDevice()?.Id;
        var (collection, daily) = Accounts.Wallet(player);
        var games = player.Games
            .OrderByDescending(g => g.StartedUtc)
            .Select(g => new PlayedGameDto(
                g.GameId,
                g.Mode,
                g.Result is not null || g.EndedUtc is not null ? "finished" : store.GetLobby(g.GameId) is not null ? "waiting" : "playing",
                g.StartedUtc,
                g.EndedUtc,
                g.Result,
                g.WinnerSeatId,
                g.OpponentName,
                g.Seats.Select(s => new PlayedSeatDto(s.SeatId, s.ChampionId, s.Token, s.Bot)).ToList()))
            .ToList();
        return new PlayerDto(
            player.Id,
            player.Name,
            player.Devices.Select(d => new PlayerDeviceDto(d.Id, d.Label, d.AddedUtc, d.LastSeenUtc, d.Id == thisDevice)).ToList(),
            games,
            collection.Coins,
            daily);
    }
}

public sealed record RegisteredPlayerDto(string PlayerId, string Token);
public sealed record SetPlayerNameRequest(string? Name);
public sealed record LinkDeviceRequest(string Code);
public sealed record LinkCodeDto(string Code, DateTime ExpiresUtc);
// DailyCoins: coins just given for today's first visit (0 if already had).
public sealed record PlayerDto(string Id, string? Name, IReadOnlyList<PlayerDeviceDto> Devices, IReadOnlyList<PlayedGameDto> Games, int Coins, int DailyCoins);
public sealed record CollectionDto(
    int Coins, int DailyCoins, int DailyAmount, int PackPrice, int PacksOpened,
    IReadOnlyDictionary<string, int> Dice, DateTime NextDailyUtc);
// Owned: dice of it now, this pack's included. New: the first of it.
public sealed record PulledCardDto(string CardId, string Rarity, int Owned, bool New);
public sealed record OpenedPackDto(IReadOnlyList<PulledCardDto> Cards, CollectionDto Collection);
public sealed record PlayerDeviceDto(string Id, string? Label, DateTime AddedUtc, DateTime LastSeenUtc, bool ThisDevice);
public sealed record PlayedGameDto(
    string GameId,
    string Mode,
    string Status,
    DateTime StartedUtc,
    DateTime? EndedUtc,
    string? Result,
    string? WinnerSeatId,
    string? OpponentName,
    IReadOnlyList<PlayedSeatDto> Seats);
// Token: only on the caller's own seats - for resuming on this device.
public sealed record PlayedSeatDto(string SeatId, string? ChampionId, string? Token, bool Bot);
