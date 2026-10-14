using DiceFight.V2;
using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

// Win-rate round-robin for the Dice Kingdom Champions: every pairing,
// GamesPerMatchup games each, seats alternated to cancel first-player
// advantage. Both seats are played by the SAME bot the web client's
// "vs computer" opponent uses (DiceFight.V2/Bot/DiceKingdomBot.cs), one
// decision at a time through BotDriver - the same step/priority machinery
// a browser game walks through - so the numbers describe the opponent
// people actually play, and isolate matchup/card-design skew from bot
// skill. Teams are built the way V2GamesController builds them: the
// Champion's 8 Characters plus its Basic Action.
//
// Run (from this folder): dotnet run -c Release
// Environment switches:
//   SIM_LEGACY_POWERS=on  with SIM_POWERS=on: the pre-2026-10-04 always-on
//                    passives instead of the once-per-turn powers
//   SIM_WOLF_PUMP=N  with SIM_POWERS=on: Wolf's pump is +N ATK instead
//   SIM_WOLF_MAIN_ONLY=on  ...and only usable in Main, before attacking
//   SIM_OWL_LEVELS=N  with SIM_POWERS=on: Owl's spin moves N levels
//   SIM_POWERS=only  the WITH-powers pass alone
//   SIM_ONLY=A,B     only the A-vs-B matchup (Champion ids)
//   SIM_MUSKOX_GLOBAL=on  Musk Ox gets Kree Captain's purchase-discount Global
//   SIM_DILUTION=N   bot persona's DilutionPerDie (each purchased die costs N plan value)
//   SIM_DUMP_ALL=1   print every game's match log
//   SIM_POWERS=on    also run a pass WITH Champion passives (default:
//                    powers-off only - every passive's Amount zeroed and
//                    Foresight removed)
//   SIM_GAMES=N      games per matchup (default 200)
//   SIM_SWEEP=all|id,id  card sweep instead of the round robin: each card
//                    that's in the Catalog but on no starter roster goes
//                    into the roster of its energy's Champion, replacing
//                    the own-energy card nearest in cost, and that team
//                    plays all four starter rosters (powers on). Reports
//                    the win-rate change against the unmodified roster.
//                    A screen for outliers, not a verdict - see
//                    v3/DESIGN_NOTES.md 2026-10-14.
//   SIM_DUMP=N       print the full match log of game N of each matchup
//   SIM_SWAP=Champ:oldId=newId;...  what-if roster swap, e.g.
//                    Armadillo:DK-SHELL-02=DK-SHELL-06 (Musk Ox -> Queen Termite)
//   SIM_PERSONAS=off play every seat with the Default persona instead of
//                    its Champion's (BotPersona.ForChampion)
//   SIM_NO_GLOBALS=id,id  strip the Global abilities off these card ids
//   SIM_BULWARK_DEF=N  what-if: the Tardigrade's Bulwark face (L3, no
//                    energy - 1/2 live) gets N DEF instead
//                    (e.g. DK-ACT-03 = Resurrection) - a what-if switch
//   SIM_TARDIGRADE=spec  what-if: rebuild every Tardigrade die from six
//                    comma-separated faces, each `energy[:level/atk/def]`
//                    where energy is 2, 1, 0 or W (one Wild). Today's die
//                    (2026-10-04): 2,2,1,W:2/1/1,0:3/1/2,W:2/1/1
//
// Known limitation: two walled-off boards can stall (blocked damage
// clears every Clean Up); games that hit MaxTurns are decided on a life
// tiebreak and counted separately.

var championIds = new[] { "Wolf", "Armadillo", "GreatHornedOwl", "GoldenEagle" };
var displayName = new Dictionary<string, string> { ["Wolf"] = "Wolf", ["Armadillo"] = "Armadillo", ["GreatHornedOwl"] = "Owl", ["GoldenEagle"] = "Eagle" };

var gamesPerMatchup = int.TryParse(Environment.GetEnvironmentVariable("SIM_GAMES"), out var n) ? n : 200;
var dumpGame = int.TryParse(Environment.GetEnvironmentVariable("SIM_DUMP"), out var dg) ? dg : -1;
const int MaxTurns = 50; // safety valve - two walled-off boards can stall

var matchups = new List<(string A, string B)>();
for (var i = 0; i < championIds.Length; i++)
    for (var j = i + 1; j < championIds.Length; j++)
        matchups.Add((championIds[i], championIds[j]));

if (Environment.GetEnvironmentVariable("SIM_SWEEP") is { Length: > 0 } sweep)
{
    Sweep.Run(sweep, championIds, displayName, gamesPerMatchup, BuildPlayer, StableHash);
    return;
}

// SIM_ONLY=ChampA,ChampB - just that one matchup.
if (Environment.GetEnvironmentVariable("SIM_ONLY") is { Length: > 0 } only)
{
    var pair = only.Split(',');
    matchups.RemoveAll(m => !(pair.Contains(m.A) && pair.Contains(m.B)));
}

