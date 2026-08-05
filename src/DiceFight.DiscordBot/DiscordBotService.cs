using System.Text;
using DiceFight.Engine.Data;
using DiceFight.Engine.Model;
using DiceFight.Engine.TeamBuilding;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiceFight.DiscordBot;

// Card lookup + team-link preview only - see DESIGN_LOG.md's status
// update for what was deliberately left out of this port (event/
// attendance/trade features needing a real datastore; the old bot's
// second "TCC" crypto bot and community-specific notifications). Runs as
// a HostedService inside the same DiceFight.Api process/container - the
// Discord gateway connection this opens is long-lived, so the host
// deploying this needs to be pinned to a single always-on instance
// rather than scaling to zero.
public sealed class DiscordBotService(
    IOptions<DiscordBotOptions> options,
    ILogger<DiscordBotService> logger) : BackgroundService
{
    private const string CardCommandName = "card";
    private const string TeamCommandName = "team";

    private readonly DiscordBotOptions _options = options.Value;
    private readonly Lazy<IReadOnlyDictionary<string, CardDef>> _catalog = new(SampleCards.BuildCatalog);
    private DiscordSocketClient? _client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            logger.LogWarning(
                "DiscordBot:Token is not configured - the Discord bot will not start. " +
                "Set the DiscordBot__Token environment variable to enable it.");
            return;
        }

        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
        });
        _client = client;

        client.Log += OnDiscordLog;
        client.Ready += OnReadyAsync;
        client.SlashCommandExecuted += OnSlashCommandExecutedAsync;

        await client.LoginAsync(TokenType.Bot, _options.Token);
        await client.StartAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        finally
        {
            await client.StopAsync();
            await client.LogoutAsync();
        }
    }

    private Task OnDiscordLog(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        logger.Log(level, message.Exception, "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        var cardCommand = new SlashCommandBuilder()
            .WithName(CardCommandName)
            .WithDescription("Look up a Dice Masters card")
            .AddOption("query", ApplicationCommandOptionType.String, "Card id (e.g. MSW018) or name", isRequired: true)
            .Build();

        var teamCommand = new SlashCommandBuilder()
            .WithName(TeamCommandName)
            .WithDescription("Preview a Teambuilder link's roster")
            .AddOption("link", ApplicationCommandOptionType.String, "A teambuilder.dicefight.app (or old Teambuilder) link", isRequired: true)
            .Build();

        try
        {
            if (_options.DevGuildId is { } guildId)
            {
                await _client!.Rest.CreateGuildCommand(cardCommand, guildId);
                await _client!.Rest.CreateGuildCommand(teamCommand, guildId);
                logger.LogInformation("Registered slash commands to dev guild {GuildId}", guildId);
            }
            else
            {
                await _client!.CreateGlobalApplicationCommandAsync(cardCommand);
                await _client!.CreateGlobalApplicationCommandAsync(teamCommand);
                logger.LogInformation("Registered global slash commands");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        try
        {
            switch (command.Data.Name)
            {
                case CardCommandName:
                    await HandleCardCommandAsync(command);
                    break;
                case TeamCommandName:
                    await HandleTeamCommandAsync(command);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling slash command {Command}", command.Data.Name);
            if (!command.HasResponded)
            {
                await command.RespondAsync("Something went wrong handling that command.", ephemeral: true);
            }
        }
    }

    private async Task HandleCardCommandAsync(SocketSlashCommand command)
    {
        var query = (string)command.Data.Options.First(o => o.Name == "query").Value;
        var catalog = _catalog.Value;

        if (catalog.TryGetValue(query.Trim().ToUpperInvariant(), out var exactMatch))
        {
            await command.RespondAsync(embed: BuildCardEmbed(exactMatch));
            return;
        }

        var nameMatches = catalog.Values
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();

        switch (nameMatches.Count)
        {
            case 0:
                await command.RespondAsync($"No card found matching \"{query}\".", ephemeral: true);
                break;
            case 1:
                await command.RespondAsync(embed: BuildCardEmbed(nameMatches[0]));
                break;
            default:
                var names = string.Join('\n', nameMatches.Take(5).Select(c => $"- {c.Name} (`{c.Id}`)"));
                var more = nameMatches.Count > 5 ? "\n(and more - try a more specific query)" : "";
                await command.RespondAsync($"Multiple cards match \"{query}\":\n{names}{more}", ephemeral: true);
                break;
        }
    }

    private static Embed BuildCardEmbed(CardDef card)
    {
        var builder = new EmbedBuilder()
            .WithTitle(card.Name)
            .WithDescription(card.Subtitle)
            .AddField("Type", card.Type, inline: true)
            .AddField("Cost", card.PurchaseCost, inline: true)
            .AddField("Ability", string.IsNullOrWhiteSpace(card.RawText) ? "(no ability text)" : card.RawText);

        if (!card.IsImplemented)
        {
            builder.WithFooter("Catalog/lookup only - this card's ability isn't simulated in the digital game yet.");
        }

        return builder.Build();
    }

    private async Task HandleTeamCommandAsync(SocketSlashCommand command)
    {
        var link = (string)command.Data.Options.First(o => o.Name == "link").Value;
        var entries = TeamLinkCodec.Decode(link);

        if (entries.Count == 0)
        {
            await command.RespondAsync("Couldn't find a `cards=` parameter in that link.", ephemeral: true);
            return;
        }

        var catalog = _catalog.Value;
        var lines = new StringBuilder();
        var unresolved = new List<string>();
        var totalDice = 0;

        foreach (var (cardId, count) in entries)
        {
            if (catalog.TryGetValue(cardId, out var card))
            {
                lines.AppendLine($"{count}x {card.Name}");
                totalDice += count;
            }
            else
            {
                unresolved.Add(cardId);
            }
        }

        lines.AppendLine().Append($"**Total dice:** {totalDice}");
        if (unresolved.Count > 0)
        {
            lines.AppendLine().Append($"Couldn't resolve: {string.Join(", ", unresolved)}");
        }

        await command.RespondAsync(lines.ToString());
    }
}
