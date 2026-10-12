using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// V2_PLAN.md Phase 8 task 2 - migrates the two curated v1 teams (~20
// cards: 8 characters + 2 Basic Actions each) from
// src/DiceFight.Engine/Data/SampleCards.cs (TeamA/TeamBCharacterIds/
// BasicActionIds), verbatim on names/subtitles/text/stats/keywords -
// this file does not re-derive or "improve" any of v1's own data,
// including its placeholder stats (v1's own class remarks: most "msw"-
// set cards never got real per-level stats sourced, only Name/Subtitle/
// RawText/DieLimit are real for those - ported as-is, not upgraded here).
//
// Face layout follows MigrationDice's one documented convention (see
// that file - v1 has no per-die face data at all, so every migrated
// die's face shape is a stated approximation, flagged once there rather
// than re-flagged per card).
public static class CardCatalog
{
    private static DieDefinition BuildCharacterDie(string dieId, string energySymbolId, params (int FieldingCost, int Attack, int Defense)[] levels) =>
        MigrationDice.Character(dieId, energySymbolId, levels);

    private static DieDefinition BuildActionDie(string dieId) => MigrationDice.BasicAction(dieId);

    // ---- Team A: 8 characters + 2 Basic Actions ----

    public static readonly CardDef Apocalypse = new(
        Id: "MSW018", Name: "Apocalypse", Subtitle: "Obsessive", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW018Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Overcrush"],
        RawText: "Overcrush (Character dice with Overcrush deal damage in excess of blocker's defense to opponent.)",
        Abilities: [], Continuous: []);

    // Regenerate isn't implemented in v2 (V2_PLAN.md Phase 7 note - not
    // CombatFlag/CombatRule-shaped, deliberately not ported) - see
    // V2_TAIL_POLICY.md.
    public static readonly CardDef Beast = new(
        Id: "MSW019", Name: "Beast", Subtitle: "Olympic Athleticism", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW019Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 3, Affiliations: [], Keywords: ["Regenerate"],
        RawText: "Regenerate (Reroll when KO'd)",
        Abilities: [], Continuous: [], IsImplemented: false, IncludeInTeamBuilder: true, KingdomName: "Axolotl", KingdomEnergyType: "Shell", HasErrata: true,
        ErrataNotes: [
            "Reference: rollinthunder_cards_with_dice(1).json, MSW019 (Beast: Olympic Athleticism).",
            "Purchase cost: 2 -> 3.",
            "Reference purchase energy: fist; stored source purchase energy: Mask; Dice Kingdom identity: Shell.",
            "Level 1 (fielding cost/attack/defense): 0/2/1 -> 0/1/2.",
            "Level 2 (fielding cost/attack/defense): 1/2/2 -> 1/2/3.",
            "Level 3 (fielding cost/attack/defense): 1/3/2 -> 2/4/4.",
        ]);