if (Environment.GetEnvironmentVariable("SIM_POWERS") is "on" or "only")
{
    Console.WriteLine("############ WITH champion powers ############");
    RunAllMatchups(championPowersEnabled: true);
    Console.WriteLine();
}
if (Environment.GetEnvironmentVariable("SIM_POWERS") == "only") return;
Console.WriteLine("############ WITHOUT champion powers (Amounts zeroed, Foresight removed) ############");
RunAllMatchups(championPowersEnabled: false);

void RunAllMatchups(bool championPowersEnabled)
{
    Stats.Reset();
    foreach (var (champA, champB) in matchups)
    {
        int winsA = 0, winsB = 0, timeouts = 0, errors = 0;
        var turnCounts = new List<int>();

        for (var g = 0; g < gamesPerMatchup; g++)
        {
            var aGoesFirst = g % 2 == 0;
            var seatOneChamp = aGoesFirst ? champA : champB;
            var seatTwoChamp = aGoesFirst ? champB : champA;

            // StableHash, not string.GetHashCode() - .NET randomizes the
            // latter per process, which made reruns non-reproducible.
            var rng = new Random(unchecked(StableHash(champA) * 397 ^ StableHash(champB) * 31 ^ g ^ 0x5EED));
            try
            {
                Stats.BeginGame(seatOneChamp, seatTwoChamp);
                var (winner, turns) = PlayOneGame(seatOneChamp, seatTwoChamp, rng, championPowersEnabled, dump: g == dumpGame || Environment.GetEnvironmentVariable("SIM_DUMP_ALL") == "1");
                Stats.EndGame(winner, seatOneChamp, seatTwoChamp, turns);
                turnCounts.Add(turns);
                if (winner == champA) winsA++;
                else if (winner == champB) winsB++;
                else timeouts++;
            }
            catch (Exception ex)
            {
                errors++;
                if (errors <= 3) Console.Error.WriteLine($"[{champA} vs {champB} game {g}] ERROR: {ex.Message}");
            }
        }

        var decided = Math.Max(1, winsA + winsB);
        Console.WriteLine($"=== {displayName[champA]} vs {displayName[champB]} ({gamesPerMatchup} games) ===");
        Console.WriteLine($"  {displayName[champA]}: {winsA} wins ({100.0 * winsA / decided:F1}%)   {displayName[champB]}: {winsB} wins ({100.0 * winsB / decided:F1}%)");
        Console.WriteLine($"  exact-tie timeouts: {timeouts}, errors (excluded): {errors}, avg game length: {(turnCounts.Count > 0 ? turnCounts.Average() : 0):F1} turns");
    }
    Console.WriteLine();
    Stats.Report(displayName);
}

// ---------------------------------------------------------------------

