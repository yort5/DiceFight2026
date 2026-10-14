namespace DiceFight.V2.Data;

public enum CardRarity { Common, Uncommon, Rare, SuperRare }

// Random packs (2026-10-12, user call). The Champion teams stay as free
// starter rosters; packs are how a player collects dice for custom teams.
// As in Dice Masters, rarity is NOT power: "I'd want someone to be able to
// be decently competitive with just a common/uncommon team." So strong
// cards are spread across every tier (Hummingbird, a 4-cost On Field KO, is
// a Common; Orca, the same text at 5, is a Rare), and every starter roster
// is Common/Uncommon apart from one or two cards.
//
// What you collect is dice: each pulled card is one die of that character.
// A team can play up to the card's DieLimit of them; copies past that are
// kept, for trading in to a higher rarity later.
//
// Every number here is a first pass - the user: "we can change the numbers
// later, but might as well pick some numbers to start."
public static class DiceKingdomPacks
{
    public const int StartingCoins = 100;
    public const int PackPrice = 10;
    // Once per UTC day, on the first visit (PlayerAccounts.ClaimDaily).
    public const int DailyCoins = 5;

    // Every playable Character and personal Action in the Catalog. Basic Actions aren't in packs.
    // 34 Common, 24 Uncommon, 16 Rare, 6 Super Rare. Incomplete cards are excluded.
    public static readonly IReadOnlyDictionary<string, CardRarity> Rarities = new Dictionary<string, CardRarity>
    {
        // Claw
        [DiceKingdomConfig.HoneyBadger.Id] = CardRarity.Common,
        [DiceKingdomConfig.Wolverine.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.GrizzlyBear.Id] = CardRarity.Common,
        [DiceKingdomConfig.Orca.Id] = CardRarity.Rare,
        [DiceKingdomConfig.PeregrineFalcon.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Silverback.Id] = CardRarity.SuperRare,
        [DiceKingdomConfig.Stoat.Id] = CardRarity.Common,
        [DiceKingdomConfig.CapeBuffalo.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Mongoose.Id] = CardRarity.Common,
        [DiceKingdomConfig.ElectricEel.Id] = CardRarity.Rare,
        [DiceKingdomConfig.ArmyAnt.Id] = CardRarity.Rare,
        [DiceKingdomConfig.KomodoDragon.Id] = CardRarity.SuperRare,
        // Shell
        [DiceKingdomConfig.Hippopotamus.Id] = CardRarity.Common,
        [DiceKingdomConfig.MuskOx.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Pangolin.Id] = CardRarity.Rare,
        [DiceKingdomConfig.HermitCrab.Id] = CardRarity.Common,
        [DiceKingdomConfig.Opossum.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.QueenTermite.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Rhinoceros.Id] = CardRarity.Rare,
        [DiceKingdomConfig.BoxTurtle.Id] = CardRarity.Common,
        [DiceKingdomConfig.Leech.Id] = CardRarity.Rare,
        [DiceKingdomConfig.Meerkat.Id] = CardRarity.Common,
        [DiceKingdomConfig.BombardierBeetle.Id] = CardRarity.Uncommon,
        // Wing
        [DiceKingdomConfig.Osprey.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.BarnSwallow.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Hummingbird.Id] = CardRarity.Common,
        [DiceKingdomConfig.MountainGoat.Id] = CardRarity.Common,
        [DiceKingdomConfig.MonarchButterfly.Id] = CardRarity.Rare,
        [DiceKingdomConfig.HomingPigeon.Id] = CardRarity.Common,
        [DiceKingdomConfig.Greyhound.Id] = CardRarity.Common,
        [DiceKingdomConfig.Phoenix.Id] = CardRarity.SuperRare,
        [DiceKingdomConfig.Swift.Id] = CardRarity.Common,
        [DiceKingdomConfig.FlyingSquirrel.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Firefly.Id] = CardRarity.Common,
        [DiceKingdomConfig.Archerfish.Id] = CardRarity.Rare,
        // Eye
        [DiceKingdomConfig.BarnOwl.Id] = CardRarity.Common,
        [DiceKingdomConfig.Hyena.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Anglerfish.Id] = CardRarity.Rare,
        [DiceKingdomConfig.Cowbird.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Magpie.Id] = CardRarity.Common,
        [DiceKingdomConfig.Basilisk.Id] = CardRarity.SuperRare,
        [DiceKingdomConfig.Elephant.Id] = CardRarity.Common,
        [DiceKingdomConfig.Fox.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Cuttlefish.Id] = CardRarity.Common,
        [DiceKingdomConfig.FrilledLizard.Id] = CardRarity.Uncommon,
        [DiceKingdomConfig.Chameleon.Id] = CardRarity.Rare,

        // Implemented adaptations. Preserve their printed source rarities from
        // src/DiceFight.Engine/Data/CardRarity.json ("Super" becomes SuperRare).
        // Stable source IDs are retained when cards receive their animal names.
        ["CW014"] = CardRarity.Common, // Pufferfish
        ["DPS029"] = CardRarity.Common, // Deathstalker Scorpion
        ["DPS031"] = CardRarity.Common, // Woodpecker
        ["DPS033"] = CardRarity.Common, // Frigatebird
        ["DPS038"] = CardRarity.Common, // Secretary Bird
        ["DPS057"] = CardRarity.Uncommon, // Gibbon
        ["DPS059"] = CardRarity.Uncommon, // Kangaroo Rat
        ["DPS061"] = CardRarity.Uncommon, // Wild Boar
        ["DPS071"] = CardRarity.Uncommon, // Orb Weaver
        ["DPS093"] = CardRarity.Uncommon, // Giant Centipede
        ["DPS099"] = CardRarity.Rare, // Dung Beetle
        ["DPS102"] = CardRarity.Rare, // Lioness
        ["DPS113"] = CardRarity.Rare, // Saiga
        ["DPS114"] = CardRarity.Rare, // Mandrill
        ["DPS142"] = CardRarity.SuperRare, // Wombat
        ["DPS148"] = CardRarity.SuperRare, // Bison
        ["GOTG105"] = CardRarity.Rare, // Sugar Glider
        ["JLL002"] = CardRarity.Common, // Stag Beetle
        ["MSW020"] = CardRarity.Common, // Puffin
        ["SKC090"] = CardRarity.Uncommon, // Spitting Cobra
        ["TAG003"] = CardRarity.Common, // Prairie Dog

        ["WF075"] = CardRarity.Uncommon, // Planarian
        ["MYST002"] = CardRarity.Common, // Abundant Waters
        ["AVX077"] = CardRarity.Uncommon, // Hercules Beetle
        ["DRS022"] = CardRarity.Common, // Marmoset
        ["XMF028"] = CardRarity.Common, // Fire Ant
        ["IG058"] = CardRarity.Uncommon, // Electric Ray
        ["AVX066"] = CardRarity.Uncommon, // Velvet Worm
        ["AVX019"] = CardRarity.Common, // Leaf Insect
        ["XFO004"] = CardRarity.Common, // Orchid Mantis
        ["XFO016"] = CardRarity.Common, // Bowerbird
        ["XFC017"] = CardRarity.Common, // Fire Salamander
        ["XFC110"] = CardRarity.Rare, // Mimic Filefish
        ["SW018"] = CardRarity.Common, // Harris’s Hawk
    };

