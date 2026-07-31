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
        bool isImplemented = true) => new()
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
        IsImplemented = isImplemented
    };

    private static CardDef BasicAction(
        string id, string name, string rawText, bool epic = false,
        IReadOnlyList<AbilityDef>? abilities = null,
        bool isImplemented = true) => new()
    {
        Id = id,
        Name = name,
        Subtitle = epic ? "Epic Basic Action" : "Basic Action",
        Type = epic ? CardType.EpicBasicAction : CardType.BasicAction,
        PurchaseCost = epic ? 4 : 2, // placeholder, see class remarks
        EnergyTypes = [], // rule 1.2.4/1.3.10 - Basic Actions have no energy type
        DieLimit = 3, // rule 1.2.11 - fixed for every Basic Action card
        RawText = rawText,
        Abilities = abilities ?? [],
        IsImplemented = isImplemented
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
        "big-barda", "Big Barda", "Formerly of Apokolips", dieLimit: 4,
        "Ignore all non-combat damage dealt to Big Barda.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ],
        isImplemented: false);

    public static readonly CardDef Apocalypse = Character(
        "apocalypse", "Apocalypse", "Obsessive", dieLimit: 4,
        "Overcrush (Character dice with Overcrush deal damage in excess of blocker's defense to opponent.)",
        keywords: [new KeywordInstance("Overcrush")]);

    public static readonly CardDef Beast = Character(
        "beast", "Beast", "Olympic Athleticism", dieLimit: 3,
        "Regenerate (Reroll when KO'd)",
        keywords: [new KeywordInstance("Regenerate")]);

    public static readonly CardDef BlackPanther = Character(
        "black-panther", "Black Panther", "Clutching Reality", dieLimit: 4,
        "Energize - Roll 2 dice from your bag. When fielded, roll a die from your bag.",
        keywords: [new KeywordInstance("Energize")],
        abilities: [
            new AbilityDef(TriggerType.Energize, Cost: null, Effect: new DrawDice(2)),
            new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new DrawDice(1)),
        ],
        affiliations: ["Avengers", "Infinity Watch"]);

    public static readonly CardDef HarleyQuinn = Character(
        "harley-quinn", "Harley Quinn", "Bright Lights Big City", dieLimit: 4,
        "", // real card, genuinely blank text box
        purchaseCost: 1, energyType: EnergyType.Mask,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4)
        ]);

    // A purchase-cost discount - no purchase-cost-modifier mechanism
    // exists yet (see RULES_ENGINE_DESIGN.md) - left vanilla,
    // isImplemented: false.
    public static readonly CardDef Robin = Character(
        "robin", "Robin", "Team Leader", dieLimit: 4,
        "Energize - The first Teen Titans die you purchase this turn costs 1 less (to a minimum of 1).",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Energize")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ],
        isImplemented: false);

    // A live, continuously-recomputed Static team-wide bonus (rule
    // 3.4.5.7) - see CardDef.GrantsStaticTeamBonus/DieStats.
    // StaticTeamBonusFor. No AbilityDef needed, same shape as Strike's
    // "no trigger at all" design.
    public static readonly CardDef CaptainMarvel = Character(
        "captain-marvel", "Captain Marvel", "Alpha Flight", dieLimit: 4,
        "While Captain Marvel is active, your Character dice get +1 attack and +1 defense.",
        purchaseCost: 4,
        grantsStaticTeamBonus: new StaticTeamBonus(AttackDelta: 1, DefenseDelta: 1));

    public static readonly CardDef Colossus = Character(
        "colossus", "Colossus", "Inferno", dieLimit: 4, ""); // real card, genuinely blank text box

    // The KO half is buildable (Ko node) but the "next purchase costs 2
    // less" half needs the same purchase-cost-modifier mechanism Robin's
    // Energize is missing - left vanilla rather than half-scripted,
    // isImplemented: false.
    public static readonly CardDef CorvusGlaive = Character(
        "corvus-glaive", "Corvus Glaive", "The Black Order", dieLimit: 3,
        "When fielded, KO a character die you control. If you do, the next die you purchase this turn costs [2] less (minimum 1).",
        isImplemented: false);

    public static readonly CardDef Dazzler = Character(
        "dazzler", "Dazzler", "Lightbringer", dieLimit: 4,
        "When fielded, deal 4 damage to target [M] character die.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new DealDamage(4, TargetSpec.CharacterDie("target [M] character die", energyType: EnergyType.Mask)))]);

    public static readonly CardDef CosmicCube = BasicAction(
        "cosmic-cube", "Cosmic Cube", "Switch life totals with your opponent.", epic: true,
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new SwapLife())]);

    public static readonly CardDef ShockingGrasp = BasicAction(
        "shocking-grasp", "Shocking Grasp",
        "Deal 1 damage to target character die. If that character is KO'd by this damage, you may Prep this die.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new DealDamage(1, TargetSpec.CharacterDie("target character die")),
            new Conditional(TargetSpec.CharacterDie("target character die"), EffectCondition.TargetWasKOd,
                new PrepDie(TargetSpec.Self))
        ]))]);

    // Distraction's Non-global ability ("target opponent chooses two...
    // cannot block") is left unscripted (multi-die opponent choice + a
    // persistent "cannot block" flag we don't model) but its separate
    // Global ability maps cleanly on its own - Non-global and Global are
    // genuinely independent ability slots (rule 3.1.3), scored separately.
    // isImplemented is still false, though - it's a whole-card flag (the
    // Non-global half is real, missing behavior, not just flavor text).
    public static readonly CardDef Distraction = BasicAction(
        "distraction", "Distraction",
        "Target opponent chooses two of their character dice. They cannot block this turn. " +
        "Global: Pay [M]. Remove target attacking character die from combat.",
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new MoveDie(
                TargetSpec.CharacterDie("target attacking character die", zones: [Zone.AttackZone]),
                Zone.FieldZone),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Mask))],
        isImplemented: false);

    // ---- Team B: 10 characters + 3 Basic Actions ----

    // Teamwatch and Global are independent ability slots (same shape as
    // Distraction above) - both now scripted. Real affiliation "Avengers"
    // (MSW027), shared with Black Panther's "Clutching Reality" printing.
    public static readonly CardDef Falcon = Character(
        "falcon", "Falcon", "Take Flight", dieLimit: 4,
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
        affiliations: ["Avengers"]);

    public static readonly CardDef FranklinsGalactus = Character(
        "franklins-galactus", "Franklin's Galactus", "Earth Shatterer", dieLimit: 4, ""); // genuinely blank

    public static readonly CardDef GodEmperorDoom = Character(
        "god-emperor-doom", "God Emperor Doom", "Harnessing the Beyonders", dieLimit: 4,
        "When fielded, deal 3 damage to target character die and reroll target character die.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new Sequence([
            new DealDamage(3, TargetSpec.CharacterDie("target character die")),
            new Reroll(TargetSpec.CharacterDie("target character die"))
        ]))]);

    // "Another active character die with Thor in the name or subtitle" -
    // no name/subtitle-substring TargetSpec/Static-bonus filter exists
    // (GrantsStaticTeamBonus only keys off CardId, not a text match) -
    // left vanilla, isImplemented: false.
    public static readonly CardDef GoddessOfThunder = Character(
        "goddess-of-thunder", "Goddess of Thunder", "Thor Corps", dieLimit: 2,
        "Goddess of Thunder gets +5 attack while you have another active character die with Thor in the name or subtitle.",
        isImplemented: false);

    public static readonly CardDef Groot = Character(
        "groot", "Groot", "Skilled Investigator", dieLimit: 4,
        "When fielded, roll 2 dice from your bag.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new DrawDice(2))]);

    // The static "+1 attack for each other active [F4]..." clause is left
    // unscripted (no "count active dice matching X" stat-modifier
    // primitive exists yet); its Global stands on its own, same
    // independent-ability-slots reasoning as Distraction/Falcon above.
    // isImplemented is still false - same reasoning as Distraction.
    public static readonly CardDef InvisibleWoman = Character(
        "invisible-woman", "Invisible Woman", "Also Dr. Richards", dieLimit: 4,
        "Invisible Woman gets +1 attack for each of your other active [F4] character dice. " +
        "Global: Pay [M]. Target character die must block this turn.",
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new ForceBlock(TargetSpec.CharacterDie("target character die")),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Mask))],
        isImplemented: false);

    // "Gain an extra 2 life for each of your other active characters
    // with Thor in their name or subtitle, or the [TCS] affiliation" -
    // same missing name-substring-match primitive as Goddess of Thunder
    // above (the affiliation half alone would be buildable, but the
    // "or" makes the whole clause one unit) - left vanilla,
    // isImplemented: false.
    public static readonly CardDef JaneFoster = Character(
        "jane-foster", "Jane Foster", "Doctor", dieLimit: 4,
        "When fielded, gain 2 life, and gain an extra 2 life for each of your other active characters " +
        "with Thor in their name or subtitle, or the [TCS] affiliation.",
        isImplemented: false);

    // "Recruit" (bring in an off-team Teen Titans die) is left unscripted -
    // no off-team-recruitment mechanic exists yet; its Global stands on
    // its own, same independent-ability-slots reasoning as above.
    // isImplemented is still false - same reasoning as Distraction.
    public static readonly CardDef Starfire = Character(
        "starfire", "Starfire", "No-Nonsense Warrior", dieLimit: 4,
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
        isImplemented: false);

    // No "pay life to reroll" cost/effect combination is built -
    // isImplemented: false.
    public static readonly CardDef Kang = Character(
        "kang", "Kang", "Prophetic Revelation", dieLimit: 3,
        "While Kang is active, once per turn, a player may pay 2 life to reroll a die in their Reserve Pool.",
        isImplemented: false);

    // A reactive "while active, when an OPPONENT uses an Action die"
    // trigger - the engine's own Attune/Amplify/Obscure precedent only
    // reacts to the controller's *own* Action-die use, not the
    // opponent's - left vanilla, isImplemented: false.
    public static readonly CardDef KingHyperion = Character(
        "king-hyperion", "King Hyperion", "Earth-21195", dieLimit: 4,
        "While King Hyperion is active, when an opponent uses an action die, deal 2 damage to target character die.",
        isImplemented: false);

    public static readonly CardDef CasketOfAncientWinters = BasicAction(
        "casket-of-ancient-winters", "Casket of Ancient Winters",
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
        ]))]);

    // "Prep up to 2 of them, roll the remainder" - a per-die player
    // choice DrawDice doesn't expose (it either draws-unrolled or the
    // caller externally rolls all of it) - left vanilla, isImplemented: false.
    public static readonly CardDef DailyBugle = BasicAction(
        "daily-bugle", "Daily Bugle", "Draw 2 dice. Prep up to 2 of them, roll the remainder.",
        isImplemented: false);

    // "Choose one" between two unrelated effects, one of which
    // ("can't be targeted this turn") needs a per-die targeting
    // restriction flag that doesn't exist - left vanilla, isImplemented: false.
    public static readonly CardDef Escape = BasicAction(
        "escape", "Escape!",
        "Choose one: Target character die can't be targeted this turn. Or: Prep a die from your Used Pile.",
        isImplemented: false);

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
        "alfred-pennyworth-caretaker", "Alfred Pennyworth", "Caretaker of Wayne Manor", dieLimit: 4,
        "Ally - When fielded, give target Batman character die or another target Sidekick +2 defense until end of turn.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Ally")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2)
        ],
        isImplemented: false);

    public static readonly CardDef AlfredPennyworthMI5 = Character(
        "alfred-pennyworth-mi5", "Alfred Pennyworth", "MI-5", dieLimit: 3,
        "Ally - When KO'd, you may roll a Sidekick or Batman die from your Used Pile. If you roll an energy " +
        "result, return Alfred to the Field Zone at level 1. Either way, return the rolled die to the Used Pile.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Ally")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2)
        ],
        isImplemented: false);

    public static readonly CardDef AlfredPennyworthToughAsNails = Character(
        "alfred-pennyworth-tough-as-nails", "Alfred Pennyworth", "Tough as Nails", dieLimit: 2,
        "Ally - When fielded, give target Batman die or target Sidekick +1 attack and +1 defense " +
        "(besides Alfred Pennyworth) while attacking this turn.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Ally")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2)
        ],
        isImplemented: false);

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
        "ant-man-amplify", "Ant-Man", "Through The Cracks", dieLimit: 4,
        "Amplify - When you use an action die, spin this character up 1 level.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        keywords: [new KeywordInstance("Amplify")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2)
        ]);

    // X-Men First Class's Cyclops, "Boy Scout" printing - a single-clause
    // Awaken effect that maps cleanly onto DealDamage, unlike most of the
    // set's Awaken text (which leans on mechanics like Unblockable/
    // Capture this engine doesn't have yet).
    public static readonly CardDef Cyclops = Character(
        "cyclops", "Cyclops", "Boy Scout", dieLimit: 4,
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
        ]);

    // Avengers Infinity's Wasp, "Flitting About" printing - picked because
    // her card layers a genuine second clause on top of the Attune
    // keyword's own built-in damage (see TurnEngine.UseActionDie's
    // AttuneDamage): "When you use Attune, Wasp gets +1A and +1D until
    // end of turn," the first sample card to actually exercise ModifyStat.
    public static readonly CardDef Wasp = Character(
        "wasp", "Wasp", "Flitting About", dieLimit: 4,
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
        ]);

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
        "black-widow", "Black Widow", "Red Scare", dieLimit: 4,
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
        ]);

    // Dark X-Men's Polaris, "Lorna Dane" printing - the simplest of a
    // handful of that set's Corrupt 2 cards (Rogue/Sage/Sunspot/
    // Thunderbird all read almost identically), picked for a plain
    // WhenFielded trigger. See EffectNode.Corrupt's remarks for the
    // keyword's own mechanics.
    public static readonly CardDef Polaris = Character(
        "polaris", "Polaris", "Lorna Dane", dieLimit: 4,
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
        ]);

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
        "cosmic-cube-infinite-possibilities", "Cosmic Cube",
        "During your Clear and Draw Step, when you draw this die from your bag, you may send it and any " +
        "other dice you've drawn this turn Out of Play. For each die sent Out of Play, draw a die.",
        abilities: [new AbilityDef(TriggerType.WhenDrawn, Cost: null,
            Effect: new RedrawFromBag(
                TargetSpec.AnyDie(
                    "dice drawn this turn", TargetOwnership.Own, [Zone.DiceFromBag, Zone.DiceFromPrep], count: 10,
                    optional: true), // "you may send ANY NUMBER of them" - zero is a legal choice
                Zone.OutOfPlay))]);

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
        "parademon", "Parademon", "Servant of Apokalips", dieLimit: 4,
        "Swarm",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Swarm")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)
        ]);

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
        "darkseid", "Darkseid", "Force of Entropy", dieLimit: 1, // Super Rare
        "While Darkseid is active, your Sidekicks gain Swarm.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        grantsToSidekicks: ["Swarm"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 3, Attack: 7, Defense: 7)
        ]);

    // Dark Phoenix Saga's Deathbird, "Treacherous" printing - purely the
    // Deadly keyword, no other text. Fully engine-level, like Overcrush/
    // Amplify/Attune/Swarm: no target or choice at all - see
    // CombatEngine.RecordDeadlyEngagements/TurnEngine.CleanUp for the
    // actual mechanics ("engaged" is recorded at Declare Blockers, KO'd
    // later at Clean Up, regardless of what happens to either die in
    // between - see the design doc for the full reasoning).
    public static readonly CardDef Deathbird = Character(
        "deathbird", "Deathbird", "Treacherous", dieLimit: 4,
        "Deadly",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Deadly")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ]);

    // Civil War's Wasp, "Pixie" printing - purely the Fast keyword, no
    // other text. Fully engine-level like Overcrush/Deadly/Swarm: no
    // target or choice, just a two-wave damage resolution baked into
    // CombatEngine.AssignCombatDamage/ResolveFastOrSlowDamage.
    public static readonly CardDef WaspPixie = Character(
        "wasp-pixie", "Wasp", "Pixie", dieLimit: 4,
        "Fast",
        purchaseCost: 3, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Fast")],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ]);

    // X-Men Forever's Madalyne Pryor, "Red Queen" printing - purely the
    // Energy Drain keyword (base X=1), no other text. Fully engine-level
    // like Deadly/Overcrush: no target or choice - see
    // CombatEngine.ResolveEnergyDrain/DieStats.EnergyDrainAmount.
    public static readonly CardDef MadalynePryor = Character(
        "madalyne-pryor", "Madalyne Pryor", "Red Queen", dieLimit: 4,
        "Energy Drain (Spin engaged character dice down 1 level.)",
        purchaseCost: 2, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Energy Drain")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4)
        ]);

    // Guardians of the Galaxy's The Spot, "Dr. Johnathan Ohnn" printing -
    // purely the Infiltrate keyword, no other text. Fully engine-level:
    // no AbilityDef needed for the base keyword itself (the choice and
    // effect are baked into CombatEngine.ResolveInfiltrate), matching
    // Deadly/Overcrush precedent.
    public static readonly CardDef TheSpot = Character(
        "the-spot", "The Spot", "Dr. Johnathan Ohnn", dieLimit: 4,
        "Infiltrate (When this character die is unblocked, you may return this die to the Field Zone and " +
        "it deals your opponent 1 damage.)",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Infiltrate")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3)
        ]);

    // Guardians of the Galaxy's Ricochet, "Slinger" printing - has
    // Infiltrate itself, plus a reactive follow-up: "While Ricochet is
    // active, each time one of your character dice uses Infiltrate, draw
    // a die from your bag and add it to your Prep Area." Not Ricochet's
    // own ability triggering off its own Infiltrate use specifically -
    // any of the controller's character dice using Infiltrate (including
    // Ricochet itself) triggers it, same shape as Attune reacting to
    // "you use an Action die." See TriggerType.WhenInfiltrates.
    public static readonly CardDef Ricochet = Character(
        "ricochet", "Ricochet", "Slinger", dieLimit: 2,
        "Infiltrate. While Ricochet is active, each time one of your character dice uses Infiltrate, draw a " +
        "die from your bag and add it to your Prep Area.",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Infiltrate")],
        abilities: [new AbilityDef(TriggerType.WhenInfiltrates, Cost: null, Effect: new PrepFromBag())],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ]);

    // Civil War's Scarlet Spider, "Former Villain" printing - purely the
    // Intimidate keyword, no other text. WhenFielded, matching the
    // keyword's own trigger, targeting an opposing Character die and
    // moving it to Zone.Intimidated (see TurnEngine.CleanUp's remarks for
    // the return-at-end-of-turn half).
    public static readonly CardDef ScarletSpider = Character(
        "scarlet-spider", "Scarlet Spider", "Former Villain", dieLimit: 4,
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
        ]);

    // Icons: Tomb of Annihilation's Drow Mercenary, "Hired Blade" printing -
    // purely the Obscure keyword, no other text. Unlike Intimidate/Deadly
    // (which needed a new zone or a tracked die-id set), Obscure's "when you
    // use an Action die" trigger and "unblockable" effect are both handled
    // generically in TurnEngine.UseActionDie and CombatEngine.DeclareBlockers
    // /ActiveCallOutTargets - this card contributes nothing but the printed
    // keyword itself, same as TheSpot's Infiltrate.
    public static readonly CardDef DrowMercenary = Character(
        "drow-mercenary", "Drow Mercenary", "Hired Blade", dieLimit: 4,
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
        ]);

    // Justice League's Superman, "Kal-El" printing - purely the
    // Retaliation keyword at its base amount (1 damage), no other text.
    // The first sample card to actually populate CardDef.Affiliations -
    // Retaliation is the first keyword that needs it (see
    // CombatEngine.ResolveRetaliation).
    public static readonly CardDef SupermanKalEl = Character(
        "superman-kal-el", "Superman", "Kal-El", dieLimit: 4,
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
        ]);

    // Justice League's Black Manta, "Deep Sea Deviant" printing - the
    // keyword's own base amount (1 damage) is entirely redefined by this
    // printing's own text ("for each of your active Villains" - a live
    // count, not a fixed number), so this needs DealDamagePerActiveAffiliate
    // rather than DealDamage - see that EffectNode's own remarks.
    public static readonly CardDef BlackMantaDeepSeaDeviant = Character(
        "black-manta-deep-sea-deviant", "Black Manta", "Deep Sea Deviant", dieLimit: 4,
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
        ]);

    // Justice League's Bizarro, "More Than a Monster" printing - purely
    // the Strike keyword, no other text. No AbilityDef needed at all - the
    // bonus is a live, continuously-recomputed check (DieStats.
    // HasStrikeBonus), not a triggered effect, same shape as Loyalty
    // counters or Darkseid's keyword grant.
    public static readonly CardDef BizarroMoreThanAMonster = Character(
        "bizarro-more-than-a-monster", "Bizarro", "More Than a Monster", dieLimit: 4,
        "Strike (This character gets +2A, +2D, and Overcrush so long as it is the only character die you " +
        "fielded this turn.)",
        purchaseCost: 7, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Strike")],
        affiliations: ["Legion of Doom", "Villains"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 7)
        ]);

    // Amazing Spider-Man's "Spidey's Last Stand" Basic Action - purely
    // the Sacrifice mechanic paired with an already-buildable effect, no
    // "you may... if you do" optional-choice branching (the Action die's
    // own use is the opt-in moment - see Sacrifice's own remarks).
    // Sacrifice, not Ko: the sacrificed die bypasses TryResolveKO/
    // ForceKO/Regenerate entirely and never fires "when KO'd."
    public static readonly CardDef SpideysLastStand = BasicAction(
        "spideys-last-stand", "Spidey's Last Stand",
        "Sacrifice a character to draw and roll 2 dice (sacrificed characters are placed in the Used Pile).",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null, Effect: new Sequence([
            new Sacrifice(TargetSpec.CharacterDie("a character die you control", TargetOwnership.Own)),
            new DrawDice(2)
        ]))]);

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
        "the-rock-know-your-role", "The Rock", "Know Your Role", dieLimit: 4,
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
        isImplemented: false);

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
        "big-e", "Big E", "Tag Team Champion", dieLimit: 4,
        "Tag Out (After blockers are declared, you may Prep this die from the Field Zone to give target " +
        "Superstar die +2A and +2D until end of turn.)",
        purchaseCost: 4, energyType: EnergyType.Mask,
        keywords: [new KeywordInstance("Tag Out")],
        affiliations: ["A New Day"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 7)
        ]);

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
        "rip-hunter-navigate-the-sands-of-time", "Rip Hunter", "Navigate the Sands of Time", dieLimit: 4,
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
        ]);

    // Justice League set's Starfire, "Starbolts" printing - purely the
    // Range keyword, no other text. A different printing from the
    // roster's own "No-Nonsense Warrior" Starfire above (different id -
    // real Dice Masters cards reuse character names across printings
    // constantly, same as the three Alfred Pennyworths or four Black
    // Mantas already in this catalog).
    public static readonly CardDef StarfireStarbolts = Character(
        "starfire-starbolts", "Starfire", "Starbolts", dieLimit: 4,
        "Range 2 (When this character attacks, all active characters with Range deal damage equal to " +
        "their Range value to target opposing character die.)",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        keywords: [new KeywordInstance("Range", Params: [2])],
        affiliations: ["Teen Titans"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ]);

    // Icons: Tomb of Annihilation's Jamilah, "Shipwrecked on Chult"
    // printing (same set as Drow Mercenary above) - Experience plus
    // Overcrush, both fully mappable, no
    // AbilityDef needed for either (Experience is entirely engine-built,
    // like Deadly/Infiltrate - see DieStats.ForceKO/TurnEngine.CleanUp).
    public static readonly CardDef JamilahShipwreckedOnChult = Character(
        "jamilah-shipwrecked-on-chult", "Jamilah", "Shipwrecked on Chult", dieLimit: 4,
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
        ]);

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
            Colossus, CorvusGlaive, Dazzler, CosmicCube, ShockingGrasp, Distraction,
            Falcon, FranklinsGalactus, GodEmperorDoom, GoddessOfThunder, Groot, InvisibleWoman,
            JaneFoster, Starfire, Kang, KingHyperion, CasketOfAncientWinters, DailyBugle, Escape,
            AlfredPennyworthCaretaker, AlfredPennyworthMI5, AlfredPennyworthToughAsNails,
            AntManAmplify, Cyclops, Wasp, BlackWidow, Polaris, CosmicCubeInfinitePossibilities, Parademon, Darkseid,
            Deathbird, WaspPixie, MadalynePryor, TheSpot, Ricochet, ScarletSpider, DrowMercenary,
            SupermanKalEl, BlackMantaDeepSeaDeviant, BizarroMoreThanAMonster,
            SpideysLastStand, TheRock, BigE, RipHunterNavigateTheSandsOfTime, StarfireStarbolts,
            JamilahShipwreckedOnChult
        ];
        return all.ToDictionary(c => c.Id);
    }
}