static (string WinnerChampionIdOrSentinel, int Turns) PlayOneGame(
    string championOne, string championTwo, Random rng, bool championPowersEnabled, bool dump)
{
    var config = championPowersEnabled
        ? (Environment.GetEnvironmentVariable("SIM_LEGACY_POWERS") == "on" ? WithLegacyPowers(DiceKingdomConfig.Config) : DiceKingdomConfig.Config)
        : StripChampionPowers(DiceKingdomConfig.Config);
    // SIM_WOLF_PUMP=N - what-if: Wolf's once-per-turn pump is +N ATK.
    if (championPowersEnabled && int.TryParse(Environment.GetEnvironmentVariable("SIM_WOLF_PUMP"), out var pump))
        config = config with { Champions = config.Champions.Select(c => c.Id == "Wolf" ? c with { Amount = pump } : c).ToList() };
    // SIM_OWL_LEVELS=N - what-if: Owl's spin moves N levels.
    if (championPowersEnabled && int.TryParse(Environment.GetEnvironmentVariable("SIM_OWL_LEVELS"), out var owlLevels))
        config = config with { Champions = config.Champions.Select(c => c.Id == "GreatHornedOwl" ? c with { Amount = owlLevels } : c).ToList() };
    // SIM_WOLF_MAIN_ONLY=on - what-if: Wolf's pump only before attacking.
    if (championPowersEnabled && Environment.GetEnvironmentVariable("SIM_WOLF_MAIN_ONLY") == "on")
        config = config with { Champions = config.Champions.Select(c => c.Id == "Wolf" ? c with { PowerBeforeBlocksOnly = true } : c).ToList() };
    if (int.TryParse(Environment.GetEnvironmentVariable("SIM_BULWARK_DEF"), out var bulwarkDef))
        config = WithBulwarkDefense(config, bulwarkDef);
    if (Environment.GetEnvironmentVariable("SIM_TARDIGRADE") is { Length: > 0 } tardigradeSpec)
        config = WithTardigradeFaces(config, tardigradeSpec);
    var state = GameSetup.NewGame(config, Catalog.Cards, BuildPlayer("p1", championOne), BuildPlayer("p2", championTwo));
    var driver = new BotDriver(state, rng);
    Priority.Sync(state);

    // Dice the engine rejected this turn (a rule the bot doesn't model) -
    // the web client keeps the same list (botSkipIdsRef).
    var skip = new HashSet<string>();
    var turn = 1;
    var lastActive = state.ActivePlayerId;

    for (var guard = 0; guard < 20_000; guard++)
    {
        if (state.PlayerOne.Life <= 0 || state.PlayerTwo.Life <= 0) break;
        if (state.ActivePlayerId != lastActive)
        {
            lastActive = state.ActivePlayerId;
            skip.Clear();
            if (++turn > MaxTurns) break;
        }

        var owner = driver.DecisionOwner();
        var persona = Environment.GetEnvironmentVariable("SIM_PERSONAS") == "off" ? BotPersona.Default : BotPersona.ForChampion(state.GetPlayer(owner).ChampionId);
        // SIM_DILUTION=N - what-if: each die a purchase adds to the Bag costs N plan value.
        if (double.TryParse(Environment.GetEnvironmentVariable("SIM_DILUTION"), System.Globalization.CultureInfo.InvariantCulture, out var dilution))
            persona = persona with { DilutionPerDie = dilution };
        var decision = DiceKingdomBot.Decide(state, owner, skip, persona)
            ?? throw new InvalidOperationException($"Bot had no decision for {owner} at step {state.CurrentStepId}.");
        var lifeBefore = (state.PlayerOne.Life, state.PlayerTwo.Life);
        if (state.PendingChoice?.Intent == ChoiceIntent.NameCard && decision.DieIds.Count > 0)
            Stats.Named(state, owner, decision.DieIds[0]);
        try
        {
            driver.Apply(owner, decision);
        }
        catch (InvalidOperationException ex) when (decision.DieId is not null)
        {
            if (dump) Console.WriteLine($"  [rejected] {decision.Kind} {decision.DieId}: {ex.Message}");
            skip.Add(decision.DieId);
            continue;
        }
        Stats.Record(state, owner, decision, lifeBefore);
    }

    Stats.CombatBreakdown(state);
    if (dump) DumpLog(state, championOne, championTwo);

    var p1Dead = state.PlayerOne.Life <= 0;
    var p2Dead = state.PlayerTwo.Life <= 0;
    if (p1Dead && p2Dead) return ("__draw__", turn);
    if (p1Dead) return (championTwo, turn);
    if (p2Dead) return (championOne, turn);
    // Turn cap - higher life wins; an exact tie is a real timeout.
    Stats.CapHit();
    if (state.PlayerOne.Life == state.PlayerTwo.Life) return ("__timeout__", turn);
    return (state.PlayerOne.Life > state.PlayerTwo.Life ? championOne : championTwo, turn);
}

static Player BuildPlayer(string id, string championId, IReadOnlyDictionary<string, string>? swapOverride = null)
{
    var player = new Player { Id = id, Name = championId, ChampionId = championId };
    var swaps = swapOverride ?? (Environment.GetEnvironmentVariable("SIM_SWAP") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Split(':')).Where(s => s[0] == championId)
        .Select(s => s[1].Split('=')).ToDictionary(s => s[0], s => s[1]);
    player.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[championId].Select(id => swaps.GetValueOrDefault(id, id)));
    player.TeamCardIds.Add(swaps.GetValueOrDefault(DiceKingdomConfig.ActionByChampion[championId], DiceKingdomConfig.ActionByChampion[championId]));
    return player;
}

// Neutralizes every Champion's power without touching team shape
// (TardigradePool and EnergySymbolId stay, so own-energy Characters are
// bought and fielded exactly as before): every kind becomes a +0 ATK buff.
static GameConfig StripChampionPowers(GameConfig config) =>
    config with
    {
        Champions = config.Champions.Select(c => c with { Amount = 0, PassiveKind = ChampionPassiveKind.AttackBuff }).ToList(),
    };

// SIM_LEGACY_POWERS=on - the always-on passives the once-per-turn powers
// replaced on 2026-10-04, for an old-vs-new comparison.
static GameConfig WithLegacyPowers(GameConfig config) =>
    config with
    {
        Champions = config.Champions.Select(c => c.Id switch
        {
            "Wolf" => c with { PassiveKind = ChampionPassiveKind.AttackBuff, Amount = 1 },
            "Armadillo" => c with { PassiveKind = ChampionPassiveKind.DefenseBuff, Amount = 1 },
            "GoldenEagle" => c with { PassiveKind = ChampionPassiveKind.FieldingCostDiscount, Amount = 1 },
            "GreatHornedOwl" => c with { PassiveKind = ChampionPassiveKind.Foresight, Amount = 1 },
            _ => c,
        }).ToList(),
    };

// The Bulwark is the Tardigrade die's stats-only level-3 face (the one
// face with a creature and no energy); rebuild each Champion's Tardigrade
// pool with its DEF replaced.
static GameConfig WithBulwarkDefense(GameConfig config, int defense) =>
    config with
    {
        Champions = config.Champions.Select(c => c with
        {
            TardigradePool = c.TardigradePool.Select(entry => entry with
            {
                Die = entry.Die with
                {
                    Faces = entry.Die.Faces.Select(f => f.Character is { Level: 3 } ch && f.SymbolCount == 0
                        ? f with { Character = ch with { Defense = defense } }
                        : f).ToList(),
                },
            }).ToList(),
        }).ToList(),
    };

