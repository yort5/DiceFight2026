namespace DiceFight.DiscordBot;

// Bound from the "DiscordBot" config section. Deliberately narrow - just
// what this bot needs to run, not the old bot's IAppSettings god-
// interface (20+ unrelated getters mixing secrets, sheet ids, and
// hardcoded individual Discord user ids). Token is never committed; set
// via the DiscordBot__Token environment variable (or dotnet user-secrets
// locally).
public sealed class DiscordBotOptions
{
    public string? Token { get; set; }

    // Optional. When set, slash commands register to this one guild only
    // (near-instant) instead of globally (~1hr propagation) - handy while
    // developing against a personal test server.
    public ulong? DevGuildId { get; set; }
}