    // A pack is five dice: three Commons, an Uncommon, and a "rare slot"
    // that's an Uncommon 55%, a Rare 40% or a Super Rare 5% (one pack in
    // twenty; about 2 in 5 players see one in their first ten packs).
    // Picks are uniform within the selected rarity; expanding its pool changes
    // per-card odds, while the slot rarity probabilities stay the same.
    public static readonly IReadOnlyList<IReadOnlyList<(CardRarity Rarity, int Weight)>> Slots =
    [
        [(CardRarity.Common, 1)],
        [(CardRarity.Common, 1)],
        [(CardRarity.Common, 1)],
        [(CardRarity.Uncommon, 1)],
        [(CardRarity.Uncommon, 55), (CardRarity.Rare, 40), (CardRarity.SuperRare, 5)],
    ];

    // Trade-ins (2026-10-12, user's rates): 5 Commons for an Uncommon, 4
    // Uncommons for a Rare, 3 Rares for a Super Rare. What comes back is
    // random - you can't pick a Super Rare outright - but only from the
    // next rarity's cards you don't have a full team's worth of yet, so
    // trading always moves you toward a complete set.
    public static readonly IReadOnlyDictionary<CardRarity, int> TradeInCost = new Dictionary<CardRarity, int>
    {
        [CardRarity.Common] = 5,
        [CardRarity.Uncommon] = 4,
        [CardRarity.Rare] = 3,
    };

    // `owned`: card id -> dice owned. Null once every card of the next
    // rarity is full too - then any of them.
    public static string TradeIn(CardRarity given, IReadOnlyDictionary<string, int> owned, Func<int, int> roll)
    {
        if (!TradeInCost.ContainsKey(given)) throw new InvalidOperationException("Super Rares can't be traded up.");
        var pool = CardsOf(given + 1);
        var missing = pool.Where(id => owned.GetValueOrDefault(id) < DiceKingdomConfig.Catalog[id].DieLimit).ToList();
        if (missing.Count > 0) pool = missing;
        return pool[roll(pool.Count)];
    }

    public static IReadOnlyList<string> CardsOf(CardRarity rarity) =>
        Rarities.Where(kv => kv.Value == rarity).Select(kv => kv.Key).Order().ToList();

    // One pack's card ids, slot by slot. `roll(n)` is a uniform pick from
    // 0..n-1 - the server passes a cryptographic one, tests a seeded one.
    // The three Commons are always three different cards.
    public static IReadOnlyList<string> Open(Func<int, int> roll)
    {
        var pulled = new List<string>();
        foreach (var slot in Slots)
        {
            var pick = roll(slot.Sum(s => s.Weight));
            var rarity = slot.First(s => (pick -= s.Weight) < 0).Rarity;
            var pool = CardsOf(rarity).Except(pulled).ToList();
            pulled.Add(pool[roll(pool.Count)]);
        }
        return pulled;
    }
}