// SIM_TARDIGRADE - see the header. The energy type is the die's own
// (TardigradeClaw -> Claw).
static GameConfig WithTardigradeFaces(GameConfig config, string spec) =>
    config with
    {
        Champions = config.Champions.Select(c => c with
        {
            TardigradePool = c.TardigradePool.Select(entry => entry with
            {
                Die = entry.Die with
                {
                    Faces = spec.Split(',').Select(f =>
                    {
                        var parts = f.Trim().Split(':');
                        var type = entry.Die.Id["Tardigrade".Length..];
                        List<SymbolAmount> energy = parts[0] switch
                        {
                            "W" => [new SymbolAmount("Wild", 1)],
                            "0" => [],
                            var k => [new SymbolAmount(type, int.Parse(k))],
                        };
                        if (parts.Length == 1) return new Face(energy, Kind: FaceKind.EnergyFace);
                        var st = parts[1].Split('/').Select(int.Parse).ToArray();
                        return new Face(energy, new CharacterFaceData(st[0], FieldingCost: 0, Attack: st[1], Defense: st[2]), Kind: FaceKind.CharacterFace);
                    }).ToList(),
                },
            }).ToList(),
        }).ToList(),
    };

static int StableHash(string s)
{
    unchecked
    {
        var h = (int)2166136261;
        foreach (var ch in s) h = (h ^ ch) * 16777619;
        return h;
    }
}

static void DumpLog(GameState state, string one, string two)
{
    Console.WriteLine($"--- match log: p1={one} p2={two} ---");
    foreach (var entry in state.Log)
        Console.WriteLine($"[{entry.PlayerId ?? "-"}] {entry.Text}");
    Console.WriteLine($"Final life: p1={state.PlayerOne.Life} p2={state.PlayerTwo.Life}");
}

// SIM_SWEEP - see the header. Its own game loop: Stats is single-threaded
// and the sweep runs candidates in parallel.
static class Sweep
{
    const int MaxTurns = 50;

    static Func<string, string, IReadOnlyDictionary<string, string>?, Player> BuildPlayer = null!;
    static Func<string, int> StableHash = null!;

    public static void Run(string which, string[] championIds, Dictionary<string, string> names, int games,
        Func<string, string, IReadOnlyDictionary<string, string>?, Player> buildPlayer, Func<string, int> stableHash)
    {
        BuildPlayer = buildPlayer;
        StableHash = stableHash;
        var rosters = DiceKingdomConfig.CharactersByChampion;
        var onRoster = rosters.Values.SelectMany(r => r).Concat(DiceKingdomConfig.ActionByChampion.Values).ToHashSet();
        var energyChamp = DiceKingdomConfig.Champions.ToDictionary(c => c.EnergySymbolId, c => c.Id);
        var candidates = Catalog.Cards.Values
            .Where(c => which == "all" ? !onRoster.Contains(c.Id) : which.Split(',').Contains(c.Id))
            .Where(c => c.CardType is CardType.Character or CardType.Action && c.EnergySymbolIds.Count == 1 || c.CardType == CardType.BasicAction)
            .OrderBy(c => c.EnergySymbolIds.FirstOrDefault()).ThenBy(c => c.PurchaseCost).ToList();

        // A Character or Action replaces the own-energy roster card nearest
        // its cost (ties: the cheaper), so the team keeps its shape. A Basic
        // Action has no energy: it replaces each Champion's Basic Action in
        // turn, one row per Champion.
        var slots = candidates.SelectMany(c => c.CardType == CardType.BasicAction
            ? championIds.Select(h => (Card: c, Host: h, Replaced: DiceKingdomConfig.ActionByChampion[h]))
            : [(Card: c, Host: energyChamp[c.EnergySymbolIds[0]], Replaced: rosters[energyChamp[c.EnergySymbolIds[0]]]
                .Select(id => Catalog.Cards[id]).Where(r => r.EnergySymbolIds.Contains(c.EnergySymbolIds[0]))
                .OrderBy(r => Math.Abs(r.PurchaseCost - c.PurchaseCost)).ThenBy(r => r.PurchaseCost).First().Id)]).ToList();
        foreach (var (c, host, replaced) in slots)
            Console.WriteLine($"  {c.Id} {c.Name} ({c.CardType}, {c.EnergySymbolIds.FirstOrDefault() ?? "no energy"} {c.PurchaseCost}) -> {names[host]} replacing {Catalog.Cards[replaced].Name}  {Faces(c)} vs {Faces(Catalog.Cards[replaced])}");
        if (games == 0) return;
        Console.WriteLine($"Sweep: {candidates.Count} cards, {games} games vs each of {championIds.Length} starter rosters, Champion powers on.");
        var baseline = championIds.AsParallel().Select(h => (h, R: Play(h, null, championIds, games))).ToDictionary(x => x.h, x => x.R);
        foreach (var h in championIds)
            Console.WriteLine($"  baseline {names[h],-9} {100 * baseline[h].WinRate,5:F1}%  ({baseline[h].Errors} errors, {100.0 * baseline[h].CapHits / games / championIds.Length:F1}% hit the turn cap)");

        var results = new (CardDef Card, string Host, string Replaced, Result R)[slots.Count];
        Parallel.For(0, slots.Count, i =>
        {
            var (card, host, replaced) = slots[i];
            results[i] = (card, host, replaced, Play(host, (replaced, card.Id), championIds, games));
        });

        var se = 100 * Math.Sqrt(0.25 / (games * championIds.Length)) * Math.Sqrt(2);
        Console.WriteLine($"\nchange = swapped team's win% minus the unmodified roster's; one standard error ~ {se:F1} points.");
        Console.WriteLine($"{"card",-22}{"rarity",-11}{"cost",5}  {"host",-10}{"replaces",-18}{"win%",7}{"change",8}{"buys/g",8}{"fielded",9}{"cap%",6}{"err",5}");
        foreach (var (card, host, replaced, r) in results.OrderByDescending(x => x.R.WinRate - baseline[x.Host].WinRate))
        {
            var rarity = DiceKingdomPacks.Rarities.TryGetValue(card.Id, out var rr) ? rr.ToString() : "-";
            Console.WriteLine($"{card.Name,-22}{rarity,-11}{card.PurchaseCost,5}  {names[host],-10}{Catalog.Cards[replaced].Name,-18}" +
                $"{100 * r.WinRate,6:F1}%{100 * (r.WinRate - baseline[host].WinRate),+8:+0.0;-0.0}" +
                $"{(double)r.Buys / r.Games,8:F2}{100.0 * r.GamesFielded / r.Games,8:F0}%{100.0 * r.CapHits / r.Games,5:F0}%{r.Errors,5}");
        }
    }

