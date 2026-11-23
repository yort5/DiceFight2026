using DiceFight.DiscordBot;
using DiceFight.DiscordBot.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiceFight.Api.Tests;

// Several Discord bots in one process (2026-11-02): each bot's config names
// its features, and only those features' commands are registered and
// routed. Nothing here talks to Discord.
public class DiscordBotsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IReadOnlyList<IBotFeature> AllFeatures() =>
    [
        new DiceMastersFeature(),
        new RadioFeature(new NoHttp()),
        new GameLibraryFeature(new GameLibraryStore(new MemoryBotDataStore())),
        new MembersFeature(new MemberDirectoryStore(new MemoryBotDataStore()), new VerificationCodes(TimeProvider.System), new NoEmail()),
    ];

    private sealed class NoEmail : IEmailSender
    {
        public Task SendAsync(SmtpSettings smtp, string to, string subject, string body, CancellationToken cancel = default) =>
            throw new InvalidOperationException("No email in tests.");
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No network in tests.");
    }

    private static BotHost Host(string name, IConfiguration config)
    {
        var (_, options, settings) = DiscordBotsRegistration.ReadBots(config).Single(b => b.Name == name);
        var features = AllFeatures().Where(f => options.Features.Contains(f.Id)).ToList();
        return new BotHost(name, options, settings, features, NullLogger.Instance);
    }

    [Fact]
    public void Each_Bot_Registers_Only_Its_Own_Features_Commands()
    {
        var config = Config(new()
        {
            ["DiscordBots:church:Features:0"] = "radio",
            ["DiscordBots:church:Features:1"] = "game-library",
            ["DiscordBots:church:Features:2"] = "members",
            ["DiscordBots:boardgames:Features:0"] = "game-library",
            ["DiscordBots:dicefight:Features:0"] = "dicemasters",
        });
        Assert.Equal(["games", "introduce", "verify", "whois"], Host("church", config).Commands.Keys.Order()); // radio has no commands
        Assert.Equal(["games"], Host("boardgames", config).Commands.Keys);
        Assert.Equal(["card", "team"], Host("dicefight", config).Commands.Keys.Order());
        Assert.IsType<GameLibraryFeature>(Host("church", config).Commands["games"]);
    }

    [Fact]
    public void Tokens_Come_From_Config_And_The_Old_Single_Bot_Setting_Still_Works()
    {
        var config = Config(new()
        {
            ["DiscordBots:dicefight:Features:0"] = "dicemasters",
            ["DiscordBots:church:Token"] = "church-token",
            ["DiscordBot:Token"] = "old-token",
            ["DiscordBot:DevGuildId"] = "42",
        });
        var bots = DiscordBotsRegistration.ReadBots(config).ToDictionary(b => b.Name, b => b.Options);
        Assert.Equal("church-token", bots["church"].Token);
        Assert.Equal("old-token", bots["dicefight"].Token);
        Assert.Equal(42UL, bots["dicefight"].DevGuildId);

        // With no DiscordBots entry at all, the old setting is the dicefight bot.
        var old = DiscordBotsRegistration.ReadBots(Config(new() { ["DiscordBot:Token"] = "old-token" })).Single();
        Assert.Equal(("dicefight", "old-token"), (old.Name, old.Options.Token));
        Assert.Equal(["dicemasters"], old.Options.Features);
    }

    private static readonly GameLibrary.Member Ann = new(1, "Ann"), Bo = new(2, "Bo"), Cy = new(3, "Cy");
    private static readonly DateTime Now = new(2026, 11, 2);

    [Fact]
    public void Members_Add_Their_Own_Games_And_Several_Can_Own_One()
    {
        var library = new GameLibrary();
        Assert.True(library.Add("Wingspan", "with Europe expansion", Ann, Now).Changed);
        Assert.False(library.Add("wingspan", null, Ann, Now).Changed); // Ann already listed hers
        Assert.True(library.Add("Wingspan", null, Bo, Now).Changed); // Bo has a copy too
        library.Add("Azul", null, Bo, Now);

        Assert.Equal(3, library.Games.Count);
        Assert.Contains("could be", library.Remove("Wing", Cy, manager: true).Message); // whose?
        Assert.False(library.Remove("Azul", Ann, manager: false).Changed); // not Ann's
        Assert.True(library.Remove("Azul", Bo, manager: false).Changed);
    }

    [Fact]
    public void Borrowing_Is_A_Request_The_Owner_Answers()
    {
        var library = new GameLibrary();
        var wingspan = library.Add("Wingspan", null, Ann, Now).Game!;

        Assert.Equal("That's your own game.", library.RequestBorrow("Wingspan", Ann, Now).Message);
        var asked = library.RequestBorrow("wing", Bo, Now);
        Assert.True(asked.Changed);
        Assert.Same(wingspan, asked.Game);
        Assert.False(library.RequestBorrow("Wingspan", Bo, Now).Changed); // already asked
        library.RequestBorrow("Wingspan", Cy, Now);

        Assert.False(library.Lend(wingspan.Id, Bo.UserId, Bo, Now).Changed); // only Ann can lend
        Assert.True(library.Lend(wingspan.Id, Bo.UserId, Ann, Now).Changed);
        Assert.Equal(Bo, wingspan.Loan!.Borrower);
        Assert.Contains("still out with Bo", library.Lend(wingspan.Id, Cy.UserId, Ann, Now).Message);
        Assert.True(library.Decline(wingspan.Id, Cy.UserId, Ann).Changed);
        Assert.Empty(wingspan.Requests);
        Assert.Contains("out with Bo", library.RequestBorrow("Wingspan", Cy, Now).Message);

        Assert.False(library.Return("Wingspan", Cy, manager: false).Changed); // Cy neither borrowed nor owns it
        Assert.True(library.Return("Wingspan", Ann, manager: false).Changed); // the owner can say it's back
        Assert.Null(wingspan.Loan);
    }

    [Fact]
    public void The_Lists_Show_Everything_What_Is_Free_And_What_Is_For_Sale()
    {
        var library = new GameLibrary();
        var wingspan = library.Add("Wingspan", null, Ann, Now).Game!;
        library.Add("Azul", "2-4 players", Bo, Now);
        library.RequestBorrow("Wingspan", Bo, Now);
        library.Lend(wingspan.Id, Bo.UserId, Ann, Now);
        Assert.False(library.Sell("Azul", "$20", null, Ann, Now).Changed); // not Ann's to sell
        Assert.True(library.Sell("Azul", "$20", "like new", Bo, Now).Changed);

        static string Name(string line) => line[1..line.IndexOf(']')];
        Assert.Equal(["Azul", "Wingspan"], library.Lines(GameLibrary.View.All).Select(Name));
        Assert.Contains("*on loan to Bo*", library.Lines(GameLibrary.View.All)[1]);
        Assert.Equal(["Azul"], library.Lines(GameLibrary.View.Available).Select(Name));
        var sale = Assert.Single(library.Lines(GameLibrary.View.ForSale));
        Assert.EndsWith(" - Bo - **$20** - like new", sale);
        Assert.StartsWith("[Azul](https://boardgamegeek.com/geeksearch.php?", sale);

        var bo = library.Mine(Bo.UserId).ToDictionary(s => s.Heading, s => s.Lines);
        Assert.Contains("for sale", Assert.Single(bo["Your games"]));
        Assert.StartsWith("Ann's [Wingspan]", Assert.Single(bo["Borrowed"]));

        Assert.True(library.Unsell("Azul", Bo, manager: false).Changed);
        Assert.Empty(library.Lines(GameLibrary.View.ForSale));
    }

    [Fact]
    public async Task Bots_Naming_The_Same_Library_Share_It_And_It_Is_Saved()
    {
        var data = new MemoryBotDataStore();
        await new GameLibraryStore(data).ChangeAsync("boardgames", l => l.Add("Azul", null, Ann, Now));
        Assert.NotNull(await data.ReadAsync(GameLibraryStore.FileName("boardgames")));

        // A fresh store (a restart) reads it back; another library is separate.
        var reloaded = new GameLibraryStore(data);
        Assert.Equal("Ann", await reloaded.UseAsync("boardgames", l => (l.Games.Single().Owner.Name, false)));
        Assert.Empty(await reloaded.UseAsync("church", l => (l.Games, false)));
    }

    [Fact]
    public void Radio_Reads_Live365s_Now_Playing_And_Posts_Each_Song_Once()
    {
        var track = RadioFeature.ParseTrack("""{"name":"KXYZ","current-track":{"title":"Amazing Grace","artist":"Choir","art":"https://x/a.jpg","duration":185.0}}""");
        Assert.Equal(new RadioFeature.Track("Amazing Grace", "Choir", "https://x/a.jpg", 185), track);
        Assert.Null(RadioFeature.ParseTrack("""{"error":"station not found"}"""));
        Assert.Equal("3:05", RadioFeature.Embed(track!).Footer!.Value.Text);

        var seen = new List<string>();
        Assert.True(RadioFeature.IsNew(seen, "Amazing Grace"));
        Assert.False(RadioFeature.IsNew(seen, "Amazing Grace"));
        foreach (var title in new[] { "A", "B", "C", "D", "E" }) RadioFeature.IsNew(seen, title);
        Assert.True(RadioFeature.IsNew(seen, "Amazing Grace")); // only the last five are remembered
    }

    [Fact]
    public void Real_Names_And_Emails_Are_Tidied_Or_Refused()
    {
        Assert.Equal(("Jane Smith", null), MemberDirectory.CleanName("  Jane   Smith "));
        Assert.NotNull(MemberDirectory.CleanName("J").Problem);
        Assert.NotNull(MemberDirectory.CleanName(new string('a', 33)).Problem); // Discord's nickname limit
        Assert.Equal(("jane@example.org", null), MemberDirectory.CleanEmail(" Jane@Example.org "));
        Assert.Equal((null, null), MemberDirectory.CleanEmail("  ")); // email is optional
        foreach (var bad in new[] { "jane", "jane@localhost", "Jane <jane@example.org>", "a b@example.org" })
            Assert.NotNull(MemberDirectory.CleanEmail(bad).Problem);
    }

    [Fact]
    public void An_Emailed_Code_Verifies_Once_Within_Its_Time_And_Tries()
    {
        var clock = new Clock(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
        var codes = new VerificationCodes(clock);
        Assert.Equal(VerificationCodes.Check.NoCode, codes.Try(1, 7, "123456").Result);

        var (code, _) = codes.Issue(1, 7, "jane@example.org");
        Assert.Matches("^[0-9]{6}$", code!);
        Assert.Null(codes.Issue(1, 7, "jane@example.org").Code); // no resending straight away
        Assert.Equal(VerificationCodes.Check.NoCode, codes.Try(2, 7, code).Result); // another server
        Assert.Equal(VerificationCodes.Check.NoCode, codes.Try(1, 8, code).Result); // another person
        Assert.Equal((VerificationCodes.Check.Ok, "jane@example.org"), codes.Try(1, 7, $" {code![..3]} {code[3..]} "));
        Assert.Equal(VerificationCodes.Check.NoCode, codes.Try(1, 7, code).Result); // used up

        // Expired.
        (code, _) = codes.Issue(1, 8, "bob@example.org");
        clock.Now += VerificationCodes.Lifetime + TimeSpan.FromSeconds(1);
        Assert.Equal(VerificationCodes.Check.Expired, codes.Try(1, 8, code).Result);

        // Too many wrong guesses lock the code, even the right one; a new code starts over.
        (code, _) = codes.Issue(1, 9, "cy@example.org");
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 1; i < VerificationCodes.MaxTries; i++)
            Assert.Equal(VerificationCodes.Check.Wrong, codes.Try(1, 9, wrong).Result);
        Assert.Equal(VerificationCodes.Check.TooManyTries, codes.Try(1, 9, wrong).Result);
        Assert.Equal(VerificationCodes.Check.TooManyTries, codes.Try(1, 9, code).Result);
        clock.Now += VerificationCodes.ResendAfter;
        (code, _) = codes.Issue(1, 9, "cy@example.org");
        Assert.Equal(VerificationCodes.Check.Ok, codes.Try(1, 9, code).Result);
    }

    [Fact]
    public async Task Introductions_Are_Saved_Per_Server_And_A_New_Address_Needs_Its_Own_Code()
    {
        var data = new MemoryBotDataStore();
        var store = new MemberDirectoryStore(data);
        await store.UseAsync("100", d => (d.Introduce(7, "jsmith", "Jane Smith", "jane@example.org", Now), true));
        await store.UseAsync("100", d => (0, d.MarkVerified(7, "jane@example.org", Now)));
        await store.UseAsync("100", d => (d.Introduce(7, "jsmith", "Jane Smith", "jane@new.org", Now), true));

        var reloaded = new MemberDirectoryStore(data); // a restart
        var jane = await reloaded.UseAsync("100", d => (d.Find(7), false));
        Assert.Equal(("Jane Smith", "jane@new.org", "jane@example.org"), (jane!.RealName, jane.Email, jane.VerifiedEmail));
        Assert.Null(await reloaded.UseAsync("200", d => (d.Find(7), false)));
        Assert.Single(await reloaded.UseAsync("100", d => (d.Members, false))); // introducing again updates
    }
}
