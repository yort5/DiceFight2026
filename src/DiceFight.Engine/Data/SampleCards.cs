using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Data;

// A curated slice of real Dice Masters cards - names, subtitles, and
// ability text taken verbatim from ~/DiceMasters/Teambuilder/cards.php
// (the "msw" set array), used here to exercise the engine end-to-end.
//
// IMPORTANT - numeric stats are placeholders. None of the six cloned
// DiceCoalition repos (Teambuilder, DiceMastersCompanion, cardservice,
// DiceBot, DM-OBS-Source, Homepage) contain real per-level attack/defense
// numbers - every community tool represents combat stats via card-face
// images, not structured data. Teambuilder's compact per-card prefix (e.g.
// "133J4") only reliably decodes to a die limit (last character; confirmed
// against rule 1.2.11's fixed "Use 3" for every Basic Action card sampled)
// - purchase cost, energy type, and full 3-level fielding/attack/defense
// are not recoverable from it. So every card below shares one placeholder
// PurchaseCost/EnergyType/Levels progression (a few characters are bumped
// to a higher placeholder cost just to exercise Epic Basic Action's
// cost-4+ gate, rule 1.2.3(4)); only Name/Subtitle/RawText/DieLimit are
// real. Replace PlaceholderLevels etc. once a real stats source is
// available.
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
        int purchaseCost = PlaceholderCost) => new()
    {
        Id = id,
        Name = name,
        Subtitle = subtitle,
        Type = CardType.Character,
        PurchaseCost = purchaseCost,
        EnergyTypes = [PlaceholderEnergy],
        DieLimit = dieLimit,
        Levels = PlaceholderLevels,
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

    public static readonly CardDef AgentBrand = Character(
        "agent-brand", "Agent Brand", "Alpha Flight", dieLimit: 3,
        "While Agent Brand is active, your character dice get +1 defense.");

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
        keywords: [new KeywordInstance("Energize")]);

    public static readonly CardDef BlackSwan = Character(
        "black-swan", "Black Swan", "Serving Rabum Alal", dieLimit: 3,
        "When fielded, the next [S] character die you purchase costs [2] less (to a minimum of 1).");

    public static readonly CardDef CaptainBritain = Character(
        "captain-britain", "Captain Britain", "Baron of Higher Avalon", dieLimit: 4,
        "While Captain Britain is active, your opponent can't field character dice at level 3.");

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

    public static readonly CardDef Falcon = Character(
        "falcon", "Falcon", "Take Flight", dieLimit: 4,
        "Teamwatch - Prep a [PAWN] from your Used Pile. Global: Pay [F]. Once during your turn, " +
        "each player must field a [PAWN] from their Used Pile if able.",
        keywords: [new KeywordInstance("Teamwatch")]);

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

    public static readonly CardDef InvisibleWoman = Character(
        "invisible-woman", "Invisible Woman", "Also Dr. Richards", dieLimit: 4,
        "Invisible Woman gets +1 attack for each of your other active [F4] character dice. " +
        "Global: Pay [M]. Target character die must block this turn.");

    public static readonly CardDef JaneFoster = Character(
        "jane-foster", "Jane Foster", "Doctor", dieLimit: 4,
        "When fielded, gain 2 life, and gain an extra 2 life for each of your other active characters " +
        "with Thor in their name or subtitle, or the [TCS] affiliation.");

    public static readonly CardDef JimmyWoo = Character(
        "jimmy-woo", "Jimmy Woo", "Agent of S.H.I.E.L.D.", dieLimit: 3,
        "Jimmy Woo can't be targeted by opposing effects.");

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

    public static readonly IReadOnlyList<string> TeamACharacterIds =
    [
        AgentBrand.Id, Apocalypse.Id, Beast.Id, BlackPanther.Id, BlackSwan.Id,
        CaptainBritain.Id, CaptainMarvel.Id, Colossus.Id, CorvusGlaive.Id, Dazzler.Id
    ];

    public static readonly IReadOnlyList<string> TeamABasicActionIds =
        [CosmicCube.Id, ShockingGrasp.Id, Distraction.Id];

    public static readonly IReadOnlyList<string> TeamBCharacterIds =
    [
        Falcon.Id, FranklinsGalactus.Id, GodEmperorDoom.Id, GoddessOfThunder.Id, Groot.Id,
        InvisibleWoman.Id, JaneFoster.Id, JimmyWoo.Id, Kang.Id, KingHyperion.Id
    ];

    public static readonly IReadOnlyList<string> TeamBBasicActionIds =
        [CasketOfAncientWinters.Id, DailyBugle.Id, Escape.Id];

    public static IReadOnlyDictionary<string, CardDef> BuildCatalog()
    {
        CardDef[] all =
        [
            AgentBrand, Apocalypse, Beast, BlackPanther, BlackSwan, CaptainBritain, CaptainMarvel,
            Colossus, CorvusGlaive, Dazzler, CosmicCube, ShockingGrasp, Distraction,
            Falcon, FranklinsGalactus, GodEmperorDoom, GoddessOfThunder, Groot, InvisibleWoman,
            JaneFoster, JimmyWoo, Kang, KingHyperion, CasketOfAncientWinters, DailyBugle, Escape
        ];
        return all.ToDictionary(c => c.Id);
    }
}
