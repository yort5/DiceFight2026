using DiceFight.V2.Bot;
using DiceFight.V2.Model;

namespace DiceFight.V2.Data;

// Boss battles (2026-10-31, user: "teams that I would provide, configure a
// specific bot style for, and then the player could progress through
// increasingly difficult teams, gaining points for defeating them as well
// as possible rare cards. I'm thinking I would just provide some Team
// Builder links for each team").
//
// The Ladder is in order: the first boss is open to everyone, and beating
// one opens the next. A boss is a Team Builder share link (pasted as is),
// a bot Style, and what winning pays - FirstWin the first time, Rematch
// after that (null = nothing more). Points are a running score on the
// player; coins buy packs; a Drop is a chance at one die of a named card or
// of a random card of a rarity (one the player is short of, when there is
// one - same as a trade-in).
//
// Adding a boss: build the team in /teambuilder, "Copy Team Link", and add
// an entry. BossBattleTests checks every link builds a legal game team.
// Since 2026-11-01 the ladder is the user's own three Dice Masters teams
// (replacing three samples on the starter rosters); cards they needed that
// Dice Kingdom lacked are boss-only (BossCards).
public static class DiceKingdomBosses
{
    // Easiest first, by tools/Simulator SIM_BOSSES=1 (2026-11-01, 200 games
    // against each starter roster, the boss in its own style): Fire Ant
    // Colony 19.5% of games won, Basilisk's Lair 26.5%, Tardigrade Swarm
    // 34.7%. Every boss is still below the starter rosters (50%).
    public static readonly IReadOnlyList<BossDef> Ladder =
    [
        // The user's Dice Masters team "Tag-Jubilee Bolt":
        // rollinthunder.xyz/teambuilder/?view&cards=3x58ig;1x27dps;4x20wwe;1x9uxm;1x17tiw;5x28xmf;3x135ig;2x24aiw;3x23hq;3x68jus
        // Eddie Guerrero is Meerkat (Tag Out at 2); Iceman, Intellect
        // Devourer, Typhoid Mary, Yawning Portal and Target Lock are boss
        // cards. Claw Tardigrades feed Fire Ant (Jubilee).
        new(
            Id: "tag-jubilee",
            Name: "The Fire Ant Colony",
            Blurb: "Every Claw creature it fields stings you - and it fields them for free.",
            TeamLink: "/teambuilder?cards=3xIG058;1xDPS027;4xDK-SHELL-10;1xUXM009;1xTIW017;5xXMF028;3xIG135;2xAIW024;3xHQ023;3xJUS068&tardigrades=8xClaw",
            Style: BotStyles.Aggressive,
            FirstWin: new(Points: 20, Coins: 15, Drops: [new(Chance: 1, Rarity: CardRarity.Uncommon)]),
            Rematch: new(Points: 2)),
        // "X-Men Colossus":
        // rollinthunder.xyz/teambuilder/?view&cards=4x66avx;1x62uxm;1x101avx;4x20xmf;3x9dxm;1x49uxm;4x19avx;2x40xfc;3x49ai;3x64xmf
        // Colossus is Basilisk (word for word), Big Entrance is Spring
        // Emergence, Counterstrike is Hibernation; Hope Summers, Mimic,
        // Professor X, Cerebro and Xavier's School are boss cards. It races
        // to Basilisk rather than playing Cautious - a cautious bot stalls.
        new(
            Id: "xmen-colossus",
            Name: "The Basilisk's Lair",
            Blurb: "It grows its creatures up a level, then lets Basilisk burn you at the end of every turn.",
            // 2026-11-02 (user: Cerebro/Mimic "might have been too defensive -
            // neither is helping get the persistent characters in the field on
            // Level 2"): Cerebro -> Frigatebird (cheap to field, 5/5, hard to
            // KO) and Big Entrance -> Mutation (its Global spins one down to
            // spin another up). Professor X stays for its Global - Tardigrades
            // from Used to Prep every turn (the bot's churn rule).
            TeamLink: "/teambuilder?cards=4xAVX066;4xDPS033;1xDK-EYE-06;4xXMF020;3xDXM009;1xUXM049;4xAVX019;2xXFC040;3xDK-ACT-04;3xTHOR009&tardigrades=8xEye",
            Style: BotStyles.Balanced with { RushCardId = DiceKingdomConfig.Basilisk.Id, RushBonus = 5 },
            FirstWin: new(Points: 40, Coins: 25, Drops: [new(Chance: 1, Rarity: CardRarity.Rare)]),
            Rematch: new(Points: 4, Drops: [new(Chance: 0.05, Rarity: CardRarity.Uncommon)])),
        // "Sidekicks":
        // rollinthunder.xyz/teambuilder/?view&cards=4x75wf;3x2myst;2x87xfc;1x27dps;2x77avx;3x36thor;1x129wf;4x22drs;3x11toa;3x68jus
        // Blob is Pangolin (the same lockout); Mr. Fixit, Superwoman and
        // Magic Missile are boss cards (BossCards).
        new(
            Id: "sidekicks",
            Name: "The Tardigrade Swarm",
            Blurb: "A swarm of Tardigrades backing a Moose that gets angrier the more it's hurt.",
            TeamLink: "/teambuilder?cards=4xWF075;3xMYST002;2xDK-SHELL-03;1xDPS027;2xAVX077;3xTHOR036;1xWF129;4xDRS022;3xTOA011;3xJUS068&tardigrades=8xClaw",
            Style: BotStyles.Balanced,
            FirstWin: new(Points: 75, Coins: 40, Drops: [new(Chance: 1, Rarity: CardRarity.Rare), new(Chance: 0.25, Rarity: CardRarity.SuperRare)]),
            Rematch: new(Points: 8, Drops: [new(Chance: 0.05, Rarity: CardRarity.Rare)])),
    ];

