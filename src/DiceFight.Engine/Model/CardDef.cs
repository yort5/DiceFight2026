using DiceFight.Engine.Effects;

namespace DiceFight.Engine.Model;

// Rule 1.3.5/1.3.6 - one die face: fielding cost, attack, defense at a given
// level, plus which burst symbol (if any) that level's face carries.
public sealed record CharacterFace(int FieldingCost, int Attack, int Defense, int? BurstStars = null);

// Static card data - the printed card. Never mutates during a game.
// RawText is kept verbatim (rule 1.2.8/1.2.9) for display and as the
// source of truth while Abilities are authored incrementally; a card with
// an empty Abilities list is legal and simply has no engine-executable
// text yet (see RULES_ENGINE_DESIGN.md's data-pipeline section).
public sealed record CardDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Subtitle { get; init; }
    public required CardType Type { get; init; }
    public required int PurchaseCost { get; init; }
    public IReadOnlyList<EnergyType> EnergyTypes { get; init; } = [];
    public IReadOnlyList<string> Affiliations { get; init; } = [];
    public Alignment? Alignment { get; init; }

    // Rule 1.2.10/1.2.11 - Max # on the card; Basic Actions always use 3.
    public required int DieLimit { get; init; }

    // Rule 1.3.5 - Character dice only. Empty for Action/Basic Action cards.
    public IReadOnlyList<CharacterFace> Levels { get; init; } = [];

    public string RawText { get; init; } = string.Empty;
    public IReadOnlyList<KeywordInstance> Keywords { get; init; } = [];
    public IReadOnlyList<AbilityDef> Abilities { get; init; } = [];

    // Bespoke text like Darkseid's "While Darkseid is active, your
    // Sidekicks gain Swarm" - a static, continuously-recomputed grant
    // (not a discrete triggered ability), scoped to "your Sidekicks,"
    // which per DieStats.CountsAsSidekick already includes any active
    // Ally-keyword die too. See DieStats.HasKeyword for how this is
    // actually applied live; empty for every card that doesn't grant
    // anything.
    public IReadOnlyList<string> GrantsToSidekicks { get; init; } = [];

    // Rule 3.4.5.7 - "an attack and/or defense value modifier provided by
    // a Character die with a 'while active' ability is a Static ability."
    // Captain Marvel's "While Captain Marvel is active, your Character
    // dice get +1 attack and +1 defense" is the textbook case: a live,
    // continuously-recomputed team-wide bonus, not a discrete modifier
    // object like AppliedModifiers (see DieStats.StaticTeamBonusFor and
    // its EffectiveAttack/EffectiveDefense callers). Null for every card
    // that doesn't grant one. Deliberately narrow in scope to "your
    // Character dice get +A/+D while I'm active," optionally scoped to
    // one affiliation (StaticTeamBonus.RequiredAffiliation) - not a
    // general static-ability framework (no debuffs, no keyword-scoped or
    // "while attacking/blocking"-only variants yet - see rule 3.4.5.6).
    public StaticTeamBonus? GrantsStaticTeamBonus { get; init; }

    // Sabretooth ("Do I Smell... Weakness?", DPS091): "gets +1A for each
    // opposing character die with 2D or less." Psylocke ("Heiress",
    // DPS128): "gets +2A for each of your X-Men dice in the Prep Area."
    // Both are a live, continuously-recomputed SELF attack bonus scaled
    // by a COUNT of other dice matching some filter - reuses TargetSpec/
    // LegalTargets.Query as that filter (Ownership/zones/
    // RequiredAffiliations/MaxDefense already exist; Count/Description
    // are irrelevant here and ignored) rather than inventing a second,
    // narrower filter shape just for counting. See DieStats.
    // EffectiveAttack for how this is actually applied live. Null for
    // every card that doesn't have one.
    public SelfAttackBonusPerMatchingDie? GrantsSelfAttackBonusPerMatchingDie { get; init; }

    // Deadpool ("Collect THIS!", DPS108): "your character dice with
    // fielding cost of 2 are free to field." Mystique ("Taught by
    // Magneto", DPS125): "Brotherhood of Mutants character dice are free
    // to field." A live, continuously-recomputed granter-side check, same
    // scan shape as GrantsStaticTeamBonus/GrantsToSidekicks - see
    // TurnEngine.Field's own IsFreeToField for where this is consulted.
    public FreeFieldingGrant? GrantsFreeFielding { get; init; }

    // Vulcan ("Aggession", DPS135): "your opponent's non-fist characters
    // get -2D." The cross-player counterpart to GrantsStaticTeamBonus -
    // that field's own granter scan is always same-controller (see
    // DieStats.StaticTeamBonusFor), so this is a genuinely different
    // shape, not a reuse of it. ExcludedEnergyType ("non-fist") is an
    // EXCLUDE filter on the RECEIVING die's own energy type, the
    // opposite sense from RequiredAffiliations elsewhere in this file -
    // still just one dimension, so a positive "RequiredEnergyType"
    // wasn't reused here on purpose (this card's own text is a
    // negation, and there's no current need to support both senses at
    // once). See DieStats.EffectiveAttack/EffectiveDefense for where
    // this is actually applied live.
    public OpponentStatDebuff? GrantsOpponentStatDebuff { get; init; }

    // Colossus ("Organic Steel", DPS063): "the first time one of your
    // character dice would take damage each turn you may have Colossus
    // take that damage instead. *Instead, prevent that damage." A
    // granter-side flag (same scan shape as GrantsFreeFielding/
    // GrantsStaticTeamBonus) consulted by the single shared choke point
    // every damage-application site now funnels through - see
    // DieStats.ApplyDamage. "You may" is simplified to "always redirect"
    // (same house convention as every other "you may [beneficial
    // action]" card this session) - the burst check on the redirecting
    // die's OWN current face (not the original recipient's) is what
    // decides redirect-and-take vs. prevent-outright.
    public bool GrantsFirstDamageRedirectToSelf { get; init; }

    // Vulcan ("Power Suppression", DPS095): "Ignore the abilities of
    // character dice blocking or blocked by Vulcan." Combat-scoped, not
    // continuous like every other Grants* field in this file - Vulcan
    // must be directly engaged (an attacker or one of its blockers), not
    // merely active elsewhere, so this is recorded once per combat by
    // CombatEngine.DeclareBlockers (mirroring RecordDeadlyEngagements'
    // own shape) into GameState.BlankedDieIds rather than recomputed
    // live like the other Grants* fields.
    public bool GrantsIgnoresAbilitiesWhileEngaged { get; init; }

    // Angel ("Xavier's Dream", DPS137): "While Angel is active, your
    // opponent can't target your Sidekick dice with Global Abilities." A
    // continuous, granter-active-scan counterpart to Gladiator's own
    // temporary Global-activated whole-team version - reuses the exact
    // same Trigger-aware filtering LegalTargets.Query added for Gladiator
    // (TriggerType.Global), just scoped to Sidekick dice specifically and
    // gated on this card being active rather than a one-shot Global use.
    public bool GrantsSidekickImmunityToOpponentGlobalTargeting { get; init; }

    // Beast ("Xavier's Dream", DPS138): "While you have an active
    // Sidekick die, Beast gets +1A." Iceman/Cyclops's own "Xavier's
    // Dream" printings share the identical "own active Sidekick" gate,
    // but on a live A=D relationship and a divided-damage WhenAttacks
    // respectively - neither fits this flat-delta shape, so only Beast's
    // is modeled this round (see DieStats.HasActiveSidekick for the
    // shared board-state check, and DESIGN_LOG for why the other two
    // stayed out). Same "named card active" condition SHAPE as
    // ConditionalSelfStatBonus just above, just keyed on "any active
    // Sidekick" instead of a specific card name - a genuinely different
    // dimension, not a reuse.
    public OwnSidekickStatBonus? GrantsSelfStatBonusWhileOwnSidekickActive { get; init; }

    // Wolverine ("Pure of Heart", DPS056): "If you have no Villains
    // character dice on your team, Wolverine is free to field." Unlike
    // GrantsFreeFielding (an ACTIVE granter card blessing some OTHER
    // matching die), this is the card's own SELF-referential fielding
    // cost, conditioned on the controller's TEAM ROSTER (Player.
    // TeamCardIds), not board state - checked directly against the die
    // being fielded's own card in TurnEngine.IsFreeToField, no granter
    // scan involved (the die isn't even active yet at the moment this is
    // checked, so it couldn't participate in a granter scan anyway).
    public string? SelfFreeFieldingUnlessTeamHasAffiliation { get; init; }

    // Jean Grey ("Marvel Girl", DPS115): "While you have a different
    // X-Men character die in your Field Zone, Jean Grey is free to
    // field." The board-state counterpart to
    // SelfFreeFieldingUnlessTeamHasAffiliation just above (roster vs.
    // live board), same self-referential shape.
    public string? SelfFreeFieldingWhileOtherActiveAffiliation { get; init; }

    // Forge ("Support Technician", DPS071): "your opponents must pay 1
    // more to purchase a die with purchase cost of 2 or less." A
    // continuous, granter-active-scan surcharge on the OPPONENT's own
    // purchases - the purchase-side mirror of GrantsOpponentStatDebuff's
    // cross-player shape. See TurnEngine.Purchase for enforcement.
    public OpponentPurchaseSurcharge? GrantsOpponentPurchaseSurcharge { get; init; }

    // Jean Grey ("Xavier's Dream"/DPS075, "Marvel Girl"/DPS115 - both
    // printings say "your opponent must pay 1 extra to use a Global
    // Ability"): a continuous, granter-active-scan surcharge on the
    // OPPONENT's Global Ability energy cost. Deliberately scoped to
    // Global only, not Action Dice too (unlike Lilandra's own similar-
    // sounding text) - Action Die usage has no energy-cost plumbing at
    // all yet (TurnEngine.UseActionDie takes no energyDieIdsToSpend
    // parameter), a bigger, still-open gap; see DESIGN_LOG for why
    // Lilandra's two printings stayed out this round. RequiresOwnActiveSidekick
    // models "Xavier's Dream"'s own extra "and one of your Sidekick dice
    // are active" clause - "Marvel Girl" leaves it false (no such
    // clause). See TurnEngine.UseGlobalAbility for enforcement.
    public OpponentGlobalSurcharge? GrantsOpponentGlobalSurcharge { get; init; }

    // Cable ("Bosom Buddies", DPS062): "your Deadpool costs 1 less to
    // purchase (to a minimum of 1) and has +2A." A continuous, granter-
    // active-scan buff aimed at a SPECIFIC named card (any printing,
    // matched by CardDef.Name) rather than an affiliation/keyword/whole-
    // team - genuinely different from GrantsStaticTeamBonus (which never
    // names a card) or GrantsSelfStatBonusWhileNamedCardActive (which
    // buffs the GRANTER itself, not some other named card). See
    // DieStats.EffectiveAttack/EffectiveDefense for the stat half and
    // TurnEngine.Purchase for the discount half.
    public NamedCardSupport? GrantsNamedCardSupport { get; init; }

    // Rogue ("Unity Squad", DPS129): "your X-Men character dice cost 1
    // less to field." A continuous, granter-active-scan FIELDING cost
    // reduction - the discount counterpart to GrantsFreeFielding (all-
    // the-way-to-zero) - consulted in TurnEngine.Field before the
    // free-fielding check, since a die that's already free needs no
    // further reduction.
    public FieldingCostReduction? GrantsFieldingCostReduction { get; init; }

    // Magneto ("Visionary", DPS081): "your Brotherhood of Mutants
    // character dice can only be blocked by 2 or more character dice."
    // A continuous, granter-active-scan restriction on how the OPPOSING
    // (blocking) player may legally respond to an attack from a matching
    // die - enforced in CombatEngine.DeclareBlockers, which rejects a
    // block assignment that gives a matching attacker exactly 1 blocker
    // (0 - unblocked - and MinBlockers-or-more both remain legal).
    public MinimumBlockersRequirement? GrantsMinimumBlockersRequirement { get; init; }

    // Beast ("Combat Ready", DPS098): "the first Beast die you purchase
    // each game costs 1 extra." A SELF-referential purchase surcharge
    // (unlike GrantsOpponentPurchaseSurcharge, this applies to the
    // purchasing player's OWN purchase of THIS card), consumed exactly
    // once per game - see Player.SurchargedFirstPurchaseCardIds and
    // TurnEngine.Purchase for enforcement.
    public int? SelfFirstPurchaseSurcharge { get; init; }

    // Dark Phoenix ("Malevolent", DPS027): "Dark Phoenix costs 1 less to
    // purchase if your opponent has an X-Men character on their team." A
    // SELF-referential purchase discount conditioned on the OPPONENT's
    // roster (Player.TeamCardIds), not board state or the purchaser's own
    // roster - checked directly against the card being purchased in
    // TurnEngine.Purchase, same "no granter scan, the card checks its
    // own condition" shape as SelfFreeFieldingUnlessTeamHasAffiliation.
    public SelfPurchaseDiscountIfOpponentHasAffiliation? GrantsSelfPurchaseDiscountIfOpponentHasAffiliation { get; init; }

    // D'Ken ("Shi'ar Civil War", DPS141): "opposing character dice with
    // Purchase Cost of 3 or less lose their abilities." A continuous,
    // cross-player counterpart to Mister Sinister's one-shot/attack-
    // triggered blanks - see DieStats.GetCard's own
    // IsBlankedByOpposingContinuousGrant for the enforcement choke point.
    public OpponentAbilityBlankGrant? GrantsOpponentAbilityBlankWhileActive { get; init; }

    // Bishop ("Time Traveller", DPS099): "if you only use energy from
    // Bishop dice to purchase a character die, Prep that die instead of
    // adding it to your Used Pile." A SELF-referential check against the
    // energy actually spent (not a granter scan, and not gated on an
    // active die of this specific printing - the text describes a
    // property of Bishop-named energy itself, not a "while active"
    // ability) - see TurnEngine.Purchase for enforcement, which matches
    // the spent dice's own card NAME against this card's Name.
    public bool GrantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy { get; init; }

    // Bishop ("I'm Back", DPS059): "if you spend THIS DIE as energy to
    // field a character die, add this die to your Prep Area." Unlike
    // GrantsPrepInsteadOfUsedPileIfPurchasedWithSameNameEnergy just
    // above (about the PURCHASED die's own destination), this is about
    // the ENERGY die's own destination - checked per-spent-die in
    // TurnEngine.Field, overriding wherever SpendEnergy just put it.
    public bool GrantsSelfPrepWhenSpentAsEnergyForFielding { get; init; }

    // See CardDef.SelfPrepFromBagIfFieldedWithEnergy's own remarks
    // (Forge "More Than Firepower"/DPS031, Professor X "Dreamer"/
    // DPS047).
    public SelfPrepFromBagIfFieldedWithEnergy? GrantsSelfPrepFromBagIfFieldedWithEnergy { get; init; }

    // Emma Frost ("Influential", DPS030): "...and gain the Hellfire Club
    // affiliation." The affiliation counterpart to GrantsToSidekicks
    // (keywords) - same "granter must be active, grantee just needs to
    // count as a Sidekick right now" shape, checked in DieStats.
    // HasAffiliation.
    public IReadOnlyList<string> GrantsAffiliationsToSidekicks { get; init; } = [];

    // Iceman ("Xavier's Dream", DPS142): "while you have a Sidekick die
    // active, Iceman's A is equal to his D." A full LIVE override, not
    // an additive bonus - see DieStats.EffectiveAttack for where this
    // short-circuits the normal face+modifiers accumulation entirely.
    public bool SelfAttackEqualsDefenseWhileOwnSidekickActive { get; init; }

    // Mystique ("Freedom Force", DPS085): "reduce damage from opposing
    // character abilities by 1." See DieStats.ApplyDamage's own remarks
    // for the choke point and the simplification this makes.
    public int GrantsOwnDamageReductionFromOpponentAbilities { get; init; }

    // Mister Sinister ("Biologist", DPS148): "prevent non-combat damage
    // dealt to your other character dice." See DieStats.ApplyDamage.
    public bool GrantsPreventsNonCombatDamageToOtherOwnDice { get; init; }

    // Dark Phoenix ("Destructive Force", DPS107): "when an opposing
    // character die damages Dark Phoenix, she deals that much damage to
    // each opponent." See DieStats.ApplyDamage - injected directly there
    // rather than through an AbilityDef/AbilityQueue round-trip, since
    // "each opponent" (a fixed player, not a choice) needs no external
    // target resolution.
    public bool GrantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent { get; init; }

    // Blob ("Immovable", DPS101): "each of your Blob dice may block 3
    // character dice instead of 1." Rule 2.7.2.4's own default ("each
    // Character die may block only one attacking Character die, unless
    // a card effect states otherwise") was never actually enforced
    // anywhere in this engine before this card - see
    // CombatEngine.ValidateBlockerCapacity for both the new default
    // check and this grant's own exception to it. Null means "use the
    // rule's own default of 1," not "no restriction" - every die is
    // subject to this cap, granted or not.
    public int? GrantsBlocksMultipleAttackers { get; init; }

    // Blob ("Immovable", DPS101): "when Blob KO's an opponent's Sidekick
    // die, return it to your opponent's bag." See CombatEngine.
    // ResolveFastOrSlowDamage's own engagement-based approximation of
    // "KO'd by Blob" (no general damage-source attribution exists in
    // this engine).
    public bool GrantsReturnsKOdOpposingSidekickToBag { get; init; }

    // Deathbird ("Usurper", DPS069): "while Deathbird is active, when
    // you KO an opposing character die with 3D or greater, deal 3
    // damage to your opponent." See CombatEngine.
    // ResolveFastOrSlowDamage - "who caused this KO" needs no real
    // attribution here, since within one combat resolution a KO'd die
    // was always caused by whoever's on the OTHER side.
    public bool GrantsDamageWhenOpposingHighDefenseDieIsKOdInCombat { get; init; }

    // Psylocke ("Adventurer", DPS048): "While Wolverine is active,
    // Psylocke gains Deadly" - a live, continuously-recomputed SELF
    // keyword grant conditioned on some OTHER named card being active
    // (any printing, any controller - the text doesn't say "your"),
    // unlike GrantsToSidekicks (unconditional, scoped to the granter's
    // own Sidekicks) or GrantsStaticTeamBonus (unconditional, whole
    // team). See DieStats.HasKeyword for how this is actually checked
    // live. Null for every card that doesn't have one - deliberately
    // narrow (exactly this one shape: grant ONE keyword to yourself
    // while ONE named card is active), not a general conditional-
    // ability framework; see GrantsSelfStatBonusWhileNamedCardActive
    // just below for the stat-bonus counterpart this comment used to
    // flag as unbuilt.
    public ConditionalSelfKeywordGrant? GrantsSelfKeywordWhileNamedCardActive { get; init; }

    // Moira ("If It's Real", DPS084): "While Wolverine is active, Moira
    // gets +1D." Mystique ("Relentless", DPS045)'s own "+2A while
    // Wolverine is active" is the same shape - same condition as
    // GrantsSelfKeywordWhileNamedCardActive just above, a stat bonus
    // instead of a keyword grant. See DieStats.EffectiveAttack/
    // EffectiveDefense for how this is actually applied live. Null for
    // every card that doesn't have one.
    public ConditionalSelfStatBonus? GrantsSelfStatBonusWhileNamedCardActive { get; init; }

    // Kitty Pryde ("Headmistress", DPS077): "can't be targeted by your
    // opponent" while Wolverine is active - same "named card active
    // anywhere on the board" condition as the two fields above, this time
    // a targeting immunity rather than a stat/keyword grant. Only blocks
    // the OPPONENT of this die's own controller from targeting it -
    // its own controller can still target it normally. See
    // LegalTargets.Query/DieStats.IsProtectedFromOpponentTargeting for
    // enforcement. Deliberately narrow (continuous, self-only, blocks
    // every kind of targeting) - Gladiator's own "can't be the target of
    // Action Dice or Global Abilities" printings are a different shape
    // (temporary, Global-activated, ability-type-scoped, applies to your
    // WHOLE team) and aren't covered by this field.
    public string? CannotBeTargetedByOpponentWhileNamedCardActive { get; init; }

    // Whether this card's FULL printed text is correctly modeled -
    // scripted via AbilityDef, built entirely into the engine (a pure
    // keyword like Deadly/Infiltrate needs no AbilityDef at all), or
    // genuinely has no text to model in the first place. False for a
    // card whose text has a clause deliberately left out (see
    // SampleCards.cs's "Scripting policy" note and each such card's own
    // comment for what's missing and why). Defaults true since most
    // cards declared via the Character()/BasicAction() factories are -
    // false is the explicit opt-out, not the default, matching how few
    // of them there actually are. Lets the web client (or a future
    // team-builder) offer only cards that behave correctly, without
    // hiding the rest of the catalog - see CardDefDto.IsImplemented.
    public bool IsImplemented { get; init; } = true;

    // Short set code (e.g. "MSW", "SKC") - which Dice Masters expansion
    // this printing is from. Nullable/optional like Subtitle/Alignment -
    // a future card added without a known set stays null rather than
    // needing a placeholder. Backfilled for every current card by
    // cross-referencing the local Teambuilder reference data (cards.php/
    // cardsb.php - the same source this catalog was originally imported
    // from) rather than guessed - see the "card Set field" status update
    // in DESIGN_LOG.md for the methodology, and its own remarks for two
    // incidental spelling discrepancies found along the way (not fixed
    // here). Lets the web client filter/sort by set, same shape as
    // Affiliation.
    public string? Set { get; init; }
}

