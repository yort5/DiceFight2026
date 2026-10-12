using DiceFight.Api.Accounts;
using DiceFight.Api.Controllers;
using DiceFight.V2.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiceFight.Api.Tests;

// Coins and random packs (2026-10-12, V2/Data/DiceKingdomPacks.cs).
public class PackTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static PlayerAccounts NewAccounts(Clock? clock = null) => new(null, NullLogger<PlayerAccounts>.Instance, clock);

    private static PlayersController Players(V2GameStore store, PlayerAccount player)
    {
        var http = new DefaultHttpContext();
        http.SetPlayer(player);
        return new(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

    [Fact]
    public void A_New_Player_Starts_With_100_Coins_And_Gets_5_More_Each_Day()
    {
        var clock = new Clock();
        var accounts = NewAccounts(clock);
        var (player, _) = accounts.Register(null);

        var (collection, daily) = accounts.Wallet(player);
        Assert.Equal((100, 0), (collection.Coins, daily));
        clock.Now = clock.Now.AddHours(8); // 23:00, same day
        Assert.Equal(0, accounts.Wallet(player).DailyCoins);
        clock.Now = clock.Now.AddHours(2); // past midnight UTC
        Assert.Equal(5, accounts.Wallet(player).DailyCoins);
        Assert.Equal(0, accounts.Wallet(player).DailyCoins);
        Assert.Equal(105, player.Collection!.Coins);
    }

    [Fact]
    public void Opening_A_Pack_Costs_10_And_Adds_Its_5_Dice()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var (player, _) = store.Accounts!.Register(null);

        var pack = Ok(Players(store, player).OpenPack());

        Assert.Equal(5, pack.Cards.Count);
        Assert.Equal(["Common", "Common", "Common", "Uncommon"], pack.Cards.Take(4).Select(c => c.Rarity));
        Assert.All(pack.Cards, c => Assert.True(c.New));
        Assert.Equal(90, pack.Collection.Coins);
        Assert.Equal(1, pack.Collection.PacksOpened);
        Assert.Equal(5, pack.Collection.Dice.Values.Sum());
    }

    [Fact]
    public void Ten_Packs_Spend_The_Starting_Coins_And_An_Eleventh_Is_Refused()
    {
        var accounts = NewAccounts();
        var (player, _) = accounts.Register(null);
        for (var i = 0; i < 10; i++) accounts.OpenPack(player);

        var refused = Assert.Throws<InvalidOperationException>(() => accounts.OpenPack(player));
        Assert.Contains("10 coins", refused.Message);
        Assert.Equal(0, player.Collection!.Coins);
        Assert.Equal(50, player.Collection.Dice.Values.Sum());
    }

    [Fact]
    public void A_Card_Already_Owned_Is_Not_New()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var (player, _) = store.Accounts!.Register(null);
        // Always each slot's first rarity and the first card left of it.
        var first = store.Accounts.OpenPack(player, _ => 0);
        Assert.Equal(5, first.Distinct().Count());

        var again = Ok(Players(store, player).OpenPack());

        Assert.All(again.Cards, c => Assert.Equal(!first.Contains(c.CardId), c.New));
        Assert.All(again.Cards, c => Assert.Equal(player.Collection!.Dice[c.CardId], c.Owned));
        Assert.Equal(10, player.Collection!.Dice.Values.Sum());
    }

    [Fact]
    public void Linking_A_Device_Brings_Its_Dice_But_Not_A_Second_Starting_Grant()
    {
        var clock = new Clock();
        var store = new V2GameStore(accounts: NewAccounts(clock));
        var accounts = store.Accounts!;
        var (laptop, _) = accounts.Register(null);
        var (phone, _) = accounts.Register(null);
        accounts.SetName(laptop, "Troy");
        accounts.OpenPack(laptop);   // laptop: 90 coins, 5 dice
        accounts.OpenPack(phone);    // phone: 90 coins, 5 dice
        accounts.OpenPack(phone);    // phone: 80 coins, 10 dice

        var (code, _) = accounts.NewLinkCode(laptop);
        accounts.RedeemLinkCode(phone, code, store.Sessions, store.Lobbies);

        Assert.Equal(90 + 80 - 100, laptop.Collection!.Coins);
        Assert.Equal(15, laptop.Collection.Dice.Values.Sum());
        Assert.Equal(3, laptop.Collection.PacksOpened);
        Assert.Null(phone.Collection);
    }

    [Fact]
    public void A_Device_Linked_Onto_An_Account_Without_A_Collection_Keeps_Its_Own()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var accounts = store.Accounts!;
        var (laptop, _) = accounts.Register(null);
        var (phone, _) = accounts.Register(null);
        accounts.SetName(laptop, "Troy");
        accounts.OpenPack(phone);

        var (code, _) = accounts.NewLinkCode(laptop);
        accounts.RedeemLinkCode(phone, code, store.Sessions, store.Lobbies);

        Assert.Equal(90, laptop.Collection!.Coins);
        Assert.Equal(5, laptop.Collection.Dice.Values.Sum());
    }

    [Fact]
    public void Me_Shows_Coins_And_Gives_The_Days_Coins_Once()
    {
        var clock = new Clock();
        var store = new V2GameStore(accounts: NewAccounts(clock));
        var (player, _) = store.Accounts!.Register(null);

        var me = Ok(Players(store, player).Me());
        Assert.Equal((100, 0), (me.Coins, me.DailyCoins));
        clock.Now = clock.Now.AddDays(1);
        me = Ok(Players(store, player).Me());
        Assert.Equal((105, 5), (me.Coins, me.DailyCoins));
        Assert.Equal(0, Ok(Players(store, player).Collection()).DailyCoins);
    }

    [Fact]
    public void The_Card_List_Carries_Each_Characters_Rarity()
    {
        var cards = Ok(new V2GamesController(new V2GameStore()).Cards());
        Assert.Equal("Common", cards.Single(c => c.Id == DiceKingdomConfig.Hummingbird.Id).Rarity);
        Assert.Equal("SuperRare", cards.Single(c => c.Id == DiceKingdomConfig.Phoenix.Id).Rarity);
        Assert.Null(cards.Single(c => c.Id == DiceKingdomConfig.AngerIssues.Id).Rarity);
    }
}
