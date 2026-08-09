using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Data;

// A curated slice of real Dice Masters cards - names, subtitles, and
// ability text taken verbatim from ~/DiceMasters/Teambuilder/cards.php
// (the "msw" set array), used here to exercise the engine end-to-end.
//
// IMPORTANT - most numeric stats below are still placeholders. None of the
// six cloned DiceCoalition repos (Teambuilder, DiceMastersCompanion,
// cardservice, DiceBot, DM-OBS-Source, Homepage) contain real per-level
// attack/defense numbers for the "msw" set specifically (the most recent
// release at the time it was sampled) - every community tool represents
// combat stats via card-face images, not structured data. Teambuilder's
// compact per-card prefix (e.g. "133J4") only reliably decodes to a die
// limit (last character; confirmed against rule 1.2.11's fixed "Use 3"
// for every Basic Action card sampled) - purchase cost, energy type, and
// full 3-level fielding/attack/defense are not recoverable from it. So
// most cards below still share one placeholder PurchaseCost/EnergyType/
// Levels progression (a few are bumped to a higher placeholder cost just
// to exercise Epic Basic Action's cost-4+ gate, rule 1.2.3(4)); only
// Name/Subtitle/RawText/DieLimit are real for those.
//
// Four characters (Big Barda, Harley Quinn, Robin, Starfire - see below)
// are sourced instead from the user's reference spreadsheet, which covers
// an older set that *does* record real cost/energy/per-level stats
// (fielding cost/attack/defense, encoded as "CAD" triplets per level,
// e.g. "133 244 255" = L1 cost1/atk3/def3, L2 cost2/atk4/def4, L3
// cost2/atk5/def5). Their die limit still isn't in that source, so it's
// a reasonable guess (4, typical for Common rarity), not sourced fact -
// flagged here rather than presented as real. Replace PlaceholderLevels
// for the rest once a real stats source for the "msw" set is available.
//
// Scripting policy: a card only gets an AbilityDef when its FULL ability
// text (not just part of it) maps onto EffectNode primitives with nothing
// dropped. Everything else is left vanilla (empty Abilities, RawText and
// Keywords still captured) rather than silently simulating a subset of
// the card's real behavior - see RULES_ENGINE_DESIGN.md's authoring policy.
public static class SampleCards
{
    private const int PlaceholderCost = 3;
    private const EnergyType PlaceholderEnergy = EnergyType.Mask;

    private static readonly IReadOnlyList<CharacterFace> PlaceholderLevels =
    [
        new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
        new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
        new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
    ];

    private static CardDef Character(
        string id, string name, string subtitle, int dieLimit, string rawText,
        IReadOnlyList<KeywordInstance>? keywords = null,
        IReadOnlyList<AbilityDef>? abilities = null,
        int purchaseCost = PlaceholderCost,
        EnergyType energyType = PlaceholderEnergy,
        IReadOnlyList<CharacterFace>? levels = null,
        IReadOnlyList<string>? grantsToSidekicks = null,
        IReadOnlyList<string>? affiliations = null,
        StaticTeamBonus? grantsStaticTeamBonus = null,
        ConditionalSelfKeywordGrant? grantsSelfKeywordWhileNamedCardActive = null,
        bool isImplemented = true,
        string? set = null) => new()
    {
        Id = id,
        Name = name,
        Subtitle = subtitle,
        Type = CardType.Character,
        PurchaseCost = purchaseCost,
        EnergyTypes = [energyType],
        DieLimit = dieLimit,
        Levels = levels ?? PlaceholderLevels,
        RawText = rawText,
        Keywords = keywords ?? [],
        Abilities = abilities ?? [],
        GrantsToSidekicks = grantsToSidekicks ?? [],
        Affiliations = affiliations ?? [],
        GrantsStaticTeamBonus = grantsStaticTeamBonus,
        GrantsSelfKeywordWhileNamedCardActive = grantsSelfKeywordWhileNamedCardActive,
        IsImplemented = isImplemented,
        Set = set
    };

    private static CardDef BasicAction(
        string id, string name, string rawText, bool epic = false,
        IReadOnlyList<AbilityDef>? abilities = null,
        bool isImplemented = true,
        int? purchaseCost = null,
        IReadOnlyList<KeywordInstance>? keywords = null,
        string? set = null) => new()
    {
        Id = id,
        Name = name,
        Subtitle = epic ? "Epic Basic Action" : "Basic Action",
        Type = epic ? CardType.EpicBasicAction : CardType.BasicAction,
        // purchaseCost overrides the epic/non-epic placeholder once a real
        // cost is known (e.g. from BulkCards.json - see the DPS remarks
        // below); real Basic Action costs vary (2-5) well beyond a flat
        // epic/non-epic split, so the placeholder is only ever a fallback.
        PurchaseCost = purchaseCost ?? (epic ? 4 : 2),
        EnergyTypes = [], // rule 1.2.4/1.3.10 - Basic Actions have no energy type
        DieLimit = 3, // rule 1.2.11 - fixed for every Basic Action card
        RawText = rawText,
        Keywords = keywords ?? [],
        Abilities = abilities ?? [],
        IsImplemented = isImplemented,
        Set = set
    };

    // ---- Team A: 10 characters + 3 Basic Actions ----

    // Real cost/energy/stats sourced from the user's reference spreadsheet
    // (see class remarks) - dieLimit is a guess (4, typical Common rarity),
    // not sourced.
    // "Ignore all non-combat damage" - no such damage-source-filtering
    // mechanism exists (only Sacrifice/Range/DealDamage-node damage do,
    // and none distinguish "combat" from "non-combat" at the point
    // they'd need to check it) - left vanilla, isImplemented: false.
    public static readonly CardDef BigBarda = Character(
        "SKC021", "Big Barda", "Formerly of Apokolips", dieLimit: 4,
        "Ignore all non-combat damage dealt to Big Barda.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ],
        isImplemented: false, set: "SKC");

    public static readonly CardDef Apocalypse = Character(
        "MSW018", "Apocalypse", "Obsessive", dieLimit: 4,
        "Overcrush (Character dice with Overcrush deal damage in excess of blocker's defense to opponent.)",
        keywords: [new KeywordInstance("Overcrush")], set: "MSW");

    public static readonly CardDef Beast = Character(
        "MSW019", "Beast", "Olympic Athleticism", dieLimit: 3,
        "Regenerate (Reroll when KO'd)",
        keywords: [new KeywordInstance("Regenerate")], set: "MSW");

