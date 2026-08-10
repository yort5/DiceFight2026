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
public sealed record StaticTeamBonus(int AttackDelta, int DefenseDelta, string? RequiredAffiliation = null);

// See CardDef.GrantsSelfKeywordWhileNamedCardActive's remarks.
public sealed record ConditionalSelfKeywordGrant(string WhileCardNamed, string Keyword);

// See CardDef.GrantsSelfStatBonusWhileNamedCardActive's remarks.
public sealed record ConditionalSelfStatBonus(string WhileCardNamed, int AttackDelta, int DefenseDelta);

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
public sealed record FreeFieldingGrant(string? RequiredAffiliation = null, int? MaxFieldingCost = null);

// See GameState.PendingPurchaseDiscount's remarks. RequiredType null
// means any purchase qualifies (Dark Phoenix's own "your next die");
// CardType.Action means only an Action die does (Magik's own "the next
// action die").
public sealed record PendingPurchaseDiscount(int Amount, CardType? RequiredType = null);

// Rule Appendix 1 - keyword abilities are a finite, engine-known set
// implemented as plugins (see design doc); Params covers e.g. Range X,
// Fabricate X-Y, Breath Weapon X.
public sealed record KeywordInstance(string Name, IReadOnlyList<int>? Params = null);