    public static BossDef? Find(string? id) => Ladder.FirstOrDefault(b => b.Id == id);

    // The boss's team as a game Player, unchecked - for tools/Simulator.
    // The API builds it through V2GamesController.BuildBoss, which checks
    // every card the way it checks a player's custom team.
    public static Player BuildPlayer(BossDef boss, string id)
    {
        var team = ParseTeamLink(boss.TeamLink);
        var player = new Player
        {
            Id = id, Name = boss.Name, ChampionId = team.ChampionId,
            CustomCardDiceCounts = team.Cards.ToDictionary(kv => kv.Key, kv => kv.Value),
            CustomBasicDicePool = team.Tardigrades.Select(kv => DiceKingdomConfig.CustomTardigrade(kv.Key, kv.Value)).ToList(),
        };
        player.TeamCardIds.AddRange(team.Cards.Keys);
        return player;
    }

    // Open = the first boss, or one whose previous boss has been beaten.
    public static bool IsOpen(string bossId, Func<string, bool> beaten)
    {
        var index = Ladder.ToList().FindIndex(b => b.Id == bossId);
        return index == 0 || index > 0 && beaten(Ladder[index - 1].Id);
    }

    // Reads a Team Builder share link (TeamBuilderPage copyTeamLink):
    //   ...?cards=4xDK-CLAW-01;2xDK-ACT-01&tardigrades=8xClaw&champion=Wolf
    // Only the query matters, so a full https:// link, a path or the bare
    // query all work. No tardigrades= means the builder's default, 8 Claw.
    public static BossTeam ParseTeamLink(string link)
    {
        var query = link.Contains('?') ? link[(link.IndexOf('?') + 1)..] : link;
        var fields = new Dictionary<string, string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0) fields[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
        }
        if (!fields.TryGetValue("cards", out var cards) || cards.Length == 0)
            throw new ArgumentException($"Team link has no cards=: {link}");
        var tardigrades = fields.TryGetValue("tardigrades", out var t) ? Counts(t, link) : new() { ["Claw"] = 8 };
        return new BossTeam(Counts(cards, link), tardigrades,
            fields.TryGetValue("champion", out var champion) && champion.Length > 0 ? champion : null);
    }

    // "4xDK-CLAW-01;2xDK-ACT-01" -> id -> count. Split on the first "x"
    // after the digits: ids may contain an x of their own.
    private static Dictionary<string, int> Counts(string list, string link)
    {
        var counts = new Dictionary<string, int>();
        foreach (var entry in list.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var x = entry.IndexOf('x');
            if (x <= 0 || !int.TryParse(entry[..x], out var count) || count < 0 || x == entry.Length - 1)
                throw new ArgumentException($"Can't read \"{entry}\" in team link {link}");
            counts[entry[(x + 1)..]] = counts.GetValueOrDefault(entry[(x + 1)..]) + count;
        }
        return counts;
    }

    // One drop roll: the card named, or one of the rarity - from the
    // current set, preferring cards the player has fewer than DieLimit of.
    // `roll(n)` picks from 0..n-1; Chance is checked against roll(1000000).
    public static string? Roll(BossDrop drop, IReadOnlyDictionary<string, int> owned, Func<int, int> roll)
    {
        if (drop.Chance < 1 && roll(1_000_000) >= drop.Chance * 1_000_000) return null;
        if (drop.CardId is { } card) return card;
        var pool = DiceKingdomPacks.CardsOf(drop.Rarity!.Value)
            .Where(id => DiceKingdomSets.Sets[DiceKingdomSets.Current].Contains(id)).ToList();
        var missing = pool.Where(id => owned.GetValueOrDefault(id) < DiceKingdomConfig.Catalog[id].DieLimit).ToList();
        if (missing.Count > 0) pool = missing;
        return pool[roll(pool.Count)];
    }
}

public sealed record BossDef(
    string Id,
    string Name,
    string Blurb,
    string TeamLink,
    BotPersona Style,
    BossReward FirstWin,
    BossReward? Rematch = null);

public sealed record BossReward(int Points = 0, int Coins = 0, IReadOnlyList<BossDrop>? Drops = null);

// One die: CardId, or a random card of Rarity. Chance 0..1.
public sealed record BossDrop(double Chance, string? CardId = null, CardRarity? Rarity = null);

// Tardigrades: energy type -> dice.
public sealed record BossTeam(IReadOnlyDictionary<string, int> Cards, IReadOnlyDictionary<string, int> Tardigrades, string? ChampionId);

// Named starting points for a boss's Style - BotPersona's knobs, explained
// there. Any of them `with { ... }` for more: RushCardId/RushBonus to race
// to a card, HoldForGlobalCardId to keep energy for a Global, Aggression
// (0 careful .. 2 reckless), ChumpThreshold (how readily it chump-blocks).
public static class BotStyles
{
    // What every Champion plays like on the start screen.
    public static readonly BotPersona Balanced = BotPersona.Default;
    // Swings into bad trades, and only chump-blocks big hits.
    public static readonly BotPersona Aggressive = BotPersona.Default with { Aggression = 2, ChumpThreshold = 3 };
    // Attacks only when nothing can kill the attacker; blocks early.
    public static readonly BotPersona Cautious = BotPersona.Default with { Aggression = 0, ChumpThreshold = 10 };
}
