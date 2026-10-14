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

// Trade-ins and starter rosters (2026-10-12).
public class TradeInTests
{
    private static PlayerAccounts NewAccounts() => new(null, NullLogger<PlayerAccounts>.Instance);

    private static T Ok<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

    private static PlayersController Players(V2GameStore store, PlayerAccount player)
    {
        var http = new DefaultHttpContext();
        http.SetPlayer(player);
        return new(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static PlayerAccount WithDice(PlayerAccounts accounts, params (string Card, int Count)[] dice)
    {
        var (player, _) = accounts.Register(null);
        var collection = accounts.Wallet(player).Collection;
        foreach (var (card, count) in dice) collection.Dice[card] = count;
        return player;
    }

    [Fact]
    public void Five_Commons_Become_An_Uncommon()
    {
        var accounts = NewAccounts();
        var badger = DiceKingdomConfig.HoneyBadger.Id;
        var stoat = DiceKingdomConfig.Stoat.Id;
        var player = WithDice(accounts, (badger, 7), (stoat, 5));

        var got = accounts.TradeIn(player, [badger, badger, badger, stoat, stoat]);

        Assert.Equal(CardRarity.Uncommon, DiceKingdomPacks.Rarities[got]);
        Assert.Equal(4, player.Collection!.Dice[badger]);
        Assert.Equal(3, player.Collection.Dice[stoat]);
        Assert.Equal(1, player.Collection.Dice[got]);
        Assert.Equal(1, player.Collection.TradeIns);
    }

    [Fact]
    public void Four_Uncommons_Make_A_Rare_And_Three_Rares_A_Super_Rare()
    {
        var accounts = NewAccounts();
        var wolverine = DiceKingdomConfig.Wolverine.Id;
        var orca = DiceKingdomConfig.Orca.Id;
        var player = WithDice(accounts, (wolverine, 4), (orca, 3));

        Assert.Equal(CardRarity.Rare, DiceKingdomPacks.Rarities[accounts.TradeIn(player, [wolverine, wolverine, wolverine, wolverine])]);
        Assert.False(player.Collection!.Dice.ContainsKey(wolverine));
        Assert.Equal(CardRarity.SuperRare, DiceKingdomPacks.Rarities[accounts.TradeIn(player, [orca, orca, orca])]);
    }

    [Theory]
    [InlineData(4)] // too few
    [InlineData(6)] // too many
    public void A_Trade_Needs_Exactly_The_Rate(int count)
    {
        var accounts = NewAccounts();
        var badger = DiceKingdomConfig.HoneyBadger.Id;
        var player = WithDice(accounts, (badger, 10));
        Assert.Throws<InvalidOperationException>(() => accounts.TradeIn(player, Enumerable.Repeat(badger, count).ToList()));
        Assert.Equal(10, player.Collection!.Dice[badger]);
    }

    [Fact]
    public void Mixed_Rarities_Unowned_Dice_And_Super_Rares_Are_Refused()
    {
        var accounts = NewAccounts();
        var badger = DiceKingdomConfig.HoneyBadger.Id;
        var wolverine = DiceKingdomConfig.Wolverine.Id;
        var phoenix = DiceKingdomConfig.Phoenix.Id;
        var player = WithDice(accounts, (badger, 4), (wolverine, 4), (phoenix, 5));

        Assert.Throws<InvalidOperationException>(() => accounts.TradeIn(player, [badger, badger, badger, badger, wolverine]));
        Assert.Throws<InvalidOperationException>(() => accounts.TradeIn(player, [badger, badger, badger, badger, badger])); // only 4
        Assert.Throws<InvalidOperationException>(() => accounts.TradeIn(player, [phoenix, phoenix, phoenix]));
        Assert.Throws<InvalidOperationException>(() => accounts.TradeIn(player, ["DK-ACT-01", "DK-ACT-01", "DK-ACT-01", "DK-ACT-01", "DK-ACT-01"]));
        Assert.Equal((4, 4, 5), (player.Collection!.Dice[badger], player.Collection.Dice[wolverine], player.Collection.Dice[phoenix]));
    }

    [Fact]
    public void A_Trade_Gives_A_Card_You_Still_Need_While_There_Is_One()
    {
        // Every Uncommon full but Fox.
        var pool = DiceKingdomPacks.CardsOf(CardRarity.Uncommon);
        var full = pool.Where(id => id != DiceKingdomConfig.Fox.Id).Select(id => (id, DiceKingdomConfig.Catalog[id].DieLimit));
        var owned = full.ToDictionary(x => x.id, x => x.Item2);
        for (var roll = 0; roll < 3; roll++)
            Assert.Equal(DiceKingdomConfig.Fox.Id, DiceKingdomPacks.TradeIn(CardRarity.Common, owned, n => roll % n));
        // Once all are full, any of them.
        owned[DiceKingdomConfig.Fox.Id] = DiceKingdomConfig.Fox.DieLimit;
        var any = Enumerable.Range(0, pool.Count).Select(r => DiceKingdomPacks.TradeIn(CardRarity.Common, owned, _ => r)).Distinct().Count();
        Assert.Equal(pool.Count, any);
    }

    [Fact]
    public void The_Trade_In_Endpoint_Returns_The_New_Die()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var badger = DiceKingdomConfig.HoneyBadger.Id;
        var player = WithDice(store.Accounts!, (badger, 9));

        var traded = Ok(Players(store, player).TradeIn(new TradeInRequest([badger, badger, badger, badger, badger])));

        Assert.Equal("Uncommon", traded.Card.Rarity);
        Assert.True(traded.Card.New);
        Assert.Equal(4, traded.Collection.Dice[badger]);
        Assert.Equal(5, traded.Collection.TradeInCost["Common"]);
    }

    [Fact]
    public void Finishing_A_Game_Adds_That_Champions_Starter_Roster_Once()
    {
        var store = new V2GameStore(accounts: NewAccounts());
        var accounts = store.Accounts!;
        var (player, _) = accounts.Register(null);
        var http = new DefaultHttpContext();
        http.SetPlayer(player);
        var games = new V2GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };

        for (var i = 0; i < 2; i++)
        {
            var id = Ok(games.Create(new CreateV2GameRequest("Wolf", "GoldenEagle"))).Game.GameId;
            var session = store.GetSession(id);
            accounts.BotSeat(session, "teamB"); // the computer plays Golden Eagle
            if (i == 0) Assert.Empty(player.Collection?.Starters ?? []); // not while it's being played
            session.State.PlayerTwo.Life = 0;
            session.State.CheckGameOver();
            accounts.AfterMove(session);
        }

        var collection = player.Collection!;
        Assert.Equal(["Wolf"], collection.Starters);
        Assert.All(DiceKingdomConfig.CharactersByChampion["Wolf"], id => Assert.Equal(4, collection.Dice[id]));
        Assert.Equal(32, collection.Dice.Values.Sum());
    }

    [Fact]
    public void A_Player_From_Before_Packs_Gets_The_Starters_They_Played()
    {
        var accounts = NewAccounts();
        var (player, _) = accounts.Register(null);
        player.Games.Add(new PlayedGame
        {
            GameId = "old", EndedUtc = DateTime.UtcNow,
            Seats = [new PlayedSeat { SeatId = "teamA", ChampionId = "GreatHornedOwl", Token = "t" }, new PlayedSeat { SeatId = "teamB", ChampionId = "Wolf", Bot = true }],
        });

        var (collection, _) = accounts.Wallet(player);

        Assert.Equal(["GreatHornedOwl"], collection.Starters);
        Assert.Equal(32, collection.Dice.Values.Sum());
        Assert.Equal(100, collection.Coins);
    }
}
