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
        IReadOnlyList<CharacterFace>? levels = null) => new()
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
        Abilities = abilities ?? []
    };

    private static CardDef BasicAction(
        string id, string name, string rawText, bool epic = false,
        IReadOnlyList<AbilityDef>? abilities = null) => new()
    {
        Id = id,
        Name = name,
        Subtitle = epic ? "Epic Basic Action" : "Basic Action",
        Type = epic ? CardType.EpicBasicAction : CardType.BasicAction,
        PurchaseCost = epic ? 4 : 2, // placeholder, see class remarks
        EnergyTypes = [], // rule 1.2.4/1.3.10 - Basic Actions have no energy type
        DieLimit = 3, // rule 1.2.11 - fixed for every Basic Action card
        RawText = rawText,
        Abilities = abilities ?? []
    };

    // ---- Team A: 10 characters + 3 Basic Actions ----

    // Real cost/energy/stats sourced from the user's reference spreadsheet
    // (see class remarks) - dieLimit is a guess (4, typical Common rarity),
    // not sourced.
    public static readonly CardDef BigBarda = Character(
        "big-barda", "Big Barda", "Formerly of Apokolips", dieLimit: 4,
        "Ignore all non-combat damage dealt to Big Barda.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ]);

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
        ]);

    public static readonly CardDef HarleyQuinn = Character(
        "harley-quinn", "Harley Quinn", "Bright Lights Big City", dieLimit: 4,
        "", // real card, genuinely blank text box
        purchaseCost: 1, energyType: EnergyType.Mask,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4)
        ]);

    public static readonly CardDef Robin = Character(
        "robin", "Robin", "Team Leader", dieLimit: 4,
        "Energize - The first Teen Titans die you purchase this turn costs 1 less (to a minimum of 1).",
        purchaseCost: 2, energyType: EnergyType.Shield,
        keywords: [new KeywordInstance("Energize")],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ]);

    public static readonly CardDef CaptainMarvel = Character(
        "captain-marvel", "Captain Marvel", "Alpha Flight", dieLimit: 4,
        "While Captain Marvel is active, your Character dice get +1 attack and +1 defense.",
        purchaseCost: 4);

    public static readonly CardDef Colossus = Character(
        "colossus", "Colossus", "Inferno", dieLimit: 4, ""); // real card, genuinely blank text box

    public static readonly CardDef CorvusGlaive = Character(
        "corvus-glaive", "Corvus Glaive", "The Black Order", dieLimit: 3,
        "When fielded, KO a character die you control. If you do, the next die you purchase this turn costs [2] less (minimum 1).");

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
    public static readonly CardDef Distraction = BasicAction(
        "distraction", "Distraction",
        "Target opponent chooses two of their character dice. They cannot block this turn. " +
        "Global: Pay [M]. Remove target attacking character die from combat.",
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new MoveDie(
                TargetSpec.CharacterDie("target attacking character die", zones: [Zone.AttackZone]),
                Zone.FieldZone),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Mask))]);

    // ---- Team B: 10 characters + 3 Basic Actions ----

    // Teamwatch (the non-Global half - "Prep a Sidekick from your Used
    // Pile") is left unscripted: Teamwatch isn't a TriggerType this engine
    // models yet (it fires on being engaged in combat, like WhenEngaged),
    // same "Non-global and Global are independent ability slots" situation
    // as Distraction above. The Global half stands on its own.
    public static readonly CardDef Falcon = Character(
        "falcon", "Falcon", "Take Flight", dieLimit: 4,
        "Teamwatch - Prep a [PAWN] from your Used Pile. Global: Pay [F]. Once during your turn, " +
        "each player must field a [PAWN] from their Used Pile if able.",
        keywords: [new KeywordInstance("Teamwatch")],
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new FieldSidekickForEachPlayer(),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Fist),
            OncePerTurn: true)]);

    public static readonly CardDef FranklinsGalactus = Character(
        "franklins-galactus", "Franklin's Galactus", "Earth Shatterer", dieLimit: 4, ""); // genuinely blank

    public static readonly CardDef GodEmperorDoom = Character(
        "god-emperor-doom", "God Emperor Doom", "Harnessing the Beyonders", dieLimit: 4,
        "When fielded, deal 3 damage to target character die and reroll target character die.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new Sequence([
            new DealDamage(3, TargetSpec.CharacterDie("target character die")),
            new Reroll(TargetSpec.CharacterDie("target character die"))
        ]))]);

    public static readonly CardDef GoddessOfThunder = Character(
        "goddess-of-thunder", "Goddess of Thunder", "Thor Corps", dieLimit: 2,
        "Goddess of Thunder gets +5 attack while you have another active character die with Thor in the name or subtitle.");

    public static readonly CardDef Groot = Character(
        "groot", "Groot", "Skilled Investigator", dieLimit: 4,
        "When fielded, roll 2 dice from your bag.",
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new DrawDice(2))]);

    // The static "+1 attack for each other active [F4]..." clause is left
    // unscripted (no "count active dice matching X" stat-modifier
    // primitive exists yet); its Global stands on its own, same
    // independent-ability-slots reasoning as Distraction/Falcon above.
    public static readonly CardDef InvisibleWoman = Character(
        "invisible-woman", "Invisible Woman", "Also Dr. Richards", dieLimit: 4,
        "Invisible Woman gets +1 attack for each of your other active [F4] character dice. " +
        "Global: Pay [M]. Target character die must block this turn.",
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new ForceBlock(TargetSpec.CharacterDie("target character die")),
            EnergyCost: new EnergyCost(Amount: 1, RequiredType: EnergyType.Mask))]);

    public static readonly CardDef JaneFoster = Character(
        "jane-foster", "Jane Foster", "Doctor", dieLimit: 4,
        "When fielded, gain 2 life, and gain an extra 2 life for each of your other active characters " +
        "with Thor in their name or subtitle, or the [TCS] affiliation.");

    // "Recruit" (bring in an off-team Teen Titans die) is left unscripted -
    // no off-team-recruitment mechanic exists yet; its Global stands on
    // its own, same independent-ability-slots reasoning as above.
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
            OncePerTurn: true)]);

    public static readonly CardDef Kang = Character(
        "kang", "Kang", "Prophetic Revelation", dieLimit: 3,
        "While Kang is active, once per turn, a player may pay 2 life to reroll a die in their Reserve Pool.");

    public static readonly CardDef KingHyperion = Character(
        "king-hyperion", "King Hyperion", "Earth-21195", dieLimit: 4,
        "While King Hyperion is active, when an opponent uses an action die, deal 2 damage to target character die.");

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

    public static readonly CardDef DailyBugle = BasicAction(
        "daily-bugle", "Daily Bugle", "Draw 2 dice. Prep up to 2 of them, roll the remainder.");

    public static readonly CardDef Escape = BasicAction(
        "escape", "Escape!",
        "Choose one: Target character die can't be targeted this turn. Or: Prep a die from your Used Pile.");

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
        ]);

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
        ]);

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
        ]);

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

    // A team is 8 character cards + 2 Basic Action cards (10 total), not
    // the 10 characters + 3 Basic Actions used here previously - that was
    // simply wrong. Colossus, Corvus Glaive, Distraction, Kang, King
    // Hyperion, and Escape! stay declared above (and in BuildCatalog's
    // full card list, so /api/cards still returns them) but aren't on
    // either team's roster below - real cards with nowhere to play them
    // yet, not deleted, since a future team-builder feature would want a
    // catalog bigger than exactly two 10-card teams.
    public static readonly IReadOnlyList<string> TeamACharacterIds =
    [
        BigBarda.Id, Apocalypse.Id, Beast.Id, BlackPanther.Id,
        HarleyQuinn.Id, Robin.Id, CaptainMarvel.Id, Dazzler.Id
    ];

    public static readonly IReadOnlyList<string> TeamABasicActionIds =
        [CosmicCube.Id, ShockingGrasp.Id];

    public static readonly IReadOnlyList<string> TeamBCharacterIds =
    [
        Falcon.Id, FranklinsGalactus.Id, GodEmperorDoom.Id, GoddessOfThunder.Id,
        Groot.Id, InvisibleWoman.Id, JaneFoster.Id, Starfire.Id
    ];

    public static readonly IReadOnlyList<string> TeamBBasicActionIds =
        [CasketOfAncientWinters.Id, DailyBugle.Id];

    public static IReadOnlyDictionary<string, CardDef> BuildCatalog()
    {
        CardDef[] all =
        [
            BigBarda, Apocalypse, Beast, BlackPanther, HarleyQuinn, Robin, CaptainMarvel,
            Colossus, CorvusGlaive, Dazzler, CosmicCube, ShockingGrasp, Distraction,
            Falcon, FranklinsGalactus, GodEmperorDoom, GoddessOfThunder, Groot, InvisibleWoman,
            JaneFoster, Starfire, Kang, KingHyperion, CasketOfAncientWinters, DailyBugle, Escape,
            AlfredPennyworthCaretaker, AlfredPennyworthMI5, AlfredPennyworthToughAsNails,
            AntManAmplify, Cyclops
        ];
        return all.ToDictionary(c => c.Id);
    }
}
