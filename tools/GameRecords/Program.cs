using System.Text.Json;
using DiceFight.Api.Recording;
using DiceFight.V2;
using DiceFight.V2.Bot;
using DiceFight.V2.Model;

// Reads recorded games (see GameRecords.csproj). For each: who played whom,
// human or bot seats, the result, and whether it replays exactly. Then, per
// kind of decision, how often the move matched the bot's own choice in that
// spot - for human seats that's where the bot has something to learn; for
// bot seats it should be ~100% (a sanity check on the replay).
// --disagreements lists every human move the bot would have made differently.

var showDisagreements = args.Contains("--disagreements");
// --bot-reasons: every move a bot seat made, with the bot's own reason and
// what each side had on the Field (2026-11-22: "why did the computer's
// Eagle never attack?").
var showBotReasons = args.Contains("--bot-reasons");
// --dump FILE: every human decision as one JSON line - what they did, what
// the bot would have done, and the board around it, in card names rather
// than die ids - for digging into where people and the bot differ.
var dumpAt = Array.IndexOf(args, "--dump");
using var dump = dumpAt >= 0 ? new StreamWriter(args[dumpAt + 1]) : null;
// --dilution N: compare against a bot whose purchases each cost N for the
// die they add to the Bag (BotPersona.DilutionPerDie) - for tuning it.
var dilutionAt = Array.IndexOf(args, "--dilution");
double? dilution = dilutionAt >= 0 ? double.Parse(args[dilutionAt + 1], System.Globalization.CultureInfo.InvariantCulture) : null;
var paths = args.Where((a, i) => !a.StartsWith("--") && !(dilutionAt >= 0 && i == dilutionAt + 1) && !(dumpAt >= 0 && i == dumpAt + 1)).ToList();
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
        // A boss seat (no Champion) plays its boss's Style on the server
        // (V2GamesController) - records don't name the boss, so find it by
        // its team.
        var player = state.GetPlayer(action.PlayerId);
        var boss = player.ChampionId is null
            ? DiceFight.V2.Data.DiceKingdomBosses.Ladder.FirstOrDefault(b => player.TeamCardIds.Count > 0 && player.TeamCardIds.All(c => b.TeamLink.Contains(c)))
            : null;
        var persona = boss?.Style ?? BotPersona.ForChampion(player.ChampionId);
        if (dilution is { } d) persona = persona with { DilutionPerDie = d };
        var bot = DiceKingdomBot.Decide(state, action.PlayerId, persona: persona);
        if (bot is null) return;
        if (showBotReasons && seatKind[action.PlayerId] == "bot")
        {
            string Field(string id) => string.Join(", ", state.Dice.Where(x => x.ControllerId == id && x.Zone is Zone.FieldZone or Zone.AttackZone && state.GetCurrentFace(x)?.Character is { } c)
                .Select(x => $"{(x.CardId is { } cid ? state.CardCatalog[cid].Name : "Tardigrade")} {QueryEngine.GetAttack(state, x)}/{QueryEngine.GetDefense(state, x)}"));
            Console.WriteLine($"  turn {action.Turn,2} {action.Step,-18} {action.Action,-18} bot: {bot.Reason}");
            if (action.Action is "DeclareAttackers" or "DeclareBlockers")
                Console.WriteLine($"      life {state.GetPlayer(action.PlayerId).Life} vs {state.GetPlayer(state.OpponentOf(action.PlayerId)).Life}; own field [{Field(action.PlayerId)}]; theirs [{Field(state.OpponentOf(action.PlayerId))}]");
        }
        var decision = Category(action.Action);
        var agreed = Same(bot, action);
        var key = (seatKind[action.PlayerId], decision);
        var (moves, ok) = agreement.GetValueOrDefault(key);
        agreement[key] = (moves + 1, ok + (agreed ? 1 : 0));
        if (dump is not null && seatKind[action.PlayerId] == "human")
            dump.WriteLine(JsonSerializer.Serialize(DecisionDump.Describe(record, state, action, bot, decision, agreed)));
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
