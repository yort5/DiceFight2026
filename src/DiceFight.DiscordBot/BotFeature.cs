using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiceFight.DiscordBot;

// Several Discord bots in one process (2026-11-02, user: "the structure
// should keep the server registration separate from the actual command
// code, and give me a way to configure which commands are available for
// each bot"). A feature is the command code - its slash commands, their
// handling, and any background work - and knows nothing about which bot
// runs it. A bot (BotHost) is a token plus the list of feature ids it
// runs, read from configuration (DiscordBots.cs). The same feature can run
// in several bots at once: the church bot and the board game bot both run
// "game-library".
public interface IBotFeature
{
    // What a bot's "Features" list names it by.
    string Id { get; }

    // The slash commands this feature adds to a bot. Their names must not
    // collide with another feature's in the same bot.
    IReadOnlyList<SlashCommandProperties> BuildCommands(BotContext bot);

    // One of this feature's commands was used.
    Task HandleAsync(SocketSlashCommand command, BotContext bot);

    // Suggestions as someone types an autocomplete option of one of its commands.
    Task AutocompleteAsync(SocketAutocompleteInteraction interaction, BotContext bot) => Task.CompletedTask;

    // Work that runs as long as the bot does, started once it's connected
    // (the radio's now-playing poll). Most features have none.
    Task RunAsync(BotContext bot, CancellationToken stop) => Task.CompletedTask;
}

// What a feature gets to know about the bot it's running in.
public sealed class BotContext(string name, DiscordSocketClient client, IConfiguration settings, ILogger logger)
{
    // The bot's name in configuration ("church").
    public string Name { get; } = name;
    public DiscordSocketClient Client { get; } = client;
    // The bot's own configuration section; a feature reads its settings
    // from a sub-section ("Radio", "GameLibrary").
    public IConfiguration Settings { get; } = settings;
    public ILogger Logger { get; } = logger;
}
