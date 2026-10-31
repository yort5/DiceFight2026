using DiceFight.Api;
using DiceFight.Api.Accounts;
using DiceFight.Api.Controllers;
using DiceFight.V2.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiceFight.Api.Tests;

// Boss battles (2026-10-31): a ladder of bosses from Team Builder links,
// each opened by beating the one before, paying points, coins and dice.
public class BossBattleTests
{
    private static V2GamesController Games(V2GameStore store, PlayerAccount? player, string? seatToken = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        if (seatToken is not null) http.Request.Headers["X-Seat-Token"] = seatToken;
        if (player is not null) http.SetPlayer(player);
        return new(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static PlayersController Players(V2GameStore store, PlayerAccount player)
    {
        var http = new DefaultHttpContext();
        http.SetPlayer(player);
        return new(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

    private static V2GameSession Fight(V2GameStore store, PlayerAccount me, string bossId)
    {
        var created = Ok(Games(store, me).Create(new CreateV2GameRequest("Wolf", BossId: bossId)));
        return store.GetSession(created.Game.GameId);
    }

    private static void End(V2GameStore store, V2GameSession session, bool won)
    {
        (won ? session.State.PlayerTwo : session.State.PlayerOne).Life = 0;
        session.State.CheckGameOver();
        store.Accounts!.AfterMove(session);
    }

    [Fact]
    public void Every_Boss_Builds_A_Legal_Team_With_Rewards_That_Exist()
    {
        Assert.NotEmpty(DiceKingdomBosses.Ladder);
        Assert.Equal(DiceKingdomBosses.Ladder.Count, DiceKingdomBosses.Ladder.Select(b => b.Id).Distinct().Count());
        foreach (var boss in DiceKingdomBosses.Ladder)
        {
            var player = V2GamesController.BuildBoss(boss); // throws on a bad link
            Assert.Equal(boss.Name, player.Name);
            Assert.NotEmpty(player.TeamCardIds);
            Assert.True(boss.Style.RushCardId is null || DiceKingdomConfig.Catalog.ContainsKey(boss.Style.RushCardId), boss.Id);
            foreach (var drop in new[] { boss.FirstWin, boss.Rematch }.SelectMany(r => r?.Drops ?? []))
            {
                Assert.InRange(drop.Chance, 0, 1);
                Assert.True(drop.CardId is null != drop.Rarity is null, $"{boss.Id}: a drop names a card or a rarity");
                if (drop.CardId is not null) Assert.True(DiceKingdomConfig.Catalog.ContainsKey(drop.CardId), drop.CardId);
            }
        }
    }

    [Fact]
    public void A_Team_Link_Reads_With_Or_Without_The_Site_And_Encoding()
    {
        var full = DiceKingdomBosses.ParseTeamLink(
            "https://example.run.app/teambuilder?cards=4xDK-CLAW-01;3xDK-ACT-01&tardigrades=4xClaw%3B4xEye&champion=Wolf");
        Assert.Equal(4, full.Cards["DK-CLAW-01"]);
        Assert.Equal(3, full.Cards["DK-ACT-01"]);
        Assert.Equal(new Dictionary<string, int> { ["Claw"] = 4, ["Eye"] = 4 }, full.Tardigrades);
        Assert.Equal("Wolf", full.ChampionId);

        var bare = DiceKingdomBosses.ParseTeamLink("cards=2xDK-CLAW-01");
        Assert.Equal(8, bare.Tardigrades["Claw"]); // the builder's default
        Assert.Null(bare.ChampionId);

        Assert.Throws<ArgumentException>(() => DiceKingdomBosses.ParseTeamLink("/teambuilder?champion=Wolf"));
        Assert.Throws<ArgumentException>(() => DiceKingdomBosses.ParseTeamLink("cards=fourxDK-CLAW-01"));
    }

    [Fact]
    public void Bosses_Open_In_Order_And_Pay_First_Wins_Then_Rematches()
    {
        var store = new V2GameStore(accounts: new PlayerAccounts(null, NullLogger<PlayerAccounts>.Instance));
        var (me, _) = store.Accounts!.Register(null);
        var first = DiceKingdomBosses.Ladder[0];
        var second = DiceKingdomBosses.Ladder[1];

        var refused = Games(store, me).Create(new CreateV2GameRequest("Wolf", BossId: second.Id));
        Assert.IsType<BadRequestObjectResult>(refused.Result);
        Assert.IsType<BadRequestObjectResult>(Games(store, null).Create(new CreateV2GameRequest("Wolf", BossId: first.Id)).Result);

        var ladder = Ok(Players(store, me).Bosses());
        Assert.Equal(["open", "locked"], ladder.Bosses.Take(2).Select(b => b.Status));

        // A loss counts, pays nothing, opens nothing.
        var lost = Fight(store, me, first.Id);
        Assert.Equal(first.Name, lost.State.PlayerTwo.Name);
        Assert.Contains("teamB", lost.BotSeats); // the computer's from the start
        End(store, lost, won: false);
        ladder = Ok(Players(store, me).Bosses());
        Assert.Equal((0, 1, "open"), (ladder.Bosses[0].Wins, ladder.Bosses[0].Losses, ladder.Bosses[0].Status));
        Assert.False(Assert.Single(ladder.Results).Won);

        var coins = Ok(Players(store, me).Collection()).Coins;
        var won = Fight(store, me, first.Id);
        store.Accounts.BotSeat(won, "teamA"); // asking the bot about your own seat doesn't make it the computer's
        End(store, won, won: true);
        store.Accounts.AfterMove(won); // settled once, however often the game is touched
        ladder = Ok(Players(store, me).Bosses());
        Assert.Equal(["beaten", "open"], ladder.Bosses.Take(2).Select(b => b.Status));
        Assert.Equal(first.FirstWin.Points, ladder.Points);
        Assert.Equal(coins + first.FirstWin.Coins, Ok(Players(store, me).Collection()).Coins);
        var result = ladder.Results[0];
        Assert.Equal((won.Id, true, true), (result.GameId, result.Won, result.FirstWin));
        Assert.Equal("boss", Ok(Players(store, me).Me()).Games.Single(g => g.GameId == won.Id).Mode);

        End(store, Fight(store, me, first.Id), won: true);
        ladder = Ok(Players(store, me).Bosses());
        Assert.Equal(first.FirstWin.Points + (first.Rematch?.Points ?? 0), ladder.Points);
        Assert.False(ladder.Results[0].FirstWin);

        // The second boss's first win drops a die (the sample's is certain).
        var dice = Ok(Players(store, me).Collection()).Dice.Values.Sum();
        End(store, Fight(store, me, second.Id), won: true);
        var paid = Ok(Players(store, me).Bosses()).Results[0];
        Assert.Equal(second.FirstWin.Drops?.Count(d => d.Chance >= 1) ?? 0, paid.Dice.Count);
        Assert.Equal(dice + paid.Dice.Count, Ok(Players(store, me).Collection()).Dice.Values.Sum());
    }

    [Fact]
    public void Linking_A_Device_Keeps_One_First_Win_Per_Boss()
    {
        var store = new V2GameStore(accounts: new PlayerAccounts(null, NullLogger<PlayerAccounts>.Instance));
        var accounts = store.Accounts!;
        var (laptop, _) = accounts.Register(null);
        var (phone, _) = accounts.Register(null);
        var first = DiceKingdomBosses.Ladder[0];
        End(store, Fight(store, laptop, first.Id), won: true);
        End(store, Fight(store, phone, first.Id), won: true);

        accounts.SetName(laptop, "Troy");
        var (code, _) = accounts.NewLinkCode(laptop);
        accounts.RedeemLinkCode(phone, code, store.Sessions, store.Lobbies);

        var ladder = Ok(Players(store, laptop).Bosses());
        Assert.Equal(first.FirstWin.Points, ladder.Points);
        Assert.Equal(2, ladder.Bosses[0].Wins);
    }
}
