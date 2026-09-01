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

    // V2_VOCABULARY_HISTORY.md Part 2 #1's worked expression, verbatim.
    public static readonly CardDef PowerBolt = new(
        Id: "DPS011", Name: "Power Bolt", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS011Die"),
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
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS013Die", 0, 1, 2),
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
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
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
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
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
        PurchaseCost: 2, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS088Die", "Mask", (0, 1, 2), (0, 2, 2), (1, 3, 3)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, target character die gets Overcrush.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new GrantTag(new TargetFilter(Kind: TargetKind.CharacterDie), ["Overcrush"]))],
        Continuous: []);

    // Part 2 #8's worked expression.
    public static readonly CardDef MasterMoldTargetingMutants = new(
        Id: "DPS082", Name: "Master Mold", Subtitle: "Targeting Mutants", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS082Die", "Shield", (1, 5, 5), (2, 6, 6), (3, 8, 8)),
        DieLimit: 3, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, KO target Brotherhood of Mutants character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Affiliations: new TagQuery(AnyOf: ["Brotherhood of Mutants"]))))],
        Continuous: []);

    public static readonly CardDef MasterMoldUntoldElectronicExpertise = new(
        Id: "DPS122", Name: "Master Mold", Subtitle: "Untold Electronic Expertise", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS122Die", "Shield", (1, 5, 5), (2, 6, 6), (3, 8, 8)),
        DieLimit: 2, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, KO target X-Men character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Affiliations: new TagQuery(AnyOf: ["X-Men"]))))],
        Continuous: []);

    // Part 2 #7's worked expression, including its own finding: "while
    // active" needs no modeling at all - a triggered ability only listens
    // while its source die is fielded, which is already how EventBus
    // scans listeners.
    public static readonly CardDef MagnetoFounderOfTheBrotherhood = new(
        Id: "DPS146", Name: "Magneto", Subtitle: "Founder of the Brotherhood", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS146Die", "Mask", (1, 4, 4), (2, 5, 7), (3, 6, 8)),
        DieLimit: 1, Affiliations: ["Brotherhood of Mutants"], Keywords: [],
        RawText: "While Magneto is active, when one of your Brotherhood of Mutants character dice is KO'd, KO target opposing character dice. Global Pay Mask. Once per turn, during your turn, if you have no dice in your Prep Area, you may draw a die and place it in your Prep Area.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieKOd,
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing)),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, Affiliations: new TagQuery(AnyOf: ["Brotherhood of Mutants"]))),
            new TriggeredAbility(TriggerKind.Global,
                new Conditional(new TurnFact(TurnFactKind.PrepAreaEmpty), new DrawToZone(1, Zone.PrepArea, Zone.Bag)),
                EnergyCost: new EnergyCost(1, "Mask"), OncePerTurn: true),
        ],
        Continuous: []);

    // Part 3 #23's worked expression - confirms keyword-as-tag reactive
    // filtering. ExcludeSelf matches v1's own WhenAnotherDieFielded.
    public static readonly CardDef CyclopsFirstClass = new(
        Id: "DPS025", Name: "Cyclops", Subtitle: "First Class", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
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
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
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
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
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
        new(Kind: TargetKind.CharacterDie, Count: count, Affiliations: new TagQuery(AnyOf: ["Villains"]));

    // Part 2 #13's worked expression - validates PerMatch's
    // fixed-multiplier-times-live-count shape. Tailed through DPS batch 1
    // because the paper pass wrote its trigger as
    // "TurnStepEntered(EndOfTurn)" without noticing the EventFilter had
    // no step discriminator, so it would equally have fired on entering
    // its own Attack Step. Un-tailed by Spike C (V2_VOCABULARY_HISTORY.md Part
    // 13): `Step: StepIds.CleanUp` names the window precisely, and
    // TurnEngine.CleanUp now emits it.
    public static readonly CardDef ColossusPiotr = new(
        Id: "DPS103", Name: "Colossus", Subtitle: "Piotr", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Fist"],
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
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS067Die", "Bolt", (1, 5, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 3, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, KO target Shi'ar or X-Men character die. When Dark Phoenix attacks, deal 2 damage to your opponent. Global: Pay Bolt and KO one of your character dice. Your next die you purchase this turn costs 2 less (to a minimum of 1).",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieFielded,
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Affiliations: new TagQuery(AnyOf: ["Shi'ar", "X-Men"])))),
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
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
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
        PurchaseCost: 2, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS029Die", "Shield", (0, 1, 1), (0, 1, 2), (1, 3, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Deadly"],
        RawText: "Deadly",
        Abilities: [], Continuous: [], IsImplemented: false);

    // Spike B's demonstration card, and its motivating case. "Swap A" is
    // two ModifyStat sets whose amounts are StatOf(...) - and it is only
    // correct because StatOf captures AT BIND TIME: step 1 binds "other"
    // (snapshotting its attack) and immediately overwrites that attack
    // with self's; step 2 then reads "other"'s CAPTURED, pre-overwrite
    // value. A use-time read would swap in the value it had just written
    // and leave both dice on Rogue's attack.
    //
    // Ground rule 8: v1 collapsed this card's "you may" to always-swap
    // (its own comment says so, and V2_PLAN.md names it as one of the two
    // cards v1 got wrong). Here it is a real MayPay choice.
    public static readonly CardDef RogueMrsX = new(
        Id: "DPS049", Name: "Rogue", Subtitle: "Mrs. X", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS049Die", "Mask", (1, 2, 3), (2, 4, 5), (2, 5, 6)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, you may swap Rogue's A with target opposing character die's A.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new MayPay(Cost: null, Then: new Sequence([
                new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, BindAs: "other"),
                    SetAttack: new StatOf("self", StatKind.Attack)),
                new ModifyStat(new TargetFilter(Self: true),
                    SetAttack: new StatOf("other", StatKind.Attack)),
            ])))],
        Continuous: []);

    // Both halves were blocked until 2026-08-24. The Global is now
    // usable because Globals became card-scoped (rule 2.6.5.2) - it sits
    // on a Basic Action card, so no die of it is ever fielded and the
    // old die-scoped UseGlobal could never reach it - and it is
    // expressible because Spike B gave SetDefense a live Amount.
    //
    // The WhenUsed half stays off: "target character die you control and
    // target opposing character die deal damage to each other equal to
    // their A" needs BOTH dice bound before EITHER takes damage, and a
    // TargetFilter only binds as a side effect of the node that uses it.
    // See V2_TAIL_POLICY.md - a `Bind(TargetFilter)` template would
    // close it. IsImplemented stays false until then, matching v1's own
    // "independent ability slots, card still not fully implemented"
    // convention.
    public static readonly CardDef Archnemesis = new(
        Id: "DPS001", Name: "Archnemesis", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS001Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Target character die you control and target opposing character die deal damage to each other equal to their A. Global: Pay Shield. Target character die has D equal to it's A (until end of turn).",
        Abilities: [new TriggeredAbility(TriggerKind.Global,
            new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, BindAs: "t"),
                SetDefense: new StatOf("t", StatKind.Attack)),
            EnergyCost: new EnergyCost(1, "Shield"))],
        Continuous: [], IsImplemented: false);


    // ---- Batch 2 (2026-08-24) ----
    // Chosen to exercise vocabulary no migrated card had touched yet:
    // Spin (both modes), Reroll's Finding-8 params, GrantCounter,
    // CostModifier, TargetingProtection, CombatRule. Known face-data
    // gap: several of these print burst symbols on character faces
    // (Gambit, Colossus) that MigrationDice.Character cannot carry -
    // no implemented card's text reads them yet, so it is recorded here
    // rather than expanded speculatively.

    // Finding 12's own worked answer, now run for real: the swap is two
    // MoveDies, and "spin that character die to level 1" is Spin's
    // absolute SetLevel against the die the second MoveDie bound. Also
    // the first card to depend on the rule-3.2.5 per-ability snapshot in
    // anger - step 1 puts a die INTO the Used Pile, and step 2's Used
    // Pile candidates must not include it.
    public static readonly CardDef Mutation = new(
        Id: "DPS009", Name: "Mutation", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS009Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Swap target character die in the Field Zone with target non-sidekick character dice in that player's Used Pile. Spin that character die to level 1. (This does not trigger \"when fielded\" effects.) Global: Pay Mask. Spin one of your character die down a level to spin another target character die up a level.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
                new MoveDie(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.FieldZone]), Zone.UsedPile),
                new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile],
                    Tags: new TagQuery(NoneOf: ["sidekick"]), BindAs: "incoming"), Zone.FieldZone),
                new Spin(new TargetFilter(Bound: "incoming"), SetLevel: 1),
            ])),
            // Two independently-chosen targets, matching v1's own shape.
            // Nothing stops the player naming the same die twice (a net
            // no-op) - TargetFilter has no "not the die bound earlier"
            // exclusion, and no authored card has needed one.
            new TriggeredAbility(TriggerKind.Global, new Sequence([
                new Spin(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), LevelDelta: -1),
                new Spin(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), LevelDelta: 1),
            ]), EnergyCost: new EnergyCost(1, "Mask")),
        ],
        Continuous: []);

    // Finding 8's Reroll params, first real user: each rerolled die that
    // lands on a non-character face moves on. The destination is its own
    // controller's Used Pile automatically - MoveToZone only sets the
    // zone, and these dice are the opponent's.
    public static readonly CardDef GambitUnlessIGotSomeoneToPlayWith = new(
        Id: "DPS112", Name: "Gambit", Subtitle: "Unless I Got Someone to Play With", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS112Die", "Mask", (1, 1, 1), (1, 2, 2), (2, 4, 6)),
        DieLimit: 2, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, reroll up to 2 target opposing character dice. Each die that doesn't roll a character goes to your opponent's Used Pile.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 2, Optional: true),
                NonCharacterMoveTo: Zone.UsedPile))],
        Continuous: []);

    // The same shape with Finding 8's other param, DamagePerMoved.
    public static readonly CardDef PsylockeAdvancedTelekineticCombatant = new(
        Id: "DPS150", Name: "Psylocke", Subtitle: "Advanced Telekinetic Combatant", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS150Die", "Mask", (0, 1, 2), (0, 2, 2), (1, 3, 3)),
        DieLimit: 1, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, reroll up to 2 opposing character dice. Each die that does not roll a character goes to your opponent's Used Pile. Psylocke deals 2 damage to your opponent for each die moved.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 2, Optional: true),
                NonCharacterMoveTo: Zone.UsedPile, DamagePerMoved: 2))],
        Continuous: []);

    // Finding 13's motivating card (Loyalty Counters), and the second
    // consumer of Spike C's step discriminator. The card's OWN ability is
    // exactly "put a counter on this card", which is fully implemented;
    // Loyalty's "+1A and +1D per counter" is reminder text for a
    // game-wide KEYWORD rule, engine behavior not yet built - same
    // category as Deadly or Regenerate. See V2_TAIL_POLICY.md.
    public static readonly CardDef JeanGreyPeacefulCoexistence = new(
        Id: "DPS035", Name: "Jean Grey", Subtitle: "Peaceful Coexistence", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS035Die", "Bolt", (1, 3, 3), (2, 5, 5), (3, 6, 6)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder", "Loyalty"],
        RawText: "Founder. While Jean Grey is active, at the end of each of your turns, if no character dice were KO'd that turn, put a Loyalty Counter on Jean Grey's card (Loyalty Counters give a character die +1A and +1D.)",
        Abilities: [new TriggeredAbility(TriggerKind.TurnStepEntered,
            new Conditional(new NoKOsThisTurn(KoScope.Any), new GrantCounter(new TargetFilter(Self: true), "Loyalty", 1)),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Step: StepIds.CleanUp))],
        Continuous: []);

    // Finding 3's FieldingCost stat kind, first real user. The threshold
    // reads the BASE fielding cost, so the -2 cannot re-qualify the die
    // it just discounted.
    public static readonly CardDef DeadpoolCollectThis = new(
        Id: "DPS108", Name: "Deadpool", Subtitle: "Collect THIS!", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS108Die", "Fist", (0, 2, 4), (0, 2, 5), (1, 3, 7)),
        DieLimit: 2, Affiliations: ["X-Men", "Deadpool Affiliation"], Keywords: [],
        RawText: "While Deadpool is active, your character dice with fielding cost of 2 are free to field.",
        Abilities: [],
        Continuous: [new CostModifier(CostKind.Fielding,
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Stat: new StatThreshold(StatKind.FieldingCost, Max: 2)),
            Delta: -2)]);

    // TargetingProtection's first real user - and the card
    // V2_VOCABULARY_HISTORY.md Part 2 cites for the "protection is always against
    // the granting player's OPPONENT" reading.
    public static readonly CardDef AngelXaviersDream = new(
        Id: "DPS137", Name: "Angel", Subtitle: "Xavier's Dream", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS137Die", "Shield", (0, 2, 2), (1, 3, 3), (1, 3, 4)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Angel is active, your opponent can't target your Sidekick dice with Global Abilities.",
        Abilities: [],
        Continuous: [new TargetingProtection(
            new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])),
            ProtectionFrom.Global)]);

    // CombatRule.MinBlockers' first real user. Teamwatch is not one of
    // the 10 frozen trigger kinds, so that clause is dropped and the
    // card stays IsImplemented: false - v1 made the same call on the
    // same card for the same reason. Its Global inverts PrepAreaEmpty
    // through an empty Then branch, exactly as v1 does.
    public static readonly CardDef MagnetoVisionary = new(
        Id: "DPS081", Name: "Magneto", Subtitle: "Visionary", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS081Die", "Mask", (1, 4, 4), (2, 5, 7), (3, 6, 8)),
        DieLimit: 3, Affiliations: ["Brotherhood of Mutants"], Keywords: ["Teamwatch"],
        RawText: "While Magneto is active, your Brotherhood of Mutants character dice can only be blocked by 2 or more character dice. Teamwatch - Prep a die from your bag. Global Pay Mask. Once per turn, during your turn, if you have any dice in your Prep Area, you may draw a die and place it in your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.Global,
            new Conditional(new TurnFact(TurnFactKind.PrepAreaEmpty),
                Then: new Sequence([]),
                Else: new DrawToZone(1, Zone.PrepArea, Zone.Bag)),
            EnergyCost: new EnergyCost(1, "Mask"), OncePerTurn: true)],
        Continuous: [new CombatRule(CombatRuleKind.MinBlockers,
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Affiliations: new TagQuery(AnyOf: ["Brotherhood of Mutants"])),
            N: 2)],
        IsImplemented: false);

    // CombatRule.BlocksN's first real user - "your Blob dice" is a card-
    // NAME tag, which the tag-unification design gives for free. The
    // second clause ("when Blob KO's an opponent's Sidekick, return it to
    // their bag") needs KO-source attribution that DieKOd's payload does
    // not carry, so the card stays IsImplemented: false.
    public static readonly CardDef BlobImmovable = new(
        Id: "DPS101", Name: "Blob", Subtitle: "Immovable", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS101Die", "Shield", (0, 1, 5), (1, 1, 6), (2, 1, 8)),
        DieLimit: 2, Affiliations: ["Brotherhood of Mutants"], Keywords: [],
        RawText: "Each of your Blob dice may block 3 character dice instead of 1. When Blob KO's an opponent's Sidekick die, return it to your opponent's bag.",
        Abilities: [],
        Continuous: [new CombatRule(CombatRuleKind.BlocksN,
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Blob"])),
            N: 3)],
        IsImplemented: false);

    // Worth contrasting with Phoenix "Psionic Maelstrom" below, which IS
    // tailed: this card looks like it needs a stat CONDITION and does
    // not. "Opposing character dice with less than 4A can't block" is a
    // stat threshold on SELECTION - which TargetFilter.Stat has always
    // handled - with Count: 0 meaning "every match, no choice"
    // (replacing v1's separate MatchAll bool). No condition involved.
    public static readonly CardDef PhoenixEternalFlame = new(
        Id: "DPS126", Name: "Phoenix", Subtitle: "Eternal Flame", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS126Die", "Bolt", (1, 5, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 2, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When Phoenix attacks, opposing character dice with less than 4A can't block.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new CombatFlag(
                new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing,
                    Stat: new StatThreshold(StatKind.Attack, Max: 3), Count: 0),
                CombatFlagKind.CantBlock))],
        Continuous: []);

    // --- Batch 2's tails (see V2_TAIL_POLICY.md for each) ---

    // Finding 8's OnFaceKind condition, first real user - and the card
    // that corrected FieldDie's default (user ruling, 2026-08-24): a die
    // is fielded AT THE LEVEL IT ROLLED, so FieldDie names no level here
    // and the rolled face stands.
    //
    // DELIBERATE DIVERGENCE from the literal rules text, user-ruled after
    // the rules validation pass (V2_VOCABULARY_HISTORY.md Part 15, Finding 3).
    // The Comprehensive Rules' "Field" glossary says ability-fielded dice
    // are "considered fielded for free on level 1, unless otherwise
    // stated", which read strictly would field this at level 1 regardless
    // of the roll. That is not how the card was understood or played. The
    // card's RawText is kept VERBATIM rather than reworded, so a future
    // cross-check against the Google Sheet still matches - the divergence
    // lives here in the comment and in the expression, not in the data.
    //
    // One stated approximation: "a character die from your Used Pile"
    // means a die of a CHARACTER-type card, but a dormant die has no
    // face to read and TargetFilter's Kind cannot say "character card"
    // (CharacterDie reads the current face; ActionDie reads CardType,
    // with no negation). `NoneOf: ["sidekick"]` excludes pool dice but
    // still admits a Basic Action die, which would be offered as a
    // choice and then always fail the character-face check and be
    // Prepped. Flagged in V2_TAIL_POLICY.md as Approximate rather than
    // left silent.
    public static readonly CardDef MakingTheTeam = new(
        Id: "DPS007", Name: "Making the Team", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS007Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Roll a character die from your Used Pile. If it rolls a character face, field it for free. Otherwise, Prep it.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
            new Reroll(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile],
                Tags: new TagQuery(NoneOf: ["sidekick"]), BindAs: "rolled")),
            new Conditional(new OnFaceKind(FaceKind.CharacterFace, "rolled"),
                Then: new FieldDie(new TargetFilter(Bound: "rolled"), Free: true),
                Else: new MoveDie(new TargetFilter(Bound: "rolled"), Zone.PrepArea)),
        ]))],
        Continuous: []);

    // Part 3 #24 expected bindings to close this, and they close half of
    // it: BindAs lets the second clause reference the SAME die the first
    // damaged. What is missing is the condition itself - "if that
    // character die is a Villains character die" is a TAG check on a
    // bound die, and the 7 frozen conditions have no tag test. Nor can
    // CountAtLeast stand in: a TargetFilter with Bound set returns that
    // die without applying Tags at all (TargetResolver short-circuits),
    // so the count is always 1.
    public static readonly CardDef PhoenixPsionicMaelstrom = new(
        Id: "DPS086", Name: "Phoenix", Subtitle: "Psionic Maelstrom", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS086Die", "Bolt", (1, 5, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When Phoenix attacks, deal 3 damage to target character die. If that character die is a Villains character die, you may deal 3 damage to another target character die.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // Confirms V2_VOCABULARY_HISTORY.md Part 2 #14's finding on a second card:
    // DamageModifier is a CONTINUOUS template, and this is a one-shot,
    // once-per-turn, optional redirect with a burst-face alternative
    // (prevent instead). None of that is expressible.
    public static readonly CardDef ColossusOrganicSteel = new(
        Id: "DPS063", Name: "Colossus", Subtitle: "Organic Steel", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS063Die", "Fist", (1, 4, 4), (1, 6, 5), (2, 8, 7)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Colossus is active, the first time one of your character dice would take damage each turn you may have Colossus take that damage instead. *Instead, prevent that damage.",
        Abilities: [], Continuous: [], IsImplemented: false);

    public static IReadOnlyList<CardDef> All =>
    [
        PowerBolt, Rally, RonanTheAccuserTreason, StormCloudCover, PsylockeTelepath,
        MasterMoldTargetingMutants, MasterMoldUntoldElectronicExpertise, MagnetoFounderOfTheBrotherhood,
        CyclopsFirstClass, JubileeXMenFieldLeader, CorsairCriminalRecord, ColossusPiotr,
        DarkPhoenixEnemyOfTheShiar, MagikWielderOfTheSoulsword, DeathbirdTreacherous, RogueMrsX, Archnemesis,
        // Batch 2
        Mutation, GambitUnlessIGotSomeoneToPlayWith, PsylockeAdvancedTelekineticCombatant,
        JeanGreyPeacefulCoexistence, DeadpoolCollectThis, AngelXaviersDream,
        MagnetoVisionary, BlobImmovable, MakingTheTeam, PhoenixPsionicMaelstrom, ColossusOrganicSteel,
        PhoenixEternalFlame,
        // Batch 3 - led by the two cards Spike A was built for.
        DKenShiarCivilWar, MisterSinisterMutantSupremacist,
        StormExtremeWeather, MasterMoldInexplicableDurability, SabretoothAmIInterrupting,
        DeadpoolMoreThanAChumpBlocker, RogueSurveillanceImmunity, RonanTheAccuserNoMercy,
        BeastCombatReady, MagikSorceressOfLimbo,
        // Batch 4 - the Energize unlock (all 15 DPS cards printing it).
        PhoenixFirepower, StormQueen, ProfessorXUncannyLeadership, CyclopsDefendingThePhoenix,
        RogueStrengthAbsorption, PsylockeHeiress, JubileeRebelliousNature, MystiqueTaughtByMagneto,
        WolverineHardenedByMadripoor, IcemanMrIceGuy, ProfessorXDreamer, AngelWingsOverTheWorld,
        CableIllDoThisAllDay, ColossusSkilledPainter, ToadLookingForComradery,
        // Batch 5.
        MutantResearchProgram, TightRanks, Radicalization, CorsairRecruitingACrew,
        EmmaFrostManipulative, CableBosomBuddies, BeastFirstClass, EmmaFrostInfluential,
        BishopTorturedTimeline, BishopImBack, ForgeSupportTechnician, GreetingsFromKrakoa,
        // Batch 6.
        TakeCover, EmmaFrostFinesse, CyclopsUtopiaRealized, CyclopsXaviersDream,
        MadelynePryorSisterhood, MagnetoIdealist, JeanGreyXaviersDream, JeanGreyMarvelGirl,
        AngelJeanGreysSchool, CableHighStakes, Explosion, ForgeReverseEngineer,
        MadelynePryorAspiring, IcemanXaviersDream,
        // Batch 7.
        KittyPrydeRightOfPassage, DKenEmperor, IcemanIcyInterference, ToadSecondaryMutation,
        VulcanRulerOfTheImperium, PsylockeAdventurer, GambitAceInTheHole, MisterSinisterGeneticist,
        GladiatorPsiResistance, MystiqueRelentless, DarkPhoenixMalevolent, DeadpoolDraftPick,
        WolverinePureOfHeart, ForgeMoreThanFirepower, SupremeIntelligence, LilandraPolitician,
        MoiraItsNotADream,
    ];

    // --- Batch 3 (2026-09-01) ---
    //
    // Led by the two cards Spike A was built for. D'Ken and Mister
    // Sinister were both named in V2_VOCABULARY_HISTORY.md Part 12 as motivating
    // the spike, and Part 19 listed Sinister's side-wide half under "what
    // it does NOT close" - BlankCardText's AllOpposing mode closes it.

    // The AbilityBlank template's first real card, and the shape the
    // whole continuous half was designed around: a conditional blank,
    // recomputed on read, that switches off when D'Ken leaves.
    //
    // "Free to field" is the second clause and needs no new template - a
    // fielding-cost CostModifier large enough to floor at zero, which
    // GetFieldingCost already does (Math.Max(0, ...)). -99 rather than a
    // set-to-zero mode because no set mode exists and one card does not
    // justify inventing one.
    public static readonly CardDef DKenShiarCivilWar = new(
        Id: "DPS141", Name: "D'Ken", Subtitle: "Shi'ar Civil War", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS141Die", "Shield", (0, 4, 4), (1, 5, 5), (2, 6, 6)),
        DieLimit: 1, Affiliations: ["Villains", "Shi'ar"], Keywords: [],
        RawText: "While D'Ken is active, opposing character dice with Purchase Cost of 3 or less lose their " +
                 "abilities and are free to field.",
        Abilities: [],
        Continuous:
        [
            new AbilityBlank(new TargetFilter(
                Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 99,
                Stat: new StatThreshold(StatKind.PurchaseCost, Max: 3))),
            new CostModifier(CostKind.Fielding, new TargetFilter(
                Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 99,
                Stat: new StatThreshold(StatKind.PurchaseCost, Max: 3)), Delta: -99),
        ]);

    // Both halves, and the second one is why card scope had to exist:
    // "ignore all text on opposing character CARDS (including Global
    // Abilities)" covers copies not yet in play and Globals that need no
    // die, neither of which a die-scoped blank can reach.
    //
    // The Global itself is die-scoped - "target attacking character die's
    // text" - so this one card uses both scopes, which is the clearest
    // evidence in the catalog that they are genuinely different things.
    public static readonly CardDef MisterSinisterMutantSupremacist = new(
        Id: "DPS083", Name: "Mister Sinister", Subtitle: "Mutant Supremacist", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS083Die", "Bolt", (1, 4, 1), (2, 5, 2), (2, 6, 3)),
        DieLimit: 4, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, ignore all text on opposing character cards (including Global Abilities) (until " +
                 "end of turn). Global: Pay 3. Ignore target attacking character die's text until end of turn.",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded, new BlankCardText(AllOpposing: true)),
            new TriggeredAbility(TriggerKind.Global,
                new BlankText(new TargetFilter(
                    Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.AttackZone])),
                EnergyCost: new EnergyCost(3)),
        ],
        Continuous: []);

    // --- The rest of batch 3: cards whose triggers and effects v2
    // already had, migrated to keep the catalog moving. ---

    public static readonly CardDef StormExtremeWeather = new(
        Id: "DPS052", Name: "Storm", Subtitle: "Extreme Weather", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS052Die", "Bolt", (0, 2, 1), (0, 3, 1), (1, 3, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, deal 1 damage to target character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // "all X-Men AND Brotherhood of Mutants dice" is a UNION of two
    // affiliations, not an intersection - no die is both. v1 spelled it
    // matchAll: true against a requiredAffiliations list, which reads as
    // an intersection and is wrong for the same reason; the printed text
    // means "every die of either team".
    public static readonly CardDef MasterMoldInexplicableDurability = new(
        Id: "DPS042", Name: "Master Mold", Subtitle: "Inexplicable Durability", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS042Die", "Shield", (1, 5, 5), (2, 6, 6), (3, 8, 8)),
        DieLimit: 4, Affiliations: ["Villains"], Keywords: [],
        RawText: "When fielded, deal 2 damage to all X-Men and Brotherhood of Mutants character dice.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(2), new TargetFilter(
                Kind: TargetKind.CharacterDie, Count: 99,
                Affiliations: new TagQuery(AnyOf: ["X-Men", "Brotherhood of Mutants"]))))],
        Continuous: []);

    // "Target Wolverine character die" - the card NAME is a tag, so this
    // needs no name-matching machinery of its own. v1 used a substring
    // match; a tag is the exact printed name, which is the same set here
    // because every Wolverine printing is named "Wolverine".
    //
    // The "or any character die with a 'While Wolverine is active'
    // ability" half is NOT migrated: it addresses another card's ability
    // TEXT, which no filter in the frozen vocabulary can see. Tailed.
    public static readonly CardDef SabretoothAmIInterrupting = new(
        Id: "DPS051", Name: "Sabretooth", Subtitle: "Am I Interrupting?", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS051Die", "Fist", (1, 3, 3), (1, 4, 4), (2, 5, 4)),
        DieLimit: 4, Affiliations: ["Brotherhood of Mutants"], Keywords: [],
        RawText: "When fielded, KO target Wolverine characer die, or any character die with a \"While Wolverine is " +
                 "active\" ability.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["Wolverine"]))))],
        Continuous: []);

    public static readonly CardDef DeadpoolMoreThanAChumpBlocker = new(
        Id: "DPS068", Name: "Deadpool", Subtitle: "More than a Chump Blocker", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS068Die", "Fist", (0, 2, 4), (0, 2, 5), (1, 3, 7)),
        DieLimit: 4, Affiliations: ["Deadpool Affiliation"], Keywords: [],
        RawText: "When Deadpool attacks, he deals your opponent 1 damage.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    // Second clause ("fielding Rogue doesn't trigger opposing effects")
    // is not migrated - v1 did not model it either, and suppressing
    // another card's trigger is the ability-class shape Part 25 tailed.
    public static readonly CardDef RogueSurveillanceImmunity = new(
        Id: "DPS089", Name: "Rogue", Subtitle: "Surveillance Immunity", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS089Die", "Mask", (1, 2, 3), (2, 4, 5), (2, 5, 6)),
        DieLimit: 5, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, send target action die from the Field Zone to your opponent's Used Pile. Fielding " +
                 "Rogue doesn't trigger opposing effects.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new MoveDie(new TargetFilter(
                Kind: TargetKind.ActionDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.FieldZone]),
                Zone.UsedPile))],
        Continuous: []);

    // "EACH player KOs a character die they control" - two KOs, and the
    // second is answered by the opponent. AnsweredBy is exactly this
    // (Part 1's cross-player-answered targets), so v1's dedicated
    // OpponentKOsOwnCharacterDie effect needs no counterpart.
    public static readonly CardDef RonanTheAccuserNoMercy = new(
        Id: "DPS090", Name: "Ronan the Accuser", Subtitle: "No Mercy", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS090Die", "Bolt", (1, 5, 5), (1, 6, 7), (2, 8, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, each player KOs a character die they control.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Sequence(
        [
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own)),
            new Ko(new TargetFilter(
                Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing,
                AnsweredBy: TargetOwnership.Opposing)),
        ]))],
        Continuous: []);

    // The surcharge clause ("the first Beast die you purchase each game
    // costs 1 extra") is NOT migrated: it is a once-per-GAME cost
    // modifier, and no Duration or condition in the frozen vocabulary
    // expresses "the first one this game". Tailed; the attack trigger is
    // migrated.
    public static readonly CardDef BeastCombatReady = new(
        Id: "DPS098", Name: "Beast", Subtitle: "Combat Ready", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS098Die", "Fist", (0, 2, 1), (1, 2, 2), (1, 3, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder When Beast attacks, Prep a die from your bag. The first Beast die you purchase each " +
                 "game costs 1 extra.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    public static readonly CardDef MagikSorceressOfLimbo = new(
        Id: "DPS120", Name: "Magik", Subtitle: "Sorceress of Limbo", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS120Die", "Mask", (0, 1, 4), (0, 1, 6), (1, 2, 7)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, target character die gains Overcrush and +2A.",
        // One target, bound once and reused - not two independent "target
        // character die" resolutions, which would let the keyword and the
        // bonus land on different dice.
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Sequence(
        [
            new GrantTag(new TargetFilter(Kind: TargetKind.CharacterDie, BindAs: "boosted"), ["Overcrush"]),
            new ModifyStat(new TargetFilter(Bound: "boosted"), AtkDelta: 2),
        ]))],
        Continuous: []);

    // --- Batch 4 (2026-09-01) - the Energize unlock. ---
    //
    // V2_TAIL_POLICY.md's Energize entry, corrected the same day: the rule
    // text is "whenever you roll this die on one of its double energy
    // faces... during the Roll and Reroll Step, only check at the end of
    // the Step" and "does not need to be active to trigger" - NOT a
    // DieFaceChanged filter (a die already showing double energy that
    // nobody rerolls has no face change to filter on). TurnStepEntered
    // (Main) is that step boundary (TurnEngine.FinishRoll now fires it,
    // and EventBus.Fire's own carve-out lets a Reserve Pool die - where a
    // just-rolled die actually sits - hear it); the CountAtLeast(Self,
    // SymbolCount>=2) condition is what actually gates on the die's own
    // current face, since the trigger itself fires for every candidate
    // die regardless of face. StatKind.SymbolCount is the one real
    // vocabulary addition this needed (user-signed-off).
    //
    // Second ability added 2026-09-01 (Part 30, user-signed-off): the
    // deferred TurnStepEntered(Main) check only covers the Roll and
    // Reroll Step's OWN roll - the rule's deferral is scoped to that one
    // step, not to every face change. A reroll thrown by an opposing
    // ability (Storm "Queen") or a die drawn-and-rolled by a Basic Action
    // (Mutant Research Program) happens OUTSIDE that step and must check
    // IMMEDIATELY per the rule's own default. DieFaceChanged + RequireSelf
    // (this reaction is about MY OWN face change, not any die's) +
    // ExcludeCause: Roll (the one cause that's always that step's own
    // roll, already covered by the first ability) is that immediate
    // check. Sharing one Conditional (`checkAndRun`) keeps both abilities
    // gating on the exact same face-state question.
    //
    // internal, not private - BonusCards.cs (Domino "Not Really A Party
    // Girl", an out-of-scope one-off) reuses this same canonical shape
    // rather than re-deriving it.
    internal static IReadOnlyList<TriggeredAbility> Energize(EffectNode effect)
    {
        var checkAndRun = new Conditional(
            new CountAtLeast(new TargetFilter(Self: true, Stat: new StatThreshold(StatKind.SymbolCount, Min: 2)), 1),
            Then: effect);
        return
        [
            new TriggeredAbility(TriggerKind.TurnStepEntered, checkAndRun, Filter: new EventFilter(Step: StepIds.Main)),
            new TriggeredAbility(TriggerKind.DieFaceChanged, checkAndRun, Filter: new EventFilter(RequireSelf: true, ExcludeCause: FaceChangeCause.Roll)),
        ];
    }

    public static readonly CardDef PhoenixFirepower = new(
        Id: "DPS046", Name: "Phoenix", Subtitle: "Firepower", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS046Die", "Bolt", (1, 5, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "When fielded, deal 3 damage to target character die. Energize - Deal 2 damage to target " +
                 "character die or player.",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded,
                new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie))),
            // "Character die or player" - PowerBolt's own identical phrase
            // already established DieOrPlayer for this text.
            ..Energize(new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.DieOrPlayer))),
        ],
        Continuous: []);

    public static readonly CardDef StormQueen = new(
        Id: "DPS132", Name: "Storm", Subtitle: "Queen", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 7, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS132Die", "Bolt", (0, 2, 1), (0, 3, 1), (1, 3, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "When Fielded, reroll target character die. When Storm attacks, reroll up to 2 opposing " +
                 "character dice. Move each die that does roll a character goes to you opponent's Used Pile. " +
                 "Storm deals 2 damage to your opponent for each die moved. Energize - Reroll target opposing " +
                 "character die",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded, new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie))),
            // Reroll's own NonCharacterMoveTo/DamagePerMoved params (Finding
            // 8) fold v1's RerollAndMoveUnlessCharacter primitive in whole.
            new TriggeredAbility(TriggerKind.DieAttacks,
                new Reroll(
                    new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 2, Optional: true),
                    NonCharacterMoveTo: Zone.UsedPile, DamagePerMoved: 2)),
            ..Energize(new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing))),
        ],
        Continuous: []);

    public static readonly CardDef ProfessorXUncannyLeadership = new(
        Id: "DPS127", Name: "Professor X", Subtitle: "Uncanny Leadership", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS127Die", "Mask", (1, 1, 5), (2, 1, 7), (3, 1, 9)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "When fielded, spin an opposing die to it's single energy face. Energize - Move an X-Men die " +
                 "from your Used Pile to your Prep Area.",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded,
                new SpinToEnergy(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing))),
            // AnyDie, not CharacterDie - a Used Pile die is always
            // unrolled (rule 1.6.8), same gap this card's own text (and
            // ProfessorXDreamer's identical Energize clause below) shares.
            ..Energize(new MoveDie(
                new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Affiliations: new TagQuery(AnyOf: ["X-Men"])),
                Zone.PrepArea)),
        ],
        Continuous: []);

    public static readonly CardDef CyclopsDefendingThePhoenix = new(
        Id: "DPS065", Name: "Cyclops", Subtitle: "Defending the Phoenix", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS065Die", "Bolt", (1, 4, 2), (1, 5, 3), (1, 6, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - Deal 1 damage to target character die and reroll this die.",
        Abilities: [..Energize(new Sequence(
        [
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)),
            new Reroll(new TargetFilter(Self: true)),
        ]))],
        Continuous: []);

    public static readonly CardDef RogueStrengthAbsorption = new(
        Id: "DPS151", Name: "Rogue", Subtitle: "Strength Absorption", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS151Die", "Mask", (1, 2, 3), (2, 4, 5), (2, 5, 6)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - Target character die has 0A this turn.",
        // SetAttack (Finding 5's absolute-snapshot mode), not a delta -
        // "has 0A" is a snapshot to an exact value.
        Abilities: [..Energize(new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie), SetAttack: new Fixed(0)))],
        Continuous: []);

    public static readonly CardDef PsylockeHeiress = new(
        Id: "DPS128", Name: "Psylocke", Subtitle: "Heiress", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS128Die", "Mask", (0, 1, 2), (0, 2, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Psylocke gets +2A for each of your X-Men dice in the Prep Area. Energize - Spin target " +
                 "character die up 1 level.",
        Abilities: [..Energize(new Spin(new TargetFilter(Kind: TargetKind.CharacterDie), LevelDelta: 1))],
        Continuous:
        [
            // Target: Self - "Psylocke gets" is self-only, unlike a normal
            // team-wide StatAura; the "self" binding is seeded per source
            // die the same way for a continuous template as for a trigger.
            new StatAura(new TargetFilter(Self: true),
                AtkDelta: new PerMatch(
                    new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.PrepArea], Affiliations: new TagQuery(AnyOf: ["X-Men"])),
                    Multiplier: 2)),
        ]);

    public static readonly CardDef JubileeRebelliousNature = new(
        Id: "DPS036", Name: "Jubilee", Subtitle: "Rebellious Nature", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS036Die", "Bolt", (0, 2, 1), (1, 3, 3), (2, 4, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - If you have less life than your opponent, you may immediately field this die for " +
                 "free at level 2.",
        // Ground rule 8 - "you may" gets MayPay even with Cost: null.
        Abilities: [..Energize(new Conditional(
            new LifeComparison(),
            Then: new MayPay(Cost: null, Then: new FieldDie(new TargetFilter(Self: true), Free: true, Level: 2))))],
        Continuous: []);

    public static readonly CardDef MystiqueTaughtByMagneto = new(
        Id: "DPS125", Name: "Mystique", Subtitle: "Taught by Magneto", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS125Die", "Mask", (1, 1, 1), (0, 1, 1), (2, 1, 1)),
        DieLimit: 4, Affiliations: ["X-Men", "Brotherhood of Mutants"], Keywords: ["Energize"],
        RawText: "While Mystique is active, Brotherhood of Mutants character dice are free to field. Energize - " +
                 "You may field a Brotherhood of Mutants character die for free.",
        // D'Ken's own -99-floor-at-zero CostModifier precedent, Ownership:
        // Own instead of Opposing (v1's GrantsFreeFielding only ever
        // scanned the ACTIVE player's own granters - see TurnEngine.
        // CanFieldFree's remarks).
        Abilities: [..Energize(new MayPay(Cost: null, Then: new FieldDie(
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.ReservePool], Affiliations: new TagQuery(AnyOf: ["Brotherhood of Mutants"])),
            Free: true)))],
        Continuous:
        [
            new CostModifier(CostKind.Fielding,
                new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Affiliations: new TagQuery(AnyOf: ["Brotherhood of Mutants"])),
                Delta: -99),
        ]);

    public static readonly CardDef WolverineHardenedByMadripoor = new(
        Id: "DPS096", Name: "Wolverine", Subtitle: "Hardened by Madripoor", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS096Die", "Fist", (1, 5, 2), (2, 6, 3), (3, 8, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "When you have at least 3 active X-Men character dice, Wolverine gains \"Energize Spin this " +
                 "die to level 1\"",
        // v1's own reading: the ability always carries the Energize
        // trigger; the affiliation count gates the EFFECT, not whether
        // the card has the ability at all.
        Abilities: [..Energize(new Conditional(
            new CountAtLeast(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Affiliations: new TagQuery(AnyOf: ["X-Men"])), 3),
            Then: new Spin(new TargetFilter(Self: true), SetLevel: 1)))],
        Continuous: []);

    // The Energize half needs "new Attack = DOUBLE a die's printed A",
    // which nothing in the closed vocabulary expresses: ModifyStat's
    // delta fields are plain int (no live/bound value), and SetAttack's
    // Amount-typed StatOf would only echo the SAME value back, not double
    // it - there is no "StatOf x2" shape. Tailed (Ask) rather than
    // guessing a wrong approximation silently (V2_TAIL_POLICY.md house
    // rule); the Continuous half fits cleanly and is implemented, so the
    // card stays IsImplemented: false rather than fully vanilla.
    public static readonly CardDef IcemanMrIceGuy = new(
        Id: "DPS114", Name: "Iceman", Subtitle: "Mr Ice Guy", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS114Die", "Bolt", (1, 2, 4), (1, 3, 6), (1, 4, 6)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder", "Energize"],
        RawText: "Founder Energize - Double target character die's printed A until the end of turn. While " +
                 "Iceman is active, your Sidekick dice get +1A.",
        Abilities: [],
        Continuous:
        [
            new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), AtkDelta: new Fixed(1)),
        ],
        IsImplemented: false);

    // The first clause needs the payment-source visibility the user
    // explicitly declined to build (V2_PLAN.md's F13 addendum names "2
    // Bishop, Forge, Professor X" as the alter-or-skip candidates this
    // card is one of) - tailed, so the card stays IsImplemented: false.
    // The Energize half is ProfessorXUncannyLeadership's own identical
    // text, migrated the same way.
    public static readonly CardDef ProfessorXDreamer = new(
        Id: "DPS047", Name: "Professor X", Subtitle: "Dreamer", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS047Die", "Mask", (1, 1, 5), (2, 1, 7), (3, 1, 9)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "If you spend an X-Men die to field Professor X, Prep a die from your bag. Energize - Move an " +
                 "X-Men die from your Used Pile to your Prep Area.",
        Abilities: [..Energize(new MoveDie(
            new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Affiliations: new TagQuery(AnyOf: ["X-Men"])),
            Zone.PrepArea))],
        Continuous: [],
        IsImplemented: false);

    public static readonly CardDef AngelWingsOverTheWorld = new(
        Id: "DPS017", Name: "Angel", Subtitle: "Wings Over the World", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS017Die", "Shield", (0, 2, 2), (1, 3, 3), (1, 3, 4)),
        DieLimit: 5, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - Target Sidekick gets +2A this turn.",
        Abilities: [..Energize(new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["sidekick"])), AtkDelta: 2))],
        Continuous: []);

    public static readonly CardDef CableIllDoThisAllDay = new(
        Id: "DPS022", Name: "Cable", Subtitle: "I'll Do This All Day", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS022Die", "Bolt", (1, 3, 2), (2, 3, 3), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - Reroll one of your character dice.",
        Abilities: [..Energize(new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own)))],
        Continuous: []);

    public static readonly CardDef ColossusSkilledPainter = new(
        Id: "DPS023", Name: "Colossus", Subtitle: "Skilled Painter", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS023Die", "Fist", (1, 4, 4), (1, 6, 5), (2, 8, 7)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize", "Overcrush"],
        RawText: "Energize - Field one of your character dice for free and spin it to level 3.Overcrush",
        // Same target bound once and reused - Magik "Sorceress of Limbo"'s
        // own precedent. FieldDie always lands at level 1 (its own doc
        // comment), so Spin(SetLevel:3) on the SAME bound die reaches
        // level 3 exactly - no need to port v1's own two-target caching
        // trick, BindAs/Bound already IS that trick.
        Abilities: [..Energize(new Sequence(
        [
            new FieldDie(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.ReservePool], BindAs: "colossus"), Free: true),
            new Spin(new TargetFilter(Bound: "colossus"), SetLevel: 3),
        ]))],
        Continuous: []);

    public static readonly CardDef ToadLookingForComradery = new(
        Id: "DPS094", Name: "Toad", Subtitle: "Looking for Comradery", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS094Die", "Fist", (1, 2, 1), (2, 3, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["Brotherhood of Mutants"], Keywords: ["Energize"],
        RawText: "Energize - You may spin one of the Character dice in your reserve pool from a character face " +
                 "to level 1.",
        // v1's own effect was a flat -2 LevelDelta (only reaches level 1
        // from a die that rolled level 3); Spin's SetLevel absolute mode
        // (unavailable to v1) matches the printed "to level 1" text
        // exactly regardless of which level the die rolled, so this uses
        // that instead of porting v1's approximation. Ground rule 8 -
        // "you may" gets MayPay.
        Abilities: [..Energize(new MayPay(Cost: null, Then: new Spin(
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.ReservePool]), SetLevel: 1)))],
        Continuous: []);

    // --- Batch 5 (2026-09-01) ---
    //
    // First batch since the affiliation-first-class split (Parts 17-18,
    // 22) to hit cards that GRANT an affiliation rather than just READ
    // one - and it doesn't work. `GrantTag` only ever touched
    // `DieInstance.GrantedTags`, which `QueryEngine.GetTags` folds in;
    // `GetAffiliations` reads ONLY `CardDef.Affiliations` with no grant
    // fold-in at all, unlike Tags. Two cards below need exactly this
    // (Radicalization's Global, Emma Frost "Influential"'s Sidekick
    // clause) - both tailed, not silently no-op'd. Flagged in
    // `V2_TAIL_POLICY.md` as a real gap the split left open, not
    // single-card misses.

    public static readonly CardDef MutantResearchProgram = new(
        Id: "DPS008", Name: "Mutant Research Program", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 2, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS008Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "If you have at least 2 active Founder character dice, draw and roll 3 dice. Otherwise, " +
                 "draw and roll a die.",
        // ReservePool, not PrepArea - "draw AND ROLL" lands rolled
        // (Groot's own "roll 2 dice from your bag" precedent), unlike the
        // "Prep a die from your bag" family elsewhere in this file.
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new Conditional(
            new CountAtLeast(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Founder"])), 2),
            Then: new DrawToZone(3, Zone.ReservePool, Zone.Bag),
            Else: new DrawToZone(1, Zone.ReservePool, Zone.Bag)))],
        Continuous: []);

    // The Global half fits (Counter is a real Stat kind, Finding 13). The
    // WhenUsed half needs "at least 3 active dice that share ANY ONE
    // affiliation" - an EXISTENTIAL over affiliations, not "at least 3 of
    // a fixed affiliation" - CountAtLeast has no way to ask "does some
    // value repeat 3+ times across the pool" rather than "does a NAMED
    // value occur 3+ times". Tailed.
    public static readonly CardDef TightRanks = new(
        Id: "DPS016", Name: "Tight Ranks", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS016Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "If you have at least 3 active character dice that share a Team Affiliation, KO target " +
                 "character die. Global: Pay Shield. Target character die with at least one Loyalty Counter " +
                 "gets -2A and -2D.",
        Abilities: [new TriggeredAbility(TriggerKind.Global,
            new ModifyStat(
                new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Counter, CounterName: "Loyalty", Min: 1)),
                AtkDelta: -2, DefDelta: -2),
            EnergyCost: new EnergyCost(1, "Shield"))],
        Continuous: [],
        IsImplemented: false);

    // The DealDamage/Ko-on-double-burst half fits. The Global
    // ("gains X-Men or Brotherhood of Mutants") is the affiliation-grant
    // gap this batch's own header note describes - tailed.
    public static readonly CardDef Radicalization = new(
        Id: "DPS012", Name: "Radicalization", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS012Die", 0, 0, 2),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Deal 3 damage to target X-Men or Brotherhood of Mutants character die. **Also, KO target " +
                 "Sidekick character die. Global: Pay Shield. Target character die gains X-Men or Brotherhood " +
                 "of Mutants (until end of turn).",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new Sequence(
        [
            new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie, Affiliations: new TagQuery(AnyOf: ["X-Men", "Brotherhood of Mutants"]))),
            new Conditional(new OnBurstFace(BurstLevel.Double),
                Then: new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["sidekick"])))),
        ]))],
        Continuous: [],
        IsImplemented: false);

    public static readonly CardDef CorsairRecruitingACrew = new(
        Id: "DPS024", Name: "Corsair", Subtitle: "Recruiting a Crew", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS024Die", "Fist", (0, 3, 4), (1, 3, 5), (1, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, place the next die you purchase this turn into your bag.",
        // Delta: 0 - only the destination-override half of PurchaseModifier
        // is in play (the one-shot, per-controller "next purchase" queue,
        // Phase 5's own note).
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new PurchaseModifier(Delta: 0, GoesToZone: Zone.Bag))],
        Continuous: []);

    // "Opponent's Attack Step" is just Ownership: Opposing on the SAME
    // TurnStepEntered + Step: SelectAttackers shape Colossus "Piotr"
    // already uses for "the end of YOUR turn" (Ownership: Own) - Spike
    // C's own precedent, flipped.
    public static readonly CardDef EmmaFrostManipulative = new(
        Id: "DPS070", Name: "Emma Frost", Subtitle: "Manipulative", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS070Die", "Shield", (1, 3, 5), (1, 4, 6), (2, 5, 7)),
        DieLimit: 4, Affiliations: ["Hellfire Club"], Keywords: [],
        RawText: "While Emma Frost is active, at the start of your opponent's Attack Step, reroll target " +
                 "character die they control.",
        Abilities: [new TriggeredAbility(TriggerKind.TurnStepEntered,
            new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing)),
            Filter: new EventFilter(Ownership: TargetOwnership.Opposing, Step: StepIds.SelectAttackers))],
        Continuous: []);

    // The +2A half is a plain StatAura keyed on the card-name tag
    // ("Wolverine character die" precedent, Sabretooth "Am I
    // Interrupting?"). The purchase-discount half needs a CONTINUOUS
    // discount scoped to ONE NAMED CARD, not a payer - CostModifier's
    // `Whose` is player-scoped for Purchase/GlobalEnergy (Part 2's own
    // Jean Grey example) with no card-identity field at all, unlike the
    // one-shot `PurchaseModifier`'s `CardKind?`. Tailed.
    public static readonly CardDef CableBosomBuddies = new(
        Id: "DPS062", Name: "Cable", Subtitle: "Bosom Buddies", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS062Die", "Bolt", (1, 3, 2), (2, 3, 3), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Cable is active, your Deadpool costs 1 less to purchase (to a minimum of 1) and has +2A.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Deadpool"])), AtkDelta: new Fixed(2))],
        IsImplemented: false);

    // ExcludeSelf: true - "a character die with Founder attacks" reads as
    // ANOTHER die in v1's own trigger choice (WhenAnotherDieAttacks), the
    // exact shape Cyclops "First Class" already established for the
    // identical Founder-attacks pattern.
    public static readonly CardDef BeastFirstClass = new(
        Id: "DPS058", Name: "Beast", Subtitle: "First Class", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS058Die", "Fist", (0, 2, 1), (1, 2, 2), (1, 3, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder While Beast is active, when a character die with Founder attacks, Prep a die from " +
                 "your bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new DrawToZone(1, Zone.PrepArea, Zone.Bag),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Founder"]), ExcludeSelf: true))],
        Continuous: []);

    // The +1A/+1D-to-Sidekicks half is StatAura (Iceman "Mr Ice Guy"'s
    // own precedent). "...and gain the Hellfire Club affiliation" is this
    // batch's affiliation-grant gap - tailed.
    public static readonly CardDef EmmaFrostInfluential = new(
        Id: "DPS030", Name: "Emma Frost", Subtitle: "Influential", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS030Die", "Shield", (1, 3, 5), (1, 4, 6), (2, 5, 7)),
        DieLimit: 4, Affiliations: ["Hellfire Club"], Keywords: [],
        RawText: "While Emma Frost is active, your Sidekick dice get +1A and +1D, and gain the Hellfire Club " +
                 "affiliation.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), AtkDelta: new Fixed(1), DefDelta: new Fixed(1))],
        IsImplemented: false);

    // "Can't be rerolled/spun by OPPOSING effects" is a protection
    // against specific EFFECT TYPES (Reroll, Spin), not against being
    // TARGETED at all - `TargetingProtection`'s `From: Global|Action|
    // Both` blocks targeting by ability SOURCE, which is a different
    // axis entirely (Bishop can still be targeted by Reroll/Spin from the
    // controller's OWN effects, and the protection has to survive even
    // when the effect doesn't target at all - Reroll's own TargetFilter
    // sometimes IS a choice). No shape in the frozen vocabulary reaches
    // this. Tailed outright.
    public static readonly CardDef BishopTorturedTimeline = new(
        Id: "DPS019", Name: "Bishop", Subtitle: "Tortured Timeline", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS019Die", "Shield", (1, 2, 5), (1, 3, 6), (2, 5, 6)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "Opposing effects can't cause Bishop to be rerolled, or cause you to spin a Bishop die up " +
                 "or down.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // The payment-source visibility gap (V2_PLAN.md's Appendix A
    // addendum: "2 Bishop, Forge, Professor X") - this is one of the two
    // Bishops. "If you spend THIS DIE as energy to field a character
    // die" needs to know what an energy payment was spent ON, which no
    // event payload carries.
    public static readonly CardDef BishopImBack = new(
        Id: "DPS059", Name: "Bishop", Subtitle: "I'm Back", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS059Die", "Shield", (1, 2, 5), (1, 3, 6), (2, 5, 6)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "If you spend this die as energy to field a character die, add this die to your Prep Area.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // The surcharge is qualified by the PURCHASED CARD's own cost
    // ("2 or less") - the same card-identity gap CableBosomBuddies hits,
    // just via a threshold instead of a name. Dropping the threshold
    // would silently surcharge every purchase, not just cheap ones - a
    // real behavior change, not a stated approximation (house rule:
    // never guess a wrong approximation silently). Tailed outright.
    public static readonly CardDef ForgeSupportTechnician = new(
        Id: "DPS071", Name: "Forge", Subtitle: "Support Technician", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS071Die", "Bolt", (1, 2, 2), (1, 4, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Forge is active, your opponents must pay 1 more to purchase a die with purchase cost " +
                 "of 2 or less.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // "Each of your dice that spins up gets +2A" is conditioned on
    // whether THAT SPECIFIC die's own spin actually moved it (a die
    // already at its max level, spun "up", doesn't move and shouldn't
    // get the bonus) - a per-die outcome-conditioned bonus bolted onto a
    // multi-die Spin, which no template composition reaches (Spin has no
    // "and a bonus for whichever ones actually changed" companion, and
    // Sequence/Conditional operate on the whole clause, not per resolved
    // die). v1 needed a dedicated primitive for exactly this
    // (AttackBonusPerActualSpinUp) for the same reason. Tailed outright.
    public static readonly CardDef GreetingsFromKrakoa = new(
        Id: "DPS004", Name: "Greetings from Krakoa", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS004Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Spin up each character whose card has a Loyalty Counter. Each of your dice that spins up " +
                 "gets +2A.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // --- Batch 6 (2026-09-01) ---
    //
    // Two recurring gaps surfaced independently this batch, both real,
    // both tailed rather than fixed inline (see V2_TAIL_POLICY.md):
    //
    // 1. TargetFilter has no way to EXCLUDE one specific die (the source
    //    itself) from a pool it would otherwise match - EventFilter has
    //    ExcludeSelf for reactive triggers, TargetFilter has nothing
    //    equivalent. Where the source die's OWN zone at query time
    //    already excludes it (an attacker scoped to "Field Zone" while
    //    it's sitting in the Attack Zone) this doesn't bite - Cyclops
    //    "Utopia Realized"/"Xavier's Dream" below both lean on exactly
    //    that. Where the source and its "other" targets genuinely share a
    //    zone at the same moment (Cable "High Stakes"'s attack-trigger
    //    buff, Angel "Jean Grey's School"'s continuous Founder aura),
    //    there is no way to express "other" today.
    //
    // 2. Every continuous template requires its OWN source die to already
    //    be active (`ContinuousRegistry.ActiveSourceDice` scans Field/
    //    Attack Zone only) - a card cannot grant a benefit to ITSELF for
    //    something that happens BEFORE it's active, like its own fielding
    //    cost. Jean Grey "Marvel Girl"'s free-fielding clause hits this;
    //    her identical surcharge clause (Part 2's own worked paper
    //    example) does not, since it only cares whether SHE is active,
    //    not the die being priced.

    public static readonly CardDef TakeCover = new(
        Id: "DPS014", Name: "Take Cover", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS014Die", 1, 2, 0),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Character dice you control get +2D. */** Target character die gets an extra +3D. Global: " +
                 "Pay Shield. Target character die gets +1D (until end of turn).",
        // Both burst levels do the identical +3D - ported as two literal
        // Conditionals (v1's own shape) rather than inventing an "any
        // burst" condition for one card.
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieUsed, new Sequence(
            [
                new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Count: 0), DefDelta: 2),
                new Conditional(new OnBurstFace(BurstLevel.Single), Then: new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie), DefDelta: 3)),
                new Conditional(new OnBurstFace(BurstLevel.Double), Then: new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie), DefDelta: 3)),
            ])),
            new TriggeredAbility(TriggerKind.Global,
                new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie), DefDelta: 1),
                EnergyCost: new EnergyCost(1, "Shield")),
        ],
        Continuous: []);

    public static readonly CardDef EmmaFrostFinesse = new(
        Id: "DPS110", Name: "Emma Frost", Subtitle: "Finesse", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS110Die", "Shield", (1, 3, 5), (1, 4, 6), (2, 5, 7)),
        DieLimit: 4, Affiliations: ["X-Men", "Hellfire Club"], Keywords: [],
        RawText: "While Emma Frost is Active, at the start of your opponents Attack Step, reroll 2 target " +
                 "Fist character dice your oppoenent controls. Those showing character faces are returned to " +
                 "the Field Zone, those on energy faces are sent to the Reserve Pool.",
        // Emma Frost "Manipulative"'s own Ownership: Opposing + Step:
        // SelectAttackers precedent (batch 5), Reroll's NonCharacterMoveTo
        // handling "those on energy faces are sent to the Reserve Pool"
        // (a character-face lander is simply left in the Field Zone by
        // that primitive, which is what "returned to" already means).
        Abilities: [new TriggeredAbility(TriggerKind.TurnStepEntered,
            new Reroll(
                new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Count: 2, Tags: new TagQuery(AnyOf: ["Fist"])),
                NonCharacterMoveTo: Zone.ReservePool),
            Filter: new EventFilter(Ownership: TargetOwnership.Opposing, Step: StepIds.SelectAttackers))],
        Continuous: []);

    public static readonly CardDef CyclopsUtopiaRealized = new(
        Id: "DPS105", Name: "Cyclops", Subtitle: "Utopia Realized", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS105Die", "Bolt", (1, 4, 2), (1, 5, 3), (1, 6, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While you have 2 or more character dice in the Field Zone, when Cyclops attacks, deal 3 " +
                 "damage to target character die.",
        // No self-exclusion needed - Cyclops is himself in the Attack
        // Zone (he's the one attacking) by the time this checks the
        // Field Zone count, same reasoning as this card's own "Xavier's
        // Dream" printing below.
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new Conditional(
            new CountAtLeast(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.FieldZone]), 2),
            Then: new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie))))],
        Continuous: []);

    public static readonly CardDef CyclopsXaviersDream = new(
        Id: "DPS140", Name: "Cyclops", Subtitle: "Xavier's Dream", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS140Die", "Bolt", (1, 4, 2), (1, 5, 3), (1, 6, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While you have a Sidekick die active, when Cyclops attacks deal X damage divided how you " +
                 "choose among any number of target character dice, where X is the number of your character " +
                 "dice in the Field Zone.",
        // "Divided how you choose" is Distribute's own motivating example
        // (V2_PLAN.md's F5 addendum). Count: 0 resolves the full
        // character-die pool as Distribute's own candidate set, not a
        // separate choice - the point-by-point assignment IS the choice.
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new Conditional(
            new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), 1),
            Then: new DealDamage(
                new PerMatch(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.FieldZone]), 1),
                new TargetFilter(Kind: TargetKind.CharacterDie, Count: 0),
                Distribute: true)))],
        Continuous: []);

    public static readonly CardDef MadelynePryorSisterhood = new(
        Id: "DPS079", Name: "Madelyne Pryor", Subtitle: "Sisterhood", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS079Die", "Mask", (0, 1, 3), (0, 1, 4), (1, 2, 4)),
        DieLimit: 4, Affiliations: ["Brotherhood of Mutants"], Keywords: [],
        RawText: "When one of your Brotherhood of Mutants character dice is KO'd besides Madelyne Pryor, put " +
                 "a Loyalty Counter on her card. (Loyaly Counters give a character die +1A and +1D).",
        Abilities: [new TriggeredAbility(TriggerKind.DieKOd,
            new GrantCounter(new TargetFilter(Self: true), "Loyalty", 1),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, ExcludeSelf: true, Affiliations: new TagQuery(AnyOf: ["Brotherhood of Mutants"])))],
        Continuous: []);

    public static readonly CardDef MagnetoIdealist = new(
        Id: "DPS041", Name: "Magneto", Subtitle: "Idealist", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS041Die", "Mask", (1, 4, 4), (2, 5, 7), (3, 6, 8)),
        DieLimit: 2, Affiliations: ["Brotherhood of Mutants"], Keywords: [],
        RawText: "When one of your Mask character dice is KO'd, put a Loyalty Counter on Magneto's card. " +
                 "Global: Pay Mask. Once per turn, during your turn, if you have no dice in your Prep Area, " +
                 "you may draw a die and place it in your Prep Area.",
        // Ground rule 8 - "you may" gets MayPay, unlike this card's own
        // "Visionary" printing (already migrated, opposite condition,
        // and predates this being caught).
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieKOd,
                new GrantCounter(new TargetFilter(Self: true), "Loyalty", 1),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Mask"]))),
            new TriggeredAbility(TriggerKind.Global,
                new Conditional(new TurnFact(TurnFactKind.PrepAreaEmpty),
                    Then: new MayPay(Cost: null, Then: new DrawToZone(1, Zone.PrepArea, Zone.Bag))),
                EnergyCost: new EnergyCost(1, "Mask"), OncePerTurn: true),
        ],
        Continuous: []);

    public static readonly CardDef JeanGreyXaviersDream = new(
        Id: "DPS075", Name: "Jean Grey", Subtitle: "Xavier's Dream", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS075Die", "Bolt", (1, 3, 3), (2, 5, 5), (3, 6, 6)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder While Jean Grey and one of your Sidekick dice are active, your opponents must pay " +
                 "1 extra to use a Global Ability.",
        // CostModifier(GlobalEnergy, Player-scoped Whose) is Part 2's own
        // worked paper example for exactly this card - never migrated as
        // a real one until now.
        Abilities: [],
        Continuous: [new CostModifier(CostKind.GlobalEnergy,
            new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing), Delta: 1,
            ActiveWhen: new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["sidekick"])), 1))]);

    public static readonly CardDef JeanGreyMarvelGirl = new(
        Id: "DPS115", Name: "Jean Grey", Subtitle: "Marvel Girl", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS115Die", "Bolt", (1, 3, 3), (2, 5, 5), (3, 6, 6)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder While Jean Grey is active, your opponent must pay 1 extra to use a Global Ability. " +
                 "While you have a different X-Men character die in your Field Zone, Jean Grey is free to field.",
        // The free-fielding clause is a real gap, found trying to build
        // it: EVERY continuous template requires its own SOURCE die to
        // already be active (`ContinuousRegistry.ActiveSourceDice` scans
        // Field/Attack Zone only) before it grants anything - a card
        // cannot make ITSELF free to field, since by definition it isn't
        // active yet at the moment its own fielding cost is being asked.
        // D'Ken/Mystique's own free-fielding grants buff OTHER dice while
        // already active themselves; this is a different shape (self-
        // referential, active-before-active) nothing in the vocabulary
        // reaches. The surcharge half is fine (Part 2's own worked
        // example, same as this card's own "Xavier's Dream" printing).
        Abilities: [],
        Continuous: [new CostModifier(CostKind.GlobalEnergy, new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing), Delta: 1)],
        IsImplemented: false);

    // "Other character dice with Founder" - Angel is herself Founder and
    // herself active at the same moment as the dice she'd be buffing, so
    // (unlike Jean Grey "Marvel Girl" above) there is no natural zone/
    // timing exclusion. TargetFilter has no way to exclude one specific
    // die from a pool (this batch's own header note). Tailed.
    public static readonly CardDef AngelJeanGreysSchool = new(
        Id: "DPS057", Name: "Angel", Subtitle: "Jean Grey's School", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS057Die", "Shield", (0, 2, 2), (1, 3, 3), (1, 3, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder When Angel is active, other character dice with Founder get +1A.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // Same self-exclusion gap, from a triggered ability's own ModifyStat
    // rather than a continuous aura - Cable is himself in the Attack
    // Zone (attacking) at the same moment as the "other" dice he'd
    // double, same zone his own filter would otherwise match.
    public static readonly CardDef CableHighStakes = new(
        Id: "DPS102", Name: "Cable", Subtitle: "High Stakes", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS102Die", "Bolt", (1, 3, 2), (2, 3, 3), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When Cable attacks, double the printed A of all your other character dice.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // v1's own call (isImplemented: false) - three real gaps, none a
    // drop-in reuse: a simultaneous both-players-and-every-die hit, an
    // arbitrary-energy-spend-for-scaling-damage cost, and a burst-
    // conditional additional-damage clause on top of both.
    public static readonly CardDef Explosion = new(
        Id: "DPS003", Name: "Explosion", Subtitle: "Basic Action", Set: "DPS", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction("DPS003Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Deal 2 damage to each player and character die. You may also spend any number of Bolt " +
                 "energy, for each that you do you may deal 1 damage to target character die. ** Deal 1 " +
                 "additional damage to each player and character die that Explosion deals damage to.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // v1's own call (isImplemented: false) - "roll an opponent's used
    // Action die and use its effect if it shows one" needs an opponent's
    // card's own effect tree resolved under a foreign controller context,
    // which nothing in the frozen vocabulary reaches.
    public static readonly CardDef ForgeReverseEngineer = new(
        Id: "DPS111", Name: "Forge", Subtitle: "Reverse Engineer", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS111Die", "Bolt", (1, 2, 2), (1, 4, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Forge is active, if an opponent uses an action die, roll it. If it shows an action " +
                 "face you may use it's effect.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // "If an opponent draws an EXTRA die" needs to know the opponent's
    // draw was above their normal Clear and Draw count - DiceDrawn is one
    // of the 8 event kinds with no payload at all (only DieDamaged/
    // DieFaceChanged carry one), so there's no way to read how many dice
    // were drawn, let alone whether that was "extra."
    public static readonly CardDef MadelynePryorAspiring = new(
        Id: "DPS119", Name: "Madelyne Pryor", Subtitle: "Aspiring", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS119Die", "Mask", (0, 1, 3), (0, 1, 4), (1, 2, 4)),
        DieLimit: 4, Affiliations: ["Brotherhood of Mutants", "Hellfire Club"], Keywords: [],
        RawText: "While Madelyne Pryor is active, if an opponent draws an extra die during their Clear and " +
                 "Draw Step, Prep 2 dice from your bag (no matter how many extra dice your opponent draws, " +
                 "you only Prep 2 dice).",
        Abilities: [], Continuous: [], IsImplemented: false);

    // "Iceman's A is equal to his D" is a continuous ABSOLUTE set, not a
    // delta - StatAura only has AtkDelta/DefDelta (additive), unlike its
    // one-shot cousin ModifyStat's SetAttack/SetDefense. No continuous
    // absolute-set mode exists.
    public static readonly CardDef IcemanXaviersDream = new(
        Id: "DPS142", Name: "Iceman", Subtitle: "Xavier's Dream", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS142Die", "Bolt", (1, 2, 4), (1, 3, 6), (1, 4, 6)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: ["Founder"],
        RawText: "Founder While you have a Sidekick die active, Iceman's A is equal to his D.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // --- Batch 7 (2026-09-01) ---
    //
    // Kitty Pryde and Toad below are the first two real Awaken cards in
    // the catalog - built on today's RequireSelf fix (Part 30), without
    // which an unrelated die's own Awaken would have cross-fired.
    //
    // A third recurring gap this batch, distinct from both of batch 6's:
    // "if you have no Villains ON YOUR TEAM" / "shares a Team Affiliation
    // with a card on your team" reads the TEAM ROSTER (the cards a player
    // BUILT their team from), not live board state - v2 has no roster
    // concept at all yet (Phase 9's own concern). Wolverine "Pure of
    // Heart", Mystique "Relentless"'s Global, and Dark Phoenix
    // "Malevolent"'s purchase discount all hit this independently.

    public static readonly CardDef KittyPrydeRightOfPassage = new(
        Id: "DPS037", Name: "Kitty Pryde", Subtitle: "Right of Passage", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS037Die", "Mask", (0, 2, 2), (0, 3, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Awaken"],
        RawText: "Awaken - Prep a die from your bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged, new DrawToZone(1, Zone.PrepArea, Zone.Bag),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    public static readonly CardDef DKenEmperor = new(
        Id: "DPS026", Name: "D'Ken", Subtitle: "Emperor", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS026Die", "Shield", (0, 4, 4), (1, 5, 5), (2, 6, 6)),
        DieLimit: 4, Affiliations: ["Villains", "Shi'ar"], Keywords: [],
        RawText: "When D'Ken attacks, Prep a die from your Used Pile.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile]), Zone.PrepArea))],
        Continuous: []);

    public static readonly CardDef IcemanIcyInterference = new(
        Id: "DPS034", Name: "Iceman", Subtitle: "Icy Interference", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS034Die", "Bolt", (1, 2, 4), (1, 3, 6), (1, 4, 6)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When Iceman attacks, spin target opposing level 1 character die to an energy face.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new SpinToEnergy(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Level, Min: 1, Max: 1))))],
        Continuous: []);

    public static readonly CardDef ToadSecondaryMutation = new(
        Id: "DPS054", Name: "Toad", Subtitle: "Secondary Mutation", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS054Die", "Fist", (1, 2, 1), (2, 3, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["Brotherhood of Mutants"], Keywords: ["Awaken", "Teamwatch"],
        RawText: "Awaken: Deal 2 damage to target character die. Teamwatch - Spin Toad up 1 level.",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFaceChanged, new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)),
                Filter: new EventFilter(LevelIncreased: true, RequireSelf: true)),
            new TriggeredAbility(TriggerKind.DieFielded, new Spin(new TargetFilter(Self: true), LevelDelta: 1),
                Filter: new EventFilter(Ownership: TargetOwnership.Own, ExcludeSelf: true, SharesAffiliationWithListener: true)),
        ],
        Continuous: []);

    public static readonly CardDef VulcanRulerOfTheImperium = new(
        Id: "DPS055", Name: "Vulcan", Subtitle: "Ruler of The Imperium", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS055Die", "Fist", (0, 3, 2), (1, 4, 4), (1, 6, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Global: Pay Fist. Target character die must attack this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.Global,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie), CombatFlagKind.MustAttack),
            EnergyCost: new EnergyCost(1, "Fist"))],
        Continuous: []);

    public static readonly CardDef PsylockeAdventurer = new(
        Id: "DPS048", Name: "Psylocke", Subtitle: "Adventurer", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS048Die", "Mask", (0, 1, 2), (0, 2, 2), (1, 3, 3)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Wolverine is active, Psylocke gains Deadly. When fielded, spin target character die " +
                 "up 1 level.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Spin(new TargetFilter(Kind: TargetKind.CharacterDie), LevelDelta: 1))],
        // "While Wolverine is active" names no owner and no affiliation -
        // any Wolverine, either side - so this is a plain Tags match, not
        // Ownership:Own.
        Continuous: [new TagAura(new TargetFilter(Self: true), ["Deadly"],
            ActiveWhen: new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Tags: new TagQuery(AnyOf: ["Wolverine"])), 1))]);

    // "You may draw and roll a die. Instead [on a single-burst face],
    // draw 2, roll one, return the other" - ground rule 8 wraps the whole
    // choice in MayPay, not just the ordinary branch. DrawAndChooseOne's
    // own first real user.
    public static readonly CardDef GambitAceInTheHole = new(
        Id: "DPS032", Name: "Gambit", Subtitle: "Ace in the Hole", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        // Levels 1-2 carry a single burst mark (v1's own BurstStars: 1 on
        // both), level 3 none - MigrationDice.Character's burst-carrying
        // overload, its first real user.
        Die: MigrationDice.Character("DPS032Die", "Mask", [1, 1, 0], (1, 1, 1), (1, 2, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, you may draw and roll a die. * Instead, draw 2 dice, Roll one and return " +
                 "the other to your bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new MayPay(Cost: null, Then: new Conditional(
                new OnBurstFace(BurstLevel.Single),
                Then: new DrawAndChooseOne(2, TargetOwnership.Own, Zone.ReservePool, Zone.Bag),
                Else: new DrawToZone(1, Zone.ReservePool, Zone.Bag))))],
        Continuous: []);

    // The Sidekick-KO half fits; "when Mister Sinister KOs an opposing
    // character, you may pay 1 life..." needs KO-SOURCE attribution -
    // DieKOd's payload has none, same family as Blob "Immovable"'s own
    // gap (batch 3).
    public static readonly CardDef MisterSinisterGeneticist = new(
        Id: "DPS043", Name: "Mister Sinister", Subtitle: "Geneticist", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS043Die", "Bolt", (1, 4, 1), (2, 5, 2), (2, 6, 3)),
        DieLimit: 4, Affiliations: ["Villains"], Keywords: ["Deadly"],
        RawText: "When fielded, KO up to 2 target Sidekick dice. When Mister Sinister KOs an opposing " +
                 "character, you may pay 1 life. If you do, your opponent loses 1 life. Global: Pay Bolt " +
                 "Bolt. Target non-Sidekick character die gains Deadly.",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded,
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["sidekick"]), Count: 2, Optional: true))),
            new TriggeredAbility(TriggerKind.Global,
                new GrantTag(new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(NoneOf: ["sidekick"])), ["Deadly"]),
                EnergyCost: new EnergyCost(2, "Bolt")),
        ],
        Continuous: [],
        IsImplemented: false);

    // The Global (a temporary but STILL fundamentally continuous grant -
    // "your character dice can't be targeted... until end of turn") has
    // no one-shot template that grants a continuous-shaped protection;
    // GrantAbility grants a triggered ability, not immunity. Intimidate
    // itself has no Zone (V2_PLAN.md Phase 2's own note: deferred, and
    // still not built). Both clauses tailed.
    public static readonly CardDef GladiatorPsiResistance = new(
        Id: "DPS033", Name: "Gladiator", Subtitle: "Psi Resistance", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS033Die", "Fist", (0, 5, 5), (1, 6, 6), (0, 7, 7)),
        DieLimit: 4, Affiliations: ["Shi'ar"], Keywords: ["Intimidate"],
        RawText: "Intimidate (When fielded, remove target opposing character die from the Field Zone until " +
                 "end of turn - place it next to your character cards.) Global: Pay Fist. Your character " +
                 "dice can't be the target of Action Dice or Global Abilities (until end of turn).",
        Abilities: [], Continuous: [], IsImplemented: false);

    // The Continuous half ("+2A while Wolverine active") fits; the
    // Global ("shares a Team Affiliation with a card on YOUR TEAM") is
    // this batch's team-roster gap.
    public static readonly CardDef MystiqueRelentless = new(
        Id: "DPS045", Name: "Mystique", Subtitle: "Relentless", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS045Die", "Mask", (1, 1, 1), (0, 1, 1), (2, 1, 1)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When Wolverine is active, Mystique gets +2A. Global: Pay Mask Mask. Target character die " +
                 "can't block this turn if it shares a Team Affiliation with a character card on your team.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true), AtkDelta: new Fixed(2),
            ActiveWhen: new CountAtLeast(new TargetFilter(Kind: TargetKind.AnyDie, Tags: new TagQuery(AnyOf: ["Wolverine"])), 1))],
        IsImplemented: false);

    // The WhenFielded (Ko, then bonus damage if the KO'd die was X-Men -
    // Bound + Affiliations, made real by today's TargetResolver.Self/
    // Bound fix) and Global (Ko one of your own, next purchase -2) both
    // fit. The purchase discount ("if your opponent has an X-Men
    // character ON THEIR TEAM") is this batch's team-roster gap.
    public static readonly CardDef DarkPhoenixMalevolent = new(
        Id: "DPS027", Name: "Dark Phoenix", Subtitle: "Malevolent", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 7, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS027Die", "Bolt", (1, 5, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 4, Affiliations: ["Villains"], Keywords: [],
        RawText: "Dark Phoenix costs 1 less to purchase if your opponent has an X-Men character on their " +
                 "team. When fielded, KO target character die. If it's an X-Men character die, deal your " +
                 "opponent 1 damage. Global: Pay Bolt and KO one of your character dice. The next die you " +
                 "purchase this turn costs 2 less (to a minimum of 1).",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded, new Sequence(
            [
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, BindAs: "victim")),
                new Conditional(
                    new CountAtLeast(new TargetFilter(Bound: "victim", Affiliations: new TagQuery(AnyOf: ["X-Men"])), 1),
                    Then: new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing))),
            ])),
            new TriggeredAbility(TriggerKind.Global, new Sequence(
            [
                new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own)),
                new PurchaseModifier(Delta: -2),
            ]), EnergyCost: new EnergyCost(1, "Bolt")),
        ],
        Continuous: [],
        IsImplemented: false);

    // "If this game is in the draft format" - no game-format concept
    // exists anywhere in the engine.
    public static readonly CardDef DeadpoolDraftPick = new(
        Id: "DPS028", Name: "Deadpool", Subtitle: "#1 Draft Pick", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS028Die", "Fist", (0, 2, 4), (0, 2, 5), (1, 3, 7)),
        DieLimit: 4, Affiliations: ["X-Men", "Deadpool Affiliation"], Keywords: [],
        RawText: "If this game is in the draft format, at the start of the game pick a card on your team. " +
                 "That card costs 1 less to purchase this game (to a minimum of 1).",
        Abilities: [], Continuous: [], IsImplemented: false);

    // "If you have no Villains character dice ON YOUR TEAM" is this
    // batch's team-roster gap, AND (independently) a card granting
    // itself free-fielding before it's active - batch 6's Jean Grey
    // "Marvel Girl" gap. Two reasons, either one sufficient.
    public static readonly CardDef WolverinePureOfHeart = new(
        Id: "DPS056", Name: "Wolverine", Subtitle: "Pure of Heart", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("DPS056Die", "Fist", (1, 5, 2), (2, 6, 3), (3, 8, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "If you have no Villains character dice on your team, Wolverine is free to field.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // The payment-source visibility gap (V2_PLAN.md's Appendix A
    // addendum: "2 Bishop, Forge, Professor X") - this is "Forge."
    public static readonly CardDef ForgeMoreThanFirepower = new(
        Id: "DPS031", Name: "Forge", Subtitle: "More Than Firepower", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("DPS031Die", "Bolt", (1, 2, 2), (1, 4, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: [],
        RawText: "When fielded, if you spent Bolt energy to field Forge, Prep a die from your bag.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // "A card with Kree IN ITS NAME" is a substring match; Tags carry the
    // exact printed card name only, no substring predicate exists.
    public static readonly CardDef SupremeIntelligence = new(
        Id: "DPS053", Name: "Supreme Intelligence", Subtitle: "Kree Science Council", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DPS053Die", "Mask", (1, 4, 4), (2, 5, 6), (2, 7, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When a card with Kree in its name is KO'd, put a Loyalty Counter on Supreme Intelligence's " +
                 "card. (Loyaly Counters give a character die +1A and +1D).",
        Abilities: [], Continuous: [], IsImplemented: false);

    // "If you have purchased a CHARACTER die this turn" - TurnFact's own
    // PurchasedThisTurn has no character-only variant; approximating with
    // the general one would also fire off a Basic Action purchase, a real
    // behavior change (house rule: never guess wrong silently).
    public static readonly CardDef LilandraPolitician = new(
        Id: "DPS038", Name: "Lilandra", Subtitle: "Politician", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS038Die", "Shield", (1, 3, 3), (1, 3, 5), (1, 5, 6)),
        DieLimit: 4, Affiliations: ["Shi'ar"], Keywords: [],
        RawText: "Global: Pay Shield. Once per turn, if you have purchased a character die this turn, you " +
                 "may draw a die from your bag and add it to your Prep Area.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // The Continuous Action die mechanic (rule 1.2.3/2.6.4.2) is still
    // not modeled (V2_PLAN.md Phase 8 batch-1 note) - "reroll it [an
    // opponent's fielded Continuous Action die]" needs it to exist first.
    public static readonly CardDef MoiraItsNotADream = new(
        Id: "DPS044", Name: "Moira", Subtitle: "It's Not a Dream", Set: "DPS", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("DPS044Die", "Shield", (0, 0, 1), (0, 1, 2), (1, 2, 2)),
        DieLimit: 3, Affiliations: ["X-Men"], Keywords: [],
        RawText: "While Moira is active, when an opponent fields a Continuous Action die, reroll it. If it " +
                 "lands on an action face, they may field it normally. Otherwise, send it to the Used Pile.",
        Abilities: [], Continuous: [], IsImplemented: false);
}
