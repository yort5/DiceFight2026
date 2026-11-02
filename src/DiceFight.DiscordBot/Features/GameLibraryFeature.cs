using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;

namespace DiceFight.DiscordBot.Features;

// "game-library": the members' own board games, to lend and sell
// (GameLibrary). One /games command:
//
//   add, remove      a game of yours (remove: sold or gone)
//   list, available  everyone's games / the ones not out on loan
//   borrow           asks the owner, who gets Lend and Decline buttons
//   return           the borrower or the owner marks it back home
//   mine             your games, loans and open requests
//   sell, unsell     list one of yours for sale / take the listing down
//   forsale          everything for sale
//
//   "GameLibrary": { "LibraryId": "boardgames", "ManagerRoleIds": [ 1234567890 ] }
//
// LibraryId picks the library - two bots naming the same id share it; it
// defaults to the bot's name. A manager (Manage Server, or one of
// ManagerRoleIds) can remove anyone's game or listing and mark any loan
// returned, for tidying up after someone leaves.
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
            .WithDescription("Board games the members own - to borrow, lend and sell")
            .WithContextTypes(InteractionContextType.Guild)
            .AddOption(Sub("add", "Add a board game you own")
                .AddOption("game", ApplicationCommandOptionType.String, "The game's name", isRequired: true)
                .AddOption("notes", ApplicationCommandOptionType.String, "Expansions, condition, ...", isRequired: false))
            .AddOption(Sub("remove", "Take one of your games off the list").AddOption(GameOption()))
            .AddOption(Sub("list", "Every game people own")
                .AddOption("search", ApplicationCommandOptionType.String, "Only names containing this", isRequired: false))
            .AddOption(Sub("available", "Games that aren't out on loan")
                .AddOption("search", ApplicationCommandOptionType.String, "Only names containing this", isRequired: false))
            .AddOption(Sub("borrow", "Ask to borrow someone's game").AddOption(GameOption()))
            .AddOption(Sub("return", "Mark a borrowed game as returned").AddOption(GameOption()))
            .AddOption(Sub("mine", "Your games, what you've borrowed, and your requests"))
            .AddOption(Sub("sell", "List one of your games for sale").AddOption(GameOption())
                .AddOption("price", ApplicationCommandOptionType.String, "e.g. $25, or make an offer", isRequired: false)
                .AddOption("notes", ApplicationCommandOptionType.String, "Condition, pickup, ...", isRequired: false))
            .AddOption(Sub("unsell", "Take one of your games off sale").AddOption(GameOption()))
            .AddOption(Sub("forsale", "Games people are selling"))
            .Build(),
    ];

    private static SlashCommandOptionBuilder Sub(string name, string description) =>
        new SlashCommandOptionBuilder().WithName(name).WithDescription(description).WithType(ApplicationCommandOptionType.SubCommand);

    private static SlashCommandOptionBuilder GameOption() =>
        new SlashCommandOptionBuilder().WithName("game").WithDescription("The game")
            .WithType(ApplicationCommandOptionType.String).WithRequired(true).WithAutocomplete(true);

    private static Settings Read(BotContext bot) => bot.Settings.GetSection("GameLibrary").Get<Settings>() ?? new Settings();

    public static string LibraryId(BotContext bot) => Read(bot).LibraryId is { Length: > 0 } id ? id : bot.Name;

    private static GameLibrary.Member Member(IUser user) =>
        new(user.Id, (user as SocketGuildUser)?.DisplayName ?? user.GlobalName ?? user.Username);

    public async Task HandleAsync(SocketSlashCommand command, BotContext bot)
    {
        var sub = command.Data.Options.First();
        string? Option(string name) => sub.Options.FirstOrDefault(o => o.Name == name)?.Value as string;
        var who = Member(command.User);
        var manager = IsManager(command.User, Read(bot));
        var now = DateTime.UtcNow;
        var library = LibraryId(bot);

        switch (sub.Name)
        {
            case "list":
                await RespondList(command, "Everyone's board games", GameLibrary.View.All, Option("search"), library, "No games yet - add yours with `/games add`.");
                break;
            case "available":
                await RespondList(command, "Board games on the shelf", GameLibrary.View.Available, Option("search"), library, "Nothing available right now.");
                break;
            case "forsale":
                await RespondList(command, "Board games for sale", GameLibrary.View.ForSale, null, library, "Nothing for sale right now - list yours with `/games sell`.");
                break;
            case "mine":
                var sections = await libraries.UseAsync(library, l => (l.Mine(who.UserId), false));
                var embed = new EmbedBuilder().WithTitle("Your board games");
                foreach (var (heading, lines) in sections)
                    embed.AddField(heading, lines.Count == 0 ? "-" : Fit(lines, 1000));
                await command.RespondAsync(embed: embed.Build(), ephemeral: true);
                break;
            case "add":
                await Reply(command, await libraries.ChangeAsync(library, l => l.Add(Option("game")!, Option("notes"), who, now)));
                break;
            case "remove":
                await Reply(command, await libraries.ChangeAsync(library, l => l.Remove(Option("game")!, who, manager)));
                break;
            case "return":
                await Reply(command, await libraries.ChangeAsync(library, l => l.Return(Option("game")!, who, manager)));
                break;
            case "sell":
                await Reply(command, await libraries.ChangeAsync(library, l => l.Sell(Option("game")!, Option("price"), Option("notes"), who, now)));
                break;
            case "unsell":
                await Reply(command, await libraries.ChangeAsync(library, l => l.Unsell(Option("game")!, who, manager)));
                break;
            case "borrow":
                var asked = await libraries.ChangeAsync(library, l => l.RequestBorrow(Option("game")!, who, now));
                if (!asked.Changed || asked.Game is not { } game)
                {
                    await command.RespondAsync(asked.Message, ephemeral: true);
                    break;
                }
                // Out in the channel, pinging the owner, who answers with a button.
                await command.RespondAsync($"<@{game.Owner.UserId}>, {asked.Message}",
                    allowedMentions: new AllowedMentions { UserIds = [game.Owner.UserId] },
                    components: new ComponentBuilder()
                        .WithButton("Lend it", $"{CommandName}:lend:{game.Id}:{who.UserId}", ButtonStyle.Success)
                        .WithButton("Not now", $"{CommandName}:decline:{game.Id}:{who.UserId}", ButtonStyle.Secondary)
                        .Build());
                break;
        }
    }

    // Lend / Not now, on a borrow request. Only the owner's press counts.
    public async Task ButtonAsync(SocketMessageComponent button, BotContext bot)
    {
        var parts = button.Data.CustomId.Split(':');
        if (parts.Length != 4 || !ulong.TryParse(parts[3], out var requesterId)) return;
        var who = Member(button.User);
        var outcome = await libraries.ChangeAsync(LibraryId(bot), l => parts[1] switch
        {
            "lend" => l.Lend(parts[2], requesterId, who, DateTime.UtcNow),
            "decline" => l.Decline(parts[2], requesterId, who),
            _ => GameLibrary.Outcome.No("That button doesn't do anything any more."),
        });
        if (!outcome.Changed)
        {
            await button.RespondAsync(outcome.Message, ephemeral: true);
            return;
        }
        // Answered: the request message becomes the answer, buttons gone, and the borrower is pinged.
        await button.UpdateAsync(m =>
        {
            m.Content = $"<@{requesterId}>, {outcome.Message}";
            m.Components = new ComponentBuilder().Build();
            m.AllowedMentions = new AllowedMentions { UserIds = [requesterId] };
        });
    }

    public async Task AutocompleteAsync(SocketAutocompleteInteraction interaction, BotContext bot)
    {
        var typed = (interaction.Data.Current.Value as string ?? "").Trim();
        var sub = interaction.Data.Options.FirstOrDefault()?.Name;
        var userId = interaction.User.Id;
        var manager = IsManager(interaction.User, Read(bot));
        var choices = await libraries.UseAsync(LibraryId(bot), l => (l.Games
            .Where(g => g.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Where(g => sub switch
            {
                "borrow" => g.Owner.UserId != userId && g.Loan is null,
                "return" => g.Loan is { } loan && (loan.Borrower.UserId == userId || g.Owner.UserId == userId || manager),
                "remove" => g.Owner.UserId == userId || manager,
                "sell" => g.Owner.UserId == userId,
                "unsell" => g.Sale is not null && (g.Owner.UserId == userId || manager),
                _ => true,
            })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(g => new AutocompleteResult(Truncate($"{g.Name} ({g.Owner.Name})", 100), g.Id))
            .ToList(), false));
        await interaction.RespondAsync(choices);
    }

    private async Task RespondList(SocketSlashCommand command, string title, GameLibrary.View view, string? search, string library, string empty)
    {
        var lines = await libraries.UseAsync(library, l => (l.Lines(view, search), false));
        var description = lines.Count == 0 ? (search is null ? empty : $"Nothing matching \"{search}\".") : Fit(lines, 4000);
        var embed = new EmbedBuilder().WithTitle(search is null ? title : $"{title} matching \"{search}\"").WithDescription(description);
        if (!libraries.Durable) embed.WithFooter("Not saved anywhere yet - this list resets when the server restarts.");
        await command.RespondAsync(embed: embed.Build());
    }

    // Changes are announced in the channel; refusals only to the person.
    private static Task Reply(SocketSlashCommand command, GameLibrary.Outcome outcome) =>
        command.RespondAsync(outcome.Message, ephemeral: !outcome.Changed, allowedMentions: AllowedMentions.None);

    private static bool IsManager(IUser user, Settings settings) =>
        user is SocketGuildUser member && (member.GuildPermissions.ManageGuild || member.Roles.Any(r => settings.ManagerRoleIds.Contains(r.Id)));

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // Discord's limits: 4096 characters in a description, 1024 in a field.
    private static string Fit(IReadOnlyList<string> lines, int max)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (text.Length + lines[i].Length + 40 > max)
            {
                text.Append($"...and {lines.Count - i} more - narrow it with `search:`");
                break;
            }
            text.AppendLine(lines[i]);
        }
        return text.ToString();
    }
}
