using DiceFight.Api;
using DiceFight.Api.Accounts;
using DiceFight.Api.Controllers;
using DiceFight.Api.Recording;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiceFight.Api.Tests;

// Players (2026-10-10): anonymous per device, a name makes an account, a
// link code moves another device (and its games) onto it; each player's
// games are kept as they're played.
public class PlayerAccountsTests
{
    private static PlayerAccounts NewAccounts(IGameRecordSink? sink = null) => new(sink, NullLogger<PlayerAccounts>.Instance);

    private static HttpContext Http(PlayerAccount? player, string? seatToken = null, string method = "POST")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        if (seatToken is not null) http.Request.Headers["X-Seat-Token"] = seatToken;
        if (player is not null) http.SetPlayer(player);
        return http;
    }

    private static V2GamesController Games(V2GameStore store, PlayerAccount? player, string? seatToken = null, string method = "POST") =>
        new(store) { ControllerContext = new ControllerContext { HttpContext = Http(player, seatToken, method) } };

    private static PlayersController Players(V2GameStore store, PlayerAccount player) =>
        new(store) { ControllerContext = new ControllerContext { HttpContext = Http(player, method: "GET") } };

    private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

    private static void End(V2GameStore store, V2GameSession session, string loserSeat)
    {
        (loserSeat == "teamA" ? session.State.PlayerOne : session.State.PlayerTwo).Life = 0;
        session.State.CheckGameOver();
        store.Accounts!.AfterMove(session);
    }

    [Fact]
    public async Task A_Token_Finds_Its_Player_And_A_Forged_One_Does_Not()
    {
        var accounts = NewAccounts();
        var (player, token) = accounts.Register("Chrome on Android");

        var found = await accounts.ResolveAsync(token);
        Assert.Same(player, found!.Value.Player);
        Assert.Equal("Chrome on Android", found.Value.Device.Label);

        Assert.Null(await accounts.ResolveAsync(player.Id + ".not-the-secret"));
        Assert.Null(await accounts.ResolveAsync("nobody.secret"));
        Assert.Null(await accounts.ResolveAsync("no-dot-at-all"));
    }

    [Fact]
    public void A_Game_Against_The_Computer_Is_Kept_With_Its_Result()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var (me, _) = store.Accounts!.Register(null);

        var created = Ok(Games(store, me).Create(new CreateV2GameRequest("Wolf", "Armadillo")));
        var session = store.GetSession(created.Game.GameId);
        var botToken = created.Seats.Single(s => s.PlayerId == "teamB").Token;
        Games(store, me, botToken, "GET").BotDecision(session.Id, null);

        var playing = Assert.Single(Ok(Players(store, me).Me()).Games);
        Assert.Equal("computer", playing.Mode);
        Assert.Equal("playing", playing.Status);
        // Both tokens - this device drives the computer's seat too.
        Assert.All(playing.Seats, s => Assert.NotNull(s.Token));

        End(store, session, loserSeat: "teamB");
        var done = Assert.Single(Ok(Players(store, me).Me()).Games);
        Assert.Equal("finished", done.Status);
        Assert.Equal("win", done.Result);
    }

    [Fact]
    public void An_Online_Game_Is_A_Win_For_One_Player_And_A_Loss_For_The_Other()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var (host, _) = store.Accounts!.Register(null);
        var (guest, _) = store.Accounts.Register(null);
        store.Accounts.SetName(host, "Troy");
        store.Accounts.SetName(guest, "Sam");

        var opened = Ok(Games(store, host).Open(new OpenV2GameRequest("Wolf")));
        Assert.Equal("waiting", Assert.Single(Ok(Players(store, host).Me()).Games).Status);

        var guestToken = opened.Seats.Single(s => s.PlayerId == "teamB").Token;
        Games(store, guest, guestToken).Join(opened.GameId, new JoinV2GameRequest("Armadillo"));
        End(store, store.GetSession(opened.GameId), loserSeat: "teamA");

        var hostGame = Assert.Single(Ok(Players(store, host).Me()).Games);
        var guestGame = Assert.Single(Ok(Players(store, guest).Me()).Games);
        Assert.Equal(("online", "loss", "Sam"), (hostGame.Mode, hostGame.Result, hostGame.OpponentName));
        Assert.Equal(("online", "win", "Troy"), (guestGame.Mode, guestGame.Result, guestGame.OpponentName));
        // Each sees only their own seat's token.
        Assert.Equal(["teamA"], hostGame.Seats.Where(s => s.Token is not null).Select(s => s.SeatId));
        Assert.Equal(["teamB"], guestGame.Seats.Where(s => s.Token is not null).Select(s => s.SeatId));
    }

    [Fact]
    public void An_Invite_Link_Hands_That_Seat_To_Whoever_Plays_It()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var (host, _) = store.Accounts!.Register(null);
        var (friend, _) = store.Accounts.Register(null);

        var created = Ok(Games(store, host).Create(new CreateV2GameRequest("Wolf", "GoldenEagle")));
        var inviteToken = created.Seats.Single(s => s.PlayerId == "teamB").Token;
        Games(store, friend, inviteToken, "GET").Get(created.Game.GameId);

        var hostGame = Assert.Single(Ok(Players(store, host).Me()).Games);
        Assert.Equal("online", hostGame.Mode);
        Assert.Null(hostGame.Seats.Single(s => s.SeatId == "teamB").Token);
        Assert.Equal("online", Assert.Single(Ok(Players(store, friend).Me()).Games).Mode);
    }

    [Fact]
    public async Task A_Link_Code_Moves_Another_Device_And_Its_Games_Onto_The_Account()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var accounts = store.Accounts!;
        var (laptop, _) = accounts.Register("Chrome on Windows");
        var (phone, phoneToken) = accounts.Register("Safari on iPhone");
        var phoneGame = Ok(Games(store, phone).Create(new CreateV2GameRequest("Wolf", "GoldenEagle"))).Game.GameId;

        Assert.Throws<InvalidOperationException>(() => accounts.NewLinkCode(laptop)); // name it first
        accounts.SetName(laptop, "Troy");
        var (code, _) = accounts.NewLinkCode(laptop);
        Assert.Throws<InvalidOperationException>(() => accounts.RedeemLinkCode(phone, "WRONG1", store.Sessions, store.Lobbies));

        var linked = accounts.RedeemLinkCode(phone, code.ToLowerInvariant(), store.Sessions, store.Lobbies);

        Assert.Same(laptop, linked);
        Assert.Equal(2, laptop.Devices.Count);
        Assert.Contains(laptop.Games, g => g.GameId == phoneGame);
        // The phone's old token now means the laptop's player...
        Assert.Same(laptop, (await accounts.ResolveAsync(phoneToken))!.Value.Player);
        // ...and so does its seat in the live game.
        Assert.All(store.GetSession(phoneGame).SeatOwners.Values, owner => Assert.Equal(laptop.Id, owner));
        // Codes work once.
        Assert.Throws<InvalidOperationException>(() => accounts.RedeemLinkCode(phone, code, store.Sessions, store.Lobbies));
    }

    [Fact]
    public async Task Players_Survive_A_Restart_But_Unplayed_Anonymous_Devices_Are_Not_Written()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dicefight-players-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = NewAccounts(new LocalFolderSink(folder));
            await first.StartAsync(CancellationToken.None);
            var (named, namedToken) = first.Register(null);
            first.SetName(named, "Troy");
            var (_, anonymousToken) = first.Register(null);
            await first.StopAsync(CancellationToken.None);

            var second = NewAccounts(new LocalFolderSink(folder));
            Assert.Equal("Troy", (await second.ResolveAsync(namedToken))!.Value.Player.Name);
            Assert.Null(await second.ResolveAsync(anonymousToken));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1", "Safari on iPhone")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36 Edg/129.0", "Edge on Windows")]
    public void Devices_Get_A_Readable_Label(string userAgent, string label) =>
        Assert.Equal(label, PlayerAccounts.DeviceLabel(userAgent));
}
