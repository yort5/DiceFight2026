using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// V2_PLAN.md Phase 8 task 4 - the Dark Phoenix Saga catalog, migrated in
// batches from v1's `SampleCards.cs` (the source of truth for stats and
// text) using `V2_VOCABULARY.md`'s own worked expressions where Phase 0
// already produced them. Face layout follows MigrationDice's documented
// convention. Cards that don't fit go to `V2_TAIL_POLICY.md` vanilla -
// no vocabulary additions (ground rule 2).
//
// Batch 1 (2026-08-24): 14 implemented, 1 tailed (Colossus un-tailed
// same day by Spike C - see its own remarks). Deliberately drawn
// from the cards Phase 0 worked out on paper, so each one is also a
// live check that the paper expression really runs - which already paid
// off: Colossus "Piotr" was marked a clean fit on paper, and wiring it
// for real surfaced that the frozen EventFilter has no step
// discriminator (see its own remarks below).
public static class DpsCards
{
    // --- Basic Actions ---

    // V2_VOCABULARY.md Part 2 #1's worked expression, verbatim.
    public static readonly CardDef PowerBolt = new(
        Id: "DPS011", Name: "Power Bolt", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolId: null,
        Die: MigrationDice.Action("DPS011Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Deal 2 damage to target character die or player.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.DieOrPlayer)))],
        Continuous: []);

    // Part 2 #4's worked expression. The "**" clause needs a real
    // double-burst face to branch on - see MigrationDice.Action's own
    // remarks on burst distribution being part of the stated
    // approximation. "Sidekick" is a plain tag (Part 2 #4's own point:
    // no dedicated SidekicksOnly filter needed, unlike v1).
    public static readonly CardDef Rally = new(
        Id: "DPS013", Name: "Rally", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolId: null,
        Die: MigrationDice.Action("DPS013Die", 0, 1, 2),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Move up to 2 Sidekick dice from your Used Pile to your Field Zone. ** Instead, move up to 3 Sidekicks instead.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed,
            new Conditional(new OnBurstFace(BurstLevel.Double),
                Then: new MoveDie(SidekicksInUsedPile(3), Zone.FieldZone),
                Else: new MoveDie(SidekicksInUsedPile(2), Zone.FieldZone)))],
        Continuous: []);

    private static TargetFilter SidekicksInUsedPile(int count) => new(
        Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Kind: TargetKind.AnyDie,
        Count: count, Tags: new TagQuery(AnyOf: ["sidekick"]), Optional: true);

    // --- Characters ---

    // Part 2 #2's worked expression - confirms LifeChange's signed-amount
    // design covers v1's separate GainLife/LoseLife in one template.
    public static readonly CardDef RonanTheAccuserTreason = new(
        Id: "DPS050", Name: "Ronan the Accuser", Subtitle: "Treason!", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolId: "Bolt",
        Die: MigrationDice.Character("DPS050Die", "Bolt", (1, 5, 5), (1, 6, 7), (2, 8, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When Ronan the Accuser is fielded, lose 1 life. When Ronan the Accuser is KO'd, your opponent loses 1 life.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(-1))),
            new TriggeredAbility(TriggerKind.DieKOd, new LifeChange(new Fixed(-1), Whose: TargetOwnership.Opposing)),
        ],
        Continuous: []);

    // Part 2 #3's worked expression - validates the single-stat-threshold
    // TargetFilter field.
    public static readonly CardDef StormCloudCover = new(
        Id: "DPS092", Name: "Storm", Subtitle: "Cloud Cover", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolId: "Bolt",
        Die: MigrationDice.Character("DPS092Die", "Bolt", (0, 2, 1), (0, 3, 1), (1, 3, 3)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, target character die with 3A or less can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Attack, Max: 3)), CombatFlagKind.CantBlock))],
        Continuous: []);

    // Part 2 #9's worked expression - keyword grants are just tag grants
    // in v2, no separate GrantKeyword/GrantAffiliation split.
    public static readonly CardDef PsylockeTelepath = new(
        Id: "DPS088", Name: "Psylocke", Subtitle: "Telepath", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolId: "Mask",
        Die: MigrationDice.Character("DPS088Die", "Mask", (0, 1, 2), (0, 2, 2), (1, 3, 3)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, target character die gets Overcrush.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new GrantTag(new TargetFilter(Kind: TargetKind.CharacterDie), ["Overcrush"]))],
        Continuous: []);

    // Part 2 #8's worked expression.
    public static readonly CardDef MasterMoldTargetingMutants = new(
        Id: "DPS082", Name: "Master Mold", Subtitle: "Targeting Mutants", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolId: "Shield",
        Die: MigrationDice.Character("DPS082Die", "Shield", (1, 5, 5), (2, 6, 6), (3, 8, 8)),
        DieLimit: 3, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, KO target Brotherhood of Mutants character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["Brotherhood of Mutants"]))))],
        Continuous: []);

    public static readonly CardDef MasterMoldUntoldElectronicExpertise = new(
        Id: "DPS122", Name: "Master Mold", Subtitle: "Untold Electronic Expertise", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolId: "Shield",
        Die: MigrationDice.Character("DPS122Die", "Shield", (1, 5, 5), (2, 6, 6), (3, 8, 8)),
        DieLimit: 2, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, KO target X-Men character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["X-Men"]))))],
        Continuous: []);

    // Part 2 #7's worked expression, including its own finding: "while
    // active" needs no modeling at all - a triggered ability only listens
    // while its source die is fielded, which is already how EventBus
    // scans listeners.
    public static readonly CardDef MagnetoFounderOfTheBrotherhood = new(
        Id: "DPS146", Name: "Magneto", Subtitle: "Founder of the Brotherhood", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolId: "Mask",
        Die: MigrationDice.Character("DPS146Die", "Mask", (1, 4, 4), (2, 5, 7), (3, 6, 8)),
        DieLimit: 1, Affiliations: ["Brotherhood of Mutants"], Keywords: [],
        RawText: "While Magneto is active, when one of your Brotherhood of Mutants character dice is KO'd, KO target opposing character dice. Global Pay Mask. Once per turn, during your turn, if you have no dice in your Prep Area, you may draw a die and place it in your Prep Area.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieKOd,
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing)),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Brotherhood of Mutants"]))),
            new TriggeredAbility(TriggerKind.Global,
                new Conditional(new TurnFact(TurnFactKind.PrepAreaEmpty), new DrawToZone(1, Zone.PrepArea, Zone.Bag)),
                EnergyCost: new EnergyCost(1, "Mask"), OncePerTurn: true),
        ],
        Continuous: []);

    // Part 3 #23's worked expression - confirms keyword-as-tag reactive
    // filtering. ExcludeSelf matches v1's own WhenAnotherDieFielded.
    public static readonly CardDef CyclopsFirstClass = new(
        Id: "DPS025", Name: "Cyclops", Subtitle: "First Class", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolId: "Bolt",
        Die: MigrationDice.Character("DPS025Die", "Bolt", (1, 4, 2), (1, 5, 3), (1, 6, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder While Cyclops is active, when you field a character die with Founder, deal 2 damage to target character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Founder"]), ExcludeSelf: true))],
        Continuous: []);

    // Part 3 #27's worked expression.
    public static readonly CardDef JubileeXMenFieldLeader = new(
        Id: "DPS143", Name: "Jubilee", Subtitle: "X-Men Field Leader", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolId: "Bolt",
        Die: MigrationDice.Character("DPS143Die", "Bolt", (0, 2, 1), (1, 3, 3), (2, 4, 3)),
        DieLimit: 1, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Jubilee is active, when you field a character die she deals 1 damage to your opponent and 1 damage to target character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Sequence([
                new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
                new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)),
            ]),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, ExcludeSelf: true))],
        Continuous: []);

    // Part 3 #26's worked expression - confirms CountAtLeast composes
    // with a Count-varying Then/Else.
    public static readonly CardDef CorsairCriminalRecord = new(
        Id: "DPS104", Name: "Corsair", Subtitle: "Criminal Record", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolId: "Fist",
        Die: MigrationDice.Character("DPS104Die", "Fist", (0, 3, 4), (1, 3, 5), (1, 4, 5)),
        DieLimit: 2, Affiliations: [], Keywords: [],
        RawText: "When fielded, KO target Villains character die, or KO 2 target Villains character dice if your opponent has 4 or more character dice in the Field Zone.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Conditional(
                new CountAtLeast(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.FieldZone]), 4),
                Then: new Ko(VillainsCharacterDice(2)),
                Else: new Ko(VillainsCharacterDice(1))))],
        Continuous: []);

    private static TargetFilter VillainsCharacterDice(int count) =>
        new(Kind: TargetKind.CharacterDie, Count: count, Tags: new TagQuery(AnyOf: ["Villains"]));

    // Part 2 #13's worked expression - validates PerMatch's
    // fixed-multiplier-times-live-count shape. Tailed through DPS batch 1
    // because the paper pass wrote its trigger as
    // "TurnStepEntered(EndOfTurn)" without noticing the EventFilter had
    // no step discriminator, so it would equally have fired on entering
    // its own Attack Step. Un-tailed by Spike C (V2_VOCABULARY.md Part
    // 13): `Step: StepIds.CleanUp` names the window precisely, and
    // TurnEngine.CleanUp now emits it.
    public static readonly CardDef ColossusPiotr = new(
        Id: "DPS103", Name: "Colossus", Subtitle: "Piotr", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolId: "Fist",
        Die: MigrationDice.Character("DPS103Die", "Fist", (1, 4, 4), (1, 6, 5), (2, 8, 7)),
        DieLimit: 2, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Colossus is active, at the end of your turn, each of your level 2 or 3 character dice deals your opponent 2 damage (not 2 damage per Colossus die)",
        Abilities: [new TriggeredAbility(TriggerKind.TurnStepEntered,
            new DealDamage(
                new PerMatch(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Stat: new StatThreshold(StatKind.Level, Min: 2)), Multiplier: 2),
                new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Step: StepIds.CleanUp))],
        Continuous: []);

    // Part 3 #29's worked expression - all three abilities, and the
    // PurchaseModifier confirmation. (Its own "minimum of 1" is
    // QueryEngine.GetPurchaseCost's floor, already corrected in Phase 3.)
    public static readonly CardDef DarkPhoenixEnemyOfTheShiar = new(
        Id: "DPS067", Name: "Dark Phoenix", Subtitle: "Enemy of the Shi'ar", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolId: "Bolt",
        Die: MigrationDice.Character("DPS067Die", "Bolt", (1, 5, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 3, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, KO target Shi'ar or X-Men character die. When Dark Phoenix attacks, deal 2 damage to your opponent. Global: Pay Bolt and KO one of your character dice. Your next die you purchase this turn costs 2 less (to a minimum of 1).",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieFielded,
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["Shi'ar", "X-Men"])))),
            new TriggeredAbility(TriggerKind.DieAttacks,
                new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing))),
            new TriggeredAbility(TriggerKind.Global,
                new Sequence([
                    new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own)),
                    new PurchaseModifier(Delta: -2),
                ]),
                EnergyCost: new EnergyCost(1, "Bolt")),
        ],
        Continuous: []);

    // v1 scopes this to its own CardType.Action; v2's CardType collapsed
    // Action into BasicAction (there is no separate Action type), so
    // CardKind: BasicAction is the faithful equivalent, not a loss.
    public static readonly CardDef MagikWielderOfTheSoulsword = new(
        Id: "DPS040", Name: "Magik", Subtitle: "Wielder of the Soulsword", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolId: "Mask",
        Die: MigrationDice.Character("DPS040Die", "Mask", (0, 1, 4), (0, 1, 6), (1, 2, 7)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, the next action die you purchase costs 1 less (to a minimum of 1)",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new PurchaseModifier(Delta: -1, CardKind: CardType.BasicAction))],
        Continuous: []);

    // Deadly isn't implemented in v2 (Phase 7 deliberately ported only
    // Overcrush and Fast) - and Deadly is this card's entire text, so
    // there is nothing else to express. See V2_TAIL_POLICY.md.
    public static readonly CardDef DeathbirdTreacherous = new(
        Id: "DPS029", Name: "Deathbird", Subtitle: "Treacherous", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolId: "Shield",
        Die: MigrationDice.Character("DPS029Die", "Shield", (0, 1, 1), (0, 1, 2), (1, 3, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Deadly"],
        RawText: "Deadly",
        Abilities: [], Continuous: [], IsImplemented: false);

    public static IReadOnlyList<CardDef> All =>
    [
        PowerBolt, Rally, RonanTheAccuserTreason, StormCloudCover, PsylockeTelepath,
        MasterMoldTargetingMutants, MasterMoldUntoldElectronicExpertise, MagnetoFounderOfTheBrotherhood,
        CyclopsFirstClass, JubileeXMenFieldLeader, CorsairCriminalRecord, ColossusPiotr,
        DarkPhoenixEnemyOfTheShiar, MagikWielderOfTheSoulsword, DeathbirdTreacherous,
    ];
}
