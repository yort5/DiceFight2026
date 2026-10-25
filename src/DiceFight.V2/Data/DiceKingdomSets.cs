namespace DiceFight.V2.Data;

// Sets (2026-10-25, user: "group these all together as 'Set 1'"). Set 1 is
// every card that was playable that day: 101 Characters, 1 Action and 21
// Basic Actions, the four starter rosters included. A set is LOCKED once
// named - its ids are listed here rather than derived, so a new card can't
// drift into Set 1. New cards go in the next set: add "Set 2" below when
// the first one is finished (a test fails on a collectible card in no set).
//
// Packs open from Current. Trade-ins still draw from every set.
public static class DiceKingdomSets
{
    public const string Set1 = "Set 1";
    public const string Current = Set1;

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Sets = new Dictionary<string, IReadOnlySet<string>>
    {
        [Set1] = new HashSet<string>
        {
            // --- Characters (101)
            // Claw: Army Ant, Bull Shark, Cape Buffalo, Crocodile, Deathstalker Scorpion, Electric Eel,
            //   Fire Ant, Fire Salamander, Giant Centipede, Gibbon, Grizzly Bear, Harris’s Hawk,
            //   Hercules Beetle, Honey Badger, Komodo Dragon, Lioness, Mandrill, Mongoose, Orca,
            //   Peregrine Falcon, Pistol Shrimp, Polar Bear, Praying Mantis, Silverback, Snow Leopard,
            //   Spitting Cobra, Stag Beetle, Stoat, Wolverine, Wombat.
            "DK-CLAW-11", "THOR021", "DK-CLAW-08", "TOA086", "DPS029", "DK-CLAW-10", "XMF028", "XFC017",
            "DPS093", "DPS057", "DK-CLAW-03", "SW018", "AVX077", "DK-CLAW-01", "DK-CLAW-12", "DPS102",
            "DPS114", "DK-CLAW-09", "DK-CLAW-04", "DK-CLAW-05", "XFC003", "WOL071", "WWE037", "DK-CLAW-06",
            "BFF033", "SKC090", "JLL002", "DK-CLAW-07", "DK-CLAW-02", "DPS142",
            // Eye: Anglerfish, Barn Owl, Basilisk, Cassowary, Chameleon, Cone Snail, Cowbird,
            //   Cuttlefish, Electric Ray, Elephant, Fox, Frigatebird, Frilled Lizard, Hyena, Leaf
            //   Insect, Magpie, Mimic Filefish, Orb Weaver, Orchid Mantis, Platypus, Pufferfish,
            //   Raccoon, Raven, Red Panda, Velvet Worm, Wild Boar.
            "DK-EYE-03", "DK-EYE-01", "DK-EYE-06", "DOOM013", "DK-EYE-11", "FUS004", "DK-EYE-04",
            "DK-EYE-09", "IG058", "DK-EYE-07", "DK-EYE-08", "DPS033", "DK-EYE-10", "DK-EYE-02", "AVX019",
            "DK-EYE-05", "XFC110", "DPS071", "XFO004", "DOOM017", "CW014", "GOTG032", "AI031", "CW054",
            "AVX066", "DPS061",
            // Shell: Bison, Bombardier Beetle, Box Jellyfish, Box Turtle, Emperor Penguin, Hermit Crab,
            //   Hippopotamus, Koala, Leech, Manatee, Meerkat, Musk Ox, Opossum, Pangolin, Planarian,
            //   Prairie Dog, Queen Termite, Rhinoceros, Saiga, Skunk.
            "DPS148", "DK-SHELL-11", "SW001", "DK-SHELL-08", "WWE033", "DK-SHELL-04", "DK-SHELL-01",
            "AI013", "DK-SHELL-09", "HQ019", "DK-SHELL-10", "DK-SHELL-02", "DK-SHELL-05", "DK-SHELL-03",
            "WF075", "TAG003", "DK-SHELL-06", "DK-SHELL-07", "DPS113", "SKC113",
            // Wing: Archerfish, Barn Swallow, Bearded Dragon, Bottlenose Dolphin, Bowerbird, Chipmunk,
            //   Dung Beetle, Firefly, Flying Squirrel, Greyhound, Hare, Homing Pigeon, Hummingbird,
            //   Kangaroo Rat, Marmoset, Monarch Butterfly, Mountain Goat, Osprey, Phoenix, Puffin,
            //   Secretary Bird, Sugar Glider, Swift, Swordfish, Woodpecker.
            "DK-WING-12", "DK-WING-02", "SWW020", "CW042", "XFO016", "GOTG021", "DPS099", "DK-WING-11",
            "DK-WING-10", "DK-WING-07", "IG068", "DK-WING-06", "DK-WING-03", "DPS059", "DRS022",
            "DK-WING-05", "DK-WING-04", "DK-WING-01", "DK-WING-08", "MSW020", "DPS038", "GOTG105",
            "DK-WING-09", "ASM110", "DPS031",
            // --- Actions (1)
            // Wing: Abundant Waters.
            "MYST002",
            // --- Basic Actions (21)
            // Claw: Anger Issues, Frenzy, Gore, Pounce, Thorn Shot.
            "DK-ACT-01", "AOU029", "JUS067", "IG014", "JUS069",
            // Eye: Dazzle, Mutation, Scatter, Snare, Stare Down.
            "MSW012", "DK-ACT-04", "FUS025", "SKC014", "WF029",
            // Shell: Archnemesis, Circle Up, Distraction, Grooming, Hibernation.
            "DK-ACT-05", "TMNT057", "DK-ACT-02", "BFF129", "THOR009",
            // Wing: Dive, Foraging, Nest Egg, Resurrection, Spring Emergence, Take Flight.
            "IMW034", "JUS068", "AOU031", "DK-ACT-03", "IG003", "SWW025",
        },
    };

    // The set a card is in, or null (a Champion, a Tardigrade, or a card
    // not finished yet).
    public static string? SetOf(string cardId) =>
        Sets.FirstOrDefault(kv => kv.Value.Contains(cardId)).Key;
}
