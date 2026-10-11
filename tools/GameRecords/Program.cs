using System.Text.Json;
using DiceFight.Api.Recording;
using DiceFight.V2;
using DiceFight.V2.Bot;

// Reads recorded games (see GameRecords.csproj). For each: who played whom,
// human or bot seats, the result, and whether it replays exactly. Then, per
// kind of decision, how often the move matched the bot's own choice in that
// spot - for human seats that's where the bot has something to learn; for
// bot seats it should be ~100% (a sanity check on the replay).
// --disagreements lists every human move the bot would have made differently.

var showDisagreements = args.Contains("--disagreements");
// --dilution N: compare against a bot whose purchases each cost N for the
// die they add to the Bag (BotPersona.DilutionPerDie) - for tuning it.
var dilutionAt = Array.IndexOf(args, "--dilution");
double? dilution = dilutionAt >= 0 ? double.Parse(args[dilutionAt + 1], System.Globalization.CultureInfo.InvariantCulture) : null;
var paths = args.Where((a, i) => !a.StartsWith("--") && !(dilutionAt >= 0 && i == dilutionAt + 1)).ToList();
if (paths.Count == 0)
{
    Console.WriteLine("usage: dotnet run -c Release -- <folder or .json files> [--disagreements] [--dilution N]");
    return 1;
}
var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.json", SearchOption.AllDirectories) : [p]).Order().ToList();

// (seat kind, decision) -> (moves, agreed)
var agreement = new Dictionary<(string Seat, string Decision), (int Moves, int Agreed)>();
var disagreements = new List<string>();

Console.WriteLine($"{"game",-10}{"date",-12}{"seat A",-24}{"seat B",-24}{"winner",-16}{"turns",6}  replay");
foreach (var file in files)
{
    GameRecord record;
    try { record = GameRecordJson.Deserialize(File.ReadAllText(file)); }
    catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(file)}: unreadable ({ex.Message})"); continue; }

    var seatKind = record.Seats.ToDictionary(s => s.PlayerId, s => s.Bot ? "bot" : "human");
    var outcome = GameReplayer.Replay(record, (state, action) =>
    {
        if (action.Error is not null || action.Auto) return; // an illegal try, or a move the client made for them, isn't a decision
        var persona = BotPersona.ForChampion(state.GetPlayer(action.PlayerId).ChampionId);
        if (dilution is { } d) persona = persona with { DilutionPerDie = d };
        var bot = DiceKingdomBot.Decide(state, action.PlayerId, persona: persona);
        if (bot is null) return;
        var decision = Category(action.Action);
        var agreed = Same(bot, action);
        var key = (seatKind[action.PlayerId], decision);
        var (moves, ok) = agreement.GetValueOrDefault(key);
        agreement[key] = (moves + 1, ok + (agreed ? 1 : 0));
        if (!agreed && seatKind[action.PlayerId] == "human")
            disagreements.Add($"{record.GameId} turn {action.Turn} ({action.Step}): played {action.Action} {action.Request?.GetRawText()}; bot: {bot.Kind} - {bot.Reason}");
    });

    string Seat(RecordedSeat s) => $"{s.ChampionId ?? "Custom Team"}{(s.Bot ? " (bot)" : "")}";
    var winner = record.Result is { GameOver: true } r
        ? r.WinnerId is { } w ? record.Seats.First(s => s.PlayerId == w).ChampionId ?? "Custom Team" : "tie"
        : "unfinished";
    Console.WriteLine($"{record.GameId,-10}{record.CreatedUtc:yyyy-MM-dd}  {Seat(record.Seats[0]),-24}{Seat(record.Seats[1]),-24}{winner,-16}{record.Turns,6}  " +
        (outcome.Divergence is null ? $"ok ({outcome.ActionsReplayed} moves)" : $"DRIFTED - {outcome.Divergence}"));
}

Console.WriteLine();
Console.WriteLine("Moves that matched the bot's choice in the same spot:");
Console.WriteLine($"  {"seat",-7}{"decision",-22}{"moves",7}{"agree",8}");
foreach (var ((seat, decision), (moves, agreed)) in agreement.OrderBy(kv => kv.Key.Seat).ThenByDescending(kv => kv.Value.Moves))
    Console.WriteLine($"  {seat,-7}{decision,-22}{moves,7}{100.0 * agreed / moves,7:F0}%");

if (showDisagreements)
{
    Console.WriteLine();
    Console.WriteLine($"Human moves the bot would have made differently ({disagreements.Count}):");
    foreach (var line in disagreements) Console.WriteLine("  " + line);
}
return 0;

// The web client passes priority through three endpoints, which the bot
// sees as one decision.
static string Category(string action) => action is "EnterAttackStep" or "AssignCombatDamage" or "SkipAttackStep" ? "Pass" : action;

// Did the recorded move do what the bot chose? Same kind of move, on the
// same die/dice where the move names any.
static bool Same(BotDecision bot, RecordedAction action)
{
    var expected = bot.Kind switch
    {
        BotActionKind.Pass => "Pass",
        BotActionKind.UseChampionPower => "ChampionPower",
        _ => bot.Kind.ToString(),
    };
    // Skipping the attack and passing into it both end Main; whether to
    // swing shows up in DeclareAttackers, so they count as one choice here.
    if (Category(action.Action) != expected) return false;
    if (action.Request is not { } body) return true;

    HashSet<string> Strings(string name) => body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
        ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet() : [];
    string? Str(string name) => body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    return bot.Kind switch
    {
        BotActionKind.Field or BotActionKind.Purchase or BotActionKind.UseAction or BotActionKind.Foresight => Str("dieId") == bot.DieId,
        BotActionKind.Reroll => Strings("dieIds").SetEquals(bot.DieIds),
        BotActionKind.ResolvePendingChoice => Strings("chosenDieIds").SetEquals(bot.DieIds),
        BotActionKind.UseGlobal => Str("cardId") == bot.CardId,
        BotActionKind.DeclareAttackers => body.GetProperty("attackers").EnumerateArray()
            .Select(a => a.GetProperty("dieId").GetString()!).ToHashSet().SetEquals(bot.AttackerLanes.Keys),
        BotActionKind.DeclareBlockers => body.GetProperty("assignments").EnumerateArray()
            .Select(a => (a.GetProperty("attackerDieId").GetString()!, a.GetProperty("blockerDieId").GetString()!)).ToHashSet()
            .SetEquals(bot.Blocks),
        _ => true,
    };
}
