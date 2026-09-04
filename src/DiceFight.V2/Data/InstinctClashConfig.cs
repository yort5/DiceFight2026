using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// v3 "Instinct Clash" - the animal-themed, from-scratch game described in
// ~/DiceFight2026/v3/DESIGN_NOTES.md, expressed as one GameConfig (the
// same "current game is just one config" proof DiceFightClassicConfig.cs
// is, for a genuinely different game rather than a variant ruleset).
//
// Deliberately small and simple, matching the brief: no Basic Actions
// (BasicActionSlots: 0), one Character ability apiece using the plainest
// templates in the closed vocabulary, no win condition/deck-out wiring
// yet (scoped out of this pass - see the mellow-sparking-comet plan).
// Every number here is a first-pass placeholder for playtesting, not a
// balanced value - v3/DESIGN_NOTES.md is the source of truth for which
// numbers are actually locked vs. still moving.
public static class InstinctClashConfig
{
    // --- Tardigrade dice (the free-to-field basic creature; one per
    // energy type, matching v3/DESIGN_NOTES.md's locked spec exactly:
    // two L1, two L2, one Bulwark, one Surge). ---

    private static DieDefinition TardigradeDie(string energyType) => new($"Tardigrade{energyType}",
    [
        new Face([new SymbolAmount(energyType, 2)], new CharacterFaceData(1, FieldingCost: 0, Attack: 0, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 2)], new CharacterFaceData(1, FieldingCost: 0, Attack: 0, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([], new CharacterFaceData(3, FieldingCost: 0, Attack: 1, Defense: 3), Kind: FaceKind.CharacterFace), // Bulwark
        new Face([new SymbolAmount("Wild", 2)], Kind: FaceKind.EnergyFace), // Surge - no character face at all
    ]);

    // --- Characters: a small, simple-ability pool - two per energy type,
    // reskinned from v3/CARD_INSPIRATION.md's already-vetted "confirmed
    // buildable" picks. Every face is a plain stat progression (no energy
    // - matches the real Dice Masters precedent Character faces never
    // print energy, per this session's DPS-catalog check), FieldingCost
    // 0 on every face is deliberately NOT set here - Characters, unlike
    // Tardigrades, cost real energy to field (any type, per rule 2.6.3.2).
    private static DieDefinition CharacterDie(string dieId, int fieldingCost, params (int Attack, int Defense)[] levels)
    {
        var faces = new List<Face>();
        foreach (var (attack, defense) in levels)
        {
            var face = new Face([], new CharacterFaceData(faces.Count / 2 + 1, fieldingCost, attack, defense), Kind: FaceKind.CharacterFace);
            faces.Add(face);
            faces.Add(face);
        }
        return new DieDefinition(dieId, faces);
    }

    private static TargetFilter OwnCreatures => new(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Count: 0);
    private static TargetFilter WeakOpposingCreatures(int maxDefense) => new(
        Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Defense, Max: maxDefense));

    // Claw