// See CardDef.GrantsStaticTeamBonus's remarks. RequiredAffiliation
// (Kitty Pryde "Experienced Leader"/DPS144's "each of your X-Men
// character dice get +1A and +1D") narrows which of the granting
// player's dice actually receive the bonus - null (every other current
// user) means every Character die, matching Captain Marvel's own
// unqualified "your Character dice" text.
// RequiredKeyword (Angel "Jean Grey's School"/DPS057's own "other
// character dice with Founder get +1A") is the keyword-scoped
// counterpart to RequiredAffiliation - "Founder" is modeled as a real
// KeywordInstance (see the WhenAnotherDieFielded/Cyclops "Founder
// prefix" status update), not an affiliation, so RequiredAffiliation
// alone can't express it. ExcludeSelf ("OTHER character dice") skips
// the bonus for any die of the SAME CARD as the granter - a per-CARD,
// not per-die-instance, approximation (see DieStats.StaticTeamBonusFor's
// own remarks for why), acceptable given rule 3.4.5.3's "does not
// stack" already collapses multiple same-card granters into one
// contribution anyway.
// SidekicksOnly (Iceman "Mr Ice Guy"/DPS114, Emma Frost "Influential"/
// DPS030 - both "your Sidekick dice get +1A[/+1D]") is a fourth
// independent scope alongside RequiredAffiliation/RequiredKeyword/
// ExcludeSelf, checked via DieStats.CountsAsSidekick rather than a
// card-level filter - no current printing combines it with the other
// three, but nothing stops one from doing so.
public sealed record StaticTeamBonus(
    int AttackDelta, int DefenseDelta, string? RequiredAffiliation = null,
    string? RequiredKeyword = null, bool ExcludeSelf = false, bool SidekicksOnly = false);

