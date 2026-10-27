using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// Dice Masters imports by batch (2026-10-20, user: "make our way through
// the Dice Masters cards and pull in the ones that can most easily be
// implemented ... if we simplify many of them, they can likely fit within
// the existing engine." One printing per Dice Masters character for now).
//
// Picked with scripts/dk_import_candidates.py, which ranks characters not
// yet used by how close their text is to what the engine already runs.
// Stats, costs and die limits are the source card's (BulkCards.json);
// rarity is the printed one except for the seven animals the user asked
// for by name, which are Rare or Super Rare (DiceKingdomPacks.Rarities).
// Text is written in the source card's terms, simplified where noted -
// ToKingdomCard renames the character and the energy.
public static class KingdomImports
{
    // ---- The user's seven ----

    // Pistol Shrimp: its snapping claw fires a shockwave that stuns
    // everything nearby - Banshee's sonic scream.
    public static readonly CardDef BansheeWailOfTheBanshee = new(
        Id: "XFC003", Name: "Banshee", Subtitle: "The Wail of the Banshee", Set: "XFC", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("XFC003Die", "Mask", (1, 2, 1), (2, 3, 2), (2, 5, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, deal 1 damage to each opposing character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0)))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Pistol Shrimp", KingdomEnergyType: "Claw");