    // Character levels as fielding cost:ATK/DEF, e.g. "L1 1:2/2 L2 2:3/3".
    static string Faces(CardDef c) => string.Join(" ", c.Die.Faces.Where(f => f.Character is not null)
        .Select(f => f.Character!).DistinctBy(f => f.Level).OrderBy(f => f.Level)
        .Select(f => $"L{f.Level} {f.FieldingCost}:{f.Attack}/{f.Defense}"));

    public sealed record Result(double WinRate, int Games, int Buys, int GamesFielded, int CapHits, int Errors);

    // The host roster (with the swap, if any) against every starter roster,
    // seats alternated; the same seeds for every candidate, so the baseline
    // and each swap see the same opening dice.
    static Result Play(string host, (string Old, string New)? swap, string[] opponents, int games)
    {
        int wins = 0, decided = 0, played = 0, buys = 0, fielded = 0, caps = 0, errors = 0;
        var swaps = swap is { } s ? new Dictionary<string, string> { [s.Old] = s.New } : new Dictionary<string, string>();
        foreach (var opp in opponents)
            for (var g = 0; g < games; g++)
            {
                var hostFirst = g % 2 == 0;
                var rng = new Random(unchecked(StableHash(host) * 397 ^ StableHash(opp) * 31 ^ g ^ 0x5EED));
                try
                {
                    var hostPlayer = BuildPlayer(hostFirst ? "p1" : "p2", host, swaps);
                    var oppPlayer = BuildPlayer(hostFirst ? "p2" : "p1", opp, new Dictionary<string, string>());
                    var (hostWon, oppWon, b, f, cap) = Game(hostFirst ? hostPlayer : oppPlayer, hostFirst ? oppPlayer : hostPlayer,
                        hostPlayer.Id, swap?.New, rng);
                    played++; buys += b; fielded += f ? 1 : 0; caps += cap ? 1 : 0;
                    if (hostWon || oppWon) decided++;
                    if (hostWon) wins++;
                }
                catch (Exception)
                {
                    errors++;
                }
            }
        return new((double)wins / Math.Max(1, decided), played, buys, fielded, caps, errors);
    }

    static (bool HostWon, bool OppWon, int Buys, bool Fielded, bool Cap) Game(Player one, Player two, string hostId, string? watch, Random rng)
    {
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, Catalog.Cards, one, two);
        var driver = new BotDriver(state, rng);
        Priority.Sync(state);
        var skip = new HashSet<string>();
        int turn = 1, buys = 0;
        var fielded = false;
        var lastActive = state.ActivePlayerId;
        for (var guard = 0; guard < 20_000; guard++)
        {
            if (state.PlayerOne.Life <= 0 || state.PlayerTwo.Life <= 0) break;
            if (state.ActivePlayerId != lastActive)
            {
                lastActive = state.ActivePlayerId;
                skip.Clear();
                if (++turn > MaxTurns) break;
            }
            var owner = driver.DecisionOwner();
            var decision = DiceKingdomBot.Decide(state, owner, skip, BotPersona.ForChampion(state.GetPlayer(owner).ChampionId))
                ?? throw new InvalidOperationException($"Bot had no decision for {owner} at step {state.CurrentStepId}.");
            if (owner == hostId && watch is not null && decision.DieId is { } dieId &&
                decision.Kind is BotActionKind.Purchase or BotActionKind.Field or BotActionKind.UseAction &&
                state.Dice.First(d => d.Id == dieId).CardId == watch)
            {
                if (decision.Kind == BotActionKind.Purchase) buys++;
                else fielded = true;
            }
            try { driver.Apply(owner, decision); }
            catch (InvalidOperationException) when (decision.DieId is not null) { skip.Add(decision.DieId); }
        }
        var host = state.GetPlayer(hostId);
        var opp = hostId == "p1" ? state.PlayerTwo : state.PlayerOne;
        var cap = host.Life > 0 && opp.Life > 0;
        bool hostWon = opp.Life <= 0 && host.Life > 0 || cap && host.Life > opp.Life;
        bool oppWon = host.Life <= 0 && opp.Life > 0 || cap && opp.Life > host.Life;
        return (hostWon, oppWon, buys, fielded, cap);
    }
}

