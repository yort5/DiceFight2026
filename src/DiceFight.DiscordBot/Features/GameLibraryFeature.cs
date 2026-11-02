using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;

namespace DiceFight.DiscordBot.Features;

// "game-library": /games list | mine | checkout | return | add | remove,
// over a GameLibrary (2026-11-02).
//
//   "GameLibrary": { "LibraryId": "boardgames", "ManagerRoleIds": [ 1234567890 ] }
//
// LibraryId picks the shelf - two bots naming the same id share one
// library; it defaults to the bot's name. Adding and removing games, and
// returning someone else's loan, take a manager: Manage Server permission
// or one of ManagerRoleIds.
public sealed class GameLibraryFeature(GameLibraryStore libraries) : IBotFeature
{
    public const string FeatureId = "game-library";
    private const string CommandName = "games";

    public sealed class Settings
    {
        public string? LibraryId { get; set; }
        public List<ulong> ManagerRoleIds { get; set; } = [];
    }

    public string Id => FeatureId;

    public IReadOnlyList<SlashCommandProperties> BuildCommands(BotContext bot) =>
    [
        new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("The board game library")
            .WithContextTypes(InteractionContextType.Guild)
            .AddOption(new SlashCommandOptionBuilder().WithName("list").WithDescription("Every game, available ones first")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("search", ApplicationCommandOptionType.String, "Only games whose name contains this", isRequired: false))
            .AddOption(new SlashCommandOptionBuilder().WithName("mine").WithDescription("The games you have checked out")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(GameSubcommand("checkout", "Borrow a game"))
            .AddOption(GameSubcommand("return", "Give a game back"))
            .AddOption(new SlashCommandOptionBuilder().WithName("add").WithDescription("Add a game to the library (managers)")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption("game", ApplicationCommandOptionType.String, "The game's name", isRequired: true)
                .AddOption("notes", ApplicationCommandOptionType.String, "Player count, where it's kept, ...", isRequired: false))
            .AddOption(GameSubcommand("remove", "Take a game out of the library (managers)"))
            .Build(),
    ];

    private static SlashCommandOptionBuilder GameSubcommand(string name, string description) =>
        new SlashCommandOptionBuilder().WithName(name).WithDescription(description)
            .WithType(ApplicationCommandOptionType.SubCommand)
            .AddOption(new SlashCommandOptionBuilder().WithName("game").WithDescription("The game")
                .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithAutocomplete(true));

    private static Settings Read(BotContext bot) => bot.Settings.GetSection("GameLibrary").Get<Settings>() ?? new Settings();

    public static string LibraryId(BotContext bot) => Read(bot).LibraryId is { Length: > 0 } id ? id : bot.Name;

    public async Task HandleAsync(SocketSlashCommand command, BotContext bot)
    {
        var sub = command.Data.Options.First();
        string? Option(string name) => sub.Options.FirstOrDefault(o => o.Name == name)?.Value as string;
        var who = new GameLibrary.Borrower(command.User.Id, (command.User as SocketGuildUser)?.DisplayName ?? command.User.GlobalName ?? command.User.Username);
        var manager = IsManager(command.User, Read(bot));
        var now = DateTime.UtcNow;
        var library = LibraryId(bot);

        switch (sub.Name)
        {
            case "list":
            case "mine":
                var lines = await libraries.UseAsync(library, l => (l.Lines(Option("search"), sub.Name == "mine" ? who.UserId : null), false));
                var title = sub.Name == "mine" ? "Your games" : Option("search") is { } search ? $"Games matching \"{search}\"" : "The game library";
                var body = lines.Count == 0
                    ? (sub.Name == "mine" ? "You don't have any games checked out." : "No games yet - a manager can add some with `/games add`.")
                    : Fit(lines);
                await command.RespondAsync(embed: new EmbedBuilder().WithTitle(title).WithDescription(body)
                    .WithFooter(libraries.Durable ? "" : "Not saved anywhere yet - this list resets when the server restarts.").Build(),
                    ephemeral: sub.Name == "mine");
                break;
            case "checkout":
                await command.RespondAsync(await libraries.UseAsync(library, l => l.CheckOut(Option("game")!, who, now)));
                break;
            case "return":
                await command.RespondAsync(await libraries.UseAsync(library, l => l.Return(Option("game")!, who, manager)));
                break;
            case "add" or "remove" when !manager:
                await command.RespondAsync("Only a library manager can do that (Manage Server, or a manager role).", ephemeral: true);
                break;
            case "add":
                await command.RespondAsync(await libraries.UseAsync(library, l => l.Add(Option("game")!, Option("notes"), who, now)));
                break;
            case "remove":
                await command.RespondAsync(await libraries.UseAsync(library, l => l.Remove(Option("game")!)));
                break;
        }
    }

    public async Task AutocompleteAsync(SocketAutocompleteInteraction interaction, BotContext bot)
    {
        var typed = interaction.Data.Current.Value as string ?? "";
        var sub = interaction.Data.Options.FirstOrDefault()?.Name;
        var userId = interaction.User.Id;
        var names = await libraries.UseAsync(LibraryId(bot), l => (l.Games
            .Where(g => g.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            // Checkout offers what's on the shelf; return what's out (yours first).
            .Where(g => sub switch { "checkout" => g.Loan is null, "return" => g.Loan is not null, _ => true })
            .OrderBy(g => g.Loan?.UserId == userId ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25).Select(g => g.Name).ToList(), false));
        await interaction.RespondAsync(names.Select(n => new AutocompleteResult(n, n)));
    }

    private static bool IsManager(IUser user, Settings settings) =>
        user is SocketGuildUser member && (member.GuildPermissions.ManageGuild || member.Roles.Any(r => settings.ManagerRoleIds.Contains(r.Id)));

    // Discord's embed description holds 4096 characters.
    private static string Fit(IReadOnlyList<string> lines)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (text.Length + lines[i].Length + 40 > 4000)
            {
                text.Append($"...and {lines.Count - i} more - try `/games list search:`");
                break;
            }
            text.AppendLine(lines[i]);
        }
        return text.ToString();
    }
}
