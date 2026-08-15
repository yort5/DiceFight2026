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
        ConditionalSelfStatBonus? grantsSelfStatBonusWhileNamedCardActive = null,
        SelfAttackBonusPerMatchingDie? grantsSelfAttackBonusPerMatchingDie = null,
        string? cannotBeTargetedByOpponentWhileNamedCardActive = null,
        FreeFieldingGrant? grantsFreeFielding = null,
        OpponentStatDebuff? grantsOpponentStatDebuff = null,
        bool grantsFirstDamageRedirectToSelf = false,
        bool grantsIgnoresAbilitiesWhileEngaged = false,
        bool grantsSidekickImmunityToOpponentGlobalTargeting = false,
        OwnSidekickStatBonus? grantsSelfStatBonusWhileOwnSidekickActive = null,
        string? selfFreeFieldingUnlessTeamHasAffiliation = null,
        string? selfFreeFieldingWhileOtherActiveAffiliation = null,
        OpponentPurchaseSurcharge? grantsOpponentPurchaseSurcharge = null,
        OpponentGlobalSurcharge? grantsOpponentGlobalSurcharge = null,
        NamedCardSupport? grantsNamedCardSupport = null,
        FieldingCostReduction? grantsFieldingCostReduction = null,
        MinimumBlockersRequirement? grantsMinimumBlockersRequirement = null,
        int? selfFirstPurchaseSurcharge = null,
        SelfPurchaseDiscountIfOpponentHasAffiliation? grantsSelfPurchaseDiscountIfOpponentHasAffiliation = null,
        OpponentAbilityBlankGrant? grantsOpponentAbilityBlankWhileActive = null,
        bool grantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy = false,
        bool grantsSelfPrepWhenSpentAsEnergyForFielding = false,
        SelfPrepFromBagIfFieldedWithEnergy? grantsSelfPrepFromBagIfFieldedWithEnergy = null,
        IReadOnlyList<string>? grantsAffiliationsToSidekicks = null,
        bool selfAttackEqualsDefenseWhileOwnSidekickActive = false,
        int grantsOwnDamageReductionFromOpponentAbilities = 0,
        bool grantsPreventsNonCombatDamageToOtherOwnDice = false,
        bool grantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent = false,
        int? grantsBlocksMultipleAttackers = null,
        bool grantsReturnsKOdOpposingSidekickToBag = false,
        bool grantsDamageWhenOpposingHighDefenseDieIsKOdInCombat = false,
        int grantsOpponentActionDieEnergySurcharge = 0,
        int grantsOpponentPaysLifeToUseActionOrGlobal = 0,
        RerollOrSpinProtection? grantsRerollOrSpinProtection = null,
        bool grantsSpinsUpInSympathyWithOwnCharacterDice = false,
        bool grantsGainLifeWhenOpponentTargetsOwnCharacterDie = false,
        bool grantsRerollsOpponentsFieldedContinuousDie = false,
        bool grantsRerollsUnblockedAttackerToPrepAreaIfCharacterFace = false,
        bool grantsMirrorsOwnStatIncreaseToOwnSidekick = false,
        bool grantsPrepsTwoOwnDiceWhenOpponentDrawsExtraDuringClearAndDraw = false,
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
        GrantsSelfStatBonusWhileNamedCardActive = grantsSelfStatBonusWhileNamedCardActive,
        GrantsSelfAttackBonusPerMatchingDie = grantsSelfAttackBonusPerMatchingDie,
        CannotBeTargetedByOpponentWhileNamedCardActive = cannotBeTargetedByOpponentWhileNamedCardActive,
        GrantsFreeFielding = grantsFreeFielding,
        GrantsOpponentStatDebuff = grantsOpponentStatDebuff,
        GrantsFirstDamageRedirectToSelf = grantsFirstDamageRedirectToSelf,
        GrantsIgnoresAbilitiesWhileEngaged = grantsIgnoresAbilitiesWhileEngaged,
        GrantsSidekickImmunityToOpponentGlobalTargeting = grantsSidekickImmunityToOpponentGlobalTargeting,
        GrantsSelfStatBonusWhileOwnSidekickActive = grantsSelfStatBonusWhileOwnSidekickActive,
        SelfFreeFieldingUnlessTeamHasAffiliation = selfFreeFieldingUnlessTeamHasAffiliation,
        SelfFreeFieldingWhileOtherActiveAffiliation = selfFreeFieldingWhileOtherActiveAffiliation,
        GrantsOpponentPurchaseSurcharge = grantsOpponentPurchaseSurcharge,
        GrantsOpponentGlobalSurcharge = grantsOpponentGlobalSurcharge,
        GrantsNamedCardSupport = grantsNamedCardSupport,
        GrantsFieldingCostReduction = grantsFieldingCostReduction,
        GrantsMinimumBlockersRequirement = grantsMinimumBlockersRequirement,
        SelfFirstPurchaseSurcharge = selfFirstPurchaseSurcharge,
        GrantsSelfPurchaseDiscountIfOpponentHasAffiliation = grantsSelfPurchaseDiscountIfOpponentHasAffiliation,
        GrantsOpponentAbilityBlankWhileActive = grantsOpponentAbilityBlankWhileActive,
        GrantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy = grantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy,
        GrantsSelfPrepWhenSpentAsEnergyForFielding = grantsSelfPrepWhenSpentAsEnergyForFielding,
        GrantsSelfPrepFromBagIfFieldedWithEnergy = grantsSelfPrepFromBagIfFieldedWithEnergy,
        GrantsAffiliationsToSidekicks = grantsAffiliationsToSidekicks ?? [],
        SelfAttackEqualsDefenseWhileOwnSidekickActive = selfAttackEqualsDefenseWhileOwnSidekickActive,
        GrantsOwnDamageReductionFromOpponentAbilities = grantsOwnDamageReductionFromOpponentAbilities,
        GrantsPreventsNonCombatDamageToOtherOwnDice = grantsPreventsNonCombatDamageToOtherOwnDice,
        GrantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent = grantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent,
        GrantsBlocksMultipleAttackers = grantsBlocksMultipleAttackers,
        GrantsReturnsKOdOpposingSidekickToBag = grantsReturnsKOdOpposingSidekickToBag,
        GrantsDamageWhenOpposingHighDefenseDieIsKOdInCombat = grantsDamageWhenOpposingHighDefenseDieIsKOdInCombat,
        GrantsOpponentActionDieEnergySurcharge = grantsOpponentActionDieEnergySurcharge,
        GrantsOpponentPaysLifeToUseActionOrGlobal = grantsOpponentPaysLifeToUseActionOrGlobal,
        GrantsRerollOrSpinProtection = grantsRerollOrSpinProtection,
        GrantsSpinsUpInSympathyWithOwnCharacterDice = grantsSpinsUpInSympathyWithOwnCharacterDice,
        GrantsGainLifeWhenOpponentTargetsOwnCharacterDie = grantsGainLifeWhenOpponentTargetsOwnCharacterDie,
        GrantsRerollsOpponentsFieldedContinuousDie = grantsRerollsOpponentsFieldedContinuousDie,
        GrantsRerollsUnblockedAttackerToPrepAreaIfCharacterFace = grantsRerollsUnblockedAttackerToPrepAreaIfCharacterFace,
        GrantsMirrorsOwnStatIncreaseToOwnSidekick = grantsMirrorsOwnStatIncreaseToOwnSidekick,
        GrantsPrepsTwoOwnDiceWhenOpponentDrawsExtraDuringClearAndDraw = grantsPrepsTwoOwnDiceWhenOpponentDrawsExtraDuringClearAndDraw,
        IsImplemented = isImplemented,
        Set = set
    };

    private static CardDef BasicAction(
        string id, string name, string rawText, bool epic = false,
        IReadOnlyList<AbilityDef>? abilities = null,
        bool isImplemented = true,
        int? purchaseCost = null,
        IReadOnlyList<KeywordInstance>? keywords = null,
        string? set = null,
        bool grantsPreventsOpponentCharacterDiceFromSpinningUp = false,
        string? opponentMayRemoveByReturningAffiliateToCard = null) => new()
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
        Set = set,
        GrantsPreventsOpponentCharacterDiceFromSpinningUp = grantsPreventsOpponentCharacterDiceFromSpinningUp,
        OpponentMayRemoveByReturningAffiliateToCard = opponentMayRemoveByReturningAffiliateToCard
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

    // Organic Steel (DPS010) - the first user of PreventDamage (new this
    // pass): "Prevent up to 2 damage to target character die and move
    // this die to your Used Pile" is the same "controller sends this die
    // to the Used Pile to trigger the effect" ContinuousResolve shape Lab
    // Test above already established - the "and move this die to your
    // Used Pile" clause is TurnEngine.ResolveContinuousDie's own
    // unconditional job, not something this card's own Effect tree
    // repeats. "If you have an active X-Men character, also gain 1 life"
    // reuses OwnActiveAffiliationOrKeywordCountAtLeast exactly like
    // Mutant Research Program's own "at least 2 active Founder" check,
    // just at threshold 1.
    public static readonly CardDef OrganicSteelPreventDamage = BasicAction(
        "DPS010", "Organic Steel",
        "Continuous: Prevent up to 2 damage to target character die and move this die to your Used Pile. If " +
        "you have an active X-Men character, also gain 1 life.",
        keywords: [new KeywordInstance("Continuous")],
        abilities: [new AbilityDef(TriggerType.ContinuousResolve, Cost: null,
            Effect: new Sequence([
                new PreventDamage(2, TargetSpec.CharacterDie("target character die")),
                new Conditional(TargetSpec.Self, EffectCondition.OwnActiveAffiliationOrKeywordCountAtLeast,
                    Then: new GainLife(1), AffiliationParam: "X-Men", CountParam: 1)
            ]))],
        purchaseCost: 3, set: "DPS");

    // Dampening Collar (DPS002) - the first user of both
    // GrantsPreventsOpponentCharacterDiceFromSpinningUp and
    // OpponentMayRemoveByReturningAffiliateToCard (new this pass - see
    // DieStats.SpinLevel/TurnEngine.OpponentResolveContinuousDie). No
    // AbilityDef needed at all - both clauses are continuous, field-
    // driven bespoke grants (the same "bool CardDef field, no AbilityDef"
    // shape Deathbird's own grant already established), not a one-shot
    // effect a ContinuousResolve ability would run - this card has no
    // "send this die to your Used Pile to..." text of its own the way
    // Lab Test/Organic Steel above do.
    public static readonly CardDef DampeningCollar = BasicAction(
        "DPS002", "Dampening Collar",
        "Continuous: Opposing character dice can't spin up. Your opponent may return an X-Men character die " +
        "they control to its card to move this die from the Field Zone to its card.",
        keywords: [new KeywordInstance("Continuous")],
        purchaseCost: 4,
        grantsPreventsOpponentCharacterDiceFromSpinningUp: true,
        opponentMayRemoveByReturningAffiliateToCard: "X-Men",
        set: "DPS");

    // Making the Team (DPS007) - the first user of RollAndFieldOrPrep
    // (new this pass). "A character die from your Used Pile" needs
    // TargetSpec.AnyDie + RequiredCharacterCardType (new this pass), not
    // CharacterDie's own CharacterDiceOnly - a Used Pile die is always
    // Status.Unrolled (rule 1.6.8), so the live-Status check can't tell
    // a dormant Character card from a dormant Basic Action the way it
    // can for an active die.
    public static readonly CardDef MakingTheTeam = BasicAction(
        "DPS007", "Making the Team",
        "Roll a character die from your Used Pile. If it rolls a character face, field it for free. " +
        "Otherwise, Prep it.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null,
            Effect: new RollAndFieldOrPrep(TargetSpec.AnyDie(
                "a character die from your Used Pile", TargetOwnership.Own, zones: [Zone.UsedPile],
                requiredCharacterCardType: true)))],
        purchaseCost: 3, set: "DPS");

    // Mutation (DPS009) - the first user of SwapFieldAndUsedPileDice
    // (new this pass - see its own remarks, including the deliberate
    // TargetOwnership.Own simplification on both targets). The Used
    // Pile target needs the same RequiredCharacterCardType filter
    // Making the Team's own above does (a dormant die's Status can't
    // express "non-Sidekick character card" via the live-Status check),
    // which also happens to exclude bare Sidekicks automatically (their
    // own CardId is always null, so RequiredCharacterCardType's own
    // "does this cardId map to a Character-type card" check already
    // fails them - no separate ExcludeSidekicks needed). Global reuses
    // the plain Spin node twice for two independently-chosen targets -
    // "spin one down to spin another up" is a compound two-target
    // effect (a fixed 1-level trade), not a proportional transfer, so
    // no new primitive needed there at all.
    public static readonly CardDef Mutation = BasicAction(
        "DPS009", "Mutation",
        "Swap target character die in the Field Zone with target non-sidekick character dice in that " +
        "player's Used Pile. Spin that character die to level 1. (This does not trigger \"when fielded\" " +
        "effects.) Global: Pay Mask.Spin one of your character die down a level to spin another target " +
        "character die up a level.",
        abilities: [
            new AbilityDef(TriggerType.WhenUsed, Cost: null,
                Effect: new SwapFieldAndUsedPileDice(
                    TargetSpec.CharacterDie("target character die in the Field Zone", TargetOwnership.Own, zones: [Zone.FieldZone]),
                    TargetSpec.AnyDie(
                        "target non-Sidekick character die in your Used Pile", TargetOwnership.Own,
                        zones: [Zone.UsedPile], requiredCharacterCardType: true),
                    UsedPileDieLevel: 1)),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Sequence([
                    new Spin(TargetSpec.CharacterDie("one of your character dice", TargetOwnership.Own), LevelDelta: -1),
                    new Spin(TargetSpec.CharacterDie("another target character die", TargetOwnership.Own), LevelDelta: +1)
                ]),
                EnergyCost: new EnergyCost(1, EnergyType.Mask))
        ],
        purchaseCost: 3, set: "DPS");

    // Archnemesis (DPS001) - the first user of MutualDamageEqualToOwnAttack
    // and SetDefenseEqualToOwnAttack (both new this pass - see their own
    // remarks).
    public static readonly CardDef Archnemesis = BasicAction(
        "DPS001", "Archnemesis",
        "Target character die you control and target opposing character die deal damage to each other " +
        "equal to their A. Global: Pay Shield. Target character die has D equal to it's A (until end of turn).",
        abilities: [
            new AbilityDef(TriggerType.WhenUsed, Cost: null,
                Effect: new MutualDamageEqualToOwnAttack(
                    TargetSpec.CharacterDie("target character die you control", TargetOwnership.Own),
                    TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing))),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new SetDefenseEqualToOwnAttack(TargetSpec.CharacterDie("target character die")),
                EnergyCost: new EnergyCost(1, EnergyType.Shield))
        ],
        purchaseCost: 4, set: "DPS");

    // The Front Line (DPS015) - the first user of GameState.
    // UnblockedAttackerIds/TargetSpec.RequiresUnblockedAttacker (new this
    // pass - see their own remarks). Global SIMPLIFIED: "can't block this
    // turn unless opponent pays 1 life" drops the "unless" escape hatch -
    // modeled as a flat CantBlock instead. A real "unless" here would
    // need CombatEngine.DeclareBlockers to accept a caller-supplied "I'm
    // paying life to block anyway despite this restriction" signal (new
    // API surface reaching the GamesController endpoint too), not a
    // drop-in reuse of anything that exists - flagged rather than
    // guessed at. This makes the Global strictly stronger than the real
    // card (no counter-play), a real, documented rules deviation.
    public static readonly CardDef TheFrontLine = BasicAction(
        "DPS015", "The Front Line",
        "Unblocked attacking character dice gain +3A until end of turn.Global: Pay Fist. Target opposing " +
        "character die can't block this turn unless opponent pays 1 life.",
        abilities: [
            new AbilityDef(TriggerType.WhenUsed, Cost: null,
                Effect: new ModifyStat(
                    TargetSpec.CharacterDie("unblocked attacking character dice", TargetOwnership.Own,
                        zones: [Zone.AttackZone], matchAll: true, requiresUnblockedAttacker: true),
                    AttackDelta: 3, DefenseDelta: null)),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new CantBlock(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing)),
                EnergyCost: new EnergyCost(1, EnergyType.Fist))
        ],
        purchaseCost: 5, set: "DPS");

    // Explosion (DPS003) - deliberately left isImplemented: false. Three
    // real gaps, none a drop-in reuse of an existing primitive: (1) "deal
    // 2 damage to EACH PLAYER and character die" needs a true "hit both
    // players AND every die on the board at once" effect - nothing here
    // combines TargetSpec.MatchAll's die-side sweep with a simultaneous
    // both-players hit; (2) "you may also spend any number of Bolt
    // energy, for each that you do deal 1 damage to target character
    // die" is a real spend-an-arbitrary-amount-for-a-scaling-effect
    // choice, a shape nothing here models (TurnEngine.SpendEnergy always
    // pays a fixed cost, never an open-ended "as much as you want");
    // (3) the burst-conditional "+1 additional damage to each recipient
    // of step 1" needs to know exactly who step 1 actually hit, which
    // (1) would need to expose. Left vanilla rather than guessed at.
    public static readonly CardDef Explosion = BasicAction(
        "DPS003", "Explosion",
        "Deal 2 damage to each player and character die. You may also spend any number of Bolt energy, " +
        "for each that you do you may deal 1 damage to target character die. ** Deal 1 additional damage " +
        "to each player and character die that Explosion deals damage to.",
        purchaseCost: 4, isImplemented: false, set: "DPS");

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

    // Gambit, "Ace in the Hole" - the card that originally motivated the
    // whole burst-symbol mechanism (see EffectCondition.OnSingleBurstFace
    // and DrawAndChooseOneToRoll's own remarks). "You may draw and roll a
    // die" is the ordinary DrawDice(1); "* Instead, draw 2 dice, Roll one
    // and return the other to your bag" needed the new choice primitive,
    // since the 2 drawn dice are visible by card identity before rolling.
    // Checked against Gambit's OWN die (TargetSpec.Self) at the moment
    // it's fielded - only single burst applies to this printing (no "**"
    // clause), so there's no Double branch to add.
    public static readonly CardDef GambitAceInTheHole = Character(
        "DPS032", "Gambit", "Ace in the Hole", dieLimit: 4,
        "When fielded, you may draw and roll a die. * Instead, draw 2 dice, Roll one and return the other " +
        "to your bag.",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.OnSingleBurstFace,
                Then: new DrawAndChooseOneToRoll(2),
                Else: new DrawDice(1)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1, BurstStars: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Gambit, "Unless I Got Someone to Play With" - the first card to use
    // RerollAndMoveUnlessCharacter (new this pass): reroll up to 2 target
    // opposing character dice, each that doesn't land on a character face
    // goes to the opponent's Used Pile. No damage follow-up on this
    // printing (unlike Psylocke/Storm below), so DamagePerMovedToOpponent
    // is left at its default 0. This printing's own stat line carries a
    // "*" burst mark too, but the ability text has no matching "Instead"/
    // "Also" clause referencing it - burst marks are purely decorative on
    // a stat line unless the card's OWN text keys off them (see the
    // burst-symbol status update), so it's recorded on the levels below
    // for data accuracy but not wired into any Conditional.
    public static readonly CardDef GambitUnlessIGotSomeoneToPlayWith = Character(
        "DPS112", "Gambit", "Unless I Got Someone to Play With", dieLimit: 2,
        "When fielded, reroll up to 2 target opposing character dice. Each die that doesn't roll a character " +
        "goes to your opponent's Used Pile.",
        purchaseCost: 5, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new RerollAndMoveUnlessCharacter(
                TargetSpec.CharacterDie("up to 2 target opposing character dice", TargetOwnership.Opposing,
                    count: 2, optional: true),
                Zone.UsedPile))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1, BurstStars: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 6)
        ], set: "DPS");

    // Psylocke, "Advanced Telekinetic Combatant" - RerollAndMoveUnlessCharacter's
    // second user, this time with the DamagePerMovedToOpponent follow-up
    // ("Psylocke deals 2 damage to your opponent for each die moved").
    public static readonly CardDef PsylockeAdvancedTelekineticCombatant = Character(
        "DPS150", "Psylocke", "Advanced Telekinetic Combatant", dieLimit: 1,
        "When fielded, reroll up to 2 opposing character dice. Each die that does not roll a character goes " +
        "to your opponent's Used Pile. Psylocke deals 2 damage to your opponent for each die moved.",
        purchaseCost: 5, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new RerollAndMoveUnlessCharacter(
                TargetSpec.CharacterDie("up to 2 opposing character dice", TargetOwnership.Opposing,
                    count: 2, optional: true),
                Zone.UsedPile, DamagePerMovedToOpponent: 2))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Storm, "Queen" - three abilities off three different triggers
    // (WhenFielded/WhenAttacks/Energize), the busiest single card this
    // pass. The sheet's own WhenAttacks clause reads "Move each die that
    // DOES roll a character goes to [...] Used Pile" - almost certainly a
    // sheet typo (transposed "does"/"does not"), not a real rules
    // variant: Psylocke's near-identical text just above is unambiguous
    // ("does NOT roll a character"), the flavor (rerolling an opponent's
    // dice hoping to knock them off a character face) only makes sense as
    // a punishment for NOT landing on a character, and "moves the die
    // that stayed a character to the Used Pile" would be a bizarre
    // self-defeating effect with no precedent anywhere in the set. Read
    // as the same RerollAndMoveUnlessCharacter shape as Psylocke.
    public static readonly CardDef StormQueen = Character(
        "DPS132", "Storm", "Queen", dieLimit: 2,
        "When Fielded, reroll target character die. When Storm attacks, reroll up to 2 opposing character " +
        "dice. Move each die that does roll a character goes to you opponent's Used Pile. Storm deals 2 " +
        "damage to your opponent for each die moved. Energize - Reroll target opposing character die",
        purchaseCost: 7, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new Reroll(TargetSpec.CharacterDie("target character die"))),
            new AbilityDef(TriggerType.WhenAttacks, Cost: null,
                Effect: new RerollAndMoveUnlessCharacter(
                    TargetSpec.CharacterDie("up to 2 opposing character dice", TargetOwnership.Opposing,
                        count: 2, optional: true),
                    Zone.UsedPile, DamagePerMovedToOpponent: 2)),
            new AbilityDef(TriggerType.Energize, Cost: null,
                Effect: new Reroll(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing)))
        ],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Magik, "Sorceress of Limbo" - the first card to use GrantKeyword
    // (new this pass): "target character die gains Overcrush and +2A."
    // The bulk sheet's own parsed Keywords list for this row is
    // ["Overcrush"] - that's the sheet mis-attributing the keyword the
    // ABILITY grants to a target as though it were printed on Magik's
    // own card; Magik's own CardDef deliberately leaves Keywords empty,
    // since giving her own die unconditional Overcrush would be a real
    // authoring bug (the same class of mistake the Kitty Pryde/Phoenix
    // Energize/Awaken bug was, just on the "own printed Keywords" side
    // instead of the "own AbilityDef" side).
    public static readonly CardDef MagikSorceressOfLimbo = Character(
        "DPS120", "Magik", "Sorceress of Limbo", dieLimit: 2,
        "When fielded, target character die gains Overcrush and +2A.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Sequence([
                new GrantKeyword(TargetSpec.CharacterDie("target character die"), "Overcrush"),
                new ModifyStat(TargetSpec.CharacterDie("target character die"), AttackDelta: 2, DefenseDelta: null)
            ]))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 7)
        ], set: "DPS");

    // Psylocke, "Telepath" - GrantKeyword's second user, no stat buff
    // attached this time.
    public static readonly CardDef PsylockeTelepath = Character(
        "DPS088", "Psylocke", "Telepath", dieLimit: 3,
        "When fielded, target character die gets Overcrush.",
        purchaseCost: 2, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new GrantKeyword(TargetSpec.CharacterDie("target character die"), "Overcrush"))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Storm, "Cloud Cover" - closes out the gap flagged when Deathbird
    // first used CantBlock: TargetSpec.MaxAttack (new this pass), the
    // stat-threshold filter "target character die with 3A or less" needs.
    public static readonly CardDef StormCloudCover = Character(
        "DPS092", "Storm", "Cloud Cover", dieLimit: 3,
        "When fielded, target character die with 3A or less can't block this turn.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new CantBlock(TargetSpec.CharacterDie("target character die with 3A or less", maxAttack: 3)))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Master Mold, "Targeting Mutants" - the first card to use
    // TargetSpec.RequiredAffiliations (new this pass), a plain single-
    // affiliation KO with nothing else on the card.
    public static readonly CardDef MasterMoldTargetingMutants = Character(
        "DPS082", "Master Mold", "Targeting Mutants", dieLimit: 3,
        "When fielded, KO target Brotherhood of Mutants character die.",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["Villains"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Ko(TargetSpec.CharacterDie(
                "target Brotherhood of Mutants character die", requiredAffiliations: ["Brotherhood of Mutants"])))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Master Mold, "Untold Electronic Expertise" - RequiredAffiliations'
    // second user, same shape targeting X-Men instead.
    public static readonly CardDef MasterMoldUntoldElectronicExpertise = Character(
        "DPS122", "Master Mold", "Untold Electronic Expertise", dieLimit: 2,
        "When fielded, KO target X-Men character die.",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["Villains"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Ko(TargetSpec.CharacterDie("target X-Men character die", requiredAffiliations: ["X-Men"])))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Master Mold, "Inexplicable Durability" - the first card to use
    // TargetSpec.MatchAll (new this pass): "deal 2 damage to ALL X-Men
    // and Brotherhood of Mutants character dice" has no target choice at
    // all in the text, unlike the other two Master Mold printings above -
    // RequiredAffiliations still does the affiliation filtering (two
    // affiliations joined by "and" in the card text is the same "matches
    // any of these" shape as one), MatchAll just skips the caller-choice
    // step entirely so every legal match is hit automatically.
    public static readonly CardDef MasterMoldInexplicableDurability = Character(
        "DPS042", "Master Mold", "Inexplicable Durability", dieLimit: 4,
        "When fielded, deal 2 damage to all X-Men and Brotherhood of Mutants character dice.",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["Villains"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new DealDamage(2, TargetSpec.CharacterDie(
                "all X-Men and Brotherhood of Mutants character dice",
                requiredAffiliations: ["X-Men", "Brotherhood of Mutants"], matchAll: true)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Mister Sinister, "Geneticist" (DPS043) - WhenFielded KO up to 2
    // Sidekicks (Ko + TargetSpec.Sidekick's existing "up to N" idiom,
    // Optional: true), a Global reusing the same GrantKeyword/Deadly
    // shape as this card's own "Biologist" printing (just gated on two
    // Bolt energy and TargetSpec's new ExcludeSidekicks filter for
    // "target non-Sidekick character die"), and the first real user of
    // both TriggerType.WhenKOsOpposingCharacter and MayPayLife (both new
    // this pass, built specifically for this card's third clause - "When
    // Mister Sinister KOs an opposing character, you may pay 1 life. If
    // you do, your opponent loses 1 life"): WhenKOsOpposingCharacter
    // fires from Mister Sinister's own die whenever it's a recorded KO
    // source (combat/Range/Deadly - see TurnEngine.ResolveKOReactions'
    // own koSourceDieIds plumbing) against an opposing character; MayPay
    // Life is the real yes/no "you may pay X, if you do, Y" choice
    // (see its own remarks for why this isn't collapsed to always-happens
    // the way SwapAttack's inconsequential "you may" is).
    public static readonly CardDef MisterSinisterGeneticist = Character(
        "DPS043", "Mister Sinister", "Geneticist", dieLimit: 4,
        "When fielded, KO up to 2 target Sidekick dice. When Mister Sinister KOs an opposing character, you may " +
        "pay 1 life. If you do, your opponent loses 1 life. Global: Pay Bolt Bolt. Target non-Sidekick character " +
        "die gains Deadly.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        keywords: [new KeywordInstance("Deadly")],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new Ko(TargetSpec.Sidekick("up to 2 target Sidekick dice", count: 2, optional: true))),
            new AbilityDef(TriggerType.WhenKOsOpposingCharacter, Cost: null,
                Effect: new MayPayLife(1, new LoseLife(1, TargetOwnership.Opposing))),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new GrantKeyword(
                    TargetSpec.CharacterDie("target non-Sidekick character die", excludeSidekicks: true), "Deadly"),
                EnergyCost: new EnergyCost(2, EnergyType.Bolt))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3)
        ], set: "DPS");

    // Phoenix, "Eternal Flame" - MatchAll's second user, combined with
    // the existing MaxAttack filter: "opposing character dice with less
    // than 4A can't block" is every legal match at once, same as Master
    // Mold above, just gated on attack value instead of affiliation.
    public static readonly CardDef PhoenixEternalFlame = Character(
        "DPS126", "Phoenix", "Eternal Flame", dieLimit: 2,
        "When Phoenix attacks, opposing character dice with less than 4A can't block.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new CantBlock(TargetSpec.CharacterDie(
                "opposing character dice with less than 4A", TargetOwnership.Opposing, maxAttack: 3, matchAll: true)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Magik, "Better than Belasco" - purely the Awaken keyword paired
    // with DrawDice, the exact same shape Kitty Pryde/Black Panther's own
    // Awaken/Energize cards already established. No new primitive needed.
    public static readonly CardDef MagikBetterThanBelasco = Character(
        "DPS080", "Magik", "Better than Belasco", dieLimit: 3,
        "Awaken Roll a die from your bag.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Awaken")],
        abilities: [new AbilityDef(TriggerType.Awaken, Cost: null, Effect: new DrawDice(1))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 7)
        ], set: "DPS");

    // Professor X, "Uncanny Leadership" - the first card to use
    // SpinToEnergyFace (new this pass): "spin an opposing die to it's
    // single energy face" targets any opposing die (the text says "die,"
    // not "character die"), not just a Character one, so TargetSpec.
    // AnyDie is the right shape here rather than CharacterDie.
    public static readonly CardDef ProfessorXUncannyLeadership = Character(
        "DPS127", "Professor X", "Uncanny Leadership", dieLimit: 2,
        "When fielded, spin an opposing die to it's single energy face. Energize - Move an X-Men die from " +
        "your Used Pile to your Prep Area.",
        purchaseCost: 6, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new SpinToEnergyFace(
                    TargetSpec.AnyDie("an opposing die", TargetOwnership.Opposing, TargetSpec.DefaultZones))),
            new AbilityDef(TriggerType.Energize, Cost: null,
                // A Used Pile die is always unrolled (rule 1.6.8 - never
                // DieStatus.Character), so this can't use TargetSpec.
                // CharacterDie's CharacterDiceOnly filter the way an
                // in-play target normally would; AnyDie + RequiredAffiliations
                // matches on the card's own Affiliations regardless of the
                // die's current (irrelevant, dormant-zone) rolled status.
                Effect: new MoveDie(
                    TargetSpec.AnyDie(
                        "an X-Men die from your Used Pile", TargetOwnership.Own,
                        zones: [Zone.UsedPile], requiredAffiliations: ["X-Men"]),
                    Zone.PrepArea))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 1, Defense: 9)
        ], set: "DPS");

    // Iceman, "Icy Interference" - SpinToEnergyFace's second user, this
    // time gated by TargetSpec.RequiredLevel (new this pass, the level-
    // restricted filter counterpart to RequiredAffiliations): "target
    // opposing LEVEL 1 character die."
    public static readonly CardDef IcemanIcyInterference = Character(
        "DPS034", "Iceman", "Icy Interference", dieLimit: 4,
        "When Iceman attacks, spin target opposing level 1 character die to an energy face.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new SpinToEnergyFace(
                TargetSpec.CharacterDie("target opposing level 1 character die", TargetOwnership.Opposing, requiredLevel: 1)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6)
        ], set: "DPS");

    // Cyclops, "Defending the Phoenix" - purely existing primitives:
    // Energize firing a Sequence of a DealDamage and a Reroll of its own
    // source die.
    public static readonly CardDef CyclopsDefendingThePhoenix = Character(
        "DPS065", "Cyclops", "Defending the Phoenix", dieLimit: 3,
        "Energize - Deal 1 damage to target character die and reroll this die.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Sequence([
                new DealDamage(1, TargetSpec.CharacterDie("target character die")),
                new Reroll(TargetSpec.Self)
            ]))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 4)
        ], set: "DPS");

    // Rogue, "Strength Absorption" - the first card to use SetStat (new
    // this pass): "has 0A this turn" is a snapshot to an exact value, not
    // a delta.
    public static readonly CardDef RogueStrengthAbsorption = Character(
        "DPS151", "Rogue", "Strength Absorption", dieLimit: 1,
        "Energize - Target character die has 0A this turn.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new SetStat(TargetSpec.CharacterDie("target character die"), Attack: 0, Defense: null))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Moira, "If It's Real" - three abilities, all buildable once
    // CardDef.GrantsSelfStatBonusWhileNamedCardActive existed (new this
    // pass - see its own remarks): the "while Wolverine active" self +1D
    // uses it directly; the WhenFielded X-Men team-wide +1A composes two
    // already-existing TargetSpec features (RequiredAffiliations +
    // MatchAll, since "your X-Men character dice get +1A" has no target
    // choice at all); the WhenKOd Prep is TargetSpec.AnyDie against the
    // Used Pile, same shape Professor X's own Energize already
    // established just with no affiliation restriction ("a die," not "an
    // X-Men die").
    public static readonly CardDef MoiraIfItsReal = Character(
        "DPS084", "Moira", "If It's Real", dieLimit: 3,
        "While Wolverine is active, Moira gets +1D. When fielded, your X-Men character dice get +1A until " +
        "end of turn. When Moira is KO'd, Prep a die from your Used Pile.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        grantsSelfStatBonusWhileNamedCardActive: new ConditionalSelfStatBonus("Wolverine", AttackDelta: 0, DefenseDelta: 1),
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new ModifyStat(
                    TargetSpec.CharacterDie(
                        "your X-Men character dice", TargetOwnership.Own, requiredAffiliations: ["X-Men"], matchAll: true),
                    AttackDelta: 1, DefenseDelta: null)),
            new AbilityDef(TriggerType.WhenKOd, Cost: null,
                Effect: new PrepDie(TargetSpec.AnyDie("a die from your Used Pile", TargetOwnership.Own, zones: [Zone.UsedPile])))
        ],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 0, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2)
        ], set: "DPS");

    // Moira, "It's Not a Dream" (DPS044) - the first user of
    // GrantsRerollsOpponentsFieldedContinuousDie (new this pass - see
    // TurnEngine.UseActionDie's own Continuous branch, and its remarks
    // on the "they may field it normally" -> always-happens
    // simplification). No AbilityDef needed - a passive, always-on grant.
    public static readonly CardDef MoiraItsNotADream = Character(
        "DPS044", "Moira", "It's Not a Dream", dieLimit: 4,
        "While Moira is active, when an opponent fields a Continuous Action die, reroll it. If it lands " +
        "on an action face, they may field it normally. Otherwise, send it to the Used Pile.",
        purchaseCost: 2, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        grantsRerollsOpponentsFieldedContinuousDie: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 0, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2)
        ], set: "DPS");

    // Deathbird, "War of Kings" - the first card to use CantBlock (new
    // this pass): GameState.CantBlockThisTurn, the restriction mirror of
    // ForceBlock/MustBlockThisTurn - enforced by CombatEngine.
    // DeclareBlockers rejecting the die as an eligible blocker outright,
    // not by removing it from a legal-blockers list a caller would still
    // have to know to consult.
    public static readonly CardDef DeathbirdWarOfKings = Character(
        "DPS109", "Deathbird", "War of Kings", dieLimit: 2,
        "When fielded, target character die cannot block this turn.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["Villains", "Shi'ar"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new CantBlock(TargetSpec.CharacterDie("target character die")))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Deadpool, "More than a Chump Blocker" - a fixed (not targeted)
    // WhenAttacks trigger dealing damage straight to the opponent, same
    // shape Superman "Kal-El"'s Retaliation effect already uses for its
    // own DealDamage(..., TargetSpec.Player(...)) call, just off a
    // different trigger.
    public static readonly CardDef DeadpoolMoreThanAChumpBlocker = Character(
        "DPS068", "Deadpool", "More than a Chump Blocker", dieLimit: 3,
        "When Deadpool attacks, he deals your opponent 1 damage.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["Deadpool Affiliation"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new DealDamage(1, TargetSpec.Player("your opponent", TargetOwnership.Opposing)))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 7)
        ], set: "DPS");

    // Ronan the Accuser, "No Exceptions" - "each player loses 3 life" is
    // just two LoseLife calls (Own then Opposing), same primitive Ronan's
    // own "Treason!" printing (DPS050) already established for one side
    // of it - no new mechanism needed for "each player" here since both
    // amounts and both sides are fixed, unlike a real per-player choice
    // (e.g. "No Mercy"'s "each player KOs a character die they control",
    // left unauthored - that needs the opposing player to make their own
    // choice, which nothing in the ability DSL threads through yet).
    public static readonly CardDef RonanTheAccuserNoExceptions = Character(
        "DPS130", "Ronan the Accuser", "No Exceptions", dieLimit: 2,
        "When fielded, each player loses 3 life.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Sequence([new LoseLife(3), new LoseLife(3, TargetOwnership.Opposing)]))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 7),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Jean Grey, "Peaceful Coexistence" - the first Loyalty card (see
    // GameState.LoyaltyCounters/DieStats.LoyaltyBonus and TriggerType.
    // EndOfYourTurn's own remarks for the new plumbing this needed).
    // "Founder" prefixing the raw text was originally left untagged as a
    // non-modeled flavor label (nothing on THIS card referenced it) -
    // correction, now that Cyclops "First Class" (DPS025) needs a real
    // Founder KeywordInstance to recognize fielded Founder dice: this is
    // a genuine printed keyword after all, just one nothing consumed
    // until now.
    public static readonly CardDef JeanGreyPeacefulCoexistence = Character(
        "DPS035", "Jean Grey", "Peaceful Coexistence", dieLimit: 4,
        "Founder While Jean Grey is active, at the end of each or your turns, if no character dice were " +
        "KO'd that turn, put a Loyalty Counter on Jean Grey's card (Loyalty Counters give a character die " +
        "+1A and +1D.)",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
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

    // Kitty Pryde, "Experienced Leader" - the first card to use an
    // affiliation-scoped StaticTeamBonus (new this pass): "each of your
    // X-Men character dice get +1A and +1D," no AbilityDef needed at all
    // (purely a live, continuously-recomputed Static ability, same as
    // Captain Marvel's own unqualified version).
    public static readonly CardDef KittyPrydeExperiencedLeader = Character(
        "DPS144", "Kitty Pryde", "Experienced Leader", dieLimit: 1,
        "While Kitty Pryde is active, each of your X-Men character dice get +1A and +1D.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        grantsStaticTeamBonus: new StaticTeamBonus(1, 1, RequiredAffiliation: "X-Men"),
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Sabretooth, "Do I Smell... Weakness?" - the first card to use
    // GrantsSelfAttackBonusPerMatchingDie (new this pass), counting via
    // TargetSpec.MaxDefense (also new this pass). No AbilityDef needed -
    // purely a live, continuously-recomputed self bonus.
    public static readonly CardDef SabretoothDoISmellWeakness = Character(
        "DPS091", "Sabretooth", "Do I Smell... Weakness?", dieLimit: 3,
        "Sabretooth gets +1A for each opposing character die with 2D or less.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["Brotherhood of Mutants"],
        grantsSelfAttackBonusPerMatchingDie: new SelfAttackBonusPerMatchingDie(
            TargetSpec.CharacterDie("opposing character die with 2D or less", TargetOwnership.Opposing, maxDefense: 2),
            AttackPerMatch: 1),
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 4)
        ], set: "DPS");

    // Sabretooth, "Am I Interrupting?" (DPS051) - the first user of
    // TargetSpec.NameContains (new this pass). Only the "target
    // Wolverine character die" half is modeled - the "OR any character
    // die with a 'While Wolverine is active' ability" half needs
    // targeting by matching a card's own raw ability TEXT for a phrase,
    // not a structured property this engine's data model has anywhere
    // (TargetSpec filters are all real fields - affiliation, energy
    // type, keyword, card id - never free text). Left out rather than
    // guessed at; a real card this excludes today: Psylocke
    // ("Adventurer", DPS048)'s own "gains Deadly while Wolverine is
    // active."
    public static readonly CardDef SabretoothAmIInterrupting = Character(
        "DPS051", "Sabretooth", "Am I Interrupting?", dieLimit: 4,
        "When fielded, KO target Wolverine characer die, or any character die with a \"While Wolverine is " +
        "active\" ability.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["Brotherhood of Mutants"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Ko(TargetSpec.CharacterDie("target Wolverine character die", nameContains: "Wolverine")))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 4)
        ], set: "DPS");

    // Psylocke, "Heiress" - GrantsSelfAttackBonusPerMatchingDie's second
    // user, counting via TargetSpec.AnyDie against the Prep Area instead
    // (a Prep Area die is always Unrolled, same "can't use CharacterDie's
    // CharacterDiceOnly filter" reasoning Professor X's own Energize
    // already established) - plus a real Energize ability on top.
    public static readonly CardDef PsylockeHeiress = Character(
        "DPS128", "Psylocke", "Heiress", dieLimit: 2,
        "Psylocke gets +2A for each of your X-Men dice in the Prep Area. Energize - Spin target character " +
        "die up 1 level.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        grantsSelfAttackBonusPerMatchingDie: new SelfAttackBonusPerMatchingDie(
            TargetSpec.AnyDie(
                "your X-Men dice in the Prep Area", TargetOwnership.Own, zones: [Zone.PrepArea],
                requiredAffiliations: ["X-Men"]),
            AttackPerMatch: 2),
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Spin(TargetSpec.CharacterDie("target character die"), +1))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Magneto, "Founder of the Brotherhood" - both abilities reuse
    // primitives Magneto's own "Idealist" printing (DPS041) already
    // established: WhenAnotherDieKOd + KOdDieMatch (affiliation instead
    // of energy type this time), and the exact same "if you have no dice
    // in your Prep Area, draw one" Global.
    public static readonly CardDef MagnetoFounderOfTheBrotherhood = Character(
        "DPS146", "Magneto", "Founder of the Brotherhood", dieLimit: 1,
        "While Magneto is active, when one of your Brotherhood of Mutants character dice is KO'd, KO " +
        "target opposing character dice. Global Pay Mask. Once per turn, during your turn, if you have no " +
        "dice in your Prep Area, you may draw a die and place it in your Prep Area.",
        purchaseCost: 6, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants"],
        abilities: [
            new AbilityDef(TriggerType.WhenAnotherDieKOd, Cost: null,
                Effect: new Ko(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing)),
                KOdFilter: new KOdDieMatch(TargetOwnership.Own, AffiliationContains: "Brotherhood of Mutants")),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Conditional(TargetSpec.Self, EffectCondition.PrepAreaEmpty, new PrepFromBag()),
                EnergyCost: new EnergyCost(1, EnergyType.Mask), OncePerTurn: true)
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 8)
        ], set: "DPS");

    // Sabretooth, "You Ready to Party?" - both abilities are existing
    // primitives recombined: WhenAttacks + a MatchAll/RequiredAffiliations
    // ModifyStat (same shape Moira/Sabretooth's own other printing
    // already established for "your [affiliation] dice get +NA"), and
    // Teamwatch + the existing plain CantBlock.
    public static readonly CardDef SabretoothYouReadyToParty = Character(
        "DPS131", "Sabretooth", "You Ready to Party?", dieLimit: 2,
        "When Sabretooth attacks, your Brotherhood of Mutants character dice get +2A. Teamwatch - Target " +
        "character die can't block this turn",
        purchaseCost: 5, energyType: EnergyType.Fist,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Teamwatch")],
        abilities: [
            new AbilityDef(TriggerType.WhenAttacks, Cost: null,
                Effect: new ModifyStat(
                    TargetSpec.CharacterDie(
                        "your Brotherhood of Mutants character dice", TargetOwnership.Own,
                        requiredAffiliations: ["Brotherhood of Mutants"], matchAll: true),
                    AttackDelta: 2, DefenseDelta: null)),
            new AbilityDef(TriggerType.Teamwatch, Cost: null,
                Effect: new CantBlock(TargetSpec.CharacterDie("target character die")))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 4)
        ], set: "DPS");

    // Toad, "Journey Into Misery" - Teamwatch + a plain MoveDie against
    // the opponent's Prep Area, no new primitive needed.
    public static readonly CardDef ToadJourneyIntoMisery = Character(
        "DPS134", "Toad", "Journey Into Misery", dieLimit: 2,
        "Teamwatch - Move a die from your opponents Prep Area to their bag.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Teamwatch")],
        abilities: [new AbilityDef(TriggerType.Teamwatch, Cost: null,
            Effect: new MoveDie(
                TargetSpec.AnyDie("a die from your opponent's Prep Area", TargetOwnership.Opposing, zones: [Zone.PrepArea]),
                Zone.Bag))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 1, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Jubilee, "Rebellious Nature" - the first card to use the new
    // EffectCondition.OwnLifeLessThanOpponent, and FieldDie's newly-fixed
    // Status transition (see FieldDie's own remarks): this ability fires
    // while its own die is still Status.Energy (Energize's own trigger
    // condition), which the OLD FieldDie couldn't handle - deliberately
    // left vanilla for exactly that reason until now.
    public static readonly CardDef JubileeRebelliousNature = Character(
        "DPS036", "Jubilee", "Rebellious Nature", dieLimit: 4,
        "Energize - If you have less life than your opponent, you may immediately field this die for free " +
        "at level 2.",
        purchaseCost: 2, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.OwnLifeLessThanOpponent,
                Then: new FieldDie(TargetSpec.Self, Free: true, Level: 2)))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "DPS");

    // Cyclops, "First Class" - the first card to use TriggerType.
    // WhenAnotherDieFielded (new this pass), filtered to Founder-keyword
    // dice. Cyclops's own text prints "Founder" too (a real
    // KeywordInstance now - see Jean Grey's own remarks for the
    // correction), though nothing currently checks Cyclops's OWN
    // Founder-ness, only the fielded die's.
    public static readonly CardDef CyclopsFirstClass = Character(
        "DPS025", "Cyclops", "First Class", dieLimit: 4,
        "Founder While Cyclops is active, when you field a character die with Founder, deal 2 damage to " +
        "target character die.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        abilities: [new AbilityDef(TriggerType.WhenAnotherDieFielded, Cost: null,
            Effect: new DealDamage(2, TargetSpec.CharacterDie("target character die")),
            FieldedFilter: new FieldedDieMatch(TargetOwnership.Own, RequiredKeyword: "Founder"))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 4)
        ], set: "DPS");

    // Jubilee, "X-Men Field Leader" - WhenAnotherDieFielded's second
    // user, this time unfiltered (any of your own dice being fielded,
    // not just Founder ones).
    public static readonly CardDef JubileeXMenFieldLeader = Character(
        "DPS143", "Jubilee", "X-Men Field Leader", dieLimit: 1,
        "While Jubilee is active, when you field a character die she deals 1 damage to your opponent and " +
        "1 damage to target character die.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAnotherDieFielded, Cost: null,
            Effect: new Sequence([
                new DealDamage(1, TargetSpec.Player("your opponent", TargetOwnership.Opposing)),
                new DealDamage(1, TargetSpec.CharacterDie("target character die"))
            ]),
            FieldedFilter: new FieldedDieMatch(TargetOwnership.Own))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "DPS");

    // Jubilee, "Things Never Change" - purely GrantsSelfStatBonusWhileNamedCardActive,
    // no AbilityDef needed at all.
    public static readonly CardDef JubileeThingsNeverChange = Character(
        "DPS076", "Jubilee", "Things Never Change", dieLimit: 3,
        "While Woverine is active, Jubilee gets +1A.",
        purchaseCost: 2, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        grantsSelfStatBonusWhileNamedCardActive: new ConditionalSelfStatBonus("Wolverine", AttackDelta: 1, DefenseDelta: 0),
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "DPS");

    // Kitty Pryde, "Headmistress" - the first card to use
    // CannotBeTargetedByOpponentWhileNamedCardActive (new this pass),
    // combined with the existing GrantsSelfStatBonusWhileNamedCardActive.
    // No AbilityDef needed - both are continuous.
    public static readonly CardDef KittyPrydeHeadmistress = Character(
        "DPS077", "Kitty Pryde", "Headmistress", dieLimit: 3,
        "While Woverine is active, Kitty Pryde gets +1A and can't be targeted by your opponent.",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        grantsSelfStatBonusWhileNamedCardActive: new ConditionalSelfStatBonus("Wolverine", AttackDelta: 1, DefenseDelta: 0),
        cannotBeTargetedByOpponentWhileNamedCardActive: "Wolverine",
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3)
        ], set: "DPS");

    // Corsair, "Criminal Record" - the first card to use
    // EffectCondition.OpponentHasAtLeastNCharacterDiceInFieldZone (new
    // this pass).
    public static readonly CardDef CorsairCriminalRecord = Character(
        "DPS104", "Corsair", "Criminal Record", dieLimit: 2,
        "When fielded, KO target Villains character die, or KO 2 target Villains character dice if your " +
        "opponent has 4 or more character dice in the Field Zone.",
        purchaseCost: 5, energyType: EnergyType.Fist,
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.OpponentHasAtLeastNCharacterDiceInFieldZone,
                Then: new Ko(TargetSpec.CharacterDie(
                    "2 target Villains character dice", requiredAffiliations: ["Villains"], count: 2)),
                Else: new Ko(TargetSpec.CharacterDie("target Villains character die", requiredAffiliations: ["Villains"])),
                CountParam: 4))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 5)
        ], set: "DPS");

    // Corsair, "Leading the Starjammers" (DPS064) - the first user of
    // GrantsMirrorsOwnStatIncreaseToOwnSidekick (new this pass - see
    // EffectInterpreter's own ModifyStat case for the enforcement and
    // its "auto-pick the first Sidekick, no real choice" simplification
    // remarks). No AbilityDef needed - a passive, always-on grant.
    public static readonly CardDef CorsairLeadingTheStarjammers = Character(
        "DPS064", "Corsair", "Leading the Starjammers", dieLimit: 3,
        "If Corsair's A or D is increasedby an effect, you may increase the A or D of a Sidekick die you " +
        "control by the same amount.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        grantsMirrorsOwnStatIncreaseToOwnSidekick: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 5)
        ], set: "DPS");

    // Phoenix, "Psionic Maelstrom" - PhoenixPsionicMaelstromTarget is
    // shared between the DealDamage and the Conditional's CheckTarget so
    // both resolve to the SAME chosen die (the same "share a TargetSpec
    // instance/cache entry" trick ColossusEnergizeTarget's own remarks
    // document) - otherwise the Conditional could ask about a
    // different, independently-chosen "target character die." The
    // second DealDamage's own target ("another target character die")
    // is deliberately a separate TargetSpec with no exclusion against
    // the first choice - the engine doesn't enforce "different die" here,
    // a minor simplification against the card's literal "another."
    public static readonly TargetSpec PhoenixPsionicMaelstromTarget = TargetSpec.CharacterDie("target character die");

    public static readonly CardDef PhoenixPsionicMaelstrom = Character(
        "DPS086", "Phoenix", "Psionic Maelstrom", dieLimit: 3,
        "When Phoenix attacks, deal 3 damage to target character die. If that character die is a Villains " +
        "character die, you may deal 3 damage to another target character die.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new Sequence([
                new DealDamage(3, PhoenixPsionicMaelstromTarget),
                new Conditional(PhoenixPsionicMaelstromTarget, EffectCondition.TargetHasAffiliation,
                    Then: new DealDamage(3, TargetSpec.CharacterDie("another target character die")),
                    AffiliationParam: "Villains")
            ]))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Dark Phoenix, "Enemy of the Shi'ar" - the first card to use
    // GrantNextPurchaseDiscount/GameState.PendingPurchaseDiscount (new
    // this pass). The Global's "Pay Bolt and KO one of your character
    // dice" cost is folded straight into the Effect as its own first
    // Sequence step, same "not via the unused AbilityDef.Cost field"
    // choice Sacrifice's own status update already made - Cost stays
    // unused engine-wide, not just for Sacrifice.
    public static readonly CardDef DarkPhoenixEnemyOfTheShiar = Character(
        "DPS067", "Dark Phoenix", "Enemy of the Shi'ar", dieLimit: 3,
        "When fielded, KO target Shi'ar or X-Men character die.When Dark Phoenix attacks, deal 2 damage to " +
        "your opponent. Global: Pay Bolt and KO one of your character dice. Your next die you purchase " +
        "this turn costs 2 less (to a minimum of 1).",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new Ko(TargetSpec.CharacterDie(
                    "target Shi'ar or X-Men character die", requiredAffiliations: ["Shi'ar", "X-Men"]))),
            new AbilityDef(TriggerType.WhenAttacks, Cost: null,
                Effect: new DealDamage(2, TargetSpec.Player("your opponent", TargetOwnership.Opposing))),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Sequence([
                    new Ko(TargetSpec.CharacterDie("one of your character dice", TargetOwnership.Own)),
                    new GrantNextPurchaseDiscount(2)
                ]),
                EnergyCost: new EnergyCost(1, EnergyType.Bolt))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Magik, "Wielder of the Soulsword" - GrantNextPurchaseDiscount's
    // second user, scoped to Action dice only via RequiredType.
    public static readonly CardDef MagikWielderOfTheSoulsword = Character(
        "DPS040", "Magik", "Wielder of the Soulsword", dieLimit: 4,
        "When fielded, the next action die you purchase costs 1 less (to a minimum of 1)",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new GrantNextPurchaseDiscount(1, CardType.Action))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 4),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 7)
        ], set: "DPS");

    // Take Cover - the "*/**" burst marks apply the SAME extra bonus off
    // either face (both conditions checked independently, per the burst-
    // symbol status update's own remarks on this exact card), so two
    // separate Conditionals (one per face) reach it rather than one
    // combined check. No new primitive needed - MatchAll (for "character
    // dice you control get +2D") and the burst conditions both already
    // existed.
    public static readonly CardDef TakeCover = BasicAction(
        "DPS014", "Take Cover",
        "Character dice you control get +2D. */** Target character die gets an extra +3D. Global: Pay " +
        "Shield. Target character die gets +1D (until end of turn).",
        purchaseCost: 3,
        abilities: [
            new AbilityDef(TriggerType.WhenUsed, Cost: null,
                Effect: new Sequence([
                    new ModifyStat(
                        TargetSpec.CharacterDie("character dice you control", TargetOwnership.Own, matchAll: true),
                        AttackDelta: null, DefenseDelta: 2),
                    new Conditional(TargetSpec.Self, EffectCondition.OnSingleBurstFace,
                        Then: new ModifyStat(TargetSpec.CharacterDie("target character die"), AttackDelta: null, DefenseDelta: 3)),
                    new Conditional(TargetSpec.Self, EffectCondition.OnDoubleBurstFace,
                        Then: new ModifyStat(TargetSpec.CharacterDie("target character die"), AttackDelta: null, DefenseDelta: 3))
                ])),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new ModifyStat(TargetSpec.CharacterDie("target character die"), AttackDelta: null, DefenseDelta: 1),
                EnergyCost: new EnergyCost(1, EnergyType.Shield))
        ], set: "DPS");

    // Deadpool, "Collect THIS!" - the first card to use
    // CardDef.GrantsFreeFielding (new this pass), scoped by fielding cost.
    public static readonly CardDef DeadpoolCollectThis = Character(
        "DPS108", "Deadpool", "Collect THIS!", dieLimit: 2,
        "While Deadpool is active, your character dice with fielding cost of 2 are free to field.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["X-Men", "Deadpool Affiliation"],
        grantsFreeFielding: new FreeFieldingGrant(MaxFieldingCost: 2),
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 7)
        ], set: "DPS");

    // Mystique, "Taught by Magneto" - GrantsFreeFielding's second user,
    // scoped by affiliation instead, plus an Energize FieldDie targeting
    // a Brotherhood of Mutants die directly (the same primitive Colossus's
    // own Energize already established).
    public static readonly CardDef MystiqueTaughtByMagneto = Character(
        "DPS125", "Mystique", "Taught by Magneto", dieLimit: 2,
        "While Mystique is active, Brotherhood of Mutants character dice are free to field. Energize - You " +
        "may field a Brotherhood of Mutants character die for free.",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["X-Men", "Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Energize")],
        grantsFreeFielding: new FreeFieldingGrant(RequiredAffiliation: "Brotherhood of Mutants"),
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new FieldDie(
                TargetSpec.CharacterDie(
                    "a Brotherhood of Mutants character die", TargetOwnership.Own,
                    zones: [Zone.ReservePool], requiredAffiliations: ["Brotherhood of Mutants"]),
                Free: true))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 1, BurstStars: 1)
        ], set: "DPS");

    // Iceman, "Frozen Fists of Fury" - the first card to use
    // EffectCondition.NamedCardIsActive (new this pass) - a one-shot
    // Conditional check for the same "while [Name] is active" idiom
    // GrantsSelfKeywordWhileNamedCardActive/
    // GrantsSelfStatBonusWhileNamedCardActive apply continuously,
    // surfaced here as a gate on a triggered effect instead.
    public static readonly CardDef IcemanFrozenFistsOfFury = Character(
        "DPS074", "Iceman", "Frozen Fists of Fury", dieLimit: 3,
        "Founder When Iceman attacks, if Wolverine is active, deal 3 damage to target character die.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.NamedCardIsActive,
                Then: new DealDamage(3, TargetSpec.CharacterDie("target character die")),
                NamedCardParam: "Wolverine"))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6)
        ], set: "DPS");

    // Ronan the Accuser, "No Mercy" - the first card to use
    // OpponentKOsOwnCharacterDie (new this pass): "each player KOs a
    // character die they control" splits into the ability controller's
    // own ordinary Ko(Own) choice (answered the normal way) and this new
    // node for the opponent's own, otherwise-unanswerable, choice.
    public static readonly CardDef RonanTheAccuserNoMercy = Character(
        "DPS090", "Ronan the Accuser", "No Mercy", dieLimit: 3,
        "When fielded, each player KOs a character die they control.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Sequence([
                new Ko(TargetSpec.CharacterDie("a character die you control", TargetOwnership.Own)),
                new OpponentKOsOwnCharacterDie()
            ]))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 7),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Emma Frost, "Manipulative" - the first card to use
    // TriggerType.StartOfOpponentsAttackStep (new this pass, fired from
    // TurnEngine.EnterAttackStep).
    public static readonly CardDef EmmaFrostManipulative = Character(
        "DPS070", "Emma Frost", "Manipulative", dieLimit: 3,
        "While Emma Frost is active, at the start of your opponent's Attack Step, reroll target character " +
        "die they control.",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["Hellfire Club"],
        abilities: [new AbilityDef(TriggerType.StartOfOpponentsAttackStep, Cost: null,
            Effect: new Reroll(TargetSpec.CharacterDie("target character die they control", TargetOwnership.Opposing)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7)
        ], set: "DPS");

    // Emma Frost, "Finesse" - StartOfOpponentsAttackStep's second user;
    // "those showing character faces are returned to the Field Zone,
    // those on energy faces are sent to the Reserve Pool" is exactly
    // RerollAndMoveUnlessCharacter's existing shape with ToZone:
    // ReservePool - a character-face lander is simply left alone by that
    // primitive, which for a die already sitting in the Field Zone
    // *is* "returned to the Field Zone." No new primitive needed.
    public static readonly CardDef EmmaFrostFinesse = Character(
        "DPS110", "Emma Frost", "Finesse", dieLimit: 2,
        "While Emma Frost is Active, at the start of your opponents Attack Step, reroll 2 target Fist " +
        "character dice your oppoenent controls. Those showing character faces are returned to the Field " +
        "Zone, those on energy faces are sent to the Reserve Pool.",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["X-Men", "Hellfire Club"],
        abilities: [new AbilityDef(TriggerType.StartOfOpponentsAttackStep, Cost: null,
            Effect: new RerollAndMoveUnlessCharacter(
                TargetSpec.CharacterDie(
                    "2 target Fist character dice your opponent controls", TargetOwnership.Opposing,
                    energyType: EnergyType.Fist, count: 2),
                Zone.ReservePool))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7)
        ], set: "DPS");

    // A Sentinel token's own "card" - see PlaceToken/CardType.Token's own
    // remarks for why this needs a real (but hidden-from-browsing)
    // CardCatalog entry rather than being purely synthetic.
    public static readonly CardDef SentinelToken = new()
    {
        Id = "TOKEN-SENTINEL",
        Name = "Sentinel",
        Subtitle = "Token",
        Type = CardType.Token,
        PurchaseCost = 0,
        DieLimit = 0,
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 5, Defense: 5)],
        RawText = "5A/5D token.",
        IsImplemented = true
    };

    // Master Mold, "Endless Sentinels" - the first card to use PlaceToken
    // (new this pass, alongside CardType.Token/SentinelToken above).
    public static readonly CardDef MasterMoldEndlessSentinels = Character(
        "DPS147", "Master Mold", "Endless Sentinels", dieLimit: 1,
        "When fielded, when Master Mold attacks, or when Master Mold is KO'd, place a Sentinel token with " +
        "5A and 5D into the Field Zone.",
        purchaseCost: 6, energyType: EnergyType.Shield,
        affiliations: ["Villains"],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new PlaceToken(SentinelToken.Id)),
            new AbilityDef(TriggerType.WhenAttacks, Cost: null, Effect: new PlaceToken(SentinelToken.Id)),
            new AbilityDef(TriggerType.WhenKOd, Cost: null, Effect: new PlaceToken(SentinelToken.Id))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Vulcan, "Aggession" - the first card to use
    // CardDef.GrantsOpponentStatDebuff (new this pass); the Global reuses
    // ForceAttack, the same primitive Vulcan's own "Ruler of the
    // Imperium" printing already established.
    public static readonly CardDef VulcanAggession = Character(
        "DPS135", "Vulcan", "Aggession", dieLimit: 2,
        "While Vulcan is active, your opponent's non-fist characters get -2D. Global: Pay Fist. Target " +
        "character die must attack this turn.",
        purchaseCost: 6, energyType: EnergyType.Fist,
        grantsOpponentStatDebuff: new OpponentStatDebuff(AttackDelta: 0, DefenseDelta: -2, ExcludedEnergyType: EnergyType.Fist),
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new ForceAttack(TargetSpec.CharacterDie("target character die")),
            EnergyCost: new EnergyCost(1, EnergyType.Fist))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 5)
        ], set: "DPS");

    // Colossus, "Organic Steel" - the first card to use
    // CardDef.GrantsFirstDamageRedirectToSelf/DieStats.ApplyDamage (new
    // this pass). No AbilityDef needed - purely a live interception every
    // damage-application site now consults. Levels 2/3 carry the real
    // single-burst mark the "*Instead, prevent that damage" clause reads.
    public static readonly CardDef ColossusOrganicSteel = Character(
        "DPS063", "Colossus", "Organic Steel", dieLimit: 3,
        "While Colossus is active, the first time one of your character dice would take damage each turn " +
        "you may have Colossus take that damage instead. *Instead, prevent that damage.",
        purchaseCost: 5, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        grantsFirstDamageRedirectToSelf: true,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 5, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 7, BurstStars: 1)
        ], set: "DPS");

    // Vulcan, "Power Suppression" - the first card to use
    // CardDef.GrantsIgnoresAbilitiesWhileEngaged/DieStats.GetCard (new
    // this pass). No AbilityDef needed - CombatEngine.DeclareBlockers'
    // own RecordVulcanTextBlanking records the engagement every combat.
    public static readonly CardDef VulcanPowerSuppression = Character(
        "DPS095", "Vulcan", "Power Suppression", dieLimit: 3,
        "Ignore the abilities of character dice blocking or blocked by Vulcan.",
        purchaseCost: 5, energyType: EnergyType.Fist,
        grantsIgnoresAbilitiesWhileEngaged: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 5)
        ], set: "DPS");

    // Mister Sinister, "Mutant Supremacist" - the first card to use
    // BlankOpposingTeamText/BlankTargetText (new this pass). The Global's
    // "target attacking character die" is a plain TargetSpec.CharacterDie
    // scoped to Zone.AttackZone only - no new primitive needed for that
    // half beyond the new blanking node itself.
    public static readonly CardDef MisterSinisterMutantSupremacist = Character(
        "DPS083", "Mister Sinister", "Mutant Supremacist", dieLimit: 3,
        "When fielded, ignore all text on opposing character cards (including Global Abilities) (until " +
        "end of turn). Global: Pay 3. Ignore target attacking character die's text until end of turn.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new BlankOpposingTeamText()),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new BlankTargetText(
                    TargetSpec.CharacterDie("target attacking character die", TargetOwnership.Opposing, zones: [Zone.AttackZone])),
                EnergyCost: new EnergyCost(3))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3)
        ], set: "DPS");

    // Gladiator, "Psi Resistance" - Intimidate (same WhenFielded shape as
    // ScarletSpider above) plus a Global shared verbatim with Gladiator's
    // own "Majestor Kallark" printing below (and, per the bulk sheet,
    // Lilandra's Loyalty-granting "Overcrush" printing DPS073 and even
    // Hawkman's unrelated cards) - GrantSelfTargetingImmunityFromAction
    // AndGlobal, no TargetSpec since it protects the whole side rather
    // than one chosen die. The printed cost is "Pay Fist when you
    // attack" - simplified here to a plain Fist cost usable any time the
    // Main/Attack Global window is open (UseGlobalAbility's own gate),
    // dropping the "only during your attack" restriction, since no
    // "currently declared an attack this turn" state exists to check yet
    // and no other authored card needs it (same house convention as
    // every other documented simplification this pass - see Colossus/
    // Vulcan's own remarks for the pattern).
    public static readonly CardDef GladiatorPsiResistance = Character(
        "DPS033", "Gladiator", "Psi Resistance", dieLimit: 4,
        "Intimidate (When fielded, remove target opposing character die from the Field Zone until end of " +
        "turn - place it next to your character cards.) Global: Pay Fist. Your character dice can't be " +
        "the target of Action Dice or Global Abilities (until end of turn).",
        purchaseCost: 5, energyType: EnergyType.Fist,
        affiliations: ["Shi'ar"],
        keywords: [new KeywordInstance("Intimidate")],
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new MoveDie(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Zone.Intimidated)),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new GrantSelfTargetingImmunityFromActionAndGlobal(),
                EnergyCost: new EnergyCost(1, EnergyType.Fist))
        ],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 0, Attack: 7, Defense: 7)
        ], set: "DPS");

    // Gladiator, "The Empire Must Stand" (DPS073) - needed no new
    // primitives at all. "When Lilandra is KO'd, put a Loyalty Counter
    // on Gladiator's card" is exactly the KOdDieMatch(NameContains:
    // "Lilandra") + GrantLoyaltyCounter shape Magneto's own "when one of
    // your Mask character dice is KO'd" already established (see that
    // field's own remarks, which literally anticipate this card as the
    // NameContains example). The Global reuses the identical
    // GrantSelfTargetingImmunityFromActionAndGlobal shape as this
    // card's own "Psi Resistance"/"Majestor Kallark" printings - the raw
    // text's "Pay Fist when you attack" is read as flavor/timing color
    // (Globals are already usable during the Main Step or the Attack
    // Step's Action/Global window per rule 2.6.5.9, same as every other
    // Global; the other two Gladiator printings' own identical Global
    // text has no such qualifier at all), not a genuine new restriction.
    public static readonly CardDef GladiatorTheEmpireMustStand = Character(
        "DPS073", "Gladiator", "The Empire Must Stand", dieLimit: 3,
        "Overcrush When Lilandra is KO'd, put a Loyalty Counter on Gladiator's card. (Loyalty Counters give " +
        "character +1A and +1D.) Global: Pay Fist when you attack. Your character dice can't be the target " +
        "of Action Dice or Global Abilities (until the end of turn).",
        purchaseCost: 6, energyType: EnergyType.Fist,
        affiliations: ["Shi'ar"],
        keywords: [new KeywordInstance("Overcrush")],
        abilities: [
            new AbilityDef(TriggerType.WhenAnotherDieKOd, Cost: null, Effect: new GrantLoyaltyCounter(),
                KOdFilter: new KOdDieMatch(NameContains: "Lilandra")),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new GrantSelfTargetingImmunityFromActionAndGlobal(),
                EnergyCost: new EnergyCost(1, EnergyType.Fist))
        ],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 0, Attack: 7, Defense: 7)
        ], set: "DPS");

    // Gladiator, "Majestor Kallark" - the same Global as "Psi Resistance"
    // above with no Intimidate half.
    public static readonly CardDef GladiatorMajestorKallark = Character(
        "DPS113", "Gladiator", "Majestor Kallark", dieLimit: 2,
        "Global: Pay Fist. Your character dice can't be the target of Action Dice or Global Abilities " +
        "(until end of turn).",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["Shi'ar"],
        abilities: [
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new GrantSelfTargetingImmunityFromActionAndGlobal(),
                EnergyCost: new EnergyCost(1, EnergyType.Fist))
        ],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 6),
            new CharacterFace(FieldingCost: 0, Attack: 7, Defense: 7)
        ], set: "DPS");

    // Deadpool, "#1 Draft Pick" - the printed text only ever does anything
    // "if this game is in the draft format," and this project has no
    // draft-format concept at all (no deckbuilding metadata beyond a
    // fixed 10-card team) - so the ability can never trigger under any
    // game mode this engine actually supports. Authored vanilla, same as
    // any other card whose full printed text has nothing to model yet,
    // rather than faking a condition that's always false.
    public static readonly CardDef DeadpoolDraftPick = Character(
        "DPS028", "Deadpool", "#1 Draft Pick", dieLimit: 4,
        "If this game is in the draft format, at the start of the game pick a card on your team. That " +
        "card costs 1 less to purchase this game (to a minimum of 1).",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["X-Men", "Deadpool Affiliation"],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 7)
        ], set: "DPS");

    // Wolverine, "Pure of Heart" - the first user of
    // SelfFreeFieldingUnlessTeamHasAffiliation (new this round - see its
    // own remarks for why this doesn't fit the existing granter-active-
    // scan GrantsFreeFielding shape).
    public static readonly CardDef WolverinePureOfHeart = Character(
        "DPS056", "Wolverine", "Pure of Heart", dieLimit: 4,
        "If you have no Villains character dice on your team, Wolverine is free to field.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        selfFreeFieldingUnlessTeamHasAffiliation: "Villains",
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3, BurstStars: 1),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 4)
        ], set: "DPS");

    // Corsair, "Recruiting a Crew" - the first user of
    // GrantNextPurchaseGoesToBag (new this round).
    public static readonly CardDef CorsairRecruitingACrew = Character(
        "DPS024", "Corsair", "Recruiting a Crew", dieLimit: 4,
        "When fielded, place the next die you purchase this turn into your bag.",
        purchaseCost: 4, energyType: EnergyType.Fist,
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null, Effect: new GrantNextPurchaseGoesToBag())],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 5)
        ], set: "DPS");

    // Corsair, "Back from Outer Space" (DPS139) - built once before,
    // then reverted when its own test exposed dieLimit: 1 made "Prep a
    // Corsair die from this card" redundant with rule 1.5.3.2's own
    // default KO destination (see the "six more DPS cards, plus one
    // abandoned mid-build" status update). The user has since confirmed
    // the sheet's dieLimit was wrong data - really 4, not 1 - which
    // resolves the exact mismatch that caused the revert: with up to 4
    // physical copies, "a Corsair die from this card" can mean one of
    // the OTHER (up to 3) copies sitting dormant in the Bag/Used Pile/
    // Unpurchased, a real and distinct effect from the die that's
    // already heading to the Prep Area on its own. Rebuilt with the same
    // shape as before - TriggerType.WhenKOd (Corsair reacting to its own
    // KO, the only trigger phrase that fits a card with no explicit
    // timing text and that needs to fire even though Corsair itself just
    // left the board), EffectCondition.OwnCharacterDiceKOdThisTurnAtLeast
    // (new this pass) for the "4 or more of your character dice" count,
    // and PrepDie (already general - Shocking Grasp's own "you may Prep
    // THIS die" just happens to pass TargetSpec.Self) with a new
    // TargetSpec.RequiredCardId (new this pass) filter for "from this
    // card" - deliberately zoned to Bag/UsedPile/Unpurchased, not Prep
    // Area, since a die already there needs no help.
    public static readonly CardDef CorsairBackFromOuterSpace = Character(
        "DPS139", "Corsair", "Back from Outer Space", dieLimit: 4,
        "If 4 or more of your character dice were KO'd this turn, you may Prep a Corsair die from this card." +
        "Deadly",
        purchaseCost: 5, energyType: EnergyType.Fist,
        keywords: [new KeywordInstance("Deadly")],
        abilities: [new AbilityDef(TriggerType.WhenKOd, Cost: null,
            Effect: new Conditional(TargetSpec.Self, EffectCondition.OwnCharacterDiceKOdThisTurnAtLeast,
                Then: new PrepDie(TargetSpec.AnyDie(
                    "a Corsair die from this card", TargetOwnership.Own,
                    zones: [Zone.Bag, Zone.UsedPile, Zone.Unpurchased], optional: true, requiredCardId: "DPS139")),
                CountParam: 4))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 5)
        ], set: "DPS");

    // Rogue, "Surveillance Immunity" - the first user of
    // TargetSpec.ActionDie (new this round). "Fielding Rogue doesn't
    // trigger opposing effects" is left unmodeled - there's no general
    // "this specific fielding is invisible to reactive triggers"
    // mechanism yet (WhenAnotherDieFielded always fires for every
    // fielding), a narrower and rarer gap than the main effect.
    public static readonly CardDef RogueSurveillanceImmunity = Character(
        "DPS089", "Rogue", "Surveillance Immunity", dieLimit: 3,
        "When fielded, send target action die from the Field Zone to your opponent's Used Pile. Fielding " +
        "Rogue doesn't trigger opposing effects.",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new MoveDie(TargetSpec.ActionDie("target action die", TargetOwnership.Opposing), Zone.UsedPile))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Rogue, "Mrs. X" - the first user of SwapAttack (new this round).
    // "You may" simplified to "always swap" (house convention).
    public static readonly CardDef RogueMrsX = Character(
        "DPS049", "Rogue", "Mrs. X", dieLimit: 4,
        "When fielded, you may swap Rogue's A with target opposing character die's A.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new SwapAttack(TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Angel, "Jean Grey's School" - the first user of StaticTeamBonus.
    // RequiredKeyword/ExcludeSelf (new this round). "Founder" is a real
    // KeywordInstance (see the WhenAnotherDieFielded status update), so
    // Angel carries it too, both as its own printed keyword and as the
    // filter its own static bonus checks against OTHER dice.
    public static readonly CardDef AngelJeanGreysSchool = Character(
        "DPS057", "Angel", "Jean Grey's School", dieLimit: 3,
        "Founder When Angel is active, other character dice with Founder get +1A.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        grantsStaticTeamBonus: new StaticTeamBonus(AttackDelta: 1, DefenseDelta: 0, RequiredKeyword: "Founder", ExcludeSelf: true),
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Mystique, "Relentless" - the first user of
    // TargetSpec.MatchesOwnTeamAffiliation (new this round). The +2A
    // half reuses the existing GrantsSelfStatBonusWhileNamedCardActive
    // primitive (Moira's own "+1D while Wolverine is active" printing
    // already uses the identical shape).
    public static readonly CardDef MystiqueRelentless = Character(
        "DPS045", "Mystique", "Relentless", dieLimit: 4,
        "When Wolverine is active, Mystique gets +2A. Global: Pay Mask Mask. Target character die can't " +
        "block this turn if it shares a Team Affiliation with a character card on your team.",
        purchaseCost: 2, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        grantsSelfStatBonusWhileNamedCardActive: new ConditionalSelfStatBonus("Wolverine", AttackDelta: 2, DefenseDelta: 0),
        abilities: [
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new CantBlock(new TargetSpec(
                    TargetOwnership.Any, CharacterDiceOnly: true, TargetSpec.DefaultZones, RequiredEnergyType: null,
                    Count: 1, "target character die", MatchesOwnTeamAffiliation: true)),
                EnergyCost: new EnergyCost(2, EnergyType.Mask))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 1, BurstStars: 1)
        ], set: "DPS");

    // Cable, "Bosom Buddies" - the first user of GrantsNamedCardSupport
    // (new this round).
    public static readonly CardDef CableBosomBuddies = Character(
        "DPS062", "Cable", "Bosom Buddies", dieLimit: 3,
        "While Cable is active, your Deadpool costs 1 less to purchase (to a minimum of 1) and has +2A.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        grantsNamedCardSupport: new NamedCardSupport("Deadpool", PurchaseDiscount: 1, AttackDelta: 2),
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ], set: "DPS");

    // Beast, "Xavier's Dream" - the first user of
    // GrantsSelfStatBonusWhileOwnSidekickActive (new this round; see its
    // own remarks for why Iceman/Cyclops's own "Xavier's Dream"
    // printings, which share the identical gate, aren't included here).
    public static readonly CardDef BeastXaviersDream = Character(
        "DPS138", "Beast", "Xavier's Dream", dieLimit: 1,
        "While you have an active Sidekick die, Beast gets +1A. Overcrush",
        purchaseCost: 3, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Overcrush")],
        grantsSelfStatBonusWhileOwnSidekickActive: new OwnSidekickStatBonus(AttackDelta: 1, DefenseDelta: 0),
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)
        ], set: "DPS");

    // Forge, "Support Technician" - the first user of
    // GrantsOpponentPurchaseSurcharge (new this round).
    public static readonly CardDef ForgeSupportTechnician = Character(
        "DPS071", "Forge", "Support Technician", dieLimit: 3,
        "While Forge is active, your opponents must pay 1 more to purchase a die with purchase cost of 2 " +
        "or less.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        grantsOpponentPurchaseSurcharge: new OpponentPurchaseSurcharge(Amount: 1, MaxPurchaseCost: 2),
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Jean Grey, "Xavier's Dream" - the first user of
    // GrantsOpponentGlobalSurcharge (new this round), with its own extra
    // "and one of your Sidekick dice are active" clause modeled via
    // OpponentGlobalSurcharge.RequiresOwnActiveSidekick.
    public static readonly CardDef JeanGreyXaviersDream = Character(
        "DPS075", "Jean Grey", "Xavier's Dream", dieLimit: 3,
        "Founder While Jean Grey and one of your Sidekick dice are active, your opponents must pay 1 " +
        "extra to use a Global Ability.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        grantsOpponentGlobalSurcharge: new OpponentGlobalSurcharge(Amount: 1, RequiresOwnActiveSidekick: true),
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 6)
        ], set: "DPS");

    // Jean Grey, "Marvel Girl" - the same GrantsOpponentGlobalSurcharge
    // as "Xavier's Dream" above, without the Sidekick clause, plus the
    // first user of SelfFreeFieldingWhileOtherActiveAffiliation (new
    // this round).
    public static readonly CardDef JeanGreyMarvelGirl = Character(
        "DPS115", "Jean Grey", "Marvel Girl", dieLimit: 2,
        "Founder While Jean Grey is active, your opponent must pay 1 extra to use a Global Ability. While " +
        "you have a different X-Men character die in your Field Zone, Jean Grey is free to field.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        grantsOpponentGlobalSurcharge: new OpponentGlobalSurcharge(Amount: 1),
        selfFreeFieldingWhileOtherActiveAffiliation: "X-Men",
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 6)
        ], set: "DPS");

    // Angel, "Xavier's Dream" - the first user of
    // GrantsSidekickImmunityToOpponentGlobalTargeting (new this round).
    public static readonly CardDef AngelXaviersDream = Character(
        "DPS137", "Angel", "Xavier's Dream", dieLimit: 3,
        "While Angel is active, your opponent can't target your Sidekick dice with Global Abilities.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        grantsSidekickImmunityToOpponentGlobalTargeting: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Angel, "Air Support" (DPS097) - the first user of
    // GrantsGainLifeWhenOpponentTargetsOwnCharacterDie (new this pass -
    // see EffectInterpreter.Resolve's own remarks on the shared choke
    // point this is checked at, and the accepted "counts an untaken
    // Conditional branch's own target choice too" approximation). No
    // AbilityDef needed - a passive, always-on grant, same shape as this
    // card's own "Xavier's Dream" printing's Global-targeting immunity.
    public static readonly CardDef AngelAirSupport = Character(
        "DPS097", "Angel", "Air Support", dieLimit: 2,
        "Founder While Angel is active, when an opponent targets one of your character dice, gain 1 life.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        grantsGainLifeWhenOpponentTargetsOwnCharacterDie: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Moira, "Strength of Foresight" - the first user of
    // FieldedDieMatch.MinPurchaseCost (new this round); the WhenFielded
    // half reuses TargetSpec.ActionDie (Rogue "Surveillance Immunity"'s
    // own new primitive above), just scoped to Zone.FieldZone on the
    // opposing side instead of Zone.AttackZone-agnostic.
    public static readonly CardDef MoiraStrengthOfForesight = Character(
        "DPS124", "Moira", "Strength of Foresight", dieLimit: 3,
        "Founder While Moira is active, when you field an X-Men character die with purchase cost of 3 or " +
        "more put a Loyalty Counter on Moira. When fielded, you may send target action die from your " +
        "opponent's Field Zone in their Used Pile.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        abilities: [
            new AbilityDef(TriggerType.WhenAnotherDieFielded, Cost: null, Effect: new GrantLoyaltyCounter(),
                FieldedFilter: new FieldedDieMatch(Ownership: TargetOwnership.Own, AffiliationContains: "X-Men", MinPurchaseCost: 3)),
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new MoveDie(TargetSpec.ActionDie("target action die", TargetOwnership.Opposing, zones: [Zone.FieldZone]), Zone.UsedPile))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Rogue, "Unity Squad" - the first user of GrantsFieldingCostReduction
    // (new this round).
    public static readonly CardDef RogueUnitySquad = Character(
        "DPS129", "Rogue", "Unity Squad", dieLimit: 2,
        "While Rogue is active, your X-Men character dice cost 1 less to field. Teamwatch - Rogue gets +2A.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Teamwatch")],
        grantsFieldingCostReduction: new FieldingCostReduction(1, "X-Men"),
        abilities: [new AbilityDef(TriggerType.Teamwatch, Cost: null,
            Effect: new ModifyStat(TargetSpec.Self, AttackDelta: 2, DefenseDelta: null))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Magneto, "Visionary" - the first user of
    // GrantsMinimumBlockersRequirement (new this round). The Global's
    // "if you have any dice in your Prep Area, you may draw..." reuses
    // the EXISTING PrepAreaEmpty condition with Then/Else swapped
    // (Then a no-op Sequence([]), Else the real PrepFromBag) rather than
    // adding a redundant "PrepAreaNotEmpty" condition just to avoid the
    // inversion.
    public static readonly CardDef MagnetoVisionary = Character(
        "DPS081", "Magneto", "Visionary", dieLimit: 3,
        "While Magneto is active, your Brotherhood of Mutants character dice can only be blocked by 2 " +
        "or more character dice. Teamwatch - Prep a die from your bag. Global Pay Mask. Once per turn, " +
        "during your turn, if you have any dice in your Prep Area, you may draw a die and place it in " +
        "your Prep Area.",
        purchaseCost: 5, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Teamwatch")],
        grantsMinimumBlockersRequirement: new MinimumBlockersRequirement(2, "Brotherhood of Mutants"),
        abilities: [
            new AbilityDef(TriggerType.Teamwatch, Cost: null, Effect: new PrepFromBag()),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Conditional(
                    TargetSpec.Self, EffectCondition.PrepAreaEmpty, Then: new Sequence([]), Else: new PrepFromBag()),
                EnergyCost: new EnergyCost(1, EnergyType.Mask), OncePerTurn: true)
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 8)
        ], set: "DPS");

    // Wolverine, "Hardened by Madripoor" - the first user of
    // EffectCondition.OwnActiveAffiliationOrKeywordCountAtLeast (new
    // this round). The keyword is granted UNCONDITIONALLY here
    // (simplification - see the condition's own remarks) with only the
    // actual spin gated by the Conditional; "spin this die to level 1"
    // needed the new SpinToCharacterLevel node (also new this round) -
    // Energize only ever fires FROM an energy face, and the ordinary
    // Spin node is a level DELTA that no-ops on a die that isn't already
    // on a character face (see DieStats.SpinLevel's own guard), so it
    // couldn't do the energy-to-character conversion this text needs.
    public static readonly CardDef WolverineHardenedByMadripoor = Character(
        "DPS096", "Wolverine", "Hardened by Madripoor", dieLimit: 3,
        "When you have at least 3 active X-Men character dice, Wolverine gains \"Energize Spin this die " +
        "to level 1\"",
        purchaseCost: 5, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OwnActiveAffiliationOrKeywordCountAtLeast,
                Then: new SpinToCharacterLevel(TargetSpec.Self, Level: 1),
                AffiliationParam: "X-Men", CountParam: 3))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3, BurstStars: 1),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 4)
        ], set: "DPS");

    // Beast, "Combat Ready" - the first user of SelfFirstPurchaseSurcharge
    // (new this round).
    public static readonly CardDef BeastCombatReady = Character(
        "DPS098", "Beast", "Combat Ready", dieLimit: 2,
        "Founder When Beast attacks, Prep a die from your bag. The first Beast die you purchase each " +
        "game costs 1 extra.",
        purchaseCost: 2, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        selfFirstPurchaseSurcharge: 1,
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null, Effect: new PrepFromBag())],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)
        ], set: "DPS");

    // Dark Phoenix, "Malevolent" - the first user of
    // GrantsSelfPurchaseDiscountIfOpponentHasAffiliation (new this
    // round). The Global reuses the exact Ko+GrantNextPurchaseDiscount
    // Sequence DarkPhoenixEnemyOfTheShiar's own printing already
    // established; the WhenFielded's "if it's an X-Men character die"
    // follow-up reuses the SAME TargetSpec instance as the Ko immediately
    // before it (structurally identical, so it resolves from the shared
    // per-ability cache instead of re-querying a board the Ko itself
    // just changed) - same "the target already answered, don't ask
    // again" reasoning as Shocking Grasp's own "if that character is
    // KO'd, [...]" follow-up.
    public static readonly CardDef DarkPhoenixMalevolent = Character(
        "DPS027", "Dark Phoenix", "Malevolent", dieLimit: 4,
        "Dark Phoenix costs 1 less to purchase if your opponent has an X-Men character on their team. " +
        "When fielded, KO target character die. If it's an X-Men character die, deal your opponent 1 " +
        "damage. Global: Pay Bolt and KO one of your character dice. The next die you purchase this " +
        "turn costs 2 less (to a minimum of 1).",
        purchaseCost: 7, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        grantsSelfPurchaseDiscountIfOpponentHasAffiliation: new SelfPurchaseDiscountIfOpponentHasAffiliation("X-Men", 1),
        abilities: [
            new AbilityDef(TriggerType.WhenFielded, Cost: null,
                Effect: new Sequence([
                    new Ko(TargetSpec.CharacterDie("target character die")),
                    new Conditional(
                        TargetSpec.CharacterDie("target character die"), EffectCondition.TargetHasAffiliation,
                        Then: new DealDamage(1, TargetSpec.Player("your opponent", TargetOwnership.Opposing)),
                        AffiliationParam: "X-Men")
                ])),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Sequence([
                    new Ko(TargetSpec.CharacterDie("one of your character dice", TargetOwnership.Own)),
                    new GrantNextPurchaseDiscount(2)
                ]),
                EnergyCost: new EnergyCost(1, EnergyType.Bolt))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Cable, "High Stakes" - the first user of DoublePrintedAttackOfEach
    // (new this round).
    public static readonly CardDef CableHighStakes = Character(
        "DPS102", "Cable", "High Stakes", dieLimit: 2,
        "When Cable attacks, double the printed A of all your other character dice.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new DoublePrintedAttackOfEach(
                TargetSpec.CharacterDie("your other character dice", TargetOwnership.Own, matchAll: true)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 5)
        ], set: "DPS");

    // Gambit, "I Like Solitaire" - the first user of
    // EffectCondition.OnlyCharacterFieldedThisTurn and
    // GrantCantFieldCharacterDiceThisTurn (both new this round); reuses
    // the existing RerollAndMoveUnlessCharacter primitive (Gambit's OTHER
    // printing, "Unless I Got Someone to Play With", already established
    // it) for the "reroll all opposing character dice; non-character
    // results go to their Used Pile" half.
    public static readonly CardDef GambitILikeSolitaire = Character(
        "DPS072", "Gambit", "I Like Solitaire", dieLimit: 3,
        "When fielded, if you have fielded no other character dice this turn, reroll all opposing " +
        "character dice. Move any that roll an energy face to their Used Pile. You may not field any " +
        "more character dice this turn.",
        purchaseCost: 5, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenFielded, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OnlyCharacterFieldedThisTurn,
                Then: new Sequence([
                    new RerollAndMoveUnlessCharacter(
                        TargetSpec.CharacterDie("all opposing character dice", TargetOwnership.Opposing, matchAll: true),
                        Zone.UsedPile),
                    new GrantCantFieldCharacterDiceThisTurn()
                ])))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1, BurstStars: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5)
        ], set: "DPS");

    // Cyclops, "Utopia Realized" - the first user of
    // EffectCondition.OwnCharacterDiceInFieldZoneAtLeast (new this
    // round, the own-side mirror of the existing opponent-side version).
    public static readonly CardDef CyclopsUtopiaRealized = Character(
        "DPS105", "Cyclops", "Utopia Realized", dieLimit: 2,
        "While you have 2 or more character dice in the Field Zone, when Cyclops attacks, deal 3 " +
        "damage to target character die.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OwnCharacterDiceInFieldZoneAtLeast,
                Then: new DealDamage(3, TargetSpec.CharacterDie("target character die")),
                CountParam: 2))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 4)
        ], set: "DPS");

    // Cyclops, "Xavier's Dream" (DPS140) - the first user of
    // EffectCondition.OwnSidekickActive and DividedDamageAmongChosenTargets
    // (both new this pass - see their own remarks, including
    // DividedDamageAmongChosenTargets' deliberate "split evenly, not a
    // fully arbitrary player-assigned amount" simplification). X's own
    // live count reuses DealDamagePerMatchingDie's "CountFilter" idiom,
    // scoped to Zone.FieldZone only - same "the attacking die has
    // already left the Field Zone by the time WhenAttacks resolves"
    // reasoning this card's own "Utopia Realized" printing's
    // OwnCharacterDiceInFieldZoneAtLeast condition already established.
    public static readonly CardDef CyclopsXaviersDream = Character(
        "DPS140", "Cyclops", "Xavier's Dream", dieLimit: 1,
        "While you have a Sidekick die active, when Cyclops attacks deal X damage divided how you choose " +
        "among any number of target character dice, where X is the number of your character dice in the " +
        "Field Zone.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OwnSidekickActive,
                Then: new DividedDamageAmongChosenTargets(
                    TargetSpec.CharacterDie("your character dice in the Field Zone", TargetOwnership.Own,
                        zones: [Zone.FieldZone], matchAll: true),
                    TargetSpec.CharacterDie("any number of target character dice", TargetOwnership.Any))))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 4)
        ], set: "DPS");

    // Mutant Research Program - the first user of
    // EffectCondition.OwnActiveAffiliationOrKeywordCountAtLeast alongside
    // Wolverine "Hardened by Madripoor" above; "draw and roll N dice" is
    // just DrawDice(N) - it already rolls what it draws (see DrawDice's
    // own remarks).
    public static readonly CardDef MutantResearchProgram = BasicAction(
        "DPS008", "Mutant Research Program",
        "If you have at least 2 active Founder character dice, draw and roll 3 dice. Otherwise, draw " +
        "and roll a die.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OwnActiveAffiliationOrKeywordCountAtLeast,
                Then: new DrawDice(3), Else: new DrawDice(1),
                AffiliationParam: "Founder", CountParam: 2))],
        purchaseCost: 2, set: "DPS");

    // Living the Dream - the first user of
    // EffectCondition.OwnTeamWideLoyaltyCounterCountAtLeast (new this
    // round) and the same Continuous/ContinuousResolve shape Lab Test
    // established. "Overcrush" here is a GRANTED keyword (via
    // GrantKeyword, Applied per rule 3.4.3.9), not a printed one on this
    // card - unlike the bulk sheet's own naive keyword scan, which
    // mis-attributed it as printed (the same class of parsing trap
    // Magik's own granted keyword hit before).
    public static readonly CardDef LivingTheDream = BasicAction(
        "DPS006", "Living the Dream",
        "Continuous: If among all character cards on your team you have at least 3 Loyalty Counters, " +
        "your character dice get +1A and Overcrush (until the end of turn).",
        keywords: [new KeywordInstance("Continuous")],
        abilities: [new AbilityDef(TriggerType.ContinuousResolve, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OwnTeamWideLoyaltyCounterCountAtLeast,
                Then: new Sequence([
                    new ModifyStat(
                        TargetSpec.CharacterDie("your character dice", TargetOwnership.Own, matchAll: true),
                        AttackDelta: 1, DefenseDelta: null),
                    new GrantKeyword(
                        TargetSpec.CharacterDie("your character dice", TargetOwnership.Own, matchAll: true), "Overcrush")
                ]),
                CountParam: 3))],
        purchaseCost: 4, set: "DPS");

    // Colossus, "Piotr" - the first user of DealDamagePerMatchingDie and
    // TargetSpec.MinLevel (both new this round). The Target uses MatchAll
    // (not the ordinary TargetSpec.Player factory) since TurnEngine's own
    // EndOfYourTurn loop resolves every ability with a hardcoded `_ => []`
    // resolver (documented there as safe only for abilities needing no
    // real target choice) - MatchAll sidesteps that entirely by not
    // needing a caller-supplied choice at all, matching "your opponent"
    // not really being a decision in the first place.
    public static readonly CardDef ColossusPiotr = Character(
        "DPS103", "Colossus", "Piotr", dieLimit: 2,
        "While Colossus is active, at the end of your turn, each of your level 2 or 3 character dice " +
        "deals your opponent 2 damage (not 2 damage per Colossus die)",
        purchaseCost: 6, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.EndOfYourTurn, Cost: null,
            Effect: new DealDamagePerMatchingDie(
                CountFilter: TargetSpec.CharacterDie("your level 2 or 3 character dice", TargetOwnership.Own, minLevel: 2),
                AmountPerMatch: 2,
                Target: new TargetSpec(
                    TargetOwnership.Opposing, CharacterDiceOnly: false, EligibleZones: [], RequiredEnergyType: null,
                    Count: 1, "your opponent", PlayersAllowed: true, MatchAll: true)))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 6, Defense: 5, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 8, Defense: 7, BurstStars: 1)
        ], set: "DPS");

    // D'Ken, "Shi'ar Civil War" - the first user of
    // GrantsOpponentAbilityBlankWhileActive (new this round) - a
    // continuous, cross-player extension of DieStats.GetCard's own
    // blanking choke point (see its own remarks), bundling the "lose
    // their abilities" and "are free to field" halves into one grant
    // since both apply to the same qualifying set of opposing dice.
    public static readonly CardDef DKenShiarCivilWar = Character(
        "DPS141", "D'Ken", "Shi'ar Civil War", dieLimit: 1,
        "While D'Ken is active, opposing character dice with Purchase Cost of 3 or less lose their " +
        "abilities and are free to field.",
        purchaseCost: 6, energyType: EnergyType.Shield,
        affiliations: ["Villains", "Shi'ar"],
        grantsOpponentAbilityBlankWhileActive: new OpponentAbilityBlankGrant(MaxPurchaseCost: 3, AlsoFreeToField: true),
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ], set: "DPS");

    // D'Ken, "Obsessed" (DPS066) - deliberately left isImplemented: false.
    // "If you take combat damage this turn" is a real, buildable per-
    // player per-turn flag (not attempted here only because the second
    // half makes it useless in isolation - see next). "You may use an
    // action die from EITHER PLAYER'S Used Pile" needs TurnEngine.
    // UseActionDie's own validation reworked to source from Zone.
    // UsedPile (not just the caller's own Zone.ReservePool) and
    // potentially the OPPONENT's dice too - a real rework of that
    // method's core eligibility check, not a bespoke bool grant checked
    // alongside it the way this pass's other UseActionDie hook (Moira)
    // was. Left vanilla rather than a half-working "tracks the trigger
    // but can't do anything with it" implementation.
    public static readonly CardDef DKenObsessed = Character(
        "DPS066", "D'Ken", "Obsessed", dieLimit: 3,
        "While D'Ken is active, if you take combat damage this turn you may use an action die from either " +
        "player's Used Pile.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["Villains", "Shi'ar"],
        isImplemented: false,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ], set: "DPS");

    // D'Ken, "M'Kraan Crystal" (DPS106) - the WhenAttacks clause reuses
    // Falcon's own "Prep a Sidekick from your Used Pile" shape (PrepDie +
    // a real chosen TargetSpec, not PrepFromBag's random draw - Used
    // Pile contents are visible/known, unlike the bag, so "Prep a die
    // from your Used Pile" with no "target" wording still means a real
    // choice here, same reading Falcon's own Teamwatch text already
    // established). The second clause - "while a D'Ken die is in your
    // Used Pile, you take no more than 7 damage during an opponent's
    // turn (further damage dealt to you is reduced to 0)" - is
    // deliberately left out, isImplemented: false: it's a real player-
    // life damage CAP, distinct from every existing damage-reduction
    // mechanism here (those all reduce damage to a DIE, via DieStats.
    // ApplyDamage's single choke point - no such choke point exists for
    // damage dealt directly to a PLAYER; `.Life -=` is written at 14
    // independent call sites across TurnEngine/CombatEngine/
    // EffectInterpreter, several of which are voluntary payments - life
    // taxes, MayPayLife - that must NOT be capped as "damage," so this
    // isn't a drop-in reuse of an existing pattern the way most other
    // partial cards in this file are). Worth a real design pass of its
    // own rather than a rushed/wrong implementation - see the "Scripting
    // policy" note at the top of this file.
    public static readonly CardDef DKenMKraanCrystal = Character(
        "DPS106", "D'Ken", "M'Kraan Crystal", dieLimit: 2,
        "When D'Ken attacks, Prep a die from your Used Pile. While a D'Ken die is in your Used Pile, you " +
        "take no more than 7 damage during an opponent's turn (further damage dealt to you is reduced to 0).",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["Villains", "Shi'ar"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new PrepDie(TargetSpec.AnyDie("a die from your Used Pile", TargetOwnership.Own, zones: [Zone.UsedPile])))],
        isImplemented: false,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 6)
        ], set: "DPS");

    // Bishop, "Time Traveller" - the first user of
    // GrantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy (new this
    // round).
    public static readonly CardDef BishopTimeTraveller = Character(
        "DPS099", "Bishop", "Time Traveller", dieLimit: 2,
        "If you only use energy from Bishop dice to purchase a character die, Prep that die instead of " +
        "adding it to your Used Pile",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        grantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy: true,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Blink, "Exiles Team Leader" - the first user of
    // EffectCondition.OwnOtherAttackingAffiliateCountAtLeast (new this
    // round). Infiltrate is a GRANTED keyword here (via GrantKeyword),
    // not printed on Blink's own card - unlike the bulk sheet's own
    // naive keyword scan, which mis-attributed it as printed (the same
    // class of parsing trap Magik's own granted keyword hit before).
    public static readonly CardDef BlinkExilesTeamLeader = Character(
        "DPS060", "Blink", "Exiles Team Leader", dieLimit: 3,
        "When Blink attacks with at least 2 other X-Men character dice, each of your X-Men character " +
        "dice in the Field Zone gains Infiltrate.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenAttacks, Cost: null,
            Effect: new Conditional(
                TargetSpec.Self, EffectCondition.OwnOtherAttackingAffiliateCountAtLeast,
                Then: new GrantKeyword(
                    TargetSpec.CharacterDie("each of your X-Men character dice in the Field Zone",
                        TargetOwnership.Own, zones: [Zone.FieldZone], requiredAffiliations: ["X-Men"], matchAll: true),
                    "Infiltrate"),
                AffiliationParam: "X-Men", CountParam: 2))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5)
        ], set: "DPS");

    // Blink, "Warp Portals" (DPS100) - deliberately left isImplemented:
    // false. "You may pay Mask and 1 life to CANCEL that Global Ability"
    // needs a real interrupt/cancellation primitive - the ability to
    // stop an already-invoked Global from resolving its effect at all -
    // which doesn't exist anywhere in this engine (TurnEngine.
    // UseGlobalAbility has no such hook, and "cancel" is a different
    // shape from every existing "reduce/prevent/redirect" mechanism,
    // none of which stop an ability from running in the first place).
    // Would also need a genuinely new "pay energy AND life together" cost
    // shape (MayPayLife only handles life) and its own once-per-turn
    // tracking independent of the opponent's own Global usage. Left
    // vanilla rather than a guessed-at partial.
    public static readonly CardDef BlinkWarpPortals = Character(
        "DPS100", "Blink", "Warp Portals", dieLimit: 2,
        "While Blink is active, once per turn when your opponent uses a Global Ability you may pay Mask " +
        "and 1 life to cancel that Global Ability.",
        purchaseCost: 4, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        isImplemented: false,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5)
        ], set: "DPS");

    // Radicalization - the first user of GrantAffiliation/
    // DieInstance.AppliedAffiliations (new this round); the burst-KO
    // follow-up reuses the existing OnDoubleBurstFace/Conditional
    // machinery Rally/Take Cover already established.
    public static readonly CardDef Radicalization = BasicAction(
        "DPS012", "Radicalization",
        "Deal 3 damage to target X-Men or Brotherhood of Mutants character die. **Also, KO target " +
        "Sidekick character die. Global: Pay Shield. Target character die gains X-Men or Brotherhood " +
        "of Mutants (until end of turn).",
        abilities: [
            new AbilityDef(TriggerType.WhenUsed, Cost: null,
                Effect: new Sequence([
                    new DealDamage(3, TargetSpec.CharacterDie(
                        "target X-Men or Brotherhood of Mutants character die",
                        requiredAffiliations: ["X-Men", "Brotherhood of Mutants"])),
                    new Conditional(TargetSpec.Self, EffectCondition.OnDoubleBurstFace,
                        Then: new Ko(TargetSpec.Sidekick("target Sidekick character die")))
                ])),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new GrantAffiliation(
                    TargetSpec.CharacterDie("target character die"), ["X-Men", "Brotherhood of Mutants"]),
                EnergyCost: new EnergyCost(1, EnergyType.Shield))
        ],
        purchaseCost: 3, set: "DPS");

    // Tight Ranks - the first user of
    // EffectCondition.OwnActiveDiceShareAnyAffiliationAtLeast and
    // TargetSpec.RequiresLoyaltyCounter (both new this round).
    public static readonly CardDef TightRanks = BasicAction(
        "DPS016", "Tight Ranks",
        "If you have at least 3 active character dice that share a Team Affiliation, KO target " +
        "character die. Global: Pay Shield. Target character die with at least one Loyalty Counter " +
        "gets -2A and -2D.",
        abilities: [
            new AbilityDef(TriggerType.WhenUsed, Cost: null,
                Effect: new Conditional(
                    TargetSpec.Self, EffectCondition.OwnActiveDiceShareAnyAffiliationAtLeast,
                    Then: new Ko(TargetSpec.CharacterDie("target character die")),
                    CountParam: 3)),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new ModifyStat(
                    TargetSpec.CharacterDie("target character die with at least one Loyalty Counter", requiresLoyaltyCounter: true),
                    AttackDelta: -2, DefenseDelta: -2),
                EnergyCost: new EnergyCost(1, EnergyType.Shield))
        ],
        purchaseCost: 3, set: "DPS");

    // Greetings from Krakoa - the first user of Spin.
    // AttackBonusPerActualSpinUp (new this round), alongside
    // TargetSpec.RequiresLoyaltyCounter (Tight Ranks above). Scoped to
    // TargetOwnership.Own throughout (both the spin and the +2A) - the
    // printed text doesn't say "your" on the spin half, but the +2A half
    // explicitly does, and applying the spin to an opponent's Loyalty-
    // Countered dice while only buffing your own would need the effect
    // to track ownership per-die separately, which Spin's own shape
    // doesn't support; scoping the whole thing to your own dice is the
    // simpler, more common-case-faithful reading.
    public static readonly CardDef GreetingsFromKrakoa = BasicAction(
        "DPS004", "Greetings from Krakoa",
        "Spin up each character whose card has a Loyalty Counter. Each of your dice that spins up " +
        "gets +2A.",
        abilities: [new AbilityDef(TriggerType.WhenUsed, Cost: null,
            Effect: new Spin(
                TargetSpec.CharacterDie("each of your characters whose card has a Loyalty Counter",
                    TargetOwnership.Own, requiresLoyaltyCounter: true, matchAll: true),
                LevelDelta: 1, AttackBonusPerActualSpinUp: 2))],
        purchaseCost: 3, set: "DPS");

    // Jubilee, "Fireworks" - the first user of
    // TriggerType.WhenXMenEnergySpentOnGlobalOrField (new this round).
    public static readonly CardDef JubileeFireworks = Character(
        "DPS116", "Jubilee", "Fireworks", dieLimit: 2,
        "While Jubilee is active, when you spend energy from an [X-Men] die to use a Global Ability " +
        "or field a character, deal 1 damage to target opponent or character die.",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        abilities: [new AbilityDef(TriggerType.WhenXMenEnergySpentOnGlobalOrField, Cost: null,
            Effect: new DealDamage(1, TargetSpec.CharacterDieOrPlayer("target opponent or character die", TargetOwnership.Opposing)))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 3)
        ], set: "DPS");

    // Beast, "First Class" - the first user of
    // TriggerType.WhenAnotherDieAttacks/AttackedDieMatch (both new this
    // round, the CombatEngine.DeclareAttackers-scanned mirror of
    // WhenAnotherDieFielded/FieldedDieMatch) - Ownership.Own matches
    // Cyclops "First Class" (DPS025)'s own WhenAnotherDieFielded
    // precedent for the identical "a character die with Founder" shape.
    public static readonly CardDef BeastFirstClass = Character(
        "DPS058", "Beast", "First Class", dieLimit: 3,
        "Founder While Beast is active, when a character die with Founder attacks, Prep a die from " +
        "your bag.",
        purchaseCost: 3, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        abilities: [new AbilityDef(TriggerType.WhenAnotherDieAttacks, Cost: null,
            Effect: new PrepFromBag(),
            AttackedFilter: new AttackedDieMatch(TargetOwnership.Own, RequiredKeyword: "Founder"))],
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 2, Defense: 1),
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2)
        ], set: "DPS");

    // Bishop, "Tortured Timeline" (DPS019) - the first user of
    // GrantsRerollOrSpinProtection (new this pass - see DieStats.
    // IsProtectedFromOpponentRerollOrSpin). Unconditional: blocks
    // opponent-caused reroll and level-spin (up OR down), but not
    // SpinToEnergyFace (Wolverine "Tough for the Kids"/DPS152's own
    // printing is the one that names that mechanism instead). No
    // AbilityDef needed - purely a passive, always-on grant.
    public static readonly CardDef BishopTorturedTimeline = Character(
        "DPS019", "Bishop", "Tortured Timeline", dieLimit: 4,
        "Opposing effects can't cause Bishop to be rerolled, or cause you to spin a Bishop die up or down.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        grantsRerollOrSpinProtection: new RerollOrSpinProtection(
            ProtectsReroll: true, ProtectsLevelSpin: true, ProtectsEnergyFaceSpin: false),
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Wolverine, "Tough for the Kids" (DPS152) - GrantsRerollOrSpin
    // Protection's second user, this time conditional (Regenerate is
    // already built - see DieStats.HasKeyword) and naming a DIFFERENT
    // spin mechanism than Bishop above (SpinToEnergyFace, not the
    // ordinary level-up/down). Global reuses PrepFromBag (Bishop "I'm
    // Back"'s own already-established "Prep a die from your bag" node) -
    // no target choice in the card text (no "target die"), just a
    // random draw-and-Prep, same as every other PrepFromBag user.
    public static readonly CardDef WolverineToughForTheKids = Character(
        "DPS152", "Wolverine", "Tough for the Kids", dieLimit: 1,
        "Regenerate *If you have at least 3 different active X-Men, Wolverine can't be spun to an energy " +
        "face or rerolled by your opponent. Global: Pay Fist. Once per turn, on your turn, Prep a die from " +
        "your bag.",
        purchaseCost: 5, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Regenerate")],
        grantsRerollOrSpinProtection: new RerollOrSpinProtection(
            ProtectsReroll: true, ProtectsLevelSpin: false, ProtectsEnergyFaceSpin: true,
            RequiresDistinctActiveAffiliation: "X-Men", RequiresDistinctActiveAffiliationCount: 3),
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new PrepFromBag(),
            EnergyCost: new EnergyCost(1, EnergyType.Fist),
            OncePerTurn: true)],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3, BurstStars: 1),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 4)
        ], set: "DPS");

    // Wolverine, "Trainer" (DPS136) - the first user of GrantsSpinsUp
    // InSympathyWithOwnCharacterDice (new this pass - see TurnEngine.
    // CheckSympatheticSpin). The Global is the identical PrepFromBag/
    // OncePerTurn shape as this card's own "Tough for the Kids"
    // printing above.
    public static readonly CardDef WolverineTrainer = Character(
        "DPS136", "Wolverine", "Trainer", dieLimit: 2,
        "Awaken: Target sidekick character die gains Deadly.*When you spin up another character die,spin " +
        "Wolverine up also. Global: Pay Fist. Once per turn, on your turn, Prep a die from your bag",
        purchaseCost: 4, energyType: EnergyType.Fist,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Deadly"), new KeywordInstance("Awaken")],
        grantsSpinsUpInSympathyWithOwnCharacterDice: true,
        abilities: [
            new AbilityDef(TriggerType.Awaken, Cost: null,
                Effect: new GrantKeyword(TargetSpec.Sidekick("target sidekick character die"), "Deadly")),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new PrepFromBag(),
                EnergyCost: new EnergyCost(1, EnergyType.Fist),
                OncePerTurn: true)
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 2, BurstStars: 1),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3, BurstStars: 1),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 4)
        ], set: "DPS");

    // Bishop, "I'm Back" - the first user of
    // GrantsSelfPrepWhenSpentAsEnergyForFielding (new this round).
    public static readonly CardDef BishopImBack = Character(
        "DPS059", "Bishop", "I'm Back", dieLimit: 3,
        "If you spend this die as energy to field a character die, add this die to your Prep Area.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["X-Men"],
        grantsSelfPrepWhenSpentAsEnergyForFielding: true,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Iceman, "Xavier's Dream" - the first user of
    // SelfAttackEqualsDefenseWhileOwnSidekickActive (new this round).
    public static readonly CardDef IcemanXaviersDream = Character(
        "DPS142", "Iceman", "Xavier's Dream", dieLimit: 1,
        "Founder While you have a Sidekick die active, Iceman's A is equal to his D.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder")],
        selfAttackEqualsDefenseWhileOwnSidekickActive: true,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6)
        ], set: "DPS");

    // Iceman, "Mr Ice Guy" - the first user of StaticTeamBonus.
    // SidekicksOnly (new this round); the Energize half reuses Cable
    // "High Stakes"'s own DoublePrintedAttackOfEach with a single chosen
    // target instead of MatchAll.
    public static readonly CardDef IcemanMrIceGuy = Character(
        "DPS114", "Iceman", "Mr Ice Guy", dieLimit: 2,
        "Founder Energize - Double target character die's printed A until the end of turn. While " +
        "Iceman is active, your Sidekick dice get +1A.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Founder"), new KeywordInstance("Energize")],
        grantsStaticTeamBonus: new StaticTeamBonus(AttackDelta: 1, DefenseDelta: 0, SidekicksOnly: true),
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            Effect: new DoublePrintedAttackOfEach(TargetSpec.CharacterDie("target character die")))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 4),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 6),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6)
        ], set: "DPS");

    // Emma Frost, "Influential" - the first user of
    // GrantsAffiliationsToSidekicks (new this round), alongside
    // StaticTeamBonus.SidekicksOnly (Iceman "Mr Ice Guy" above).
    public static readonly CardDef EmmaFrostInfluential = Character(
        "DPS030", "Emma Frost", "Influential", dieLimit: 4,
        "While Emma Frost is active, your Sidekick dice get +1A and +1D, and gain the Hellfire Club " +
        "affiliation.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["Hellfire Club"],
        grantsStaticTeamBonus: new StaticTeamBonus(AttackDelta: 1, DefenseDelta: 1, SidekicksOnly: true),
        grantsAffiliationsToSidekicks: ["Hellfire Club"],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7)
        ], set: "DPS");

    // Forge, "More Than Firepower" - the first user of
    // GrantsSelfPrepFromBagIfFieldedWithEnergy (new this round, checked
    // procedurally in TurnEngine.Field rather than through the ability
    // queue - see the field's own remarks).
    public static readonly CardDef ForgeMoreThanFirepower = Character(
        "DPS031", "Forge", "More Than Firepower", dieLimit: 4,
        "When fielded, if you spent Bolt energy to field Forge, Prep a die from your bag.",
        purchaseCost: 3, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        grantsSelfPrepFromBagIfFieldedWithEnergy: new SelfPrepFromBagIfFieldedWithEnergy(RequiredEnergyType: EnergyType.Bolt),
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Forge, "Reverse Engineer" (DPS111) - deliberately left
    // isImplemented: false. "Roll it. If it shows an action face you may
    // use it's effect" means COMMANDEERING an opponent's Action die's
    // own ability - running the die's card's Effect tree with Forge's
    // controller as ctx.ControllerId instead of the die's real
    // controller. Nothing in this engine's EffectContext/AbilityQueue
    // pipeline supports resolving a card's ability under a DIFFERENT
    // controller than whoever actually owns/used the die - a real,
    // deep change to how abilities are attributed, not a bespoke bool
    // grant the way this pass's other reactive hooks were. Left vanilla
    // rather than guessed at.
    public static readonly CardDef ForgeReverseEngineer = Character(
        "DPS111", "Forge", "Reverse Engineer", dieLimit: 2,
        "While Forge is active, if an opponent uses an action die, roll it. If it shows an action face " +
        "you may use it's effect.",
        purchaseCost: 4, energyType: EnergyType.Bolt,
        affiliations: ["X-Men"],
        isImplemented: false,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4)
        ], set: "DPS");

    // Professor X, "Dreamer" - the second user of
    // GrantsSelfPrepFromBagIfFieldedWithEnergy (Forge above is the
    // first), this time the RequiredAffiliation half; the Energize half
    // reuses existing MoveDie/RequiredAffiliations primitives.
    public static readonly CardDef ProfessorXDreamer = Character(
        "DPS047", "Professor X", "Dreamer", dieLimit: 4,
        "If you spend an X-Men die to field Professor X, Prep a die from your bag. Energize - Move an " +
        "X-Men die from your Used Pile to your Prep Area.",
        purchaseCost: 5, energyType: EnergyType.Mask,
        affiliations: ["X-Men"],
        keywords: [new KeywordInstance("Energize")],
        grantsSelfPrepFromBagIfFieldedWithEnergy: new SelfPrepFromBagIfFieldedWithEnergy(RequiredAffiliation: "X-Men"),
        abilities: [new AbilityDef(TriggerType.Energize, Cost: null,
            // AnyDie + RequiredAffiliations, not CharacterDie - a Used
            // Pile die is always unrolled (rule 1.6.8), same gap
            // ProfessorXUncannyLeadership's own identical Energize text
            // already worked around.
            Effect: new MoveDie(
                TargetSpec.AnyDie("an X-Men die from your Used Pile", TargetOwnership.Own,
                    zones: [Zone.UsedPile], requiredAffiliations: ["X-Men"]),
                Zone.PrepArea))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 1, Defense: 9)
        ], set: "DPS");

    // Mystique, "Freedom Force" - the first user of
    // GrantsOwnDamageReductionFromOpponentAbilities (new this round -
    // see DieStats.ApplyDamage's own remarks for the "ability vs.
    // combat" distinction this needed). The WhenKOd half is the first
    // user of TargetSpec.MinPurchaseCost.
    public static readonly CardDef MystiqueFreedomForce = Character(
        "DPS085", "Mystique", "Freedom Force", dieLimit: 3,
        "When Mystique is active, reduce damage from opposing character abilities by 1. When Mystique " +
        "is KO'd, you may move a Brotherhood of Mutants die with purchase cost 4 or more from your " +
        "Used Pile to your Prep Area.",
        purchaseCost: 2, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants"],
        grantsOwnDamageReductionFromOpponentAbilities: 1,
        abilities: [new AbilityDef(TriggerType.WhenKOd, Cost: null,
            Effect: new MoveDie(
                TargetSpec.AnyDie(
                    "a Brotherhood of Mutants die with purchase cost 4 or more from your Used Pile",
                    TargetOwnership.Own, zones: [Zone.UsedPile], requiredAffiliations: ["Brotherhood of Mutants"],
                    minPurchaseCost: 4),
                Zone.PrepArea))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 1, BurstStars: 1)
        ], set: "DPS");

    // Mister Sinister, "Biologist" - the first user of
    // GrantsPreventsNonCombatDamageToOtherOwnDice (new this round).
    public static readonly CardDef MisterSinisterBiologist = Character(
        "DPS148", "Mister Sinister", "Biologist", dieLimit: 1,
        "While Mister Sinister is active, prevent non-combat damage dealt to your other character " +
        "dice. Global: Pay 3. Target character die gains Overcrush.",
        purchaseCost: 6, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        keywords: [new KeywordInstance("Overcrush")],
        grantsPreventsNonCombatDamageToOtherOwnDice: true,
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new GrantKeyword(TargetSpec.CharacterDie("target character die"), "Overcrush"),
            EnergyCost: new EnergyCost(3))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3)
        ], set: "DPS");

    // Mister Sinister, "Dark Experimentation" (DPS123) - "after blockers
    // are declared" read the same way Gladiator ("The Empire Must
    // Stand", DPS073)'s own "Pay Fist WHEN YOU ATTACK" was earlier this
    // pass: descriptive timing color, not a distinct new sub-step-scoped
    // trigger this engine would need to build - modeled as a plain
    // WhenAttacks reusing MayPayLife (already built for Mister Sinister
    // "Geneticist"/DPS043's own third clause) for "you may pay 2 life
    // and have him gain +3A." The Global's two Sidekick-from-Used-Pile
    // actions reuse FieldDie/PrepDie against TargetSpec.Sidekick, same
    // shape already established elsewhere (Falcon's own Teamwatch Prep,
    // rule 2.6.3's "ability-driven fielding is free by default" note).
    public static readonly CardDef MisterSinisterDarkExperimentation = Character(
        "DPS123", "Mister Sinister", "Dark Experimentation", dieLimit: 2,
        "When Mister Sinister attacks, after blockers are declared, you may pay 2 life and have him gain " +
        "+3A Global: Pay 2. Field a Sidekick from your Used Pile, Prep a Sidekick from your Used Pile.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        abilities: [
            new AbilityDef(TriggerType.WhenAttacks, Cost: null,
                Effect: new MayPayLife(2, new ModifyStat(TargetSpec.Self, AttackDelta: 3, DefenseDelta: null))),
            new AbilityDef(TriggerType.Global, Cost: null,
                // Two DELIBERATELY different Description strings, even
                // though the underlying TargetSpec is otherwise
                // identical - Execute's own upfront resolution caches by
                // structural TargetSpec equality (rule 3.2.5's "resolved
                // once, shared" idiom for a genuinely repeated
                // reference), which would otherwise make "Field a
                // Sidekick" and "Prep another Sidekick" resolve to the
                // SAME chosen die - wrong here, since the card means two
                // independent choices, not one shared reference the way
                // Shocking Grasp's own repeated target is.
                Effect: new Sequence([
                    new FieldDie(TargetSpec.Sidekick("a Sidekick die from your Used Pile to field", TargetOwnership.Own, zones: [Zone.UsedPile]), Free: true),
                    new PrepDie(TargetSpec.Sidekick("a Sidekick die from your Used Pile to prep", TargetOwnership.Own, zones: [Zone.UsedPile]))
                ]),
                EnergyCost: new EnergyCost(2))
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 6, Defense: 3)
        ], set: "DPS");

    // Dark Phoenix, "Destructive Force" - the first user of
    // GrantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent (new
    // this round - the first WhenDamaged-shaped text this engine models,
    // via direct injection in DieStats.ApplyDamage rather than a real
    // AbilityDef/TriggerType.WhenDamaged, since "each opponent" needs no
    // external target choice - see ApplyDamage's own remarks). The
    // Global reuses DarkPhoenixEnemyOfTheShiar's own
    // Ko+GrantNextPurchaseDiscount Sequence verbatim (both Dark Phoenix
    // printings that have this Global share identical text).
    public static readonly CardDef DarkPhoenixDestructiveForce = Character(
        "DPS107", "Dark Phoenix", "Destructive Force", dieLimit: 2,
        "When an opposing character die damages Dark Phoenix, she deals that much damage to each " +
        "opponent. Global: Pay Bolt and KO one of your character dice. The next die you purchase this " +
        "turn costs 2 less (to a minimum of 1).",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["Villains"],
        grantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent: true,
        abilities: [new AbilityDef(TriggerType.Global, Cost: null,
            Effect: new Sequence([
                new Ko(TargetSpec.CharacterDie("one of your character dice", TargetOwnership.Own)),
                new GrantNextPurchaseDiscount(2)
            ]),
            EnergyCost: new EnergyCost(1, EnergyType.Bolt))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 5),
            new CharacterFace(FieldingCost: 2, Attack: 7, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 8, Defense: 8)
        ], set: "DPS");

    // Blob, "Immovable" - the first user of GrantsBlocksMultipleAttackers
    // and GrantsReturnsKOdOpposingSidekickToBag (both new this round;
    // see CombatEngine.ValidateBlockerCapacity/ResolveFastOrSlowDamage's
    // own remarks - the former also closes a real pre-existing gap,
    // rule 2.7.2.4's own "each Character die may block only one
    // attacking Character die" default was never actually enforced
    // anywhere in this engine before this card).
    public static readonly CardDef BlobImmovable = Character(
        "DPS101", "Blob", "Immovable", dieLimit: 2,
        "Each of your Blob dice may block 3 character dice instead of 1. When Blob KO's an opponent's " +
        "Sidekick die, return it to your opponent's bag.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["Brotherhood of Mutants"],
        grantsBlocksMultipleAttackers: 3,
        grantsReturnsKOdOpposingSidekickToBag: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 6),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 8)
        ], set: "DPS");

    // Deathbird, "Usurper" - the first user of
    // GrantsDamageWhenOpposingHighDefenseDieIsKOdInCombat (new this
    // round - closes the "who caused this KO" gap flagged several
    // rounds back, at least for the combat-scoped case; see
    // CombatEngine.ResolveFastOrSlowDamage's own remarks for why no
    // real per-die attribution was needed here after all).
    public static readonly CardDef DeathbirdUsurper = Character(
        "DPS069", "Deathbird", "Usurper", dieLimit: 3,
        "While Deathbird is active, when you KO an opposing character die with 3D or greater, deal 3 " +
        "damage to your opponent.",
        purchaseCost: 3, energyType: EnergyType.Shield,
        affiliations: ["Villains", "Shi'ar"],
        grantsDamageWhenOpposingHighDefenseDieIsKOdInCombat: true,
        levels: [
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 2),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4)
        ], set: "DPS");

    // Firestar, "Amazing Friend" (ASM117, not DPS - deliberately pulled in
    // from Amazing Spider-Man to give TriggerType.WhenDamaged a real
    // consumer). WhenDamaged was a genuinely unwired trigger point in this
    // engine before this card (see TurnEngine.ResolveWhenDamagedReactions'
    // own remarks) - Dark Phoenix's own retaliation bypassed it entirely
    // since her text needed no real target choice, but "deal 1 damage to
    // target character OR PLAYER" is a real one, so this is the first card
    // that actually needs the queue-based trigger wired up for real.
    public static readonly CardDef FirestarAmazingFriend = Character(
        "ASM117", "Firestar", "Amazing Friend", dieLimit: 2,
        "When Firestar takes damage, deal 1 damage to target character or player.",
        purchaseCost: 5, energyType: EnergyType.Bolt,
        affiliations: ["Spider-Friends"],
        abilities: [new AbilityDef(TriggerType.WhenDamaged, Cost: null,
            Effect: new DealDamage(1, TargetSpec.CharacterDieOrPlayer("target character or player")))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 2),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 3, Attack: 5, Defense: 4)
        ], set: "ASM");

    // Lilandra, "Freedom Fighter" (DPS078) - "your opponent must spend 1
    // to use each Action Die." The first card needing real energy-cost
    // plumbing on Action-Die use at all (TurnEngine.UseActionDie had none
    // before this - see its own remarks); no AbilityDef at all, since the
    // whole effect is the surcharge itself, enforced directly at the
    // engine choke point rather than through the ability queue.
    public static readonly CardDef LilandraFreedomFighter = Character(
        "DPS078", "Lilandra", "Freedom Fighter", dieLimit: 4,
        "While Lilandra is active, your opponent must spend 1 to use each Action Die.",
        purchaseCost: 4, energyType: EnergyType.Shield,
        affiliations: ["Shi'ar"],
        grantsOpponentActionDieEnergySurcharge: 1,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 2, Defense: 3),
            new CharacterFace(FieldingCost: 2, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5)
        ], set: "DPS");

    // Lilandra, "Majestrix" (DPS145) - "your opponent must pay 2 life to
    // use an Action Die or Global Ability." A mandatory LIFE tax instead
    // of an energy one, covering BOTH usage points - see TurnEngine.
    // UseActionDie/UseGlobalAbility's own matching halves.
    public static readonly CardDef LilandraMajestrix = Character(
        "DPS145", "Lilandra", "Majestrix", dieLimit: 4,
        "While Lilandra is active, your opponent must pay 2 life to use an Action Die or Global Ability.",
        purchaseCost: 5, energyType: EnergyType.Shield,
        affiliations: ["Shi'ar"],
        grantsOpponentPaysLifeToUseActionOrGlobal: 2,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 4, Defense: 5),
            new CharacterFace(FieldingCost: 3, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Lilandra, "Grand Admiral of the Guard" (DPS118) - the first user
    // of GrantsRerollsUnblockedAttackerToPrepAreaIfCharacterFace (new
    // this pass - see CombatEngine.AssignCombatDamage's own unblocked-
    // attacker branch). No AbilityDef needed - a passive, always-on
    // grant.
    public static readonly CardDef LilandraGrandAdmiralOfTheGuard = Character(
        "DPS118", "Lilandra", "Grand Admiral of the Guard", dieLimit: 2,
        "While Lilandra is active, if one of your character dice attacks and is unblocked, reroll them " +
        "after damage is dealt. If they land on a character face, put them in your Prep Area instead of " +
        "your Used Pile.",
        purchaseCost: 6, energyType: EnergyType.Shield,
        affiliations: ["Shi'ar"],
        grantsRerollsUnblockedAttackerToPrepAreaIfCharacterFace: true,
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 3),
            new CharacterFace(FieldingCost: 1, Attack: 3, Defense: 5),
            new CharacterFace(FieldingCost: 1, Attack: 5, Defense: 6)
        ], set: "DPS");

    // Magneto, "Master of Magnetism" (DPS121) and Mystique, "She Walks
    // Among Us" (DPS149) both read "...to an energy face of your
    // opponent's choice" - real opponent-choice-of-face machinery (a
    // PendingChoice asked of the SPUN die's own controller, not the
    // ability's) doesn't exist in this engine and would be a real,
    // separate primitive. Per the user's own call, simplified instead to
    // always land on the double energy face (SpinToEnergyFace's existing
    // `Amount` param, already built for Professor X/Iceman's own single-
    // face use of the same node) - a rational opponent facing this choice
    // would almost always take the higher-value double face anyway, so
    // this is a deliberate approximation, not a guessed default.
    public static readonly CardDef MagnetoMasterOfMagnetism = Character(
        "DPS121", "Magneto", "Master of Magnetism", dieLimit: 4,
        "Teamwatch - Spin target opposing character die to an energy face of your opponent's choice. Global: " +
        "Pay Mask. Once per turn, during your turn, if you have no dice in your Prep Area, you may draw a die " +
        "and place it in your Prep Area.",
        purchaseCost: 5, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants"],
        keywords: [new KeywordInstance("Teamwatch")],
        abilities: [
            new AbilityDef(TriggerType.Teamwatch, Cost: null,
                Effect: new SpinToEnergyFace(
                    TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Amount: 2)),
            new AbilityDef(TriggerType.Global, Cost: null,
                Effect: new Conditional(
                    TargetSpec.Self, EffectCondition.PrepAreaEmpty, Then: new PrepFromBag(), Else: new Sequence([])),
                EnergyCost: new EnergyCost(1, EnergyType.Mask), OncePerTurn: true)
        ],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 4, Defense: 4),
            new CharacterFace(FieldingCost: 2, Attack: 5, Defense: 7),
            new CharacterFace(FieldingCost: 3, Attack: 6, Defense: 8)
        ], set: "DPS");

    public static readonly CardDef MystiqueSheWalksAmongUs = Character(
        "DPS149", "Mystique", "She Walks Among Us", dieLimit: 1,
        "Teamwatch - Spin target opposing character die to an energy face of your opponent's choice.",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants", "Villains"],
        keywords: [new KeywordInstance("Teamwatch")],
        abilities: [new AbilityDef(TriggerType.Teamwatch, Cost: null,
            Effect: new SpinToEnergyFace(
                TargetSpec.CharacterDie("target opposing character die", TargetOwnership.Opposing), Amount: 2))],
        levels: [
            new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1),
            new CharacterFace(FieldingCost: 2, Attack: 1, Defense: 1, BurstStars: 1)
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

    // Madelyne Pryor, "Aspiring" (DPS119) - the first user of
    // GrantsPrepsTwoOwnDiceWhenOpponentDrawsExtraDuringClearAndDraw (new
    // this pass - see TurnEngine.ClearAndDraw's own swarmBonusDice hook).
    // No AbilityDef needed - a passive, always-on grant.
    public static readonly CardDef MadelynePryorAspiring = Character(
        "DPS119", "Madelyne Pryor", "Aspiring", dieLimit: 2,
        "While Madelyne Pryor is active, if an opponent draws an extra die during their Clear and Draw " +
        "Step, Prep 2 dice from your bag (no matter how many extra dice your opponent draws, you only " +
        "Prep 2 dice).",
        purchaseCost: 3, energyType: EnergyType.Mask,
        affiliations: ["Brotherhood of Mutants", "Hellfire Club"],
        grantsPrepsTwoOwnDiceWhenOpponentDrawsExtraDuringClearAndDraw: true,
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
            BlobMGHDependent, SupremeIntelligencePsionicCollective, ToadLookingForComradery, Rally,
            GambitAceInTheHole, DeathbirdWarOfKings, DeadpoolMoreThanAChumpBlocker, RonanTheAccuserNoExceptions,
            GambitUnlessIGotSomeoneToPlayWith, PsylockeAdvancedTelekineticCombatant, StormQueen,
            MagikSorceressOfLimbo, PsylockeTelepath, StormCloudCover,
            MasterMoldTargetingMutants, MasterMoldUntoldElectronicExpertise, MasterMoldInexplicableDurability,
            PhoenixEternalFlame, MagikBetterThanBelasco, ProfessorXUncannyLeadership, IcemanIcyInterference,
            CyclopsDefendingThePhoenix, RogueStrengthAbsorption, MoiraIfItsReal,
            KittyPrydeExperiencedLeader, SabretoothDoISmellWeakness, PsylockeHeiress,
            MagnetoFounderOfTheBrotherhood, SabretoothYouReadyToParty, ToadJourneyIntoMisery,
            JubileeRebelliousNature, CyclopsFirstClass, JubileeXMenFieldLeader,
            JubileeThingsNeverChange, KittyPrydeHeadmistress, CorsairCriminalRecord, PhoenixPsionicMaelstrom,
            DarkPhoenixEnemyOfTheShiar, MagikWielderOfTheSoulsword, TakeCover, DeadpoolCollectThis,
            MystiqueTaughtByMagneto, IcemanFrozenFistsOfFury,
            RonanTheAccuserNoMercy, EmmaFrostManipulative, EmmaFrostFinesse, SentinelToken, MasterMoldEndlessSentinels,
            VulcanAggession, ColossusOrganicSteel, VulcanPowerSuppression, MisterSinisterMutantSupremacist,
            GladiatorPsiResistance, GladiatorMajestorKallark,
            DeadpoolDraftPick, WolverinePureOfHeart, CorsairRecruitingACrew, RogueSurveillanceImmunity,
            RogueMrsX, AngelJeanGreysSchool, MystiqueRelentless, CableBosomBuddies, BeastXaviersDream,
            ForgeSupportTechnician, JeanGreyXaviersDream, JeanGreyMarvelGirl, AngelXaviersDream,
            MoiraStrengthOfForesight,
            RogueUnitySquad, MagnetoVisionary, WolverineHardenedByMadripoor, BeastCombatReady,
            DarkPhoenixMalevolent, CableHighStakes, GambitILikeSolitaire, CyclopsUtopiaRealized,
            MutantResearchProgram, LivingTheDream,
            ColossusPiotr, DKenShiarCivilWar, BishopTimeTraveller, BlinkExilesTeamLeader, Radicalization,
            TightRanks, GreetingsFromKrakoa, JubileeFireworks, BeastFirstClass,
            BishopImBack, IcemanXaviersDream, IcemanMrIceGuy, EmmaFrostInfluential,
            ForgeMoreThanFirepower, ProfessorXDreamer,
            MystiqueFreedomForce, MisterSinisterBiologist, DarkPhoenixDestructiveForce, BlobImmovable, DeathbirdUsurper,
            FirestarAmazingFriend, LilandraFreedomFighter, LilandraMajestrix, MagnetoMasterOfMagnetism, MystiqueSheWalksAmongUs,
            MisterSinisterGeneticist, OrganicSteelPreventDamage, DampeningCollar, CorsairBackFromOuterSpace,
            BishopTorturedTimeline, WolverineToughForTheKids, MakingTheTeam, Mutation, GladiatorTheEmpireMustStand,
            WolverineTrainer, AngelAirSupport, DKenMKraanCrystal, CyclopsXaviersDream,
            Archnemesis, TheFrontLine, Explosion, MoiraItsNotADream, SabretoothAmIInterrupting,
            CorsairLeadingTheStarjammers, DKenObsessed, BlinkWarpPortals, ForgeReverseEngineer,
            LilandraGrandAdmiralOfTheGuard, MadelynePryorAspiring, MisterSinisterDarkExperimentation
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