static class Catalog
{
    public static IReadOnlyDictionary<string, CardDef> Cards => Built.Value;
    private static readonly Lazy<IReadOnlyDictionary<string, CardDef>> Built = new(() =>
    {
        var strip = (Environment.GetEnvironmentVariable("SIM_NO_GLOBALS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var cards = DiceKingdomConfig.Catalog.ToDictionary(kv => kv.Key, kv => strip.Contains(kv.Key)
            ? kv.Value with { Abilities = kv.Value.Abilities.Where(a => a.Trigger != TriggerKind.Global).ToList() }
            : kv.Value);
        foreach (var proto in Prototypes.All) cards[proto.Id] = proto;
        // SIM_MUSKOX_GLOBAL=on - what-if: Musk Ox carries Kree Captain's
        // purchase-discount Global again (on 2026-10-04, then taken off).
        if (Environment.GetEnvironmentVariable("SIM_MUSKOX_GLOBAL") == "on")
            cards[DiceKingdomConfig.MuskOx.Id] = DiceKingdomConfig.WithKreeCaptainGlobal(cards[DiceKingdomConfig.MuskOx.Id]);
        return cards;
    });
}

// Candidate cards under evaluation - simulator-only, NOT in the live
// catalog or any real roster; reach them with SIM_SWAP. Promote one into
// DiceKingdomConfig only once the user has seen the numbers. (The lockout
// Pangolin started here and went live 2026-09-29.) Build a variant from a
// live card, e.g. `DiceKingdomConfig.HermitCrab with { Id = "PROTO-...", ... }`.
static class Prototypes
{
    public static readonly CardDef[] All = [];
}

static class Stats
{
    static string[] _champ = new string[2]; // p1, p2 champion ids this game
    static readonly HashSet<(string P, string Card)> _fieldedThisGame = [];
    static readonly Dictionary<(string Champ, string Card), int> Buys = [];
    static readonly Dictionary<(string Champ, string Card), int> Fields = [];
    static readonly Dictionary<(string Champ, string Card), int> GamesFielded = [];
    static readonly Dictionary<(string Champ, string Card), int> WinsWhenFielded = [];
    static readonly Dictionary<(string Champ, string Src), int> DamageDealt = [];
    static readonly Dictionary<(string Champ, string What), int> Uses = [];
    static readonly Dictionary<string, int> Games = [], Wins = [], Rerolls = [];
    // What combat damage is made of (2026-10-04, user question: does Wolf win
    // through the 2+ attackers = Overcrush lane rule, or just chip damage?).
    static readonly Dictionary<(string Champ, string What), int> Combat = [];
    static int _seatOneWins, _decided, _capHits, _deckOutDeaths;

    static string C(string pid) => _champ[pid == "p1" ? 0 : 1];
    static void Inc<K>(Dictionary<K, int> d, K k, int by = 1) where K : notnull => d[k] = d.GetValueOrDefault(k) + by;

    public static void Reset()
    {
        Buys.Clear(); Fields.Clear(); GamesFielded.Clear(); WinsWhenFielded.Clear(); DamageDealt.Clear();
        Uses.Clear(); Combat.Clear(); Games.Clear(); Wins.Clear(); Rerolls.Clear(); NamedCards.Clear(); CapWins.Clear();
        _seatOneWins = _decided = _capHits = _deckOutDeaths = 0;
    }

    public static void BeginGame(string one, string two) { _champ = [one, two]; _fieldedThisGame.Clear(); _capThisGame = false; }
    static bool _capThisGame;
    static readonly Dictionary<string, int> CapWins = [];
    public static void CapHit() { _capHits++; _capThisGame = true; }
    static readonly Dictionary<(string Champ, string Card), int> NamedCards = [];
    public static void Named(GameState s, string pid, string dieId) =>
        Inc(NamedCards, (C(pid), s.CardCatalog[s.Dice.First(d => d.Id == dieId).CardId!].Name));