// See CardDef.GrantsSelfPrepFromBagIfFieldedWithEnergy's remarks. Only
// one of RequiredEnergyType/RequiredAffiliation is ever set by any
// current printing (Forge's own Bolt-energy-type check vs. Professor
// X's own X-Men-affiliation check), same "independent, whichever's set"
// shape as FreeFieldingGrant's own two filters.
public sealed record SelfPrepFromBagIfFieldedWithEnergy(EnergyType? RequiredEnergyType = null, string? RequiredAffiliation = null);

// See CardDef.GrantsSelfKeywordWhileNamedCardActive's remarks.
public sealed record ConditionalSelfKeywordGrant(string WhileCardNamed, string Keyword);

// See CardDef.GrantsSelfStatBonusWhileNamedCardActive's remarks.
public sealed record ConditionalSelfStatBonus(string WhileCardNamed, int AttackDelta, int DefenseDelta);

// See CardDef.GrantsSelfStatBonusWhileOwnSidekickActive's remarks.
public sealed record OwnSidekickStatBonus(int AttackDelta, int DefenseDelta);

// See CardDef.GrantsOpponentPurchaseSurcharge's remarks. MaxPurchaseCost
// null means every purchase is surcharged, matching how MaxFieldingCost/
// MaxAttack/MaxDefense elsewhere all use null for "no threshold."
public sealed record OpponentPurchaseSurcharge(int Amount, int? MaxPurchaseCost = null);

