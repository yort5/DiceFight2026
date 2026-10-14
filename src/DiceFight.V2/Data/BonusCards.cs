using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// Cards migrated one-off, on request, outside both systematic sweeps -
// CardCatalog.cs's curated teams and DpsCards.cs's 145-card DPS set. Kept
// separate so neither of those counts (nor their own catalog-wide
// invariant tests, once Task 5 lands) silently picks up an extra card.
// Source of truth for stats/text is the bulk catalog
// (src/DiceFight.Engine/Data/BulkCards.json), not SampleCards.cs - this
// card was never in v1's curated set at all.
public static class BonusCards
{
    // Domino, "Not Really A Party Girl" (XFO010) - user-requested
    // 2026-09-01, right after the Energize unlock (DpsCards.cs Batch 4)
    // made her trivial: same Sequence(DealDamage, Reroll(Self)) shape as
    // Cyclops "Defending the Phoenix", just damage-to-player instead of
    // damage-to-a-character-die. Reuses DpsCards.Energize rather than
    // re-deriving the same signed-off shape.
    public static readonly CardDef DominoNotReallyAPartyGirl = new(
        Id: "XFO010", Name: "Domino", Subtitle: "Not Really A Party Girl", Set: "XFO", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("XFO010Die", "Shield", (0, 2, 2), (1, 3, 2), (1, 5, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - Deal 1 damage to target opponent and reroll this die.",
        Abilities: [..DpsCards.Energize(new Sequence(
        [
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            new Reroll(new TargetFilter(Self: true)),
        ]))],
        Continuous: []);

    // User-selected adaptations (2026-10-13); shared effects, stable source IDs.
    // Text/costs: rollinthunder_cards_with_dice(1).json. Three-level Hulk/Beast
    // stats are recovered from BulkCards.json, whose level parsing is complete.
    // Basic Action burst layout follows the existing reference DieFaces table.
    private static EffectNode OpposingBoardDamage(int amount) =>
        new DealDamage(new Fixed(amount), new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0));

    public static readonly CardDef AlfredPennyworthMI5 = new(
        Id: "WF075", Name: "Alfred Pennyworth", Subtitle: "MI-5", Set: "WF", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("WF075Die", "Shield", [0, 0, 0], (0, 1, 1), (0, 1, 2), (0, 2, 2)),
        DieLimit: 4, Affiliations: ["Batman Family"], Keywords: ["Kinfolk"],
        RawText: "Kinfolk. When KO'd, you may roll a Sidekick die from your Used Pile. If you roll an energy result, return Alfred Pennyworth to the Field Zone at level 1. Either way, return the rolled die to the Used Pile.",
        Abilities: [new TriggeredAbility(TriggerKind.DieKOd,
            new Conditional(new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own,
                Zones: [Zone.UsedPile], Tags: new TagQuery(AnyOf: ["sidekick"]), Count: 0), 1),
                new MayPay(null, new Sequence([
                    new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own,
                        Zones: [Zone.UsedPile], Tags: new TagQuery(AnyOf: ["sidekick"]), BindAs: "rolled",
                        Prompt: "Choose a Tardigrade from your Used Pile to test for recovery."), Zone.ReservePool),
                    new Reroll(new TargetFilter(Bound: "rolled")),
                    new Conditional(new OnFaceKind(FaceKind.EnergyFace, "rolled"), new Sequence([
                        new MoveDie(new TargetFilter(Self: true), Zone.FieldZone),
                        new Spin(new TargetFilter(Self: true), SetLevel: 1),
                    ])),
                    new MoveDie(new TargetFilter(Bound: "rolled"), Zone.UsedPile),
                ]), Prompt: "Roll a Tardigrade from Used to try to return this creature at level 1?")))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Planarian", KingdomEnergyType: "Shell", HasErrata: true,
        ErrataNotes: ["Reference: rollinthunder_cards_with_dice(1).json, WF075 (Alfred Pennyworth: MI-5).", "Removed Batman dice from the recovery-roll choices; only Sidekick dice remain eligible."]);

    public static readonly CardDef AtlantisWealthyKingdom = new(
        Id: "MYST002", Name: "Atlantis", Subtitle: "Wealthy Kingdom", Set: "MYST", CardType: CardType.Action,
        PurchaseCost: 2, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Action("MYST002Die", ["Shield"], 0, 0, 0),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Until end of turn, whenever you field a Sidekick character die, draw and roll 2 dice.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed,
            new GrantTriggeredAbility(new TriggeredAbility(TriggerKind.DieFielded,
                new DrawToZone(2, Zone.ReservePool),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])))))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Abundant Waters", KingdomEnergyType: "Wing");

    public static readonly CardDef HulkGreenGoliath = new(
        Id: "AVX077", Name: "Hulk", Subtitle: "Green Goliath", Set: "AVX", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("AVX077Die", "Fist", [1, 1, 0], (1, 6, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 5, Affiliations: ["Avengers"], Keywords: [],
        RawText: "While Hulk is active, whenever you or a Hulk die takes damage, deal 2 damage to each opposing character die (no matter how many Hulk dice are active).",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieDamaged, OpposingBoardDamage(2),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, SameCardAsListener: true, RequireActive: true), OncePerCard: true),
            new TriggeredAbility(TriggerKind.PlayerDamaged, OpposingBoardDamage(2),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, RequireActive: true), OncePerCard: true),
        ],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Hercules Beetle", KingdomEnergyType: "Claw", HasErrata: true,
        ErrataNotes: ["Reference: rollinthunder_cards_with_dice(1).json, AVX077 (Hulk: Green Goliath).", "Removed the single-burst upgrade from 2 damage to 3; the retaliation always deals 2 damage."]);

    public static readonly CardDef WongExpertofKamarTaj = new(
        Id: "DRS022", Name: "Wong", Subtitle: "Expert of Kamar-Taj", Set: "DRS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DRS022Die", "Fist", [0, 0, 0], (0, 2, 2), (1, 3, 3), (1, 4, 3)),
        DieLimit: 4, Affiliations: ["Mystic"], Keywords: ["Kinfolk", "Fast"],
        RawText: "Kinfolk. Fast.",
        Abilities: [],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Marmoset", KingdomEnergyType: "Wing");

    public static readonly CardDef JubileeOlympicGymnast = new(
        Id: "XMF028", Name: "Jubilee", Subtitle: "Olympic Gymnast", Set: "XMF", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("XMF028Die", "Bolt", [0, 0, 0], (0, 2, 1), (1, 3, 3), (2, 4, 3)),
        DieLimit: 5, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Jubilee is active, On Field: when you field a Bolt character die, deal 1 damage to the opponent.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Bolt"])), OncePerCard: true)],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Fire Ant", KingdomEnergyType: "Claw");

    public static readonly CardDef AngelaOdinsdottir = new(
        Id: "IG058", Name: "Angela", Subtitle: "Odinsdottir", Set: "IG", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("IG058Die", "Bolt", [0, 0, 0], (0, 2, 3), (1, 4, 3), (1, 4, 4)),
        DieLimit: 4, Affiliations: ["Guardians of the Galaxy"], Keywords: [],
        RawText: "While Angela is active, ignore your opponent's On Field abilities.",
        Abilities: [],
        Continuous: [new AbilityBlank(new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0), Trigger: TriggerKind.DieFielded)], IncludeInTeamBuilder: true,
        KingdomName: "Electric Ray", KingdomEnergyType: "Eye");

    public static readonly CardDef BeastKreature = new(
        Id: "AVX066", Name: "Beast", Subtitle: "Kreature", Set: "AVX", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("AVX066Die", "Mask", [1, 0, 0], (0, 1, 2), (0, 2, 3), (1, 2, 4)),
        DieLimit: 5, Affiliations: ["X-Men"], Keywords: [],
        RawText: "On Block: spin each blocked character die down 1 level. If it is already level 1, KO it.",
        Abilities: [new TriggeredAbility(TriggerKind.DieBlocks, new Sequence([
            new Ko(new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0, EngagedWith: "self",
                Stat: new StatThreshold(StatKind.Level, Max: 1))),
            new Spin(new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0, EngagedWith: "self",
                Stat: new StatThreshold(StatKind.Level, Min: 2)), LevelDelta: -1),
        ]))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Velvet Worm", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: rollinthunder_cards_with_dice(1).json, AVX066 (Beast: Kreature).", "Removed the single-burst ability that gains 2 life when this blocking effect KOs a character."]);

    public static readonly CardDef StormAfricanPriestess = new(
        Id: "AVX019", Name: "Storm", Subtitle: "African Priestess", Set: "AVX", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("AVX019Die", "Mask", [0, 0, 0], (0, 2, 1), (1, 2, 2), (1, 3, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "On Field: reroll a target opposing character die. If the result is not a character face, place it in your opponent's Used Pile.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Reroll(new TargetFilter(Ownership: TargetOwnership.Opposing), NonCharacterMoveTo: Zone.UsedPile))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Leaf Insect", KingdomEnergyType: "Eye");

    public static readonly CardDef DaniMoonstarEmpathicIllusionCasting = new(
        Id: "XFO004", Name: "Dani Moonstar", Subtitle: "Empathic Illusion Casting", Set: "XFO", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("XFO004Die", "Mask", [0, 0, 0], (0, 1, 1), (0, 2, 1), (1, 2, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When Dani Moonstar is blocked, reroll the blocking character dice. Each energy result goes to its controller's Used Pile and deals 2 damage to that controller. Character results remain blocking on their rolled levels.",
        Abilities: [new TriggeredAbility(TriggerKind.DieBlocked,
            new Reroll(new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0, EngagedWith: "self"),
                NonCharacterMoveTo: Zone.UsedPile, DamagePerMoved: 2, ResultKindForMove: FaceKind.EnergyFace, DamageToRolledController: true))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Orchid Mantis", KingdomEnergyType: "Eye");

    public static readonly CardDef ForgeIntuitiveGenius = new(
        Id: "XFO016", Name: "Forge", Subtitle: "Intuitive Genius", Set: "XFO", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("XFO016Die", "Bolt", [0, 0, 0], (1, 2, 2), (1, 4, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize: Action dice, including Basic Action dice, cost you 2 less to purchase until end of turn (minimum 1).",
        Abilities: [..KeywordAbilities.Energize(new PurchaseModifier(-2, CardKind: CardType.Action, EveryPurchase: true))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Bowerbird", KingdomEnergyType: "Wing");

    public static readonly CardDef JubileeLifeontheStreets = new(
        Id: "XFC017", Name: "Jubilee", Subtitle: "Life on the Streets", Set: "XFC", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("XFC017Die", "Bolt", [0, 0, 0], (0, 2, 1), (1, 3, 3), (2, 4, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Awaken"],
        RawText: "Awaken: you may deal 1 damage to all opposing character dice.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged,
            new MayPay(null, OpposingBoardDamage(1), Prompt: "Awaken: deal 1 damage to every opposing creature?"),
            Filter: new EventFilter(RequireSelf: true, LevelIncreased: true))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Fire Salamander", KingdomEnergyType: "Claw");

    public static readonly CardDef StormMorlockChampion = new(
        Id: "XFC110", Name: "Storm", Subtitle: "Morlock Champion", Set: "XFC", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("XFC110Die", "Bolt", [0, 0, 0], (0, 2, 1), (0, 3, 1), (1, 3, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Awaken"],
        RawText: "Awaken: you may reroll a target opposing die. Energy results go to its controller's Used Pile; otherwise restore its original face.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged,
            new MayPay(null, new Reroll(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing),
                NonCharacterMoveTo: Zone.UsedPile, RestoreOtherFaces: true, ResultKindForMove: FaceKind.EnergyFace),
                Prompt: "Awaken: reroll an opposing die to try to send it to Used?"),
            Filter: new EventFilter(RequireSelf: true, LevelIncreased: true))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Mimic Filefish", KingdomEnergyType: "Eye");

    public static readonly CardDef VenerableDreadnaughtCustodianofKnowledge = new(
        Id: "SW018", Name: "Venerable Dreadnaught", Subtitle: "Custodian of Knowledge", Set: "SW", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("SW018Die", "Shield", [0, 0, 0], (1, 3, 4), (1, 4, 6), (2, 5, 7)),
        DieLimit: 2, Affiliations: ["Imperium", "Space Wolves"], Keywords: [],
        RawText: "While Venerable Dreadnaught is active, your active character dice have Range 1.",
        Abilities: [],
        Continuous: [new TagAura(new TargetFilter(Ownership: TargetOwnership.Own, Count: 0), ["Range 1"])], IncludeInTeamBuilder: true,
        KingdomName: "Harris’s Hawk", KingdomEnergyType: "Claw");

    public static readonly CardDef BigEntranceBasicActionCard = new(
        Id: "IG003", Name: "Big Entrance", Subtitle: "Basic Action Card", Set: "IG", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("IG003Die", 0, 1, 2),
        DieLimit: 3, Affiliations: [], Keywords: ["Impulse"],
        RawText: "Impulse: you may add this die to your Bag when purchased. When used, dice purchased this turn cost 1 less (minimum 1) and may be put directly into your Bag instead of Used. Multiple uses do not stack.",
        Abilities: [
            new TriggeredAbility(TriggerKind.PurchaseMade, new MayPay(null,
                new MoveDie(new TargetFilter(Self: true), Zone.Bag), Prompt: "Impulse: put this purchase directly into your Bag?")),
            new TriggeredAbility(TriggerKind.DieUsed, new PurchaseModifier(-1, GoesToZone: Zone.Bag,
                EveryPurchase: true, OptionalDestination: true, NonStacking: true)),
        ],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Spring Emergence", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: ["Reference: rollinthunder_cards_with_dice(1).json, IG003.", "Energy faces: Generic 2 / Generic 2 / Generic 2 -> Wing 2 / Wing 2 / Wing 1; standardized to the Dice Kingdom action-die pattern."]);

    public static readonly CardDef PingBasicActionCard = new(
        Id: "JUS069", Name: "Ping", Subtitle: "Basic Action Card", Set: "JUS", CardType: CardType.BasicAction,
        PurchaseCost: 2, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("JUS069Die", 0, 1, 2),
        DieLimit: 3, Affiliations: [], Keywords: ["Rebound"],
        RawText: "Deal 1 damage to the opponent. */** Rebound.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Thorn Shot", KingdomEnergyType: "Claw", HasErrata: true,
        ErrataNotes: ["Reference: rollinthunder_cards_with_dice(1).json, JUS069.", "Energy faces: Generic 2 / Generic 2 / Generic 2 -> Claw 2 / Claw 2 / Claw 1; standardized to the Dice Kingdom action-die pattern."],
        KeywordConditions: new Dictionary<string, Condition> { ["Rebound"] = new OnBurstFace(BurstLevel.Single, AtLeast: true) });

    public static readonly CardDef InvestigationBasicActionCard = new(
        Id: "JUS068", Name: "Investigation", Subtitle: "Basic Action Card", Set: "JUS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("JUS068Die", 0, 1, 2),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Draw and roll 2 dice into your Reserve Pool.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new DrawToZone(2, Zone.ReservePool))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Foraging", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: ["Reference: rollinthunder_cards_with_dice(1).json, JUS068.", "Energy faces: Generic 2 / Generic 2 / Generic 2 -> Wing 2 / Wing 2 / Wing 1; standardized to the Dice Kingdom action-die pattern."]);

    public static IReadOnlyList<CardDef> All =>
    [
        DominoNotReallyAPartyGirl,
        AlfredPennyworthMI5,
        AtlantisWealthyKingdom,
        HulkGreenGoliath,
        WongExpertofKamarTaj,
        JubileeOlympicGymnast,
        AngelaOdinsdottir,
        BeastKreature,
        StormAfricanPriestess,
        DaniMoonstarEmpathicIllusionCasting,
        ForgeIntuitiveGenius,
        JubileeLifeontheStreets,
        StormMorlockChampion,
        VenerableDreadnaughtCustodianofKnowledge,
        BigEntranceBasicActionCard,
        PingBasicActionCard,
        InvestigationBasicActionCard,
    ];
}