    // Unblocked hits and Overcrush carry-through, read off the match log
    // (each line is logged by the attacking player).
    public static void CombatBreakdown(GameState s)
    {
        foreach (var e in s.Log)
        {
            if (e.PlayerId is not { } pid) continue;
            var hit = System.Text.RegularExpressions.Regex.Match(e.Text, @" hits .* directly for (\d+)\.$");
            if (hit.Success) Inc(Combat, (C(pid), "unblocked hits"), int.Parse(hit.Groups[1].Value));
            var over = System.Text.RegularExpressions.Regex.Match(e.Text, @"^Overcrush: (\d+) excess");
            if (over.Success) Inc(Combat, (C(pid), "Overcrush carry-through"), int.Parse(over.Groups[1].Value));
        }
    }

    public static void Record(GameState s, string pid, BotDecision d, (int P1, int P2) lifeBefore)
    {
        if (d.Kind == BotActionKind.DeclareAttackers && d.AttackerLanes.Count > 0)
        {
            Inc(Combat, (C(pid), "#attack declarations"));
            Inc(Combat, (C(pid), "#attackers"), d.AttackerLanes.Count);
            Inc(Combat, (C(pid), "#lanes with 2+ attackers (auto-Overcrush)"), d.AttackerLanes.GroupBy(kv => kv.Value).Count(g => g.Count() >= 2));
        }
        // The defender's side of each attack: blockers it had vs used, keyed
        // by the ATTACKER's Champion ("vs" = what that Champion's attacks met).
        if (d.Kind == BotActionKind.DeclareBlockers && s.DiceIn(s.ActivePlayerId, Zone.AttackZone).Any())
        {
            var attacker = C(s.ActivePlayerId);
            var able = s.DiceIn(pid, Zone.FieldZone).Count(x => s.GetCurrentFace(x)?.Character is not null && !x.CombatFlags.Contains(CombatFlagKind.CantBlock));
            Inc(Combat, (attacker, "#attacks met"));
            Inc(Combat, (attacker, "#attacks met with no blocker available"), able == 0 ? 1 : 0);
            Inc(Combat, (attacker, "#blockers available"), able);
            Inc(Combat, (attacker, "#blocks made"), d.Blocks.Select(b => b.BlockerId).Distinct().Count());
        }
        if (d.Kind == BotActionKind.UseChampionPower && ChampionPowers.Of(s, pid)?.PassiveKind == ChampionPassiveKind.PumpOneAttack)
        {
            var blocks = s.DeclaredBlocks;
            var mine = s.DiceIn(pid, Zone.AttackZone).ToList();
            var blockedLanes = mine.Where(a => blocks?.BlockersOf(a.Id).Count > 0).Select(a => a.Lane).ToHashSet();
            Inc(Combat, (C(pid), mine.Any(a => !blockedLanes.Contains(a.Lane)) ? "#pump on an unblocked attacker" : "#pump on a blocked attacker"));
        }
        string Name(string? dieId) => s.Dice.First(x => x.Id == dieId) is { CardId: { } c } ? s.CardCatalog[c].Name : "Tardigrade";
        switch (d.Kind)
        {
            case BotActionKind.Purchase: Inc(Buys, (C(pid), Name(d.DieId))); break;
            case BotActionKind.Field:
                if (d.Free) Inc(Uses, (C(pid), "champion power (free field)"));
                Inc(Fields, (C(pid), Name(d.DieId)));
                _fieldedThisGame.Add((pid, Name(d.DieId)));
                break;
            case BotActionKind.UseAction: Inc(Uses, (C(pid), $"action: {Name(d.DieId)}")); break;
            case BotActionKind.UseGlobal: Inc(Uses, (C(pid), $"global: {s.CardCatalog[d.CardId!].Name}")); break;
            case BotActionKind.Foresight: Inc(Uses, (C(pid), "Foresight")); break;
            case BotActionKind.UseChampionPower: Inc(Uses, (C(pid), "champion power")); break;
            case BotActionKind.Reroll: Inc(Rerolls, C(pid), d.DieIds.Count); break;
        }

        var source = d.Kind switch
        {
            BotActionKind.Pass or BotActionKind.DeclareAttackers or BotActionKind.DeclareBlockers => "combat",
            BotActionKind.CleanUp => "end of turn (Basilisk)",
            BotActionKind.ClearAndDraw => "opponent's deck-out",
            BotActionKind.UseAction or BotActionKind.UseGlobal => "actions/globals",
            _ => "main (On Field/Awaken/On Attack)",
        };
        if (d.Kind == BotActionKind.ClearAndDraw && s.GetPlayer(pid).Life <= 0)
        {
            _deckOutDeaths++;
            if (Environment.GetEnvironmentVariable("SIM_DEBUG_DECKOUT") == "1" && _deckOutDeaths <= 2)
            {
                Console.WriteLine($"--- deck-out death #{_deckOutDeaths} ({C(pid)}) - last 60 log lines ---");
                foreach (var e in s.Log.TakeLast(60)) Console.WriteLine($"  [{e.PlayerId}] {e.Text}");
            }
        }
        var toP2 = lifeBefore.P2 - s.PlayerTwo.Life;
        var toP1 = lifeBefore.P1 - s.PlayerOne.Life;
        if (toP2 > 0) Inc(DamageDealt, (C("p1"), source), toP2);
        if (toP1 > 0) Inc(DamageDealt, (C("p2"), source), toP1);
    }

