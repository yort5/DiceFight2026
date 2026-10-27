namespace DiceFight.V2.Data;

// Sets (2026-10-25, user: "group these all together as 'Set 1'"). Set 1 is
// every card that was playable that day: 101 Characters, 1 Action and 21
// Basic Actions, the four starter rosters included. A set is LOCKED once
// named - its ids are listed here rather than derived, so a new card can't
// drift into Set 1. New cards go in the next set: add "Set 2" below when
// the first one is finished (a test fails on a collectible card in no set).
//
// One exception, at the user's request (2026-10-27): twelve animals with
// no public-domain painting moved to Set 2 and twelve painted animals took
// their places, so every Set 1 card can show its painting ("source other
// animals that we can find paintings for, find a card to build for them,
// and swap them into Set 1"). Same 101/1/21, same energy and rarity per
// slot.
//
// Packs open from Current. Trade-ins still draw from every set.
public static class DiceKingdomSets
{
    public const string Set1 = "Set 1";
    public const string Set2 = "Set 2";
    public const string Current = Set1;

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Sets = new Dictionary<string, IReadOnlySet<string>>
    {
        [Set1] = new HashSet<string>
        {
            // --- Characters (101)
            // Claw: Bull Shark, Cape Buffalo, Crocodile, Deathstalker Scorpion, Electric Eel, Fire Salamander, Giant
            //   Centipede, Gibbon, Grizzly Bear, Harris’s Hawk, Hercules Beetle, Honey Badger, Komodo Dragon,
            //   Lioness, Lionfish, Mandrill, Mongoose, Peregrine Falcon, Pistol Shrimp, Polar Bear, Praying
            //   Mantis, Silverback, Snow Leopard, Spitting Cobra, Stag Beetle, Stoat, Tasmanian Devil, Wasp,
            //   Wolverine, Wombat.
            "DPS086", "THOR021", "DK-CLAW-08", "TOA086", "DPS029", "DK-CLAW-10", "DPS139", "XFC017",
            "DPS093", "DPS057", "DK-CLAW-03", "SW018", "AVX077", "DK-CLAW-01", "DK-CLAW-12", "DPS102",
            "DPS114", "DK-CLAW-09", "DPS027", "DK-CLAW-05", "XFC003", "WOL071", "WWE037", "DK-CLAW-06",
            "BFF033", "SKC090", "JLL002", "DK-CLAW-07", "DK-CLAW-02", "DPS142",
            // Eye: Anglerfish, Barn Owl, Basilisk, Cassowary, Cone Snail, Cowbird, Cuckoo, Cuttlefish, Electric Ray,
            //   Elephant, Fox, Frigatebird, Frilled Lizard, Hyena, King Cobra, Leaf Insect, Magpie, Mimic
            //   Filefish, Orb Weaver, Peacock, Platypus, Pufferfish, Raccoon, Raven, Red Panda, Wild Boar.
            "DK-EYE-03", "DK-EYE-01", "DK-EYE-06", "DOOM013", "DPS072", "FUS004", "DK-EYE-04",
            "DK-EYE-09", "IG058", "DK-EYE-07", "DK-EYE-08", "DPS033", "DK-EYE-10", "DK-EYE-02", "AVX019",
            "DK-EYE-05", "XFC110", "DPS071", "CW056", "DOOM017", "CW014", "GOTG032", "AI031", "CW054",
            "DPS095", "DPS061",
            // Shell: Beaver, Bison, Box Jellyfish, Box Turtle, Emperor Penguin, Giant Tortoise, Hermit Crab,
            //   Hippopotamus, Horseshoe Crab, Koala, Manatee, Meerkat, Musk Ox, Opossum, Pangolin, Prairie
            //   Dog, Queen Termite, Rhinoceros, Skunk, Starfish.
            "DPS148", "DPS019", "SW001", "DK-SHELL-08", "WWE033", "DK-SHELL-04", "DK-SHELL-01",
            "AI013", "AOU136", "HQ019", "DK-SHELL-10", "DK-SHELL-02", "DK-SHELL-05", "DK-SHELL-03",
            "DPS152", "TAG003", "DK-SHELL-06", "DK-SHELL-07", "DPS063", "SKC113",
            // Wing: Archerfish, Barn Swallow, Bearded Dragon, Bottlenose Dolphin, Bowerbird, Caracal, Chipmunk, Dung
            //   Beetle, Flying Squirrel, Greyhound, Hare, Homing Pigeon, Hummingbird, Kangaroo Rat, Marmoset,
            //   Monarch Butterfly, Mountain Goat, Nutcracker, Osprey, Phoenix, Puffin, Secretary Bird, Sugar
            //   Glider, Swift, Woodpecker.
            "DK-WING-12", "DK-WING-02", "SWW020", "CW042", "XFO016", "GOTG021", "DPS099", "DPS047",
            "DK-WING-10", "DK-WING-07", "IG068", "DK-WING-06", "DK-WING-03", "DPS059", "DRS022",
            "DK-WING-05", "DK-WING-04", "DK-WING-01", "DK-WING-08", "MSW020", "DPS038", "GOTG105",
            "DK-WING-09", "DPS056", "DPS031",
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
        // Set 2 (started 2026-10-27): the twelve Set 1 animals no public-domain
        // painting was found for, swapped out so Set 1 can be all paintings
        // ("Then we can find a different aesthetic for Set 2"). Not open in
        // packs yet; anyone who already owns them keeps them.
        [Set2] = new HashSet<string>
        {
            // Claw: Orca, Army Ant, Fire Ant. Wing: Swordfish, Firefly.
            "DK-CLAW-04", "DK-CLAW-11", "XMF028", "ASM110", "DK-WING-11",
            // Eye: Chameleon, Velvet Worm, Orchid Mantis.
            "DK-EYE-11", "AVX066", "XFO004",
            // Shell: Saiga, Bombardier Beetle, Planarian, Leech.
            "DPS113", "DK-SHELL-11", "WF075", "DK-SHELL-09",
        },
    };

    // The set a card is in, or null (a Champion, a Tardigrade, or a card
    // not finished yet).
    public static string? SetOf(string cardId) =>
        Sets.FirstOrDefault(kv => kv.Value.Contains(cardId)).Key;
}
