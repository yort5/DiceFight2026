using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiceFight.DiscordBot;

// One configured bot: its gateway connection, the commands of the features
// it runs (registered as a set, so a feature taken off a bot takes its
// commands with it), each command routed to the feature that owns it, and
// the features' background work.
public sealed class BotHost
{
    private readonly BotOptions _options;
    private readonly IReadOnlyList<IBotFeature> _features;
    private readonly ILogger _logger;
    private readonly Dictionary<string, IBotFeature> _byCommand = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SlashCommandProperties> _commands = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _background = [];
    private int _started;

    public BotHost(string name, BotOptions options, IConfiguration settings, IReadOnlyList<IBotFeature> features, ILogger logger)
    {
        _options = options;
        _features = features;
        _logger = logger;
        Client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds });
        Context = new BotContext(name, Client, settings, logger);

        foreach (var feature in features)
            foreach (var command in feature.BuildCommands(Context))
            {
                var commandName = command.Name.GetValueOrDefault()
                    ?? throw new InvalidOperationException($"A {feature.Id} command has no name.");
                if (!_byCommand.TryAdd(commandName, feature))
                {
                    logger.LogError("Bot {Bot}: /{Command} from {Feature} is already {Other}'s - skipped.",
                        name, commandName, feature.Id, _byCommand[commandName].Id);
                    continue;
                }
                _commands.Add(command);
            }

        Client.Log += OnDiscordLog;
        Client.Ready += OnReadyAsync;
        Client.SlashCommandExecuted += OnSlashCommandAsync;
        Client.AutocompleteExecuted += OnAutocompleteAsync;
        Client.ButtonExecuted += OnButtonAsync;
    }

    public DiscordSocketClient Client { get; }
    public BotContext Context { get; }

    // Command name -> the feature that answers it.
    public IReadOnlyDictionary<string, IBotFeature> Commands => _byCommand;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await Client.LoginAsync(TokenType.Bot, _options.Token);
        await Client.StartAsync();
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            await _stop.CancelAsync();
            await Task.WhenAll(_background.Select(t => t.ContinueWith(_ => { }, TaskScheduler.Default)));
            await Client.StopAsync();
            await Client.LogoutAsync();
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
        _logger.Log(level, message.Exception, "Bot {Bot} {Source}: {Message}", Context.Name, message.Source, message.Message);
        return Task.CompletedTask;
    }

    // Ready fires again after every reconnect; the commands and background
    // work only need starting once.
    private async Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        try
        {
            var all = _commands.Cast<ApplicationCommandProperties>().ToArray();
            if (_options.DevGuildId is { } guildId)
                await Client.Rest.BulkOverwriteGuildCommands(all, guildId);
            else
                await Client.BulkOverwriteGlobalApplicationCommandsAsync(all);
            _logger.LogInformation("Bot {Bot}: registered {Commands} ({Where})", Context.Name,
                string.Join(", ", _byCommand.Keys.Select(c => "/" + c)), _options.DevGuildId is { } g ? $"guild {g}" : "global");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {Bot}: failed to register slash commands", Context.Name);
        }

        foreach (var feature in _features)
            _background.Add(Task.Run(async () =>
            {
                try { await feature.RunAsync(Context, _stop.Token); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogError(ex, "Bot {Bot}: {Feature} stopped", Context.Name, feature.Id); }
            }));
    }

    private async Task OnSlashCommandAsync(SocketSlashCommand command)
    {
        if (!_byCommand.TryGetValue(command.Data.Name, out var feature)) return;
        try
        {
            await feature.HandleAsync(command, Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {Bot}: /{Command} failed", Context.Name, command.Data.Name);
            if (!command.HasResponded)
                await command.RespondAsync("Something went wrong handling that command.", ephemeral: true);
        }
    }

    private async Task OnButtonAsync(SocketMessageComponent button)
    {
        var commandName = button.Data.CustomId.Split(':')[0];
        if (!_byCommand.TryGetValue(commandName, out var feature)) return;
        try
        {
            await feature.ButtonAsync(button, Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {Bot}: button {Button} failed", Context.Name, button.Data.CustomId);
            if (!button.HasResponded)
                await button.RespondAsync("Something went wrong with that button.", ephemeral: true);
        }
    }

    private async Task OnAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        if (!_byCommand.TryGetValue(interaction.Data.CommandName, out var feature)) return;
        try
        {
            await feature.AutocompleteAsync(interaction, Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {Bot}: autocomplete for /{Command} failed", Context.Name, interaction.Data.CommandName);
        }
    }
}