// See CardDef.GrantsOpponentGlobalSurcharge's remarks.
public sealed record OpponentGlobalSurcharge(int Amount, bool RequiresOwnActiveSidekick = false);

// See CardDef.GrantsNamedCardSupport's remarks. Matched against the
// RECEIVING die's own CardDef.Name (any printing) - not affiliation,
// not keyword, a third independent dimension alongside those two.
public sealed record NamedCardSupport(string CardName, int PurchaseDiscount = 0, int AttackDelta = 0, int DefenseDelta = 0);

// See CardDef.GrantsFieldingCostReduction's remarks.
public sealed record FieldingCostReduction(int Amount, string? RequiredAffiliation = null);

// See CardDef.GrantsMinimumBlockersRequirement's remarks.
public sealed record MinimumBlockersRequirement(int MinBlockers, string? RequiredAffiliation = null);

// See CardDef.GrantsSelfPurchaseDiscountIfOpponentHasAffiliation's remarks.
public sealed record SelfPurchaseDiscountIfOpponentHasAffiliation(string OpponentAffiliation, int Amount);

// See CardDef.GrantsOpponentAbilityBlankWhileActive's remarks.
// AlsoFreeToField models D'Ken's own "...and are free to field" half -
// bundled into the same record (rather than a second field elsewhere)
// since both halves apply to the exact same qualifying set of opposing
// dice (MaxPurchaseCost); see TurnEngine.IsFreeToField for the second
// enforcement site this same grant is checked at.
public sealed record OpponentAbilityBlankGrant(int? MaxPurchaseCost = null, bool AlsoFreeToField = false);

