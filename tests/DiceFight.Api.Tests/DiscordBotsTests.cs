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
    ];

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
            ["DiscordBots:boardgames:Features:0"] = "game-library",
            ["DiscordBots:dicefight:Features:0"] = "dicemasters",
        });
        Assert.Equal(["games"], Host("church", config).Commands.Keys); // radio has no commands
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

    [Fact]
    public void The_Library_Lends_Each_Game_To_One_Person_At_A_Time()
    {
        var library = new GameLibrary();
        var ann = new GameLibrary.Borrower(1, "Ann");
        var bo = new GameLibrary.Borrower(2, "Bo");
        var now = new DateTime(2026, 11, 2);
        Assert.True(library.Add("Wingspan", "1-5 players", ann, now).Changed);
        Assert.False(library.Add("wingspan", null, ann, now).Changed); // already there
        library.Add("Catan", null, ann, now);
        library.Add("Catan: Seafarers", null, ann, now);

        Assert.True(library.CheckOut("wing", ann, now).Changed); // a unique part of the name is enough
        var taken = library.CheckOut("Wingspan", bo, now);
        Assert.False(taken.Changed);
        Assert.Contains("out with Ann", taken.Message);
        Assert.Contains("which one", library.CheckOut("Cat", bo, now).Message); // Catan or Catan: Seafarers
        Assert.True(library.CheckOut("Catan", bo, now).Changed); // an exact name wins

        Assert.False(library.Return("Wingspan", bo, manager: false).Changed); // not Bo's to return
        Assert.True(library.Return("Wingspan", bo, manager: true).Changed); // a manager can
        Assert.Equal(["**Catan: Seafarers**", "**Wingspan** - 1-5 players", "~~Catan~~ - out with Bo since Nov 2"], library.Lines());
        Assert.Equal(["~~Catan~~ - out with Bo since Nov 2"], library.Lines(mine: 2));
    }

    [Fact]
    public async Task Bots_Naming_The_Same_Library_Share_It_And_It_Is_Saved()
    {
        var data = new MemoryBotDataStore();
        var who = new GameLibrary.Borrower(1, "Ann");
        await new GameLibraryStore(data).UseAsync("boardgames", l => l.Add("Azul", null, who, DateTime.UtcNow));
        Assert.NotNull(await data.ReadAsync(GameLibraryStore.FileName("boardgames")));

        // A fresh store (a restart) reads it back; another library is separate.
        var reloaded = new GameLibraryStore(data);
        Assert.Equal(["**Azul**"], await reloaded.UseAsync("boardgames", l => (l.Lines(), false)));
        Assert.Empty(await reloaded.UseAsync("church", l => (l.Lines(), false)));
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
}
