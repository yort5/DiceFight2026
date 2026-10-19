using System.Net.Http.Json;
using System.Text.Json;
using DiceFight.Api.Recording;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace DiceFight.Api.Tests;

// Game recording (2026-10-04), end to end through the real HTTP pipeline -
// the recording hooks are an action filter, which only runs there. Both
// seats are played by the server's own bot, the way the web client plays
// its "vs computer" seat (GET bot-decision, then the matching endpoint).
public class GameRecordingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dk-records-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private WebApplicationFactory<Program> Server() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["GameRecords:LocalPath"] = _folder, ["GameRecords:Bucket"] = "" })));

    // A custom team (Team Builder) has no Champion: its dice counts and
    // Tardigrade mix are recorded so the replay deals the same team.
    private static readonly object CustomTeam = new
    {
        cards = DiceFight.V2.Data.DiceKingdomConfig.CharactersByChampion["Wolf"].Select(id => new { cardId = id, diceCount = 2 }).ToArray(),
        tardigrades = new[] { new { energyType = "Claw", diceCount = 6 }, new { energyType = "Eye", diceCount = 4 } },
    };

    // ...and one that names a Champion (2026-10-19) keeps its power and its
    // custom dice through the replay.
    private static readonly object CustomWolfTeam = new
    {
        cards = DiceFight.V2.Data.DiceKingdomConfig.CharactersByChampion["Wolf"].Select(id => new { cardId = id, diceCount = 2 })
            .Append(new { cardId = DiceFight.V2.Data.DiceKingdomConfig.AngerIssues.Id, diceCount = 3 }).ToArray(),
        tardigrades = new[] { new { energyType = "Claw", diceCount = 6 }, new { energyType = "Eye", diceCount = 4 } },
        championId = "Wolf",
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_Played_Game_Is_Recorded_And_Replays_To_The_Same_State(bool customTeam, bool withChampion)
    {
        string gameId;
        await using (var server = Server())
        {
            var http = server.CreateClient();
            object body = customTeam
                ? new { playerOneCustomTeam = withChampion ? CustomWolfTeam : CustomTeam, playerTwoChampionId = "Armadillo" }
                : new { playerOneChampionId = "Wolf", playerTwoChampionId = "Armadillo" };
            var created = await (await http.PostAsJsonAsync("/api/v2/games", body))
                .Content.ReadFromJsonAsync<JsonElement>();
            gameId = created.GetProperty("game").GetProperty("gameId").GetString()!;
            var tokens = created.GetProperty("seats").EnumerateArray()
                .ToDictionary(s => s.GetProperty("playerId").GetString()!, s => s.GetProperty("token").GetString()!);

            // One illegal move - recorded with its error, and replayed as one.
            var bogus = await Send(http, gameId, tokens["teamA"], "purchase", new { dieId = "no-such-die", energyDieIds = Array.Empty<string>() });
            Assert.False(bogus.IsSuccessStatusCode);

            // Six turns of bot-vs-bot (two turn-end writes at least).
            var turnsSeen = 0;
            string? lastActive = null;
            for (var guard = 0; guard < 3000 && turnsSeen < 6; guard++)
            {
                var moved = false;
                foreach (var (seat, token) in tokens)
                {
                    using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/games/{gameId}/bot-decision");
                    get.Headers.Add("X-Seat-Token", token);
                    var response = await http.SendAsync(get);
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent) continue;
                    var d = await response.Content.ReadFromJsonAsync<JsonElement>();
                    var result = await Act(http, gameId, token, d);
                    Assert.True(result.IsSuccessStatusCode, $"{d.GetProperty("kind")}: {await result.Content.ReadAsStringAsync()}");
                    var state = await result.Content.ReadFromJsonAsync<JsonElement>();
                    var active = state.GetProperty("activePlayerId").GetString();
                    if (active != lastActive) { turnsSeen++; lastActive = active; }
                    moved = true;
                    break;
                }
                Assert.True(moved, "Neither seat had a move.");
            }
        } // disposing the server flushes the write queue

        var file = Assert.Single(Directory.GetFiles(_folder, $"{gameId}.json", SearchOption.AllDirectories));
        var record = GameRecordJson.Deserialize(File.ReadAllText(file));

        Assert.Equal([customTeam && !withChampion ? null : "Wolf", "Armadillo"], record.Seats.Select(s => s.ChampionId));
        if (customTeam)
        {
            Assert.All(record.Seats[0].CardDiceCounts!.Where(kv => kv.Key != DiceFight.V2.Data.DiceKingdomConfig.AngerIssues.Id).Select(kv => kv.Value), n => Assert.Equal(2, n));
            Assert.Equal(new Dictionary<string, int> { ["Claw"] = 6, ["Eye"] = 4 }, record.Seats[0].Tardigrades);
        }
        Assert.All(record.Seats, s => Assert.True(s.Bot)); // both asked for the computer's move
        Assert.True(record.Turns >= 5);
        var first = record.Actions[0];
        Assert.Equal("Purchase", first.Action);
        Assert.NotNull(first.Error);
        Assert.Equal("no-such-die", first.Request!.Value.GetProperty("dieId").GetString());
        Assert.Contains(record.Actions, a => a.Action == "DeclareAttackers");
        Assert.NotEmpty(record.Log);

        var outcome = GameReplayer.Replay(record);
        Assert.Null(outcome.Divergence);
        Assert.Equal(record.Actions.Count, outcome.ActionsReplayed);
        Assert.Equal(record.Result!.Life["teamA"], outcome.State.PlayerOne.Life);
    }

    [Fact]
    public void A_Replay_Says_Where_It_Drifted()
    {
        var record = new GameRecord
        {
            GameId = "drift", CreatedUtc = DateTime.UtcNow, Seed = 1,
            Seats =
            [
                new RecordedSeat { PlayerId = "teamA", ChampionId = "Wolf", TeamCardIds = [.. DiceFight.V2.Data.DiceKingdomConfig.CharactersByChampion["Wolf"]] },
                new RecordedSeat { PlayerId = "teamB", ChampionId = "Armadillo", TeamCardIds = [.. DiceFight.V2.Data.DiceKingdomConfig.CharactersByChampion["Armadillo"]] },
            ],
            Actions = [new RecordedAction { Seq = 1, AtUtc = DateTime.UtcNow, PlayerId = "teamA", Action = "ClearAndDraw", Turn = 1, Step = "start-of-turn", Check = "00000000" }],
        };

        var outcome = GameReplayer.Replay(record);

        Assert.Equal(0, outcome.ActionsReplayed);
        Assert.Contains("Action 1 (ClearAndDraw)", outcome.Divergence);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient http, string gameId, string token, string path, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/games/{gameId}/{path}")
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add("X-Seat-Token", token);
        return await http.SendAsync(request);
    }

    // As web/src/dicekingdom/bot.ts's botDecisionCall maps a decision onto
    // its endpoint.
    private static Task<HttpResponseMessage> Act(HttpClient http, string gameId, string token, JsonElement d)
    {
        string? Str(string name) => d.GetProperty(name).ValueKind == JsonValueKind.Null ? null : d.GetProperty(name).GetString();
        var ids = (string name) => d.GetProperty(name).EnumerateArray().Select(x => x.GetString()).ToArray();
        return d.GetProperty("kind").GetString() switch
        {
            "clearAndDraw" => Send(http, gameId, token, "clear-and-draw", null),
            "roll" => Send(http, gameId, token, "roll", null),
            "reroll" => Send(http, gameId, token, "reroll", new { dieIds = ids("dieIds") }),
            "finishRoll" => Send(http, gameId, token, "finish-roll", null),
            "foresight" => Send(http, gameId, token, "foresight", new { dieId = Str("dieId") }),
            "field" => Send(http, gameId, token, "field", new { dieId = Str("dieId"), energyDieIds = ids("energyDieIds"), free = d.GetProperty("free").GetBoolean() }),
            "useChampionPower" => Send(http, gameId, token, "champion-power", null),
            "purchase" => Send(http, gameId, token, "purchase", new { dieId = Str("dieId"), energyDieIds = ids("energyDieIds") }),
            "useAction" => Send(http, gameId, token, "use-action", new { dieId = Str("dieId") }),
            "useGlobal" => Send(http, gameId, token, "use-global", new { cardId = Str("cardId"), abilityIndex = d.GetProperty("abilityIndex").GetInt32(), energyDieIds = ids("energyDieIds") }),
            "pass" => Send(http, gameId, token, d.GetProperty("skipAttack").GetBoolean() ? "skip-attack-step" : "pass", null),
            "declareAttackers" => Send(http, gameId, token, "declare-attackers", new { attackers = d.GetProperty("attackers") }),
            "declareBlockers" => Send(http, gameId, token, "declare-blockers", new { assignments = d.GetProperty("assignments") }),
            "resolvePendingChoice" => Send(http, gameId, token, "resolve-pending-choice", new { chosenDieIds = ids("dieIds") }),
            "cleanUp" => Send(http, gameId, token, "clean-up", null),
            var k => throw new InvalidOperationException($"Unknown bot decision {k}"),
        };
    }
}