// See CardDef.GrantsSelfAttackBonusPerMatchingDie's remarks. CountFilter
// is a TargetSpec repurposed as a counting filter rather than a real
// choice - LegalTargets.Query still does the actual matching.
public sealed record SelfAttackBonusPerMatchingDie(TargetSpec CountFilter, int AttackPerMatch);

// See CardDef.GrantsOpponentStatDebuff's remarks.
public sealed record OpponentStatDebuff(int AttackDelta, int DefenseDelta, EnergyType? ExcludedEnergyType = null);

// See CardDef.GrantsFreeFielding's remarks. Both filters are independent
// (an implicit AND if both are set) - only one is ever set by any current
// printing, matching how a real card's text only ever states one
// restriction, but nothing stops a future card needing both at once.
// D'Ken ("Shi'ar Civil War", DPS141)'s own "...are free to field" half
// is the first user of MaxPurchaseCost - a THIRD independent filter
// alongside RequiredAffiliation/MaxFieldingCost (checked against the
// card's own printed PurchaseCost, not the specific face's fielding
// cost MaxFieldingCost already covers).
public sealed record FreeFieldingGrant(string? RequiredAffiliation = null, int? MaxFieldingCost = null, int? MaxPurchaseCost = null);

// See GameState.PendingPurchaseDiscount's remarks. RequiredType null
// means any purchase qualifies (Dark Phoenix's own "your next die");
// CardType.Action means only an Action die does (Magik's own "the next
// action die").
public sealed record PendingPurchaseDiscount(int Amount, CardType? RequiredType = null);

// Rule Appendix 1 - keyword abilities are a finite, engine-known set
// implemented as plugins (see design doc); Params covers e.g. Range X,
// Fabricate X-Y, Breath Weapon X.
public sealed record KeywordInstance(string Name, IReadOnlyList<int>? Params = null);