    // Both clauses use the ordinary draw-and-roll effect; Energize's
    // shared wrapper handles the end-of-step check and later rerolls.
    public static readonly CardDef BlackPanther = new(
        Id: "MSW020", Name: "Black Panther", Subtitle: "Clutching Reality", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW020Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["Avengers", "Infinity Watch"], Keywords: ["Energize"],
        RawText: "Energize - Roll 2 dice from your bag. When fielded, roll a die from your bag.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(1, Zone.ReservePool)),
            .. KeywordAbilities.Energize(new DrawToZone(2, Zone.ReservePool)),
        ], Continuous: [], IsImplemented: true, IncludeInTeamBuilder: true, KingdomName: "Puffin", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: [
            "Reference: rollinthunder_cards_with_dice(1).json, MSW020 (Black Panther: Clutching Reality).",
            "Purchase cost: 5 -> 3.",
            "Level 1 (fielding cost/attack/defense): 1/5/7 -> 0/1/2.",
            "Level 2 (fielding cost/attack/defense): 2/7/8 -> 1/2/3.",
            "Level 3 (fielding cost/attack/defense): 2/8/8 -> 2/4/4.",
        ]);

    public static readonly CardDef HarleyQuinn = new(
        Id: "SKC032", Name: "Harley Quinn", Subtitle: "Bright Lights Big City", Set: "SKC", CardType: CardType.Character,
        PurchaseCost: 1, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("SKC032Die", "Mask", (0, 2, 2), (1, 3, 3), (1, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "", // real card, genuinely blank text box
        Abilities: [], Continuous: []);

    public static readonly CardDef CaptainMarvel = new(
        Id: "MSW023", Name: "Captain Marvel", Subtitle: "Alpha Flight", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW023Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While Captain Marvel is active, your Character dice get +1 attack and +1 defense.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own), AtkDelta: new Fixed(1), DefDelta: new Fixed(1))]);

    // Finding 4 (V2_VOCABULARY_HISTORY.md Part 1) - "[M] character die" = a Mask-
    // tagged CharacterDie filter, since printed energy symbol is part of
    // every die's tag set.
    public static readonly CardDef Dazzler = new(
        Id: "MSW026", Name: "Dazzler", Subtitle: "Lightbringer", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW026Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, deal 4 damage to target [M] character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(4), new TargetFilter(Kind: TargetKind.CharacterDie, Tags: new TagQuery(AnyOf: ["Mask"]))))],
        Continuous: []);

    // Call Out isn't implemented in v2 (Phase 7 note) - see V2_TAIL_POLICY.md.
    public static readonly CardDef BlackWidow = new(
        Id: "GOTG005", Name: "Black Widow", Subtitle: "Red Scare", Set: "GOTG", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: BuildCharacterDie("GOTG005Die", "Fist", (0, 3, 1), (0, 3, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["Call Out"],
        RawText: "Call Out - When this character die attacks, target character die is the only character die that may block this character die.",
        Abilities: [], Continuous: [], IsImplemented: false, IncludeInTeamBuilder: true, KingdomName: "Trapdoor Spider", KingdomEnergyType: "Eye");

    // Amplify is supplied by the shared keyword ability registry.
    public static readonly CardDef AntManAmplify = new(
        Id: "JLL002", Name: "Ant-Man", Subtitle: "Through The Cracks", Set: "JLL", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: BuildCharacterDie("JLL002Die", "Fist", (0, 2, 1), (0, 3, 1), (1, 5, 2)),
        DieLimit: 4, Affiliations: [], Keywords: ["Amplify"],
        RawText: "Amplify - When you use an action die, spin this character up 1 level.",
        Abilities: [], Continuous: [], IsImplemented: true, IncludeInTeamBuilder: true, KingdomName: "Stag Beetle", KingdomEnergyType: "Claw");

    // SwapLife (life-total swap) - explicitly named non-coverage
    // (V2_PLAN.md Appendix A) - see V2_TAIL_POLICY.md.
    public static readonly CardDef CosmicCube = new(
        Id: "MSW002", Name: "Cosmic Cube", Subtitle: "Epic Basic Action", Set: "MSW", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: BuildActionDie("MSW002Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Switch life totals with your opponent.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // V2_VOCABULARY_HISTORY.md Part 1's own MayPay motivating example. BindAs
    // remembers the damaged die so the Conditional can check TargetWasKOd
    // against that same die (Finding 9's binding table).
    public static readonly CardDef ShockingGrasp = new(
        Id: "MSW011", Name: "Shocking Grasp", Subtitle: "Basic Action", Set: "MSW", CardType: CardType.BasicAction,
        PurchaseCost: 2, EnergySymbolIds: [],
        Die: BuildActionDie("MSW011Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Deal 1 damage to target character die. If that character is KO'd by this damage, you may Prep this die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie, BindAs: "target")),
            new Conditional(new TargetWasKOd("target"),
                new MayPay(Cost: null, Then: new MoveDie(new TargetFilter(Self: true), Zone.PrepArea))),
        ]))],
        Continuous: []);

    // ---- Team B: 8 characters + 2 Basic Actions ----

    public static readonly CardDef FranklinsGalactus = new(
        Id: "MSW028", Name: "Franklin's Galactus", Subtitle: "Earth Shatterer", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW028Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "", // genuinely blank
        Abilities: [], Continuous: []);

    public static readonly CardDef GodEmperorDoom = new(
        Id: "MSW029", Name: "God Emperor Doom", Subtitle: "Harnessing the Beyonders", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW029Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, deal 3 damage to target character die and reroll target character die.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Sequence([
            new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie)),
            new Reroll(new TargetFilter(Kind: TargetKind.CharacterDie)),
        ]))],
        Continuous: []);

    public static readonly CardDef Groot = new(
        Id: "MSW031", Name: "Groot", Subtitle: "Skilled Investigator", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW031Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, roll 2 dice from your bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(2, Zone.ReservePool, Zone.Bag))],
        Continuous: []);

    // Teamwatch isn't one of the 10 frozen trigger kinds, and
    // FieldSidekickForEachPlayer's per-player "field one if able" shape
    // has no template equivalent - see V2_TAIL_POLICY.md.
    public static readonly CardDef Falcon = new(
        Id: "MSW027", Name: "Falcon", Subtitle: "Take Flight", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW027Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["Avengers"], Keywords: ["Teamwatch"],
        RawText: "Teamwatch - Prep a [PAWN] from your Used Pile. Global: Pay [F]. Once during your turn, each player must field a [PAWN] from their Used Pile if able.",
        Abilities: [], Continuous: [], IsImplemented: false, IncludeInTeamBuilder: true, KingdomName: "Weaverbird", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: [
            "Reference: rollinthunder_cards_with_dice(1).json, MSW027 (Falcon: Take Flight).",
            "Reference purchase energy: fist; stored source purchase energy: Mask; Dice Kingdom identity: Wing.",
            "Level 1 (fielding cost/attack/defense): 0/2/2 -> 0/1/2.",
            "Level 2 (fielding cost/attack/defense): 1/3/3 -> 1/2/3.",
            "Level 3 (fielding cost/attack/defense): 1/4/3 -> 2/4/4.",
        ]);

    // React only when an Infiltrate offer is accepted and its effect completes.
    public static readonly CardDef Ricochet = new(
        Id: "GOTG105", Name: "Ricochet", Subtitle: "Slinger", Set: "GOTG", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt"],
        Die: BuildCharacterDie("GOTG105Die", "Bolt", (0, 2, 1), (1, 3, 2), (2, 4, 3)),
        DieLimit: 2, Affiliations: [], Keywords: ["Infiltrate"],
        RawText: "Infiltrate. While Ricochet is active, each time one of your character dice uses Infiltrate, draw a die from your bag and add it to your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.InfiltrateUsed, new DrawToZone(1, Zone.PrepArea),
            Filter: new EventFilter(Ownership: TargetOwnership.Own))], Continuous: [], IsImplemented: true, IncludeInTeamBuilder: true, KingdomName: "Sugar Glider", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: [
            "Reference: rollinthunder_cards_with_dice(1).json, GOTG105 (Ricochet: Slinger).",
            "Maximum dice: 4 -> 2.",
        ]);

    // Tag Out uses the shared post-block choice and stat modifier.
    public static readonly CardDef BigE = new(
        Id: "TAG003", Name: "Big E", Subtitle: "Tag Team Champion", Set: "TAG", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("TAG003Die", "Mask", (0, 1, 4), (1, 2, 5), (1, 2, 7)),
        DieLimit: 4, Affiliations: ["A New Day"], Keywords: ["Tag Out"],
        RawText: "Tag Out (After blockers are declared, you may Prep this die from the Field Zone to give target Superstar die +2A and +2D until end of turn.)",
        Abilities: [], Continuous: [], IsImplemented: true, IncludeInTeamBuilder: true, KingdomName: "Prairie Dog", KingdomEnergyType: "Shell", HasErrata: true,
        ErrataNotes: [
            "Reference: rollinthunder_cards_with_dice(1).json, TAG003 (Big E: Tag Team Champion).",
            "Reference purchase energy: mask/shield; stored source purchase energy: Mask; Dice Kingdom identity: Shell.",
        ]);

    // The printed Range value feeds the shared combat shot resolution.
    public static readonly CardDef StarfireStarbolts = new(
        Id: "SKC090", Name: "Starfire", Subtitle: "Starbolts", Set: "SKC", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: BuildCharacterDie("SKC090Die", "Bolt", (1, 3, 3), (2, 4, 4), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["Teen Titans"], Keywords: ["Range 2"],
        RawText: "Range 2 (When this character attacks, all active characters with Range deal damage equal to their Range value to target opposing character die.)",
        Abilities: [], Continuous: [], IsImplemented: true, IncludeInTeamBuilder: true, KingdomName: "Spitting Cobra", KingdomEnergyType: "Claw");

    // Intimidate is supplied by the shared keyword ability registry.
    public static readonly CardDef ScarletSpider = new(
        Id: "CW014", Name: "Scarlet Spider", Subtitle: "Former Villain", Set: "CW", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("CW014Die", "Mask", (1, 3, 3), (1, 4, 3), (2, 6, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["Intimidate"],
        RawText: "Intimidate (When fielded, remove target opposing character die from the Field Zone until end of turn - place it next to your character cards.)",
        Abilities: [], Continuous: [], IsImplemented: true, IncludeInTeamBuilder: true, KingdomName: "Pufferfish", KingdomEnergyType: "Eye");

    // Rule 3.2.5's per-ability snapshot (EffectInterpreter's class
    // remarks - this card was its motivating case twice over: named in
    // v1's own comments, then the first migrated card to actually hit
    // Phase 5's resolve-live simplification) keeps the Ko clause's own
    // KO'd dice out of the later Prep-Area clause's candidate pool.
    // Epic Basic Action mechanics (rule 1.2.3 - once-per-turn limiter,
    // returns to its card instead of Out of Play) remain unmodeled -
    // CardType has no Epic distinction - so the die itself behaves as an
    // ordinary Basic Action die; see V2_TAIL_POLICY.md (Approximate).
    public static readonly CardDef CasketOfAncientWinters = new(
        Id: "MSW001", Name: "Casket of Ancient Winters", Subtitle: "Epic Basic Action", Set: "MSW", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: BuildActionDie("MSW001Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Your opponent KOs three of their character dice, moves 3 dice from their Reserve Pool to their bag, and moves 3 dice from their Prep Area to their Used Pile.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
            new Ko(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.FieldZone], Count: 3)),
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.ReservePool], Count: 3), Zone.Bag),
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.PrepArea], Count: 3), Zone.UsedPile),
        ]))],
        Continuous: []);

    // A "redraw a chosen subset of dice already drawn this turn" flow -
    // explicitly named non-coverage (V2_PLAN.md Appendix A: "draw-and-
    // choose flows") - see V2_TAIL_POLICY.md.
    public static readonly CardDef CosmicCubeInfinitePossibilities = new(
        Id: "GOTG008", Name: "Cosmic Cube", Subtitle: "Basic Action", Set: "GOTG", CardType: CardType.BasicAction,
        PurchaseCost: 2, EnergySymbolIds: [],
        Die: BuildActionDie("GOTG008Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "During your Clear and Draw Step, when you draw this die from your bag, you may send it and any other dice you've drawn this turn Out of Play. For each die sent Out of Play, draw a die.",
        Abilities: [], Continuous: [], IsImplemented: false, IncludeInTeamBuilder: true, KingdomName: "Seasonal Renewal", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: [
            "Reference: rollinthunder_cards_with_dice(1).json, GOTG008 (Cosmic Cube: Basic Action).",
            "Purchase cost: 3 -> 2.",
            "Maximum dice: 4 -> 3.",
            "Reference purchase energy: bolt; stored source purchase energy: none (Basic Action); Dice Kingdom identity: Wing.",
            "Dice Kingdom import only: energy faces Generic 2 / Generic 2 / Generic 2 -> Wing 2 / Wing 2 / Wing 1. The three action faces and their burst marks are retained.",
        ]);

    public static readonly IReadOnlyList<string> TeamACharacterIds =
        [Apocalypse.Id, Beast.Id, BlackPanther.Id, HarleyQuinn.Id, CaptainMarvel.Id, Dazzler.Id, BlackWidow.Id, AntManAmplify.Id];

    public static readonly IReadOnlyList<string> TeamABasicActionIds = [CosmicCube.Id, ShockingGrasp.Id];

    public static readonly IReadOnlyList<string> TeamBCharacterIds =
        [Falcon.Id, FranklinsGalactus.Id, GodEmperorDoom.Id, Groot.Id, Ricochet.Id, BigE.Id, StarfireStarbolts.Id, ScarletSpider.Id];

    public static readonly IReadOnlyList<string> TeamBBasicActionIds = [CasketOfAncientWinters.Id, CosmicCubeInfinitePossibilities.Id];

    public static IReadOnlyDictionary<string, CardDef> BuildCatalog()
    {
        CardDef[] all =
        [
            Apocalypse, Beast, BlackPanther, HarleyQuinn, CaptainMarvel, Dazzler, BlackWidow, AntManAmplify, CosmicCube, ShockingGrasp,
            Falcon, FranklinsGalactus, GodEmperorDoom, Groot, Ricochet, BigE, StarfireStarbolts, ScarletSpider, CasketOfAncientWinters, CosmicCubeInfinitePossibilities,
        ];
        return all.ToDictionary(c => c.Id);
    }
}
