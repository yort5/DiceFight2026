using DiceFight.DiscordBot.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceFight.DiscordBot;

// Every Discord bot this server runs (2026-11-02), one entry per bot under
// "DiscordBots" in configuration:
//
//   "DiscordBots": {
//     "church": {
//       "Features": [ "radio", "game-library" ],
//       "Radio": { "Stations": [ ... ] },
//       "GameLibrary": { "LibraryId": "church" }
//     }
//   }
//
// The token is never committed - set it as DiscordBots__church__Token (an
// environment variable or a Cloud Run secret). A bot with no token is
// skipped. The old single-bot "DiscordBot:Token" still works: it runs a bot
// named "dicefight" with the Dice Masters commands.
//
// Every bot holds its own gateway connection, which is long-lived - the
// host has to keep one instance always on, with CPU always allocated (the
// radio polls between requests).
public sealed class BotOptions
{
    public string? Token { get; set; }
    // Feature ids (IBotFeature.Id) - which commands and background work it runs.
    public List<string> Features { get; set; } = [];
    // Register the commands in this one server only - near-instant, for
    // trying things out - instead of everywhere (up to an hour).
    public ulong? DevGuildId { get; set; }
}

public static class DiscordBotsRegistration
{
    public const string Section = "DiscordBots";

    public static IServiceCollection AddDiscordBots(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IBotFeature, DiceMastersFeature>();
        services.AddSingleton<IBotFeature, RadioFeature>();
        services.AddSingleton<IBotFeature, GameLibraryFeature>();
        services.AddSingleton<GameLibraryStore>();
        services.AddHostedService<DiscordBotsService>();
        return services;
    }

    // The configured bots, the old single-bot setting included.
    public static IReadOnlyList<(string Name, BotOptions Options, IConfiguration Settings)> ReadBots(IConfiguration configuration)
    {
        var bots = new List<(string, BotOptions, IConfiguration)>();
        foreach (var section in configuration.GetSection(Section).GetChildren())
            bots.Add((section.Key, section.Get<BotOptions>() ?? new BotOptions(), section));
        // The old single bot (DiscordBot__Token) is the "dicefight" bot: its
        // token fills that entry's when the entry has none.
        var legacy = configuration.GetSection("DiscordBot");
        if (legacy["Token"] is { Length: > 0 } token)
        {
            var index = bots.FindIndex(b => b.Item1 == "dicefight");
            if (index < 0)
                bots.Add(("dicefight", new BotOptions { Token = token, Features = [DiceMastersFeature.FeatureId] }, legacy));
            else if (string.IsNullOrWhiteSpace(bots[index].Item2.Token))
                bots[index].Item2.Token = token;
            var guild = ulong.TryParse(legacy["DevGuildId"], out var g) ? g : (ulong?)null;
            var dicefight = bots.First(b => b.Item1 == "dicefight").Item2;
            dicefight.DevGuildId ??= guild;
        }
        return bots;
    }
}

public sealed class DiscordBotsService(
    IConfiguration configuration,
    IEnumerable<IBotFeature> features,
    ILoggerFactory loggers,
    ILogger<DiscordBotsService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var known = features.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
        var runs = new List<Task>();
        foreach (var (name, options, settings) in DiscordBotsRegistration.ReadBots(configuration))
        {
            if (string.IsNullOrWhiteSpace(options.Token))
            {
                logger.LogInformation("Discord bot {Bot} has no token (DiscordBots__{Bot}__Token) - not started.", name, name);
                continue;
            }
            var botFeatures = new List<IBotFeature>();
            foreach (var id in options.Features)
                if (known.TryGetValue(id, out var feature)) botFeatures.Add(feature);
                else logger.LogError("Discord bot {Bot}: no feature called {Feature} (known: {Known}).", name, id, string.Join(", ", known.Keys));
            var host = new BotHost(name, options, settings, botFeatures, loggers.CreateLogger($"DiscordBot.{name}"));
            runs.Add(Task.Run(async () =>
            {
                try { await host.RunAsync(stoppingToken); }
                catch (Exception ex) { logger.LogError(ex, "Discord bot {Bot} stopped.", name); }
            }, CancellationToken.None));
        }
        if (runs.Count == 0) logger.LogInformation("No Discord bots configured with a token.");
        return Task.WhenAll(runs);
    }
}