    public static readonly CardDef BlackPanther = Character(
        "MSW020", "Black Panther", "Clutching Reality", dieLimit: 4,
        "Energize - Roll 2 dice from your bag. When fielded, roll a die from your bag.",
        keywords: [new KeywordInstance("Energize")],
        abilities: [
            new AbilityDef(TriggerType.Energize, Cost: null, Effect: new DrawDice(2)),
            new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new DrawDice(1)),
        ],
        affiliations: ["Avengers", "Infinity Watch"], set: "MSW");

    public static readonly CardDef HarleyQuinn = Character(
        "SKC032", "Harley Quinn", "Bright Lights Big City", dieLimit: 4,
        "", // real card, genuinely blank text box
        purchaseCost: 1, energyType: EnergyType.Mask,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4)
        ], set: "SKC");

    // A purchase-cost discount - no purchase-cost-modifier mechanism
    // exists yet (see RULES_ENGINE_DESIGN.md) - left vanilla,
    // isImplemented: false.
    public static readonly CardDef Robin = Character(
        "SKC048", "Robin", "Team Leader", dieLimit: 4,
        "Energize - The first Teen Titans die you purchase this turn costs 1 less (to a minimum of 1).",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Energize")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ],
        isImplemented: false, set: "SKC");

    // A live, continuously-recomputed Static team-wide bonus (rule
    // 3.4.5.7) - see CardDef.GrantsStaticTeamBonus/DieStats.
    // StaticTeamBonusFor. No AbilityDef needed, same shape as Strike's
    // "no trigger at all" design.
    public static readonly CardDef CaptainMarvel = Character(
        "MSW023", "Captain Marvel", "Alpha Flight", dieLimit: 4,
        "While Captain Marvel is active, your Character dice get +1 attack and +1 defense.",
        purchaseCost: 4,
        grantsStaticTeamBonus: new StaticTeamBonus(AttackDelta: 1, DefenseDelta: 1), set: "MSW");

    public static readonly CardDef Colossus = Character(
        "MSW024", "Colossus", "Inferno", dieLimit: 4, "", set: "MSW"); // real card, genuinely blank text box

    // The KO half is buildable (Ko node) but the "next purchase costs 2
    // less" half needs the same purchase-cost-modifier mechanism Robin's
    // Energize is missing - left vanilla rather than half-scripted,
    // isImplemented: false.
    public static readonly CardDef CorvusGlaive = Character(
        "MSW025", "Corvus Glaive", "The Black Order", dieLimit: 3,
        "When fielded, KO a character die you control. If you do, the next die you purchase this turn costs [2] less (minimum 1).",
        isImplemented: false, set: "MSW");

    public static readonly CardDef Dazzler = Character(
        "MSW026", "Dazzler", "Lightbringer", dieLimit: 4,
        "When fielded, deal 4 damage to target [M] character die.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new DealDamage(4, TargetSpec.CharacterDie("target [M] character die", energyType: EnergyType.Mask)))], set: "MSW");

    public static readonly CardDef CosmicCube = BasicAction(
        "MSW002", "Cosmic Cube", "Switch life totals with your opponent.", epic: true,
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new SwapLife())], set: "MSW");

    public static readonly CardDef ShockingGrasp = BasicAction(
        "MSW011", "Shocking Grasp",
        "Deal 1 damage to target character die. If that character is KO'd by this damage, you may Prep this die.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new DealDamage(1, TargetSpec.CharacterDie("target character die")),
            new Conditional(TargetSpec.CharacterDie("target character die"), EffectCondition.TargetWasKOd,
                new PrepDie(TargetSpec.Self))
        ]))], set: "MSW");

    // Shocking Grasp is a genuine cross-set reprint (identical effect,
    // slightly different printed wording per set) - MSW011 above stays
    // the one on Team A's roster; these two are declared for the
    // catalog only, same text as MSW011 for consistency (the sheet's
    // FUS034/TIW057 rows word it as "...put this die into your Prep
    // Area" instead - not fixed at the source, see DESIGN_LOG.md).
    public static readonly CardDef ShockingGraspFus = BasicAction(
        "FUS034", "Shocking Grasp",
        "Deal 1 damage to target character die. If that character is KO'd by this damage, you may Prep this die.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new DealDamage(1, TargetSpec.CharacterDie("target character die")),
            new Conditional(TargetSpec.CharacterDie("target character die"), EffectCondition.TargetWasKOd,
                new PrepDie(TargetSpec.Self))
        ]))], set: "FUS");

    public static readonly CardDef ShockingGraspTiw = BasicAction(
        "TIW057", "Shocking Grasp",
        "Deal 1 damage to target character die. If that character is KO'd by this damage, you may Prep this die.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new DealDamage(1, TargetSpec.CharacterDie("target character die")),
            new Conditional(TargetSpec.CharacterDie("target character die"), EffectCondition.TargetWasKOd,
                new PrepDie(TargetSpec.Self))
        ]))], set: "TIW");

    // Distraction's Non-global ability ("target opponent chooses two...
    // cannot block") is left unscripted (multi-die opponent choice + a
    // persistent "cannot block" flag we don't model) but its separate
    // Global ability maps cleanly on its own - Non-global and Global are
    // genuinely independent ability slots (rule 3.1.3), scored separately.
    // isImplemented is still false, though - it's a whole-card flag (the
    // Non-global half is real, missing behavior, not just flavor text).
    public static readonly CardDef Distraction = BasicAction(
        "MSW005", "Distraction",
        "Target opponent chooses two of their character dice. They cannot block this turn. " +
        "Global: Pay [M]. Remove target attacking character die from combat.",
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new MoveDie(
                TargetSpec.CharacterDie("target attacking character die", zones: [Zone.AttackZone]),
                Zone.FieldZone),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Mask))],
        isImplemented: false, set: "MSW");

    // ---- Team B: 10 characters + 3 Basic Actions ----

    // Teamwatch and Global are independent ability slots (same shape as
    // Distraction above) - both now scripted. Real affiliation "Avengers"
    // (MSW027), shared with Black Panther's "Clutching Reality" printing.
    public static readonly CardDef Falcon = Character(
        "MSW027", "Falcon", "Take Flight", dieLimit: 4,
        "Teamwatch - Prep a [PAWN] from your Used Pile. Global: Pay [F]. Once during your turn, " +
        "each player must field a [PAWN] from their Used Pile if able.",
        keywords: [new KeywordInstance("Teamwatch")],
        abilities: [
            new AbilityDef(TriggerType.Teamwatch, Cost: null,
                Effect: new PrepDie(TargetSpec.Sidekick("a Sidekick die from your Used Pile", TargetOwnership.Own, zones: [Zone.UsedPile]))),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new FieldSidekickForEachPlayer(),
                EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Fist),
                OncePerTurn: true),
        ],
        affiliations: ["Avengers"], set: "MSW");

    public static readonly CardDef FranklinsGalactus = Character(
        "MSW028", "Franklin's Galactus", "Earth Shatterer", dieLimit: 4, "", set: "MSW"); // genuinely blank

    public static readonly CardDef GodEmperorDoom = Character(
        "MSW029", "God Emperor Doom", "Harnessing the Beyonders", dieLimit: 4,
        "When fielded, deal 3 damage to target character die and reroll target character die.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new Sequence([
            new DealDamage(3, TargetSpec.CharacterDie("target character die")),
            new Reroll(TargetSpec.CharacterDie("target character die"))
        ]))], set: "MSW");

    // "Another active character die with Thor in the name or subtitle" -
    // no name/subtitle-substring TargetSpec/Static-bonus filter exists
    // (GrantsStaticTeamBonus only keys off CardId, not a text match) -
    // left vanilla, isImplemented: false.
    public static readonly CardDef GoddessOfThunder = Character(
        "MSW030", "Goddess of Thunder", "Thor Corps", dieLimit: 2,
        "Goddess of Thunder gets +5 attack while you have another active character die with Thor in the name or subtitle.",
        isImplemented: false, set: "MSW");

    public static readonly CardDef Groot = Character(
        "MSW031", "Groot", "Skilled Investigator", dieLimit: 4,
        "When fielded, roll 2 dice from your bag.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new DrawDice(2))], set: "MSW");

    // The static "+1 attack for each other active [F4]..." clause is left
    // unscripted (no "count active dice matching X" stat-modifier
    // primitive exists yet); its Global stands on its own, same
    // independent-ability-slots reasoning as Distraction/Falcon above.
    // isImplemented is still false - same reasoning as Distraction.
    public static readonly CardDef InvisibleWoman = Character(
        "MSW032", "Invisible Woman", "Also Dr. Richards", dieLimit: 4,
        "Invisible Woman gets +1 attack for each of your other active [F4] character dice. " +
        "Global: Pay [M]. Target character die must block this turn.",
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new ForceBlock(TargetSpec.CharacterDie("target character die")),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Mask))],
        isImplemented: false, set: "MSW");

    // "Gain an extra 2 life for each of your other active characters
    // with Thor in their name or subtitle, or the [TCS] affiliation" -
    // same missing name-substring-match primitive as Goddess of Thunder
    // above (the affiliation half alone would be buildable, but the
    // "or" makes the whole clause one unit) - left vanilla,
    // isImplemented: false.
    public static readonly CardDef JaneFoster = Character(
        "MSW033", "Jane Foster", "Doctor", dieLimit: 4,
        "When fielded, gain 2 life, and gain an extra 2 life for each of your other active characters " +
        "with Thor in their name or subtitle, or the [TCS] affiliation.",
        isImplemented: false, set: "MSW");

    // "Recruit" (bring in an off-team Teen Titans die) is left unscripted -
    // no off-team-recruitment mechanic exists yet; its Global stands on
    // its own, same independent-ability-slots reasoning as above.
    // isImplemented is still false - same reasoning as Distraction.
    public static readonly CardDef Starfire = Character(
        "SKC050", "Starfire", "No-Nonsense Warrior", dieLimit: 4,
        "Recruit - a Teen Titans character die.\n" +
        "Global: Pay Shield. Once per turn, if you purchased a die this turn, Prep a die from your bag.",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ],
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new PrepFromBagIfPurchasedThisTurn(),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Shield),
            OncePerTurn: true)],
        isImplemented: false, set: "SKC");

    // No "pay life to reroll" cost/effect combination is built -
    // isImplemented: false.
    public static readonly CardDef Kang = Character(
        "MSW035", "Kang", "Prophetic Revelation", dieLimit: 3,
        "While Kang is active, once per turn, a player may pay 2 life to reroll a die in their Reserve Pool.",
        isImplemented: false, set: "MSW");

    // A reactive "while active, when an OPPONENT uses an Action die"
    // trigger - the engine's own Attune/Amplify/Obscure precedent only
    // reacts to the controller's *own* Action-die use, not the
    // opponent's - left vanilla, isImplemented: false.
    public static readonly CardDef KingHyperion = Character(
        "MSW036", "King Hyperion", "Earth-21195", dieLimit: 4,
        "While King Hyperion is active, when an opponent uses an action die, deal 2 damage to target character die.",
        isImplemented: false, set: "MSW");

    public static readonly CardDef CasketOfAncientWinters = BasicAction(
        "MSW001", "Casket of Ancient Winters",
        "Your opponent KOs three of their character dice, moves 3 dice from their Reserve Pool to their bag, " +
        "and moves 3 dice from their Prep Area to their Used Pile.", epic: true,
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new Ko(TargetSpec.CharacterDie(
                "opponent's 3 character dice", TargetOwnership.Opposing, count: 3, zones: [Zone.FieldZone])),
            new MoveDie(
                TargetSpec.AnyDie("opponent's 3 reserve pool dice", TargetOwnership.Opposing, [Zone.ReservePool], count: 3),
                Zone.Bag),
            new MoveDie(
                TargetSpec.AnyDie("opponent's 3 prep area dice", TargetOwnership.Opposing, [Zone.PrepArea], count: 3),
                Zone.UsedPile)
        ]))], set: "MSW");

    // "Prep up to 2 of them, roll the remainder" - a per-die player
    // choice DrawDice doesn't expose (it either draws-unrolled or the
    // caller externally rolls all of it) - left vanilla, isImplemented: false.
    public static readonly CardDef DailyBugle = BasicAction(
        "MSW004", "Daily Bugle", "Draw 2 dice. Prep up to 2 of them, roll the remainder.",
        isImplemented: false, set: "MSW");

    // "Choose one" between two unrelated effects, one of which
    // ("can't be targeted this turn") needs a per-die targeting
    // restriction flag that doesn't exist - left vanilla, isImplemented: false.
    public static readonly CardDef Escape = BasicAction(
        "MSW006", "Escape!",
        "Choose one: Target character die can't be targeted this turn. Or: Prep a die from your Used Pile.",
        isImplemented: false, set: "MSW");

    // Three real printings of Alfred Pennyworth (World's Finest set, same
    // reference spreadsheet as Big Barda/Harley Quinn/Robin/Starfire - see
    // class remarks), added to exercise the Ally keyword: rule Appendix 1
    // says an Ally Character die counts as a Sidekick Character die while
    // in the Field Zone (see DieStats.CountsAsSidekick), in addition to its
    // normal attributes - not just while active. Die limits aren't in the
    // spreadsheet; guessed from each printing's rarity stripe using the
    // usual Common/Uncommon/Rare -> 4/3/2 die-limit convention, same
    // caveat as the other spreadsheet-sourced cards' guessed die limits.
    //
    // All three left with an empty Abilities list rather than force-fit:
    // each one reads "target Batman die OR target Sidekick," a compound
    // target this engine can't express yet (TargetSpec has no
    // affiliation-based filter, and no "either of these two specs"
    // union) - see RULES_ENGINE_DESIGN.md. Real stats/keyword/RawText
    // still capture everything that maps cleanly, per the usual policy.
    public static readonly CardDef AlfredPennyworthCaretaker = Character(
        "WF035", "Alfred Pennyworth", "Caretaker of Wayne Manor", dieLimit: 4,
        "Ally - When fielded, give target Batman character die or another target Sidekick +2 defense until end of turn.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Ally")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2)
        ],
        isImplemented: false, set: "WF");

    public static readonly CardDef AlfredPennyworthMI5 = Character(
        "WF075", "Alfred Pennyworth", "MI-5", dieLimit: 3,
        "Ally - When KO'd, you may roll a Sidekick or Batman die from your Used Pile. If you roll an energy " +
        "result, return Alfred to the Field Zone at level 1. Either way, return the rolled die to the Used Pile.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Ally")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2)
        ],
        isImplemented: false, set: "WF");

    public static readonly CardDef AlfredPennyworthToughAsNails = Character(
        "WF107", "Alfred Pennyworth", "Tough as Nails", dieLimit: 2,
        "Ally - When fielded, give target Batman die or target Sidekick +1 attack and +1 defense " +
        "(besides Alfred Pennyworth) while attacking this turn.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Ally")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2)
        ],
        isImplemented: false, set: "WF");

    // Two more real spreadsheet-sourced printings, this time for Amplify
    // and Awaken - a paired keyword the user asked for together since
    // Amplify's own spin ("When you use an Action die, spin each
    // Character die with Amplify up one level") is exactly the trigger
    // Awaken reacts to ("When a Character die with Awaken spins up 1 or
    // more levels..."), whatever the source of the spin. Both keywords
    // are fully implemented (DieStats.SpinLevel/TurnEngine.CheckAwaken),
    // not just data - see RULES_ENGINE_DESIGN.md's status update.
    //
    // Justice Like Lightning's Ant-Man, "Through The Cracks" printing -
    // Amplify is entirely the built-in keyword spin, no card-specific
    // effect to script beyond it.
    public static readonly CardDef AntManAmplify = Character(
        "JLL002", "Ant-Man", "Through The Cracks", dieLimit: 4,
        "Amplify - When you use an action die, spin this character up 1 level.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        keywords: [new KeywordInstance("Amplify")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2)
        ], set: "JLL");

    // X-Men First Class's Cyclops, "Boy Scout" printing - a single-clause
    // Awaken effect that maps cleanly onto DealDamage, unlike most of the
    // set's Awaken text (which leans on mechanics like Unblockable/
    // Capture this engine doesn't have yet).
    public static readonly CardDef Cyclops = Character(
        "XFC010", "Cyclops", "Boy Scout", dieLimit: 4,
        "Awaken - Deal 3 damage to target character die. (When this die spins up 1 or more levels, " +
        "you may use this effect.)",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Awaken")],
        abilities: [new AbilityDef(TriggerType.Awaken, Cost: null,
            Effect: new DealDamage(3, TargetSpec.CharacterDie("target character die")))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 4)
        ], set: "XFC");

    // Avengers Infinity's Wasp, "Flitting About" printing - picked because
    // her card layers a genuine second clause on top of the Attune
    // keyword's own built-in damage (see TurnEngine.UseActionDie's
    // AttuneDamage): "When you use Attune, Wasp gets +1A and +1D until
    // end of turn," the first sample card to actually exercise ModifyStat.
    public static readonly CardDef Wasp = Character(
        "AI047", "Wasp", "Flitting About", dieLimit: 4,
        "Attune - While this character is active, when you use an action die, deal 1 damage to target " +
        "player or character die. When you use Attune, Wasp gets +1 attack and +1 defense until end of turn.",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Attune")],
        abilities: [new AbilityDef(TriggerType.Attune, Cost: null,
            Effect: new ModifyStat(TargetSpec.Self, AttackDelta: 1, DefenseDelta: 1))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 4)
        ], set: "AI");

    // Guardians of the Galaxy's Black Widow, "Red Scare" printing. Stick
    // ("Nobody Feels Sorry For You," same set) was the other option the
    // user offered - checked, and its card text turns out to be word-for-
    // word identical reminder text, not a trickier variant; the actual
    // complexity lives in the keyword's own rules-appendix wording (the
    // two-directional block restriction, plus its cancellation clause),
    // not in either printing's card text, so either would have scripted
    // identically. Went with Black Widow (cheaper, matches "the simple
    // one" framing) - see CombatEngine.ValidateCallOuts/
    // ActiveCallOutTargets for the actual keyword implementation.
    public static readonly CardDef BlackWidow = Character(
        "GOTG005", "Black Widow", "Red Scare", dieLimit: 4,
        "Call Out - When this character die attacks, target character die is the only character die that " +
        "may block this character die.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        keywords: [new KeywordInstance("Call Out")],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new SetCallOutTarget(TargetSpec.CharacterDie("target character die", TargetOwnership.Opposing)))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "GOTG");

    // Dark X-Men's Polaris, "Lorna Dane" printing - the simplest of a
    // handful of that set's Corrupt 2 cards (Rogue/Sage/Sunspot/
    // Thunderbird all read almost identically), picked for a plain
    // WhenFielded trigger. See EffectNode.Corrupt's remarks for the
    // keyword's own mechanics.
    public static readonly CardDef Polaris = Character(
        "DXM010", "Polaris", "Lorna Dane", dieLimit: 4,
        "When Polaris is fielded, Corrupt 2 (Target player draws 2 dice, places 1 in the Used Pile, and " +
        "returns the rest).",
        purchaseCost: 4, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Corrupt", Params: [2])], // the X in "Corrupt X"
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Corrupt(2, TargetSpec.Player("target player")))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ], set: "DXM");

    // Guardians of the Galaxy's Cosmic Cube, "Infinite Possibilities"
    // printing - a distinct real card from the already-present MSW
    // "Epic Basic Action... switch life totals" Cosmic Cube (different
    // set, different id, different text entirely; both are genuine real
    // printings). Its own energy type on the reference spreadsheet reads
    // "Bolt," but rule 1.3.10 is unambiguous - "Sidekick and Basic Action
    // dice have no energy type attribute" - so that column is presumably
    // flavor/die-color metadata rather than an actual purchase
    // requirement; treated as a plain, typeless Basic Action here, per
    // the rules text rather than the sheet.
    //
    // This is the card that actually exercises TriggerType.WhenDrawn/
    // RedrawFromBag: "During your Clear and Draw Step, when you draw this
    // die from your bag, you may send it and any other dice you've drawn
    // this turn Out of Play. For each die sent Out of Play, draw a die."
    // Rip Hunter's "Navigate the Sands of Time" (Batman set) has the same
    // shape (send chosen drawn dice to the Used Pile instead of Out of
    // Play, draw replacements) but adds a "while active" gate and a
    // "once during your Clear and Draw Step" limiter this engine doesn't
    // model yet - left for whenever that specific card gets picked up,
    // since RedrawFromBag/WhenDrawn already generalize to it.
    public static readonly CardDef CosmicCubeInfinitePossibilities = BasicAction(
        "GOTG008", "Cosmic Cube",
        "During your Clear and Draw Step, when you draw this die from your bag, you may send it and any " +
        "other dice you've drawn this turn Out of Play. For each die sent Out of Play, draw a die.",
        abilities: [new AbilityDef(TriggerType.WhenDrawn, Cost: null,
            Effect: new RedrawFromBag(
                TargetSpec.AnyDie(
                    "dice drawn this turn", TargetOwnership.Own, [Zone.DiceFromBag, Zone.DiceFromPrep], count: 10,
                    optional: true), // "you may send ANY NUMBER of them" - zero is a legal choice
                Zone.OutOfPlay))], set: "GOTG");

    // Batman set's Parademon, "Servant of Apokalips" printing - purely
    // the Swarm keyword, no extra text, the simplest possible card to
    // exercise it against. Appendix 1's real wording ("While a Character
    // die with Swarm is active, and you draw another copy of that die
    // from your bag during your Clear and Draw Step, draw an extra die
    // from your bag and add it to your Roll and Reroll") checks card
    // identity, not a rolled face - see TurnEngine.ClearAndDraw's remarks
    // on why that distinction matters here (nothing drawn this early in
    // the turn has a face to compare yet in the first place). Purely
    // engine-level keyword behavior, like Overcrush/Amplify/Attune - no
    // AbilityDef needed, since there's no choice or target involved.
    public static readonly CardDef Parademon = Character(
        "BAT027", "Parademon", "Servant of Apokalips", dieLimit: 4,
        "Swarm",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Swarm")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)
        ], set: "BAT");

    // Batman set's Darkseid, "Force of Entropy" printing (Super Rare, as
    // requested): "While Darkseid is active, your Sidekicks gain Swarm."
    // Not a triggered ability at all - a static, continuously-recomputed
    // grant (CardDef.GrantsToSidekicks, applied live by DieStats.
    // HasKeyword), the first sample card to use that mechanism.
    //
    // The interesting case this unlocks: "your Sidekicks" reaches an
    // active Ally die too (Alfred Pennyworth counts as a Sidekick while
    // fielded - DieStats.CountsAsSidekick), so Darkseid grants Swarm to
    // him as well. But Swarm's own match is still on *that specific
    // die's* card identity (see TurnEngine.ClearAndDraw's remarks) - a
    // granted-Swarm Alfred only triggers on drawing another Alfred, not
    // on drawing a plain Sidekick, and a granted-Swarm plain Sidekick
    // only triggers on drawing another plain Sidekick (they're mutually
    // fungible - real Sidekicks have no CardId to tell them apart at
    // all), not on drawing Alfred. Two keyword systems composing
    // correctly without cross-wiring anything Ally- or Swarm-specific
    // together - each one only knows its own rule.
    public static readonly CardDef Darkseid = Character(
        "BAT117", "Darkseid", "Force of Entropy", dieLimit: 1, // Super Rare
        "While Darkseid is active, your Sidekicks gain Swarm.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        grantsToSidekicks: ["Swarm"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 3, Attack: 7, Defense: 7)
        ], set: "BAT");

    // Dark Phoenix Saga's Deathbird, "Treacherous" printing - purely the
    // Deadly keyword, no other text. Fully engine-level, like Overcrush/
    // Amplify/Attune/Swarm: no target or choice at all - see
    // CombatEngine.RecordDeadlyEngagements/TurnEngine.CleanUp for the
    // actual mechanics ("engaged" is recorded at Declare Blockers, KO'd
    // later at Clean Up, regardless of what happens to either die in
    // between - see the design doc for the full reasoning).
    public static readonly CardDef Deathbird = Character(
        "DPS029", "Deathbird", "Treacherous", dieLimit: 4,
        "Deadly",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Deadly")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Civil War's Wasp, "Pixie" printing - purely the Fast keyword, no
    // other text. Fully engine-level like Overcrush/Deadly/Swarm: no
    // target or choice, just a two-wave damage resolution baked into
    // CombatEngine.AssignCombatDamage/ResolveFastOrSlowDamage.
    public static readonly CardDef WaspPixie = Character(
        "CW021", "Wasp", "Pixie", dieLimit: 4,
        "Fast",
        purchaseCost: 3, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Fast")],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "CW");

    // X-Men Forever's Madalyne Pryor, "Red Queen" printing - purely the
    // Energy Drain keyword (base X=1), no other text. Fully engine-level
    // like Deadly/Overcrush: no target or choice - see
    // CombatEngine.ResolveEnergyDrain/DieStats.EnergyDrainAmount.
    public static readonly CardDef MadalynePryor = Character(
        "XMF035", "Madalyne Pryor", "Red Queen", dieLimit: 4,
        "Energy Drain (Spin engaged character dice down 1 level.)",
        purchaseCost: 2, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Energy Drain")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4)
        ], set: "XMF");

    // Guardians of the Galaxy's The Spot, "Dr. Johnathan Ohnn" printing -
    // purely the Infiltrate keyword, no other text. Fully engine-level:
    // no AbilityDef needed for the base keyword itself (the choice and
    // effect are baked into CombatEngine.ResolveInfiltrate), matching
    // Deadly/Overcrush precedent.
    public static readonly CardDef TheSpot = Character(
        "GOTG038", "The Spot", "Dr. Johnathan Ohnn", dieLimit: 4,
        "Infiltrate (When this character die is unblocked, you may return this die to the Field Zone and " +
        "it deals your opponent 1 damage.)",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Infiltrate")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3)
        ], set: "GOTG");

    // Guardians of the Galaxy's Ricochet, "Slinger" printing - has
    // Infiltrate itself, plus a reactive follow-up: "While Ricochet is
    // active, each time one of your character dice uses Infiltrate, draw
    // a die from your bag and add it to your Prep Area." Not Ricochet's
    // own ability triggering off its own Infiltrate use specifically -
    // any of the controller's character dice using Infiltrate (including
    // Ricochet itself) triggers it, same shape as Attune reacting to
    // "you use an Action die." See TriggerType.WhenInfiltrates.
    public static readonly CardDef Ricochet = Character(
        "GOTG105", "Ricochet", "Slinger", dieLimit: 2,
        "Infiltrate. While Ricochet is active, each time one of your character dice uses Infiltrate, draw a " +
        "die from your bag and add it to your Prep Area.",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Infiltrate")],
        abilities: [new AbilityDef(TriggerType.WhenInfiltrates, Cost: null, Effect: new PrepFromBag())],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "GOTG");

    // Civil War's Scarlet Spider, "Former Villain" printing - purely the
    // Intimidate keyword, no other text. WhenFielded, matching the
    // keyword's own trigger, targeting an opposing Character die and
    // moving it to Zone.Intimidated (see TurnEngine.CleanUp's remarks for
    // the return-at-end-of-turn half).
    public static readonly CardDef ScarletSpider = Character(
        "CW014", "Scarlet Spider", "Former Villain", dieLimit: 4,
        "Intimidate (When fielded, remove target opposing character die from the Field Zone until end of " +
        "turn - place it next to your character cards.)",
        purchaseCost: 4, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Intimidate")],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new MoveDie(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Zone.Intimidated))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3)
        ], set: "CW");

    // Icons: Tomb of Annihilation's Drow Mercenary, "Hired Blade" printing -
    // purely the Obscure keyword, no other text. Unlike Intimidate/Deadly
    // (which needed a new zone or a tracked die-id set), Obscure's "when you
    // use an Action die" trigger and "unblockable" effect are both handled
    // generically in TurnEngine.UseActionDie and CombatEngine.DeclareBlockers
    // /ActiveCallOutTargets - this card contributes nothing but the printed
    // keyword itself, same as TheSpot's Infiltrate.
    public static readonly CardDef DrowMercenary = Character(
        "TIW007", "Drow Mercenary", "Hired Blade", dieLimit: 4,
        "Obscure (When you use an Action die, this character is unblockable until end of turn.)",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Obscure")],
        // Real Affinity "Neutral Equip Monster" - split into tokens (this
        // set's alignment/class tags aren't "/"-joined like Marvel/DC's
        // multi-affiliation cards). "Monster" is the token keyword
        // Experience's own earning condition checks for.
        affiliations: ["Neutral", "Equip", "Monster"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4)
        ], set: "TIW");

    // Justice League's Superman, "Kal-El" printing - purely the
    // Retaliation keyword at its base amount (1 damage), no other text.
    // The first sample card to actually populate CardDef.Affiliations -
    // Retaliation is the first keyword that needs it (see
    // CombatEngine.ResolveRetaliation).
    public static readonly CardDef SupermanKalEl = Character(
        "JL018", "Superman", "Kal-El", dieLimit: 4,
        "Retaliation - If an affiliated character is KO'd, deal 1 damage to an opposing player.",
        purchaseCost: 6, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Retaliation")],
        abilities: [new AbilityDef(TriggerType.Retaliation, Cost: null,
            Effect: new DealDamage(1, TargetSpec.Player("an opposing player", TargetOwnership.Opposing)))],
        affiliations: ["Justice League"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "JL");

    // Justice League's Black Manta, "Deep Sea Deviant" printing - the
    // keyword's own base amount (1 damage) is entirely redefined by this
    // printing's own text ("for each of your active Villains" - a live
    // count, not a fixed number), so this needs DealDamagePerActiveAffiliate
    // rather than DealDamage - see that EffectNode's own remarks.
    public static readonly CardDef BlackMantaDeepSeaDeviant = Character(
        "JL078", "Black Manta", "Deep Sea Deviant", dieLimit: 4,
        "Retaliation - If one of your Villains is KO'd, deal 1 damage to your opponent for each of your " +
        "active Villains.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        keywords: [new KeywordInstance("Retaliation")],
        abilities: [new AbilityDef(TriggerType.Retaliation, Cost: null,
            Effect: new DealDamagePerActiveAffiliate(TargetSpec.Player("your opponent", TargetOwnership.Opposing)))],
        affiliations: ["Legion of Doom", "Villains"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5)
        ], set: "JL");

    // Justice League's Bizarro, "More Than a Monster" printing - purely
    // the Strike keyword, no other text. No AbilityDef needed at all - the
    // bonus is a live, continuously-recomputed check (DieStats.
    // HasStrikeBonus), not a triggered effect, same shape as Loyalty
    // counters or Darkseid's keyword grant.
    public static readonly CardDef BizarroMoreThanAMonster = Character(
        "JUS008", "Bizarro", "More Than a Monster", dieLimit: 4,
        "Strike (This character gets +2A, +2D, and Overcrush so long as it is the only character die you " +
        "fielded this turn.)",
        purchaseCost: 7, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Strike")],
        affiliations: ["Legion of Doom", "Villains"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 7)
        ], set: "JUS");

    // Amazing Spider-Man's "Spidey's Last Stand" Basic Action - purely
    // the Sacrifice mechanic paired with an already-buildable effect, no
    // "you may... if you do" optional-choice branching (the Action die's
    // own use is the opt-in moment - see Sacrifice's own remarks).
    // Sacrifice, not Ko: the sacrificed die bypasses TryResolveKO/
    // ForceKO/Regenerate entirely and never fires "when KO'd."
    public static readonly CardDef SpideysLastStand = BasicAction(
        "ASM031", "Spidey's Last Stand",
        "Sacrifice a character to draw and roll 2 dice (sacrificed characters are placed in the Used Pile).",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new Sacrifice(TargetSpec.CharacterDie("a character die you control", TargetOwnership.Own)),
            new DrawDice(2)
        ]))], set: "ASM");

    // WWE's The Rock, "Know Your Role" printing - the user's own
    // suggested Sacrifice example (Global: "Pay Mask, and Sacrifice one
    // of your Superstar dice. Reduce the cost of the next die you
    // purchase by 2."). Left fully vanilla rather than partially
    // scripted: the Global needs a purchase-cost-modifier mechanism
    // (same gap already noted for Robin's Energize) that doesn't exist,
    // and the card's own Intimidate text has a further wrinkle on top
    // ("You may use Intimidate twice when you field The Rock" - two
    // independently-chosen targets from one ability, not yet attempted).
    // RawText/Keywords still capture both real keywords for display.
    public static readonly CardDef TheRock = Character(
        "BIT017", "The Rock", "Know Your Role", dieLimit: 4,
        "Intimidate (When fielded, remove target opposing Superstar die from the Field Zone until end of " +
        "turn - place it next to your Superstar cards.) You may use Intimidate twice when you field The " +
        "Rock. Global: Pay [M], and Sacrifice one of your Superstar dice. Reduce the cost of the next die " +
        "you purchase by 2.",
        purchaseCost: 6, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Intimidate"), new KeywordInstance("Sacrifice")],
        affiliations: ["Superstar"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 6)
        ],
        isImplemented: false, set: "BIT");

    // WWE's Big E, "Tag Team Champion" printing - purely the Tag Out
    // keyword, no other text. No AbilityDef at all - like Infiltrate/
    // Deadly, Tag Out's fixed +2A/+2D is built directly into CombatEngine
    // (see ResolveTagOut), so any card with the keyword gets full
    // functionality with zero scripting. Real WWE-branded cards all
    // print "target Superstar die" rather than "target Character die" -
    // treated as this brand's own universal term for a Character die
    // (every printing says it identically; nothing suggests it's an
    // affiliation-restricting filter the way "Villains"/"Avengers" are),
    // so this uses the ordinary TargetSpec.CharacterDie, not an
    // affiliation-scoped one.
    public static readonly CardDef BigE = Character(
        "TAG003", "Big E", "Tag Team Champion", dieLimit: 4,
        "Tag Out (After blockers are declared, you may Prep this die from the Field Zone to give target " +
        "Superstar die +2A and +2D until end of turn.)",
        purchaseCost: 4, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Tag Out")],
        affiliations: ["A New Day"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 7)
        ], set: "TAG");

    // Batman set's Rip Hunter, "Navigate the Sands of Time" printing -
    // previously next-steps item #10: flagged as buildable once its own
    // primitives existed, and by now they do twice over. Not itself an
    // Appendix 1 keyword (see TriggerType.ClearAndDraw), and its "send
    // dice to the Used Pile instead of Out of Play" is exactly Cosmic
    // Cube's RedrawFromBag shape with a different ToZone - nothing new
    // needed there either. "when you draw dice from your bag" (not
    // "you've drawn this turn" like Cosmic Cube's broader wording) -
    // restricted to Zone.DiceFromBag only, not DiceFromPrep.
    public static readonly CardDef RipHunterNavigateTheSandsOfTime = Character(
        "BAT030", "Rip Hunter", "Navigate the Sands of Time", dieLimit: 4,
        "While Rip Hunter is active, once during your Clear and Draw Step, when you draw dice from your " +
        "bag you may send any number of them to the Used Pile and draw one new die for each of them.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        abilities: [new AbilityDef(TriggerType.ClearAndDraw, Cost: null,
            Effect: new RedrawFromBag(
                TargetSpec.AnyDie(
                    "dice drawn from your bag this turn", TargetOwnership.Own, [Zone.DiceFromBag], count: 10,
                    optional: true), // "you may send ANY NUMBER of them" - zero is a legal choice
                Zone.UsedPile))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5)
        ], set: "BAT");

    // Justice League set's Starfire, "Starbolts" printing - purely the
    // Range keyword, no other text. A different printing from the
    // roster's own "No-Nonsense Warrior" Starfire above (different id -
    // real Dice Masters cards reuse character names across printings
    // constantly, same as the three Alfred Pennyworths or four Black
    // Mantas already in this catalog).
    public static readonly CardDef StarfireStarbolts = Character(
        "SKC090", "Starfire", "Starbolts", dieLimit: 4,
        "Range 2 (When this character attacks, all active characters with Range deal damage equal to " +
        "their Range value to target opposing character die.)",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Range", Params: [2])],
        affiliations: ["Teen Titans"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ], set: "SKC");

    // Icons: Tomb of Annihilation's Jamilah, "Shipwrecked on Chult"
    // printing (same set as Drow Mercenary above) - Experience plus
    // Overcrush, both fully mappable, no
    // AbilityDef needed for either (Experience is entirely engine-built,
    // like Deadly/Infiltrate - see DieStats.ForceKO/TurnEngine.CleanUp).
    public static readonly CardDef JamilahShipwreckedOnChult = Character(
        "TIW019", "Jamilah", "Shipwrecked on Chult", dieLimit: 4,
        "Experience (If you KO'd an opposing Monster during your turn, place one Experience Token on " +
        "this character die's card at the end of your turn.) Overcrush (Damage dealt in excess of " +
        "blocker's D is dealt to opponent.)",
        purchaseCost: 4, energyType: EnergyType.Fist,
        keywords: [new KeywordInstance("Experience"), new KeywordInstance("Overcrush")],
        affiliations: ["Neutral", "Equip", "Force Grey"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "TIW");

    // ---- Dark Phoenix Saga (DPS) - working through the set card by card,
    // per the user's own framing. Unlike every card above, DPS's bulk-
    // imported stats (cost/energy/dieLimit/levels/affiliations) are
    // already real (sourced from the reference spreadsheet by the bulk
    // importer, see BulkCards.json) - copied verbatim below, not
    // transcribed from a placeholder. Hand-curating a DPS card is now
    // purely an authoring decision (does the full text map to
    // EffectNode primitives?), never a stats-fixing one.
    //
    // Several DPS cards surfaced real engine gaps rather than one-off
    // unscriptable text - left vanilla and NOT reattempted card-by-card,
    // since each is really a small missing subsystem, not a single-card
    // gap: the Continuous keyword (a Basic Action die that sits in the
    // Field Zone as a repeatable "whenever you could use a Global
    // Ability" activated ability, e.g. DPS002/005/006/010 - a
    // fundamentally different activation shape from the WhenUsed-then-
    // Used-Pile flow every other authored Action die uses); Loyalty
    // Counters (a per-CARD, not per-die, persistent marker with its own
    // "+1A/+1D per counter" payoff - DPS004/006/016/035/041/053/073/079/
    // 124); per-die "can't be targeted"/"can't block" protection statuses
    // (DPS033, ties to next-steps item 3's capturing-adjacent gap);
    // affiliation- or level-restricted TargetSpec filters (DPS034/042,
    // already flagged in the bulk-card-catalog memory); purchase/
    // fielding-cost modifiers (DPS024/040/056, same family as Robin's
    // Energize/Alfred's Ally/The Rock's Sacrifice gaps in next-steps
    // item 1); and "while [a specific other named card] is active"
    // conditional self-buffs/keyword-grants (DPS045 Mystique/DPS048
    // Psylocke both key off "While Wolverine is active" - a real, small
    // recurring pattern, not modeled by GrantsStaticTeamBonus/
    // GrantsToSidekicks, which only grant unconditionally to the whole
    // team/Sidekicks). None built this pass - worth the user's input on
    // priority before investing, since Continuous especially recurs
    // constantly outside DPS too (see the grep sample in DESIGN_LOG.md's
    // "Dark Phoenix Saga, first pass" status update).

    // Storm, "Extreme Weather" - a plain WhenFielded ping, the simplest
    // possible template for this trigger.
    public static readonly CardDef StormExtremeWeather = Character(
        "DPS052", "Storm", "Extreme Weather", dieLimit: 4,
        "When fielded, deal 1 damage to target character die.",
        purchaseCost: 2, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new DealDamage(1, TargetSpec.CharacterDie("target character die")))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Kitty Pryde, "Right of Passage" - Awaken (TriggerType.Awaken) paired
    // with PrepFromBag, the exact mechanic Ricochet's Infiltrate follow-up
    // already uses, just on a different trigger.
    public static readonly CardDef KittyPrydeRightOfPassage = Character(
        "DPS037", "Kitty Pryde", "Right of Passage", dieLimit: 4,
        "Awaken - Prep a die from your bag.",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Awaken")],
        abilities: [new AbilityDef(TriggerType.Awaken, Cost: null, Effect: new PrepFromBag())],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Phoenix, "Firepower" - two independent abilities on one card
    // (WhenFielded + Energize), both plain damage. Energize's target
    // reuses CharacterDieOrPlayer, same union DealDamage already
    // interprets for Attune.
    public static readonly CardDef PhoenixFirepower = Character(
        "DPS046", "Phoenix", "Firepower", dieLimit: 4,
        "When fielded, deal 3 damage to target character die. Energize - Deal 2 damage to target character " +
        "die or player.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new DealDamage(3, TargetSpec.CharacterDie("target character die"))),
            new AbilityDef(TriggerType.Energize, Cost: null,
                Effect: new DealDamage(2, TargetSpec.CharacterDieOrPlayer("target character die or player")))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // D'Ken, "Emperor" - WhenAttacks + PrepDie sourced from the Used Pile
    // rather than Self (PrepDie's Source is just a TargetSpec, so
    // pointing its EligibleZones at UsedPile is all this needs - no new
    // primitive, just a different zone than Shocking Grasp's precedent).
    public static readonly CardDef DKenEmperor = Character(
        "DPS026", "D'Ken", "Emperor", dieLimit: 4,
        "When D'Ken attacks, Prep a die from your Used Pile.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["Villains", "Shi'ar"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new PrepDie(TargetSpec.AnyDie(
                "a die from your Used Pile", TargetOwnership.Own, [Zone.UsedPile])))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ], set: "DPS");

    // Ronan the Accuser, "Treason!" - the card that motivated LoseLife's
    // new Whose parameter: "When fielded, lose 1 life" is the controller
    // (LoseLife's existing default), but "When KO'd, your opponent loses
    // 1 life" needs the other player - every other LoseLife-using card
    // so far only ever meant the ability's own controller, so the node
    // had no way to say otherwise until now.
    public static readonly CardDef RonanTheAccuserTreason = Character(
        "DPS050", "Ronan the Accuser", "Treason!", dieLimit: 4,
        "When Ronan the Accuser is fielded, lose 1 life. When Ronan the Accuser is KO'd, your opponent loses 1 life.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new LoseLife(1)),
            new AbilityDef(TriggerType.WhenKOd, Cost: null, Effect: new LoseLife(1, TargetOwnership.Opposing))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 7),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Power Bolt - a Basic Action with no trigger phrase at all, i.e. its
    // effect just runs when the die is used (rule 2.6.4 - same WhenUsed
    // shape Casket of Ancient Winters already established), no Cost/
    // Sequence needed since it's a single primitive.
    public static readonly CardDef PowerBolt = BasicAction(
        "DPS011", "Power Bolt", "Deal 2 damage to target character die or player.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null,
            Effect: new DealDamage(2, TargetSpec.CharacterDieOrPlayer("target character die or player")))],
        purchaseCost: 3, set: "DPS");

    // Lab Test - the first Continuous Basic Action authored (see
    // TurnEngine.UseActionDie/ResolveContinuousDie and TriggerType.
    // ContinuousResolve's own remarks for the new lifecycle this needed).
    // "Send this die to your Used Pile to reroll..." maps to just Reroll -
    // the move to the Used Pile is ResolveContinuousDie's own job, not
    // something this card's own Effect tree has to say (every currently-
    // authored Continuous card's text bundles them the same way). Also
    // the card that motivated actually implementing Reroll's interpreter
    // case (previously a stub - see EffectInterpreter's remarks) since no
    // prior card exercised it.
    public static readonly CardDef LabTest = BasicAction(
        "DPS005", "Lab Test",
        "Continuous: You may send this die to your Used Pile to reroll one of the character dice in your Reserve Pool.",
        keywords: [new KeywordInstance("Continuous")],
        abilities: [new AbilityDef(TriggerType.ContinuousResolve, Cost: null,
            Effect: new Reroll(TargetSpec.CharacterDie(
                "a character die in your Reserve Pool", TargetOwnership.Own, zones: [Zone.ReservePool])))],
        purchaseCost: 2, set: "DPS");

    // Rally - the first burst-conditional Basic Action (see
    // EffectCondition.OnDoubleBurstFace/DieInstance.BurstStars's own
    // remarks for the plumbing this needed): "Move up to 2 Sidekick
    // dice... ** Instead, move up to 3." Checked against Rally's OWN die
    // (TargetSpec.Self, its current action face) via Conditional.Else -
    // Then handles the double-burst face, Else the ordinary one. "Up to
    // N" is TargetSpec's `optional: true` (a real 0-to-N voluntary
    // choice, not "as many as available, capped").
    public static readonly CardDef Rally = BasicAction(
        "DPS013", "Rally",
        "Move up to 2 Sidekick dice from your Used Pile to your Field Zone. ** Instead, move up to 3 " +
        "Sidekicks instead.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.OnDoubleBurstFace,
                Then: new MoveDie(
                    TargetSpec.Sidekick("up to 3 Sidekick dice from your Used Pile", TargetOwnership.Own,
                        count: 3, zones: [Zone.UsedPile], optional: true),
                    Zone.FieldZone),
                Else: new MoveDie(
                    TargetSpec.Sidekick("up to 2 Sidekick dice from your Used Pile", TargetOwnership.Own,
                        count: 2, zones: [Zone.UsedPile], optional: true),
                    Zone.FieldZone)))],
        purchaseCost: 3, set: "DPS");

    // Jean Grey, "Peaceful Coexistence" - the first Loyalty card (see
    // GameState.LoyaltyCounters/DieStats.LoyaltyBonus and TriggerType.
    // EndOfYourTurn's own remarks for the new plumbing this needed).
    // "Founder" prefixing the raw text isn't a real structured field
    // anywhere in the source sheet (same non-modeled flavor-label
    // treatment as Darkseid's own remarks describe for similar cases) -
    // nothing else on this card references it, so it's not worth
    // inventing a mechanism for here.
    public static readonly CardDef JeanGreyPeacefulCoexistence = Character(
        "DPS035", "Jean Grey", "Peaceful Coexistence", dieLimit: 4,
        "Founder While Jean Grey is active, at the end of each or your turns, if no character dice were " +
        "KO'd that turn, put a Loyalty Counter on Jean Grey's card (Loyalty Counters give a character die " +
        "+1A and +1D.)",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.EndOfYourTurn, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.NoCharacterKOdThisTurn, new GrantLoyaltyCounter()))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 6)
        ], set: "DPS");

    // Magneto, "Idealist" - the first TriggerType.WhenAnotherDieKOd card
    // (see KOdDieMatch's own remarks): "one of YOUR Mask character dice"
    // is Ownership.Own + RequiredEnergyType.Mask, no other filter needed.
    // The Global's "if you have no dice in your Prep Area" reuses the
    // same Conditional/TargetSpec.Self shape Jean Grey's own condition
    // does, just against EffectCondition.PrepAreaEmpty instead. "Once per
    // turn, during your turn" - OncePerTurn=true covers the once-per-turn
    // half; the whose-turn-scoping half is the same known engine gap
    // Falcon's own "Once during your turn" already has (see next-steps
    // item 6), not a new one.
    public static readonly CardDef Magneto = Character(
        "DPS041", "Magneto", "Idealist", dieLimit: 4,
        "When one of your Mask character dice is KO'd, put a Loyalty Counter on Magneto's card. (Loyaly " +
        "Counters give a character die +1A and +1D). Global: Pay Mask. Once per turn, during your turn, " +
        "if you have no dice in your Prep Area, you may draw a die and place it in your Prep Area.",
        purchaseCost: 6, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants"],
        abilities: [
            new AbilityDef(TriggerType.WhenAnotherDieKOd, Cost: null, Effect: new GrantLoyaltyCounter(),
                KOdFilter: new KOdDieMatch(TargetOwnership.Own, RequiredEnergyType: EnergyType.Mask)),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Conditional(TargetSpec.Self, EffectCondition.PrepAreaEmpty, new PrepFromBag()),
                EnergyCost: new EnergyCost(1, EnergyType.Mask), OncePerTurn: true)
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 8)
        ], set: "DPS");

    // Supreme Intelligence, "Kree Science Council" - purely a Loyalty
    // grant, no other text. NameContains is a real substring match here
    // ("a card with Kree in its name"), unlike Gladiator's "When Lilandra
    // is KO'd" (not authored - see below), which just happens to reuse
    // the same field for an exact reference.
    public static readonly CardDef SupremeIntelligence = Character(
        "DPS053", "Supreme Intelligence", "Kree Science Council", dieLimit: 4,
        "When a card with Kree in its name is KO'd, put a Loyalty Counter on Supreme Intelligence's card. " +
        "(Loyaly Counters give a character die +1A and +1D).",
        purchaseCost: 6, energyType: EnergyType.Mask,
        abilities: [new AbilityDef(TriggerType.WhenAnotherDieKOd, Cost: null, Effect: new GrantLoyaltyCounter(),
            KOdFilter: new KOdDieMatch(NameContains: "Kree"))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 6)
        ], set: "DPS");

    // Madelyne Pryor, "Sisterhood" - the ExcludeSelf case: "besides
    // Madelyne Pryor" means her own death doesn't grant her own card a
    // counter (which, being posthumous, wouldn't even matter for stats,
    // but the filter should still say what the card says).
    public static readonly CardDef MadelynePryorSisterhood = Character(
        "DPS079", "Madelyne Pryor", "Sisterhood", dieLimit: 3,
        "When one of your Brotherhood of Mutants character dice is KO'd besides Madelyne Pryor, put a " +
        "Loyalty Counter on her card. (Loyaly Counters give a character die +1A and +1D).",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants"],
        abilities: [new AbilityDef(TriggerType.WhenAnotherDieKOd, Cost: null, Effect: new GrantLoyaltyCounter(),
            KOdFilter: new KOdDieMatch(TargetOwnership.Own, AffiliationContains: "Brotherhood of Mutants", ExcludeSelf: true))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4)
        ], set: "DPS");

    // Angel, "Wings Over the World" - a plain Energize + ModifyStat,
    // straightforward now that Energize's own keyword-gating bug (see
    // Kitty Pryde/Phoenix's remarks in EffectInterpreterTests) is known
    // to matter and checked for.
    public static readonly CardDef AngelWingsOverTheWorld = Character(
        "DPS017", "Angel", "Wings Over the World", dieLimit: 4,
        "Energize - Target Sidekick gets +2A this turn.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new ModifyStat(TargetSpec.Sidekick("target Sidekick"), AttackDelta: 2, DefenseDelta: null))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Cable, "I'll Do This All Day" - Energize + Reroll, the second real
    // card exercising Reroll (Lab Test's Continuous resolution was the
    // first) and the first via a normal trigger rather than an
    // activated ability.
    public static readonly CardDef CableIllDoThisAllDay = Character(
        "DPS022", "Cable", "I'll Do This All Day", dieLimit: 4,
        "Energize - Reroll one of your character dice.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Reroll(TargetSpec.CharacterDie("one of your character dice", TargetOwnership.Own)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ], set: "DPS");

    // Colossus's own Energize target - defined once and reused by BOTH
    // the FieldDie and Spin clauses below, deliberately NOT two separate
    // `TargetSpec.CharacterDie(...)` call expressions - EffectInterpreter
    // resolves and caches by TargetSpec structural equality, and two
    // array-literal EligibleZones (`zones: [Zone.ReservePool]` written
    // twice) would NOT share a cache entry, which could let the Spin
    // clause pick a DIFFERENT die than the one FieldDie just moved (see
    // EffectInterpreter's own class-level remarks on this exact gotcha).
    public static readonly TargetSpec ColossusEnergizeTarget =
        TargetSpec.CharacterDie("one of your character dice", TargetOwnership.Own, zones: [Zone.ReservePool]);

    // Colossus, "Skilled Painter" - "field one of your character dice for
    // free and spin it to level 3" needs no new primitive at all: FieldDie
    // always fields at level 1 (rule 2.6.3 note), so Spin(+2) on that same
    // target reaches level 3 exactly, composed as a Sequence. Overcrush
    // is the separate, already engine-native keyword.
    public static readonly CardDef ColossusSkilledPainter = Character(
        "DPS023", "Colossus", "Skilled Painter", dieLimit: 4,
        "Energize - Field one of your character dice for free and spin it to level 3.Overcrush",
        purchaseCost: 5, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize"), new KeywordInstance("Overcrush")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Sequence([new FieldDie(ColossusEnergizeTarget, Free: true), new Spin(ColossusEnergizeTarget, +2)]))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 5, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 7, BurstStars: 1)
        ], set: "DPS");

    // Toad, "Secondary Mutation" - Awaken + Teamwatch, both already
    // engine-native trigger points (Teamwatch previously only exercised
    // by Falcon/Black Panther); each needs its own Keywords entry (see
    // TurnEngine.Field's Teamwatch scan and CheckAwaken, both of which
    // gate on DieStats.HasKeyword, not just an authored AbilityDef).
    public static readonly CardDef ToadSecondaryMutation = Character(
        "DPS054", "Toad", "Secondary Mutation", dieLimit: 4,
        "Awaken: Deal 2 damage to target character die. Teamwatch - Spin Toad up 1 level.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Awaken"), new KeywordInstance("Teamwatch")],
        abilities: [
            new AbilityDef(TriggerType.Awaken, Cost: null,
                Effect: new DealDamage(2, TargetSpec.CharacterDie("target character die"))),
            new AbilityDef(TriggerType.Teamwatch, Cost: null, Effect: new Spin(TargetSpec.Self, +1))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 1, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Lilandra, "Politician" - Starfire's own PrepFromBagIfPurchasedThisTurn,
    // just narrowed to CharacterOnly (Player.PurchasedCharacterDieThisTurn,
    // set alongside PurchasedDieThisTurn in TurnEngine.Purchase).
    public static readonly CardDef LilandraPolitician = Character(
        "DPS038", "Lilandra", "Politician", dieLimit: 4,
        "Global: Pay Shield. Once per turn, if you have purchased a character die this turn, you may draw " +
        "a die from your bag and add it to your Prep Area.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["Shi'ar"],
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new PrepFromBagIfPurchasedThisTurn(CharacterOnly: true),
            EnergyCost: new EnergyCost(1, EnergyType.Shield), OncePerTurn: true)],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Vulcan, "Ruler of The Imperium" - the first "must attack" card,
    // needing a new GameState.MustAttackThisTurn/ForceAttack pair -
    // Declare Attackers' own side of Invisible Woman's ForceBlock/
    // MustBlockThisTurn (see CombatEngine.DeclareAttackers and
    // TurnEngine.SkipAttackStep, which also had to learn to reject
    // skipping the Attack Step outright while an obligation is
    // outstanding, not just enforce it once the step is entered).
    public static readonly CardDef VulcanRulerOfTheImperium = Character(
        "DPS055", "Vulcan", "Ruler of The Imperium", dieLimit: 4,
        "Global: Pay Fist. Target character die must attack this turn.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new ForceAttack(TargetSpec.CharacterDie("target character die")),
            EnergyCost: new EnergyCost(1, EnergyType.Fist))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 5)
        ], set: "DPS");

    // Psylocke, "Adventurer" - the first conditional self keyword grant
    // (CardDef.GrantsSelfKeywordWhileNamedCardActive/DieStats.
    // HasConditionalSelfGrant): "gains Deadly while Wolverine is active"
    // is a live, continuously-recomputed check against any active die
    // named Wolverine (any printing, either controller - the text has no
    // "your"), not a discrete triggered effect, so it's a CardDef-level
    // grant like GrantsToSidekicks/GrantsStaticTeamBonus rather than an
    // AbilityDef. "When fielded, spin target character die up 1 level" is
    // the separate, ordinary half. Mystique's own "+2A while Wolverine is
    // active" is the same condition but a stat bonus, not built - her
    // Global needs unrelated, bigger new work (an affiliation-vs-team-
    // roster check plus a "can't block" mechanism) that would block her
    // regardless, so the shared condition alone wasn't worth splitting out
    // into its own reusable piece yet.
    public static readonly CardDef PsylockeAdventurer = Character(
        "DPS048", "Psylocke", "Adventurer", dieLimit: 4,
        "While Wolverine is active, Psylocke gains Deadly. When fielded, spin target character die up 1 level.",
        purchaseCost: 2, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        grantsSelfKeywordWhileNamedCardActive: new ConditionalSelfKeywordGrant("Wolverine", "Deadly"),
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Spin(TargetSpec.CharacterDie("target character die"), +1))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Blob, "MGH Dependent" - two independent WhenFielded effects on one
    // trigger: the card's own "lose 1 life" plus Intimidate's own built-in
    // "remove target opposing character die" (same MoveDie-to-Intimidated
    // shape Scarlet Spider's own Intimidate printing uses).
    public static readonly CardDef BlobMGHDependent = Character(
        "DPS061", "Blob", "MGH Dependent", dieLimit: 3,
        "When fielded, lose 1 life. Intimidate.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Intimidate")],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new LoseLife(1)),
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new MoveDie(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Zone.Intimidated))
        ],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 8)
        ], set: "DPS");

    // Supreme Intelligence, "Psionic Collective" - a different printing
    // from "Kree Science Council" (DPS053) above, purely two keywords:
    // Overcrush (engine-native, no AbilityDef) and Intimidate (needs its
    // own WhenFielded AbilityDef, same as Blob/Scarlet Spider above) -
    // "Intimidate Overcrush" together isn't a bare single keyword, so the
    // bulk importer's own pure-keyword auto-detection doesn't catch it.
    public static readonly CardDef SupremeIntelligencePsionicCollective = Character(
        "DPS093", "Supreme Intelligence", "Psionic Collective", dieLimit: 3,
        "Intimidate Overcrush",
        purchaseCost: 7, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Intimidate"), new KeywordInstance("Overcrush")],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new MoveDie(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Zone.Intimidated))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 6)
        ], set: "DPS");

    // Toad, "Looking for Comradery" - a different printing from
    // "Secondary Mutation" (DPS054) above. "Spin ... to level 1" needs no
    // new primitive: DieStats.SpinLevel clamps to [1, maxLevel]
    // regardless of how negative the delta is, and every Character card
    // in this game has exactly 3 levels (rule 1.3.5's fixed structure),
    // so any delta of -2 or further negative always lands on exactly
    // level 1 from any starting level - same trick as Colossus's own
    // Spin(+2) reaching exactly level 3 from a guaranteed level-1 start.
    public static readonly CardDef ToadLookingForComradery = Character(
        "DPS094", "Toad", "Looking for Comradery", dieLimit: 3,
        "Energize - You may spin one of the Character dice in your reserve pool from a character face to level 1.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Spin(TargetSpec.CharacterDie(
                "one of the character dice in your reserve pool", TargetOwnership.Own, zones: [Zone.ReservePool]), -2))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 1, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // A team is 8 character cards + 2 Basic Action cards (10 total). Both
    // rosters below are drawn exclusively from IsImplemented: true cards
    // (see CardDef.IsImplemented) - the 16 cards with a deliberately
    // dropped clause (BigBarda, Robin, CorvusGlaive, Distraction,
    // GoddessOfThunder, InvisibleWoman, JaneFoster, Starfire "No-Nonsense
    // Warrior", Kang, KingHyperion, DailyBugle, Escape!, all three Alfred
    // Pennyworth printings, and The Rock) stay declared above (and in
    // BuildCatalog's full card list, so /api/cards still returns them)
    // but aren't on either roster, same as every other off-roster real
    // card - not deleted, since a future team-builder feature would want
    // a catalog bigger than exactly two 10-card teams. Deliberately
    // includes one live example of each keyword the web client's Attack
    // Step UI now needs to exercise: Call Out (Black Widow), Infiltrate
    // (Ricochet, also exercises its own WhenInfiltrates reactor), Tag
    // Out (Big E), Range (Starfire "Starbolts"), and Intimidate (Scarlet
    // Spider) - plus Dazzler/God Emperor Doom for extra WhenFielded-
    // targeting coverage.
    public static readonly IReadOnlyList<string> TeamACharacterIds =
    [
        Apocalypse.Id, Beast.Id, BlackPanther.Id, HarleyQuinn.Id,
        CaptainMarvel.Id, Dazzler.Id, BlackWidow.Id, AntManAmplify.Id
    ];

    public static readonly IReadOnlyList<string> TeamABasicActionIds =
        [CosmicCube.Id, ShockingGrasp.Id];

    public static readonly IReadOnlyList<string> TeamBCharacterIds =
    [
        Falcon.Id, FranklinsGalactus.Id, GodEmperorDoom.Id, Groot.Id,
        Ricochet.Id, BigE.Id, StarfireStarbolts.Id, ScarletSpider.Id
    ];

    public static readonly IReadOnlyList<string> TeamBBasicActionIds =
        [CasketOfAncientWinters.Id, CosmicCubeInfinitePossibilities.Id];

    public static IReadOnlyDictionary<string, CardDef> BuildCatalog()
    {
        CardDef[] all =
        [
            BigBarda, Apocalypse, Beast, BlackPanther, HarleyQuinn, Robin, CaptainMarvel,
            Colossus, CorvusGlaive, Dazzler, CosmicCube, ShockingGrasp, ShockingGraspFus, ShockingGraspTiw, Distraction,
            Falcon, FranklinsGalactus, GodEmperorDoom, GoddessOfThunder, Groot, InvisibleWoman,
            JaneFoster, Starfire, Kang, KingHyperion, CasketOfAncientWinters, DailyBugle, Escape,
            AlfredPennyworthCaretaker, AlfredPennyworthMI5, AlfredPennyworthToughAsNails,
            AntManAmplify, Cyclops, Wasp, BlackWidow, Polaris, CosmicCubeInfinitePossibilities, Parademon, Darkseid,
            Deathbird, WaspPixie, MadalynePryor, TheSpot, Ricochet, ScarletSpider, DrowMercenary,
            SupermanKalEl, BlackMantaDeepSeaDeviant, BizarroMoreThanAMonster,
            SpideysLastStand, TheRock, BigE, RipHunterNavigateTheSandsOfTime, StarfireStarbolts,
            JamilahShipwreckedOnChult,
            StormExtremeWeather, KittyPrydeRightOfPassage, PhoenixFirepower, DKenEmperor,
            RonanTheAccuserTreason, PowerBolt, LabTest, JeanGreyPeacefulCoexistence,
            Magneto, SupremeIntelligence, MadelynePryorSisterhood,
            AngelWingsOverTheWorld, CableIllDoThisAllDay, ColossusSkilledPainter, ToadSecondaryMutation,
            LilandraPolitician, VulcanRulerOfTheImperium, PsylockeAdventurer,
            BlobMGHDependent, SupremeIntelligencePsionicCollective, ToadLookingForComradery, Rally
        ];

        // Hand-curated cards win on id collision - shouldn't happen in
        // practice (the bulk import script already excludes every id
        // in this file), but hand-curated data is authoritative either
        // way, since it has real engine behavior the bulk import can't
        // match. See BulkCardCatalog's own remarks for what "bulk"
        // means here (browsable data only, no AbilityDefs).
        var catalog = all.ToDictionary(c => c.Id);
        foreach (var bulkCard in BulkCardCatalog.Load())
        {
            catalog.TryAdd(bulkCard.Id, bulkCard);
        }
        return catalog;
    }
}