    // Snow Leopard: the "ghost of the mountains" - one defender never
    // catches it.
    public static readonly CardDef FrostGiantLesserElemental = new(
        Id: "BFF033", Name: "Frost Giant", Subtitle: "Lesser Elemental", Set: "BFF", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("BFF033Die", "Bolt", (1, 5, 2), (2, 6, 4), (3, 8, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Frost Giant can only be blocked by two or more character dice.",
        Abilities: [],
        Continuous: [new CombatRule(CombatRuleKind.MinBlockers, new TargetFilter(Self: true), N: 2)], IncludeInTeamBuilder: true,
        KingdomName: "Snow Leopard", KingdomEnergyType: "Claw");

    // Red Panda: rears up on its hind legs to look bigger. Simplified:
    // any attacker, not only non-Shield ones.
    public static readonly CardDef NamoritaTauntingVillains = new(
        Id: "CW054", Name: "Namorita", Subtitle: "Taunting Villains", Set: "CW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("CW054Die", "Shield", (1, 3, 2), (2, 4, 3), (2, 5, 4)),
        DieLimit: 4, Affiliations: ["New Warriors"], Keywords: [],
        RawText: "On Block: target attacking character die gets -2A until end of turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieBlocks,
            new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Opposing, AttackersOnly: true,
                Prompt: "choose an attacker to get -2 ATK."), AtkDelta: -2))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Red Panda", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json.", "Removed \"non-Shield\" from the attacker it can target."]);

    // Koala: carries its joey on its back - the Tardigrades are the joeys.
    public static readonly CardDef CaptainAmericaLivingLegend = new(
        Id: "AI013", Name: "Captain America", Subtitle: "Living Legend", Set: "AI", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("AI013Die", "Shield", (1, 3, 3), (2, 5, 4), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["Avengers"], Keywords: [],
        RawText: "While Captain America is active, your Sidekick character dice get +2D.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Ownership: TargetOwnership.Own, Count: 0, Tags: new TagQuery(AnyOf: ["sidekick"])),
            DefDelta: new Fixed(2))], IncludeInTeamBuilder: true,
        KingdomName: "Koala", KingdomEnergyType: "Shell");

    // Manatee: the gentle giant with no natural predators - the bigger the
    // crowd across the water, the more it soothes.
    public static readonly CardDef PowerGirlKryptonian = new(
        Id: "HQ019", Name: "Power Girl", Subtitle: "Kryptonian", Set: "HQ", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("HQ019Die", "Bolt", (1, 4, 5), (1, 5, 5), (2, 6, 6)),
        DieLimit: 4, Affiliations: ["Superman Family"], Keywords: [],
        RawText: "When fielded, gain 1 life for each opposing non-Sidekick character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new PerMatch(
            new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0, Tags: new TagQuery(NoneOf: ["sidekick"])), Multiplier: 1)))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Manatee", KingdomEnergyType: "Shell");

    // Platypus: a venomous spur, and a bill that senses prey's electric
    // field before it moves. The source's Fast is on burst faces only;
    // the sheet doesn't say which, so it's always Fast here.
    public static readonly CardDef NegativeManPhasing = new(
        Id: "DOOM017", Name: "Negative Man", Subtitle: "Phasing", Set: "DOOM", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DOOM017Die", "Mask", (1, 2, 4), (1, 2, 5), (1, 2, 7)),
        DieLimit: 4, Affiliations: ["Doom Patrol"], Keywords: ["Deadly", "Fast"],
        RawText: "Deadly. Fast.",
        Abilities: [],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Platypus", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json.", "Fast on every face; the source has it only on its burst faces, which the reference data doesn't identify."]);

    // Bearded Dragon: basks to warm up before it does anything - and
    // Superman is the other one powered by the sun.
    public static readonly CardDef SupermanLivingInAWorldOfCardboard = new(
        Id: "SWW020", Name: "Superman", Subtitle: "Living in a world of Cardboard", Set: "SWW", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("SWW020Die", "Shield", (1, 4, 6), (2, 6, 7), (2, 7, 8)),
        DieLimit: 4, Affiliations: ["Justice League"], Keywords: [],
        RawText: "Superman gets +1A and +1D for each different energy type in your Reserve Pool (Wild doesn't count).",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true),
            AtkDelta: ReserveEnergyTypes, DefDelta: ReserveEnergyTypes)], IncludeInTeamBuilder: true,
        KingdomName: "Bearded Dragon", KingdomEnergyType: "Wing");

    private static Amount ReserveEnergyTypes => new PerMatch(
        new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.ReservePool], Count: 0),
        Multiplier: 1, Unit: CountUnit.EnergyTypes);

    // ---- Keyword-only cards ----

    // Box Jellyfish: drifts, and whatever touches it doesn't survive.
    public static readonly CardDef GreyHunterSoneOfRuss = new(
        Id: "SW001", Name: "Grey Hunter", Subtitle: "Sone of Russ", Set: "SW", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("SW001Die", "Mask", (0, 2, 2), (1, 3, 2), (2, 4, 2)),
        DieLimit: 5, Affiliations: ["Imperium", "Space Wolves"], Keywords: ["Deadly"],
        RawText: "Deadly.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Box Jellyfish", KingdomEnergyType: "Shell");

    // Raccoon: gets into everything and comes back for more.
    public static readonly CardDef SquirrelGirlUnbeatable = new(
        Id: "GOTG032", Name: "Squirrel Girl", Subtitle: "Unbeatable", Set: "GOTG", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("GOTG032Die", "Fist", (0, 3, 3), (1, 4, 3), (1, 4, 4)),
        DieLimit: 4, Affiliations: ["Avengers"], Keywords: ["Infiltrate"],
        RawText: "Infiltrate.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Raccoon", KingdomEnergyType: "Eye");

    // Praying Mantis: one of the fastest strikes there is.
    public static readonly CardDef RondaRouseyRowdy = new(
        Id: "WWE037", Name: "Ronda Rousey", Subtitle: "Rowdy", Set: "WWE", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("WWE037Die", "Fist", (0, 2, 3), (1, 3, 3), (1, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Fast"],
        RawText: "Fast.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Praying Mantis", KingdomEnergyType: "Claw");

    // Polar Bear: the largest land carnivore, and nothing stops it.
    public static readonly CardDef SupergirlAngryAlien = new(
        Id: "WOL071", Name: "Supergirl", Subtitle: "Angry Alien", Set: "WOL", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("WOL071Die", "Shield", (1, 5, 4), (2, 7, 6), (3, 8, 7)),
        DieLimit: 4, Affiliations: ["Red Lanterns"], Keywords: ["Overcrush"],
        RawText: "Overcrush.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Polar Bear", KingdomEnergyType: "Claw");

    // Cassowary: the bird people are told to back away from.
    public static readonly CardDef HawkmanCrimeIsADisease = new(
        Id: "DOOM013", Name: "Hawkman", Subtitle: "Crime is a Disease", Set: "DOOM", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DOOM013Die", "Bolt", (0, 2, 2), (1, 3, 3), (1, 3, 4)),
        DieLimit: 4, Affiliations: ["Justice League"], Keywords: ["Intimidate"],
        RawText: "Intimidate.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Cassowary", KingdomEnergyType: "Eye");

    // Skunk: hits from a distance, so nobody gets close.
    public static readonly CardDef HawkgirlMedievalWeaponsExpert = new(
        Id: "SKC113", Name: "Hawkgirl", Subtitle: "Medieval Weapons Expert", Set: "SKC", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("SKC113Die", "Mask", (0, 2, 2), (1, 3, 3), (1, 4, 4)),
        DieLimit: 4, Affiliations: ["Justice League"], Keywords: ["Range"],
        RawText: "Range.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Skunk", KingdomEnergyType: "Shell");

    // Emperor Penguin: the huddle takes turns at the cold edge.
    public static readonly CardDef MankindMrsFoleysBabyBoy = new(
        Id: "WWE033", Name: "Mankind", Subtitle: "Mrs. Foley's Baby Boy", Set: "WWE", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("WWE033Die", "Mask", (0, 1, 4), (0, 2, 5), (1, 2, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["Tag Out"],
        RawText: "Tag Out.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Emperor Penguin", KingdomEnergyType: "Shell");

    // Raven: the trickster of a hundred stories, and a solver of puzzles.
    public static readonly CardDef LokiThroneOfContention = new(
        Id: "AI031", Name: "Loki", Subtitle: "Throne of Contention", Set: "AI", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("AI031Die", "Mask", (1, 1, 5), (1, 1, 7), (2, 2, 8)),
        DieLimit: 4, Affiliations: ["Villains"], Keywords: ["Attune"],
        RawText: "Attune.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Raven", KingdomEnergyType: "Eye");

    // ---- Short abilities ----

    // Chipmunk: stuffs its cheeks for later.
    public static readonly CardDef MantisCelestialMadonna = new(
        Id: "GOTG021", Name: "Mantis", Subtitle: "Celestial Madonna", Set: "GOTG", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("GOTG021Die", "Mask", (0, 1, 3), (0, 2, 4), (1, 3, 4)),
        DieLimit: 4, Affiliations: ["Guardians of the Galaxy"], Keywords: [],
        RawText: "When fielded, draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Chipmunk", KingdomEnergyType: "Wing");

    // Hare: off and running the moment it lands.
    public static readonly CardDef EbonyMawTwistedBrilliance = new(
        Id: "IG068", Name: "Ebony Maw", Subtitle: "Twisted Brilliance", Set: "IG", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("IG068Die", "Mask", (0, 1, 2), (0, 2, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: ["Black Order"], Keywords: [],
        RawText: "When fielded, draw and roll a die into your Reserve Pool.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(1, Zone.ReservePool))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Hare", KingdomEnergyType: "Wing");

    // Swordfish: slashes with its bill to slow its prey. Simplified: the
    // target is an opposing creature.
    public static readonly CardDef BladeVengeful = new(
        Id: "ASM110", Name: "Blade", Subtitle: "Vengeful", Set: "ASM", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("ASM110Die", "Fist", (1, 4, 5), (1, 4, 4), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["Spider-Friends"], Keywords: [],
        RawText: "When Blade attacks, target opposing character die gets -2A and Blade gets +2A until end of turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new Sequence([
            new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Opposing,
                Prompt: "choose an opposing creature to get -2 ATK."), AtkDelta: -2),
            new ModifyStat(new TargetFilter(Self: true), AtkDelta: 2),
        ]))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Swordfish", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json.", "Targets an opposing character die only; the source could target any."]);

    // Cone Snail: its harpoon stops small prey cold.
    public static readonly CardDef CockatriceMinionMonstrosity = new(
        Id: "FUS004", Name: "Cockatrice", Subtitle: "Minion Monstrosity", Set: "FUS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("FUS004Die", "Bolt", (0, 1, 3), (1, 2, 4), (2, 3, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, KO target character die with purchase cost 4 or less.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(
            Stat: new StatThreshold(StatKind.PurchaseCost, Max: 4))))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Cone Snail", KingdomEnergyType: "Eye");

    // Crocodile: waits at the water's edge for the young ones.
    public static readonly CardDef TriceratopsGreaterBeast = new(
        Id: "TOA086", Name: "Triceratops", Subtitle: "Greater Beast", Set: "TOA", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("TOA086Die", "Fist", (1, 3, 1), (1, 4, 2), (2, 6, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, KO target level 1 character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(
            Stat: new StatThreshold(StatKind.Level, Max: 1))))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Crocodile", KingdomEnergyType: "Claw");

    // Bull Shark: the most aggressive shark there is. Simplified: the
    // source also has to attack every turn, which no rule here enforces
    // continuously.
    public static readonly CardDef DestroyerSkyfathersDesign = new(
        Id: "THOR021", Name: "Destroyer", Subtitle: "Skyfathers' Design", Set: "THOR", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("THOR021Die", "Fist", (1, 3, 3), (1, 3, 4), (2, 5, 4)),
        DieLimit: 4, Affiliations: ["Villains"], Keywords: [],
        RawText: "When Destroyer attacks, Destroyer gets +2A until end of turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new ModifyStat(new TargetFilter(Self: true), AtkDelta: 2))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Bull Shark", KingdomEnergyType: "Claw", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json.", "Removed \"must attack each turn (if able)\" - no continuous must-attack rule exists."]);

    // Bottlenose Dolphin: hunts as a pod, herding fish for the others.
    public static readonly CardDef HerculesTheIncredibleHerc = new(
        Id: "CW042", Name: "Hercules", Subtitle: "The Incredible Herc", Set: "CW", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("CW042Die", "Fist", (1, 4, 4), (1, 5, 5), (2, 5, 7)),
        DieLimit: 4, Affiliations: ["Avengers"], Keywords: [],
        RawText: "When Hercules attacks, other attacking character dice get +1A until end of turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new ModifyStat(new TargetFilter(
            Ownership: TargetOwnership.Own, Count: 0, AttackersOnly: true, ExcludeSelf: true), AtkDelta: 1))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Bottlenose Dolphin", KingdomEnergyType: "Wing");

    // ---- Basic Actions (2026-10-20, user: "another bunch of Basic Action
    // cards"). Three or four per energy, named for an animal behaviour.
    // One per pack, no rarity (DiceKingdomPacks.BasicActions). Burst marks (*/**) are the printed
    // ones on MigrationDice.BasicAction's 0/1/2 faces. ----

    private static CardDef BasicAction(string id, string name, string set, int cost, string rawText,
        IReadOnlyList<TriggeredAbility> abilities, string kingdomName, string energy, string? simplified = null) => new(
        Id: id, Name: name, Subtitle: "Basic Action", Set: set, CardType: CardType.BasicAction,
        PurchaseCost: cost, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction(id + "Die", 0, 1, 2),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: rawText, Abilities: abilities, Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: kingdomName, KingdomEnergyType: energy, HasErrata: true,
        ErrataNotes: [$"Reference: BulkCards.json, {id} ({name}).",
            $"Energy faces: Generic 2 / Generic 2 / Generic 2 -> {energy} 2 / {energy} 2 / {energy} 1; standardized to the Dice Kingdom action-die pattern.",
            .. simplified is null ? Array.Empty<string>() : [simplified]]);

    private static TriggeredAbility Used(EffectNode effect) => new(TriggerKind.DieUsed, effect);
    private static Condition Burst => new OnBurstFace(BurstLevel.Single, AtLeast: true);
    private static TargetFilter Creature(string prompt, TargetOwnership owner = TargetOwnership.Any, string? bind = null) =>
        new(Ownership: owner, BindAs: bind, Prompt: prompt);

    // Claw
    public static readonly CardDef SurpriseAttack = BasicAction("IG014", "Surprise Attack", "IG", 2,
        "Deal 1 damage to target character die. */** Instead, deal 2 damage.",
        [Used(new Conditional(Burst,
            Then: new DealDamage(new Fixed(2), Creature("choose a creature to take 2 damage.")),
            Else: new DealDamage(new Fixed(1), Creature("choose a creature to take 1 damage."))))],
        "Pounce", "Claw");

    public static readonly CardDef ExcessiveForce = BasicAction("JUS067", "Excessive Force", "JUS", 2,
        "Deal 6 damage to target character die. Global: Pay Bolt. Target character die gets +1A.",
        [Used(new DealDamage(new Fixed(6), Creature("choose a creature to take 6 damage."))),
         new TriggeredAbility(TriggerKind.Global, new ModifyStat(Creature("choose a creature to get +1A this turn."), AtkDelta: 1),
            EnergyCost: new EnergyCost(1, "Bolt"))],
        "Gore", "Claw");

    public static readonly CardDef HulkOut = BasicAction("AOU029", "Hulk Out", "AOU", 2,
        "Target character die gains Overcrush until end of turn. */** That character die also gets +1A.",
        [Used(new Sequence([
            new GrantTag(Creature("choose a creature to gain Overcrush this turn.", bind: "frenzied"), ["Overcrush"]),
            new Conditional(Burst, new ModifyStat(new TargetFilter(Bound: "frenzied"), AtkDelta: 1)),
        ]))],
        "Frenzy", "Claw");

    // Shell
    public static readonly CardDef TacticalCover = BasicAction("TMNT057", "Tactical Cover", "TMNT", 3,
        "Your character dice get +2D until end of turn. */** Also, target character die gets +3D. Global: Pay Shield. Target character die gets +1D.",
        [Used(new Sequence([
            new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own, Count: 0), DefDelta: 2),
            new Conditional(Burst, new ModifyStat(Creature("choose a creature to get +3D more."), DefDelta: 3)),
         ])),
         new TriggeredAbility(TriggerKind.Global, new ModifyStat(Creature("choose a creature to get +1D this turn."), DefDelta: 1),
            EnergyCost: new EnergyCost(1, "Shield"))],
        "Circle Up", "Shell");

    public static readonly CardDef Blessing = BasicAction("BFF129", "Blessing", "BFF", 3,
        "Your character dice get +1A and +1D until end of turn. ** Two of your character dice get another +1A and +1D.",
        [Used(new Sequence([
            new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own, Count: 0), AtkDelta: 1, DefDelta: 1),
            new Conditional(new OnBurstFace(BurstLevel.Double),
                new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own, Count: 2,
                    Prompt: "choose two of your creatures to get another +1A and +1D."), AtkDelta: 1, DefDelta: 1)),
        ]))],
        "Grooming", "Shell");

    public static readonly CardDef ReleasedFromTheIce = BasicAction("THOR009", "Released from the Ice", "THOR", 2,
        "Prep a die from your Used Pile.",
        [Used(new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile],
            Prompt: "choose a die in your Used Pile to wake up into your Prep Area."), Zone.PrepArea))],
        "Hibernation", "Shell");

    // Wing
    public static readonly CardDef NastyPlot = BasicAction("AOU031", "Nasty Plot", "AOU", 4,
        "Draw 2 dice and put them into your Prep Area.",
        [Used(new DrawToZone(2, Zone.PrepArea))],
        "Nest Egg", "Wing");

    public static readonly CardDef DimensionDoor = BasicAction("SWW025", "Dimension Door", "SWW", 4,
        "Target character die you control can't be blocked this turn.",
        [Used(new CombatFlag(Creature("choose one of your creatures - it can't be blocked this turn.", TargetOwnership.Own),
            CombatFlagKind.Unblockable))],
        "Take Flight", "Wing");

    // Source: "If it is a Bolt character die" - Wing is Bolt's Dice Kingdom
    // energy, written here directly because effect filters aren't renamed.
    public static readonly CardDef UpgradeUnibeam = BasicAction("IMW034", "Upgrade - Unibeam", "IMW", 2,
        "Target character die you control gets +2A until end of turn. If it is a Wing character die, it also gains Fast.",
        [Used(new Sequence([
            new ModifyStat(Creature("choose one of your creatures to get +2A this turn.", TargetOwnership.Own, "diver"), AtkDelta: 2),
            new GrantTag(new TargetFilter(Bound: "diver", Tags: new TagQuery(AnyOf: ["Wing"])), ["Fast"]),
        ]))],
        "Dive", "Wing");

    // Eye
    public static readonly CardDef TooBigToFly = BasicAction("SKC014", "Too Big to Fly", "SKC", 4,
        "KO target character die with 5A or more. Global: Pay Fist. Target character die gets +1A.",
        [Used(new Ko(new TargetFilter(Stat: new StatThreshold(StatKind.Attack, Min: 5), Prompt: "choose a creature with 5 ATK or more to KO."))),
         new TriggeredAbility(TriggerKind.Global, new ModifyStat(Creature("choose a creature to get +1A this turn."), AtkDelta: 1),
            EnergyCost: new EnergyCost(1, "Fist"))],
        "Snare", "Eye");

    public static readonly CardDef KryptoniteThreat = BasicAction("WF029", "Kryptonite Threat", "WF", 4,
        "Target character die can't block this turn. */** Also, draw a die into your Prep Area.",
        [Used(new Sequence([
            new CombatFlag(Creature("choose a creature that can't block this turn.", TargetOwnership.Opposing), CombatFlagKind.CantBlock),
            new Conditional(Burst, new DrawToZone(1, Zone.PrepArea)),
        ]))],
        "Stare Down", "Eye");

    // Simplified: only opposing creatures (the source could reroll any two).
    public static readonly CardDef TheBeyonder = BasicAction("MSW012", "The Beyonder", "MSW", 4,
        "Reroll up to 2 target opposing character dice. Gain 1 life.",
        [Used(new Sequence([
            new Reroll(new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 2, Optional: true,
                Prompt: "choose up to two opposing creatures to reroll.")),
            new LifeChange(new Fixed(1)),
        ]))],
        "Dazzle", "Eye", simplified: "Rerolls only opposing character dice; the source could reroll any two.");

    public static readonly CardDef Banishment = BasicAction("FUS025", "Banishment", "FUS", 2,
        "Put target die from your opponent's Prep Area into their Used Pile.",
        [Used(new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.PrepArea],
            Prompt: "choose a die in your opponent's Prep Area to scatter to their Used Pile."), Zone.UsedPile))],
        "Scatter", "Eye");

    // ---- Painted swaps (2026-10-27) ----
    // Animals with a public-domain painting, swapped into Set 1 for ones
    // that had none (the user: "source other animals that we can find
    // paintings for, find a card to build for them, and swap them into
    // Set 1"). The other ten are DpsCards entries built the same day.

    // Peacock: fans its train of eyes and the other bird backs off.
    public static readonly CardDef PunisherUnlikelyAlly = new(
        Id: "CW056", Name: "Punisher", Subtitle: "Unlikely ally", Set: "CW", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("CW056Die", "Fist", (1, 4, 1), (1, 5, 2), (2, 6, 2)),
        DieLimit: 4, Affiliations: ["Thunderbolts"], Keywords: ["Intimidate"],
        RawText: "Intimidate.",
        Abilities: [], Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Peacock", KingdomEnergyType: "Eye");

    // Beaver: builds the dam the whole pond shelters behind.
    public static readonly CardDef GrootWeAreGroot = new(
        Id: "AOU136", Name: "Groot", Subtitle: "We are Groot", Set: "AOU", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("AOU136Die", "Shield", [0, 1, 1], (0, 1, 2), (1, 2, 6), (2, 3, 7)),
        DieLimit: 4, Affiliations: ["Guardians of The Galaxy"], Keywords: [],
        RawText: "While Groot is active, your other character dice in the Field Zone get +3D.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Count: 0, ExcludeSelf: true),
            DefDelta: new Fixed(3))], IncludeInTeamBuilder: true,
        KingdomName: "Beaver", KingdomEnergyType: "Shell");

    public static IReadOnlyList<CardDef> All =>
    [
        BansheeWailOfTheBanshee, FrostGiantLesserElemental, NamoritaTauntingVillains, CaptainAmericaLivingLegend,
        PowerGirlKryptonian, NegativeManPhasing, SupermanLivingInAWorldOfCardboard,
        GreyHunterSoneOfRuss, SquirrelGirlUnbeatable, RondaRouseyRowdy, SupergirlAngryAlien, HawkmanCrimeIsADisease,
        HawkgirlMedievalWeaponsExpert, MankindMrsFoleysBabyBoy, LokiThroneOfContention,
        MantisCelestialMadonna, EbonyMawTwistedBrilliance, BladeVengeful, CockatriceMinionMonstrosity,
        TriceratopsGreaterBeast, DestroyerSkyfathersDesign, HerculesTheIncredibleHerc,
        SurpriseAttack, ExcessiveForce, HulkOut, TacticalCover, Blessing, ReleasedFromTheIce,
        NastyPlot, DimensionDoor, UpgradeUnibeam, TooBigToFly, KryptoniteThreat, TheBeyonder, Banishment,
        PunisherUnlikelyAlly, GrootWeAreGroot,
    ];
}