    public static readonly CardDef HoneyBadger = new(
        Id: "IC-CLAW-01", Name: "Honey Badger", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-01Die", fieldingCost: 1, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Wolverine = new(
        Id: "IC-CLAW-02", Name: "Wolverine", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-02Die", fieldingCost: 2, (0, 2), (0, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    // 6 more Claw picks (2026-09-06, "build out a full roster... 8
    // different animals") - same v3/CARD_INSPIRATION.md sourcing pass as
    // the original two, picked for being directly buildable against the
    // same plain templates already proven above rather than the fuller
    // multi-clause/bonus-on-double-roll/Global text CARD_INSPIRATION.md
    // records as those cards' actual printed text. "Wolf" itself was
    // skipped as a pick - it's the Claw Champion's own name now.
    public static readonly CardDef GrizzlyBear = new(
        Id: "IC-CLAW-03", Name: "Grizzly Bear", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-03Die", fieldingCost: 2, (1, 5), (2, 6), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Orca = new(
        Id: "IC-CLAW-04", Name: "Orca", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-04Die", fieldingCost: 2, (0, 3), (1, 3), (1, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef PeregrineFalcon = new(
        Id: "IC-CLAW-05", Name: "Peregrine Falcon", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-05Die", fieldingCost: 2, (1, 5), (2, 7), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 3 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Tiger = new(
        Id: "IC-CLAW-06", Name: "Tiger", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-06Die", fieldingCost: 2, (1, 5), (2, 7), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: deal 2 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef Stoat = new(
        Id: "IC-CLAW-07", Name: "Stoat", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-07Die", fieldingCost: 1, (0, 2), (1, 3), (2, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef CapeBuffalo = new(
        Id: "IC-CLAW-08", Name: "Cape Buffalo", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-08Die", fieldingCost: 2, (1, 4), (1, 6), (2, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 ATK.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, AtkDelta: new Fixed(1))]);

    // Shell

    public static readonly CardDef Hippopotamus = new(
        Id: "IC-SHELL-01", Name: "Hippopotamus", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-01Die", fieldingCost: 2, (0, 1), (1, 1), (2, 1)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: gain 2 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(2)))],
        Continuous: []);

    public static readonly CardDef MuskOx = new(
        Id: "IC-SHELL-02", Name: "Musk Ox", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-02Die", fieldingCost: 2, (0, 2), (1, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 DEF.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, DefDelta: new Fixed(1))]);

    // Wing

    public static readonly CardDef Osprey = new(
        Id: "IC-WING-01", Name: "Osprey", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-01Die", fieldingCost: 2, (0, 4), (1, 5), (2, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: move a die from your discard to your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Count: 1), Zone.PrepArea))],
        Continuous: []);

    public static readonly CardDef BarnSwallow = new(
        Id: "IC-WING-02", Name: "Barn Swallow", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-02Die", fieldingCost: 1, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Whenever this levels up: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged, new DrawToZone(1, Zone.PrepArea, Zone.Bag),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    // Eye

    public static readonly CardDef BarnOwl = new(
        Id: "IC-EYE-01", Name: "Barn Owl", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-01Die", fieldingCost: 2, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: a weak target creature (3 ATK or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Attack, Max: 3)), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Hyena = new(
        Id: "IC-EYE-02", Name: "Hyena", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-02Die", fieldingCost: 2, (1, 3), (1, 4), (2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Gets +1 ATK for each weak opposing creature (2 DEF or less).",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true), AtkDelta: new PerMatch(WeakOpposingCreatures(maxDefense: 2), Multiplier: 1))]);

    public static readonly IReadOnlyDictionary<string, CardDef> Catalog = new List<CardDef>
    {
        HoneyBadger, Wolverine, Hippopotamus, MuskOx, Osprey, BarnSwallow, BarnOwl, Hyena,
    }.ToDictionary(c => c.Id);

    // Which two Characters a team gets when it picks a Champion - the
    // v3 "one energy type per team" starting point (v3/DESIGN_NOTES.md's
    // own open question: whether Champion/Character energy must match).
    // API layer (Phase 3) reads this to build TeamCardIds from a Champion
    // choice alone, no deckbuilding UI needed yet.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CharactersByEnergyType = new Dictionary<string, IReadOnlyList<string>>
    {
        ["Claw"] = [HoneyBadger.Id, Wolverine.Id],
        ["Shell"] = [Hippopotamus.Id, MuskOx.Id],
        ["Wing"] = [Osprey.Id, BarnSwallow.Id],
        ["Eye"] = [BarnOwl.Id, Hyena.Id],
    };

    // --- Champions: no die, one flat always-on passive, plus the
    // Tardigrade pool their team draws from (GameSetup.SeedBasicDicePool
    // reads ChampionDef.TardigradePool in preference to the shared
    // Config.BasicDicePool - see that change's own remarks). ---

    // Starter-deck sizing (2026-09-05, was 4): 8 Tardigrades is enough
    // Bag depth to cover a full two turns of drawing DrawCount(4) each
    // without an early Used-Pile reshuffle, matching the "traditional
    // deck-building starter hand" the user asked for rather than a bag
    // that's already thin by turn two.
    public static readonly IReadOnlyList<ChampionDef> Champions =
    [
        // Renamed from "Lion" (2026-09-06) - a real fan-art avatar exists
        // for this one now (icons.tsx's WolfIcon), so the Claw Champion
        // became the animal the art actually is.
        new("Wolf", "Wolf", "Claw", ChampionPassiveKind.AttackBuff, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Claw"), Count: 8)],
        },
        new("Armadillo", "Armadillo", "Shell", ChampionPassiveKind.DefenseBuff, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Shell"), Count: 8)],
        },
        new("GoldenEagle", "Golden Eagle", "Wing", ChampionPassiveKind.FieldingCostDiscount, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Wing"), Count: 8)],
        },
        new("GreatHornedOwl", "Great Horned Owl", "Eye", ChampionPassiveKind.PurchaseCostDiscount, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Eye"), Count: 8)],
        },
    ];

    public static readonly GameConfig Config = new(
        Id: "instinct-clash",
        Name: "Instinct Clash",
        EnergySymbols:
        [
            new SymbolDef("Claw"), new SymbolDef("Shell"), new SymbolDef("Wing"), new SymbolDef("Eye"),
            new SymbolDef("Wild", IsWild: true),
        ],
        Keywords: [],
        Rules: new RulesConfig(
            StartingLife: 20,
            DrawCount: 4,
            MaxTeamCards: 2, // both Characters matching the chosen Champion's energy type
            MaxTeamDice: 8, // 2 Characters x DieLimit 4
            BasicActionCount: 0),
        BasicDicePool: [], // every player has a ChampionId, so this is never actually read
        BasicActionSlots: 0)
    {
        Champions = Champions,
    };
}
