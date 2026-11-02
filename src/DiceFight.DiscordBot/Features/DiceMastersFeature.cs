using System.Text;
using DiceFight.Engine.Data;
using DiceFight.Engine.Model;
using DiceFight.Engine.TeamBuilding;
using Discord;
using Discord.WebSocket;

namespace DiceFight.DiscordBot.Features;

// "dicemasters": /card lookup and /team link preview over the Dice Masters
// catalog - the original bot's two commands (2026-08-05), unchanged since
// they moved here from DiscordBotService (2026-11-02). See DESIGN_LOG.md
// for what was left out of the port from the community bot.
public sealed class DiceMastersFeature : IBotFeature
{
    public const string FeatureId = "dicemasters";
    private const string CardCommandName = "card";
    private const string TeamCommandName = "team";

    private readonly Lazy<IReadOnlyDictionary<string, CardDef>> _catalog = new(SampleCards.BuildCatalog);

    public string Id => FeatureId;

    public IReadOnlyList<SlashCommandProperties> BuildCommands(BotContext bot) =>
    [
        new SlashCommandBuilder()
            .WithName(CardCommandName)
            .WithDescription("Look up a Dice Masters card")
            .AddOption("query", ApplicationCommandOptionType.String, "Card id (e.g. MSW018) or name", isRequired: true)
            .Build(),
        new SlashCommandBuilder()
            .WithName(TeamCommandName)
            .WithDescription("Preview a Teambuilder link's roster")
            .AddOption("link", ApplicationCommandOptionType.String, "A teambuilder.dicefight.app (or old Teambuilder) link", isRequired: true)
            .Build(),
    ];

    public Task HandleAsync(SocketSlashCommand command, BotContext bot) => command.Data.Name switch
    {
        CardCommandName => HandleCardCommandAsync(command),
        TeamCommandName => HandleTeamCommandAsync(command),
        _ => Task.CompletedTask,
    };
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
