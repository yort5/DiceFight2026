using DiceFight.V2.Bot;

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
// an entry. DiceKingdomBossesTests checks every link builds a legal game
// team. The three below are SAMPLES on the starter rosters, there so the
// ladder can be tried end to end - replace them with real bosses.
public static class DiceKingdomBosses
{
    public static readonly IReadOnlyList<BossDef> Ladder =
    [
        new(
            Id: "sample-1",
            Name: "Wolf Cub",
            Blurb: "Sample boss: the Wolf starter roster, played cautiously.",
            TeamLink: "/teambuilder?cards=4xDK-CLAW-01;4xDK-CLAW-09;4xDK-CLAW-02;4xDK-CLAW-06;4xDK-WING-04;4xDK-WING-07;4xDK-SHELL-01;4xDK-EYE-07;3xDK-ACT-01&tardigrades=8xClaw&champion=Wolf",
            Style: BotStyles.Cautious,
            FirstWin: new(Points: 10, Coins: 10),
            Rematch: new(Points: 1)),
        new(
            Id: "sample-2",
            Name: "Iron Armadillo",
            Blurb: "Sample boss: the Armadillo starter roster, racing to Rhinoceros.",
            TeamLink: "/teambuilder?cards=4xDK-SHELL-04;4xDK-SHELL-03;4xDK-SHELL-02;4xDK-SHELL-07;4xDK-EYE-08;4xDK-EYE-04;4xDK-CLAW-08;4xDK-WING-03;3xDK-ACT-05&tardigrades=8xShell&champion=Armadillo",
            Style: BotStyles.Balanced with { RushCardId = DiceKingdomConfig.Rhinoceros.Id, RushBonus = 5 },
            FirstWin: new(Points: 20, Coins: 15, Drops: [new(Chance: 1, Rarity: CardRarity.Uncommon)]),
            Rematch: new(Points: 2, Drops: [new(Chance: 0.1, Rarity: CardRarity.Uncommon)])),
        new(
            Id: "sample-3",
            Name: "The Night Owl",
            Blurb: "Sample boss: the Great Horned Owl starter roster, attacking at every chance.",
            TeamLink: "/teambuilder?cards=4xDK-EYE-09;4xDK-EYE-05;4xDK-EYE-02;4xDK-EYE-06;4xDK-SHELL-05;4xDK-SHELL-06;4xDK-CLAW-07;4xDK-WING-06;3xDK-ACT-04&tardigrades=8xEye&champion=GreatHornedOwl",
            Style: BotStyles.Aggressive,
            FirstWin: new(Points: 40, Coins: 25, Drops: [new(Chance: 1, Rarity: CardRarity.Rare)]),
            Rematch: new(Points: 4, Drops: [new(Chance: 0.05, CardId: DiceKingdomConfig.Basilisk.Id)])),
    ];

    public static BossDef? Find(string? id) => Ladder.FirstOrDefault(b => b.Id == id);

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