    public static void EndGame(string winner, string one, string two, int turns)
    {
        Inc(Games, one); Inc(Games, two);
        if (winner is not ("__timeout__" or "__draw__"))
        {
            _decided++;
            if (winner == one) _seatOneWins++;
            Inc(Wins, winner);
            if (_capThisGame) Inc(CapWins, winner);
        }
        foreach (var (pid, card) in _fieldedThisGame)
        {
            var c = C(pid);
            Inc(GamesFielded, (c, card));
            if (winner == c) Inc(WinsWhenFielded, (c, card));
        }
    }

    public static void Report(Dictionary<string, string> names)
    {
        Console.WriteLine($"=== STATS === seat one (goes first) wins {_seatOneWins}/{_decided} decided ({100.0 * _seatOneWins / Math.Max(1, _decided):F1}%); games that hit the turn cap: {_capHits}; lost to deck-out burn: {_deckOutDeaths}");
        foreach (var champ in Games.Keys)
        {
            var games = Games[champ];
            Console.WriteLine($"--- {names[champ]} ({games} games, win {100.0 * Wins.GetValueOrDefault(champ) / games:F1}%, {100.0 * CapWins.GetValueOrDefault(champ) / games:F1}% of games won on the turn-cap life tiebreak) - rerolled dice/game: {(double)Rerolls.GetValueOrDefault(champ) / games:F1} ---");
            var totalDmg = Math.Max(1, DamageDealt.Where(k => k.Key.Champ == champ).Sum(k => k.Value));
            foreach (var kv in DamageDealt.Where(k => k.Key.Champ == champ).OrderByDescending(k => k.Value))
                Console.WriteLine($"  dmg via {kv.Key.Src}: {(double)kv.Value / games:F2}/game ({100.0 * kv.Value / totalDmg:F0}%)");
            var named = NamedCards.Where(k => k.Key.Champ == champ).OrderByDescending(k => k.Value).ToList();
            if (named.Count > 0)
                Console.WriteLine($"  lockout named ({(double)named.Sum(k => k.Value) / games:F2}/game): " +
                    string.Join(", ", named.Take(6).Select(k => $"{k.Key.Card} {k.Value}")));
            var decl = Math.Max(1, Combat.GetValueOrDefault((champ, "#attack declarations")));
            Console.WriteLine($"  combat: unblocked {(double)Combat.GetValueOrDefault((champ, "unblocked hits")) / games:F2}/game, Overcrush carry-through {(double)Combat.GetValueOrDefault((champ, "Overcrush carry-through")) / games:F2}/game; " +
                $"{(double)Combat.GetValueOrDefault((champ, "#attack declarations")) / games:F1} attacks/game, {(double)Combat.GetValueOrDefault((champ, "#attackers")) / decl:F1} attackers each, " +
                $"lanes with 2+ attackers {(double)Combat.GetValueOrDefault((champ, "#lanes with 2+ attackers (auto-Overcrush)")) / games:F2}/game" +
                (Combat.ContainsKey((champ, "#pump on an unblocked attacker")) || Combat.ContainsKey((champ, "#pump on a blocked attacker"))
                    ? $"; pumps: {(double)Combat.GetValueOrDefault((champ, "#pump on an unblocked attacker")) / games:F2} unblocked, {(double)Combat.GetValueOrDefault((champ, "#pump on a blocked attacker")) / games:F2} blocked /game"
                    : ""));
            var met = Math.Max(1, Combat.GetValueOrDefault((champ, "#attacks met")));
            Console.WriteLine($"  its attacks met: no blocker available {100.0 * Combat.GetValueOrDefault((champ, "#attacks met with no blocker available")) / met:F0}% of the time; " +
                $"{(double)Combat.GetValueOrDefault((champ, "#blockers available")) / met:F1} blockers available, {(double)Combat.GetValueOrDefault((champ, "#blocks made")) / met:F1} used");
            foreach (var kv in Uses.Where(k => k.Key.Champ == champ).OrderByDescending(k => k.Value))
                Console.WriteLine($"  {kv.Key.What}: {(double)kv.Value / games:F2}/game");
            Console.WriteLine($"  {"card",-18}{"buys/g",8}{"fields/g",10}{"%games fielded",16}{"win% when fielded",20}");
            var cards = Fields.Keys.Concat(Buys.Keys).Where(k => k.Champ == champ).Select(k => k.Card).Distinct()
                .OrderByDescending(c => Fields.GetValueOrDefault((champ, c)));
            foreach (var card in cards)
            {
                var gf = GamesFielded.GetValueOrDefault((champ, card));
                var wf = gf > 0 ? $"{100.0 * WinsWhenFielded.GetValueOrDefault((champ, card)) / gf:F1}%" : "-";
                Console.WriteLine($"  {card,-18}{(double)Buys.GetValueOrDefault((champ, card)) / games,8:F2}{(double)Fields.GetValueOrDefault((champ, card)) / games,10:F2}{100.0 * gf / games,15:F0}%{wf,20}");
            }
        }
    }
}
