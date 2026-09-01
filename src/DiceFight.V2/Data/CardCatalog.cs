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
        Abilities: [], Continuous: [], IsImplemented: false);

    // Energize's precise trigger shape (an energy face showing 2+ pips,
    // during Roll & Reroll specifically) isn't expressible with the
    // frozen EventFilter/Condition set - no symbol-count Stat kind exists
    // (V2_PLAN.md's own Phase 5 note flagged this exact wiring as
    // deferred to whichever card needed it first; this is that card).
    // v1's own scripting policy (full text or nothing) applies here too -
    // the WhenFielded half alone IS buildable, but the card goes vanilla
    // as a whole rather than silently dropping the Energize clause. See
    // V2_TAIL_POLICY.md.
    public static readonly CardDef BlackPanther = new(
        Id: "MSW020", Name: "Black Panther", Subtitle: "Clutching Reality", Set: "MSW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("MSW020Die", "Mask", (0, 1, 2), (1, 2, 3), (2, 4, 4)),
        DieLimit: 4, Affiliations: ["Avengers", "Infinity Watch"], Keywords: ["Energize"],
        RawText: "Energize - Roll 2 dice from your bag. When fielded, roll a die from your bag.",
        Abilities: [], Continuous: [], IsImplemented: false);

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

    // Finding 4 (V2_VOCABULARY.md Part 1) - "[M] character die" = a Mask-
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
        Abilities: [], Continuous: [], IsImplemented: false);

    // Amplify isn't implemented (no Action-die-use reactive mechanism
    // exists for "any Action die," only the specific die's own DieUsed) -
    // see V2_TAIL_POLICY.md.
    public static readonly CardDef AntManAmplify = new(
        Id: "JLL002", Name: "Ant-Man", Subtitle: "Through The Cracks", Set: "JLL", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Fist"],
        Die: BuildCharacterDie("JLL002Die", "Fist", (0, 2, 1), (0, 3, 1), (1, 5, 2)),
        DieLimit: 4, Affiliations: [], Keywords: ["Amplify"],
        RawText: "Amplify - When you use an action die, spin this character up 1 level.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // SwapLife (life-total swap) - explicitly named non-coverage
    // (V2_PLAN.md Appendix A) - see V2_TAIL_POLICY.md.
    public static readonly CardDef CosmicCube = new(
        Id: "MSW002", Name: "Cosmic Cube", Subtitle: "Epic Basic Action", Set: "MSW", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: BuildActionDie("MSW002Die"),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: "Switch life totals with your opponent.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // V2_VOCABULARY.md Part 1's own MayPay motivating example. BindAs
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
        Abilities: [], Continuous: [], IsImplemented: false);

    // Infiltrate isn't implemented (Phase 7 note) - see V2_TAIL_POLICY.md.
    public static readonly CardDef Ricochet = new(
        Id: "GOTG105", Name: "Ricochet", Subtitle: "Slinger", Set: "GOTG", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt"],
        Die: BuildCharacterDie("GOTG105Die", "Bolt", (0, 2, 1), (1, 3, 2), (2, 4, 3)),
        DieLimit: 2, Affiliations: [], Keywords: ["Infiltrate"],
        RawText: "Infiltrate. While Ricochet is active, each time one of your character dice uses Infiltrate, draw a die from your bag and add it to your Prep Area.",
        Abilities: [], Continuous: [], IsImplemented: false);

    // Tag Out isn't implemented (Phase 7 note) - see V2_TAIL_POLICY.md.
    public static readonly CardDef BigE = new(
        Id: "TAG003", Name: "Big E", Subtitle: "Tag Team Champion", Set: "TAG", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("TAG003Die", "Mask", (0, 1, 4), (1, 2, 5), (1, 2, 7)),
        DieLimit: 4, Affiliations: ["A New Day"], Keywords: ["Tag Out"],
        RawText: "Tag Out (After blockers are declared, you may Prep this die from the Field Zone to give target Superstar die +2A and +2D until end of turn.)",
        Abilities: [], Continuous: [], IsImplemented: false);

    // Range isn't implemented (Phase 7 note) - see V2_TAIL_POLICY.md.
    public static readonly CardDef StarfireStarbolts = new(
        Id: "SKC090", Name: "Starfire", Subtitle: "Starbolts", Set: "SKC", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt"],
        Die: BuildCharacterDie("SKC090Die", "Bolt", (1, 3, 3), (2, 4, 4), (2, 5, 5)),
        DieLimit: 4, Affiliations: ["Teen Titans"], Keywords: ["Range"],
        RawText: "Range 2 (When this character attacks, all active characters with Range deal damage equal to their Range value to target opposing character die.)",
        Abilities: [], Continuous: [], IsImplemented: false);

    // Intimidate isn't implemented, and its own destination
    // (Zone.Intimidated in v1) has no equivalent in v2's 10-zone list at
    // all - see V2_TAIL_POLICY.md.
    public static readonly CardDef ScarletSpider = new(
        Id: "CW014", Name: "Scarlet Spider", Subtitle: "Former Villain", Set: "CW", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: BuildCharacterDie("CW014Die", "Mask", (1, 3, 3), (1, 4, 3), (2, 6, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["Intimidate"],
        RawText: "Intimidate (When fielded, remove target opposing character die from the Field Zone until end of turn - place it next to your character cards.)",
        Abilities: [], Continuous: [], IsImplemented: false);

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
        Abilities: [], Continuous: [], IsImplemented: false);

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
