using System.Collections.Concurrent;
using System.Text.Json;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiceFight.DiscordBot.Features;

// "radio": posts each new song a Live365 station plays to the bot's media
// channels - the church bot's one feature (github.com/yort5/churchdiscordbot,
// RadioStationService), moved here 2026-11-02. No commands; it polls.
//
//   "Radio": {
//     "PollSeconds": 65,
//     "Stations": [ { "Name": "...", "Url": "https://api.live365.com/station/<id>", "Channels": [ 1234567890 ] } ]
//   }
//
// The church bot drove a headless Chrome (PuppeteerSharp) to read the
// station JSON; a plain request gets the same JSON, so no browser in the
// container.
public sealed class RadioFeature(IHttpClientFactory http) : IBotFeature
{
    public const string FeatureId = "radio";

    public sealed class Settings
    {
        public int PollSeconds { get; set; } = 65;
        public List<Station> Stations { get; set; } = [];
    }

    public sealed class Station
    {
        public string Name { get; set; } = "";
        // The station's now-playing JSON (Live365: https://api.live365.com/station/<id>).
        public string Url { get; set; } = "";
        public List<ulong> Channels { get; set; } = [];
    }

    public sealed record Track(string Title, string Artist, string? Art, double DurationSeconds);

    public string Id => FeatureId;

    public IReadOnlyList<SlashCommandProperties> BuildCommands(BotContext bot) => [];

    public Task HandleAsync(Discord.WebSocket.SocketSlashCommand command, BotContext bot) => Task.CompletedTask;

    public async Task RunAsync(BotContext bot, CancellationToken stop)
    {
        var settings = bot.Settings.GetSection("Radio").Get<Settings>() ?? new Settings();
        if (settings.Stations.Count == 0)
        {
            bot.Logger.LogWarning("Bot {Bot}: radio has no Radio:Stations configured.", bot.Name);
            return;
        }
        // The last few titles per station, so a song isn't posted twice.
        var recent = new ConcurrentDictionary<string, List<string>>();
        while (!stop.IsCancellationRequested)
        {
            foreach (var station in settings.Stations)
            {
                try
                {
                    if (await NowPlayingAsync(station, stop) is not { } track) continue;
                    var seen = recent.GetOrAdd(station.Name, _ => []);
                    if (!IsNew(seen, track.Title)) continue;
                    foreach (var channelId in station.Channels)
                        if (await bot.Client.GetChannelAsync(channelId) is IMessageChannel channel)
                            await channel.SendMessageAsync(embed: Embed(track));
                        else
                            bot.Logger.LogWarning("Bot {Bot}: radio channel {Channel} not found.", bot.Name, channelId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    bot.Logger.LogError(ex, "Bot {Bot}: checking radio station {Station} failed.", bot.Name, station.Name);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, settings.PollSeconds)), stop);
        }
    }

    // Remembers the title; true when it wasn't among the last five.
    public static bool IsNew(List<string> seen, string title)
    {
        if (seen.Contains(title)) return false;
        seen.Add(title);
        if (seen.Count > 5) seen.RemoveAt(0);
        return true;
    }

    private async Task<Track?> NowPlayingAsync(Station station, CancellationToken stop)
    {
        var client = http.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DiceFightDiscordBot/1.0");
        using var response = await client.GetAsync(station.Url, stop);
        if (!response.IsSuccessStatusCode) return null;
        return ParseTrack(await response.Content.ReadAsStringAsync(stop));
    }

    // Live365's station JSON: "current-track": { title, artist, art, duration }.
    public static Track? ParseTrack(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("current-track", out var current) || current.ValueKind != JsonValueKind.Object) return null;
        string? Text(string name) => current.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var title = Text("title");
        if (string.IsNullOrWhiteSpace(title)) return null;
        var duration = current.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
        return new Track(title, Text("artist") ?? "", Text("art"), duration);
    }

    public static Embed Embed(Track track)
    {
        var length = TimeSpan.FromSeconds(track.DurationSeconds);
        var builder = new EmbedBuilder()
            .WithTitle(string.IsNullOrWhiteSpace(track.Artist) ? "Now playing" : track.Artist)
            .AddField(track.Title, "-------")
            .WithFooter(length.Hours > 0 ? length.ToString(@"h\:mm\:ss") : length.ToString(@"m\:ss"));
        if (!string.IsNullOrEmpty(track.Art)) builder.WithThumbnailUrl(track.Art);
        return builder.Build();
    }
}
