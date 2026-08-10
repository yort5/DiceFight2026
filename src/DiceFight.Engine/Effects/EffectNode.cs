namespace DiceFight.Engine.Effects;

// The effect DSL: a small, closed set of primitives that authored card
// abilities are composed from (see RULES_ENGINE_DESIGN.md - "Ability
// representation"). New card text is expressed by combining these, not by
// adding new C# code paths. TargetSpec/EffectNode are intentionally data
// (records), not behavior, so they can be authored, serialized, and
// round-tripped as JSON alongside CardDef.
public abstract record EffectNode;

// Rule 3.1.31 - which side of the requesting ability's controller a
// target must be on. "Own"/"Opposing" are relative to whoever controls
// the ability at resolution time (rule 3.1.4/3.1.5), not fixed players.
public enum TargetOwnership
{
    Any,
    Own,
    Opposing
}

// Rule 3.3 - Targeting. A structured filter over legal targets - zone,
// ownership, character-vs-any-die, required energy type, and how many -
// resolved by LegalTargets.Query against the current GameState rather
// than left for a caller to interpret from a free-text Description. This
// is the "shared legal-target predicate system" called out as an open
// question in the design doc; it still doesn't model captured dice
// (3.8) or per-die "cannot be targeted" abilities, since neither
// Capturing nor per-die targeting restrictions are implemented yet.
// MaxAttack (Storm "Cloud Cover"/DPS092's "target character die with 3A
// or less") is the first stat-threshold filter - checked against
// DieStats.EffectiveAttack (the live, modifier-inclusive value, not the
// printed face) since a card text threshold means "as it currently
// stands," same as every other targeting/condition check elsewhere.
// RequiredAffiliations (Master Mold's "target Brotherhood of Mutants
// character die"/"target X-Men character die" printings) matches ANY of
// the listed affiliations, not all - card text with two affiliations
// joined by "or" (e.g. "target Shi'ar or X-Men character die") is the
// same shape as one, just a longer list. MatchAll (Phoenix "Eternal
// Flame"'s "opposing character dice with less than 4A can't block," Master
// Mold's own "deal 2 damage to ALL X-Men and Brotherhood of Mutants
// character dice") skips the caller-choice step in Resolve entirely and
// applies to every legal match automatically - there's no "target" at
// all in that card text, unlike everything else here. RequiredLevel
// (Iceman "Icy Interference"/DPS034's "target opposing level 1
// character die") is the level-restricted filter previously flagged as
// missing alongside RequiredAffiliations.
public sealed record TargetSpec(
    TargetOwnership Ownership,
    bool CharacterDiceOnly,
    IReadOnlyList<Model.Zone>? EligibleZones,
    Model.EnergyType? RequiredEnergyType,
    int Count,
    string Description,
    bool IsSelf = false,
    bool SidekicksOnly = false,
    bool PlayersAllowed = false,
    bool Optional = false,
    int? MaxAttack = null,
    IReadOnlyList<string>? RequiredAffiliations = null,
    bool MatchAll = false,
    int? RequiredLevel = null,
    // Sabretooth "Do I Smell... Weakness?" (DPS091)'s "opposing character
    // die with 2D or less" - the defense-side counterpart to MaxAttack,
    // same "live EffectiveDefense, not printed face" convention.
    int? MaxDefense = null)
{
    // Rule 3.3.4/3.3.5 - only dice in the Field Zone (which includes the
    // Attack Zone) may be targeted, unless otherwise stated.
    public static readonly IReadOnlyList<Model.Zone> DefaultZones = [Model.Zone.FieldZone, Model.Zone.AttackZone];

    public static TargetSpec CharacterDie(
        string description,
        TargetOwnership ownership = TargetOwnership.Any,
        Model.EnergyType? energyType = null,
        int count = 1,
        IReadOnlyList<Model.Zone>? zones = null,
        bool optional = false,
        int? maxAttack = null,
        IReadOnlyList<string>? requiredAffiliations = null,
        bool matchAll = false,
        int? requiredLevel = null,
        int? maxDefense = null) =>
        new(ownership, CharacterDiceOnly: true, zones ?? DefaultZones, energyType, count, description,
            Optional: optional, MaxAttack: maxAttack, RequiredAffiliations: requiredAffiliations, MatchAll: matchAll,
            RequiredLevel: requiredLevel, MaxDefense: maxDefense);

    // optional: true models "you MAY target up to Count" (any number,
    // including zero, is a legal chosen count) rather than rule 3.3.11's
    // usual "as many as legally available, capped at Count" (which
    // requires choosing at least min(Count, legal.Count) - see Resolve).
    // Needed for card text like Cosmic Cube's "you may send ANY NUMBER of
    // them" - a voluntary selection, not a mandatory one just capped by
    // availability.
    public static TargetSpec AnyDie(
        string description, TargetOwnership ownership, IReadOnlyList<Model.Zone> zones, int count = 1,
        bool optional = false, IReadOnlyList<string>? requiredAffiliations = null) =>
        new(ownership, CharacterDiceOnly: false, zones, RequiredEnergyType: null, count, description,
            Optional: optional, RequiredAffiliations: requiredAffiliations);

    // "target player or Character die" card text (e.g. Attune) - a single
    // choice between the two, not two separate targets. LegalTargets
    // appends the matching player id(s) alongside the usual die
    // candidates; DealDamage's interpreter tells them apart by checking
    // whether the resolved id is a real player id (see GameState.
    // IsPlayerId) before falling back to treating it as a die id.
    public static TargetSpec CharacterDieOrPlayer(
        string description,
        TargetOwnership ownership = TargetOwnership.Any,
        int count = 1,
        IReadOnlyList<Model.Zone>? zones = null) =>
        new(ownership, CharacterDiceOnly: true, zones ?? DefaultZones, RequiredEnergyType: null, count, description,
            PlayersAllowed: true);

    // "target Sidekick" card text - matches real Sidekick dice, plus any
    // Ally-keyword Character die currently in the Field/Attack Zone (see
    // DieStats.CountsAsSidekick).
    public static TargetSpec Sidekick(
        string description,
        TargetOwnership ownership = TargetOwnership.Any,
        int count = 1,
        IReadOnlyList<Model.Zone>? zones = null,
        bool optional = false) =>
        new(ownership, CharacterDiceOnly: false, zones ?? DefaultZones, RequiredEnergyType: null, count, description,
            SidekicksOnly: true, Optional: optional);

    // "target player" card text (e.g. Corrupt) - no die option at all,
    // unlike CharacterDieOrPlayer. EligibleZones: [] means the die-side of
    // LegalTargets.Query never matches anything, so only the matching
    // player id(s) come back.
    public static TargetSpec Player(string description, TargetOwnership ownership = TargetOwnership.Any) =>
        new(ownership, CharacterDiceOnly: false, EligibleZones: [], RequiredEnergyType: null, Count: 1, description,
            PlayersAllowed: true);

    // Rule 3.1.15-style self-reference (e.g. Shocking Grasp's "you may
    // Prep this die"). Bypasses legal-target filtering entirely -
    // EffectInterpreter resolves it straight to the ability's source die.
    public static readonly TargetSpec Self =
        new(TargetOwnership.Any, CharacterDiceOnly: false, EligibleZones: null, RequiredEnergyType: null,
            Count: 1, Description: "self", IsSelf: true);
}

public sealed record DealDamage(int Amount, TargetSpec Target) : EffectNode;
// Keyword Retaliation's Black Manta ("Deep Sea Deviant") printing:
// "deal 1 damage to your opponent for each of your active Villains" -
// amount isn't a fixed number, it's computed at resolution time from the
// ability's own source die's controller's active dice that share an
// affiliation with the source's own card (including the source itself,
// and other copies of it). Kept general rather than Retaliation-specific
// naming, since this "X for each of your active [affiliation]" idiom
// shows up on other cards' stat-scaling text too (e.g. Black Manta's own
// "+1A/+1D for each OTHER active Villain" printing) - just not scripted
// yet.
public sealed record DealDamagePerActiveAffiliate(TargetSpec Target) : EffectNode;
public sealed record Ko(TargetSpec Target) : EffectNode;
// Keyword Sacrifice - "Sacrificed Character dice are moved from the
// Field Zone to Out of Play or the Used Pile, as applicable." Distinct
// from Ko: not a KO at all (Appendix 1 clarification 3 - "will not
// trigger 'when KO'd' abilities"), so this bypasses DieStats.
// TryResolveKO/ForceKO entirely - no defense check, no Regenerate
// interception, just a direct zone move. See EffectInterpreter's own
// remarks on which zone it lands in.
public sealed record Sacrifice(TargetSpec Target) : EffectNode;
// Invisible Woman's Global ("target character die must block this turn") -
// flags the target in GameState.MustBlockThisTurn; enforced by
// CombatEngine.DeclareBlockers, cleared at Clean Up.
public sealed record ForceBlock(TargetSpec Target) : EffectNode;
// Vulcan's Global ("target character die must attack this turn", DPS055) -
// the Declare Attackers side of ForceBlock above, same shape (GameState.
// MustAttackThisTurn, enforced by CombatEngine.DeclareAttackers).
public sealed record ForceAttack(TargetSpec Target) : EffectNode;
// Deathbird's "War of Kings" (DPS109) - "target character die cannot
// block this turn" - the restriction mirror of ForceBlock above (GameState.
// CantBlockThisTurn, enforced by CombatEngine.DeclareBlockers rejecting the
// die as an eligible blocker at all, cleared at Clean Up). Unlike
// ForceBlock, there's no "if able" framing to honor - a die that can't
// block simply never appears in a legal blockerDieIds list.
public sealed record CantBlock(TargetSpec Target) : EffectNode;
// Keyword Call Out ("when this Character die attacks, target character
// die is the only character die that may block this character die") -
// records the choice in GameState.CallOutTargets, keyed by the attacking
// die (the ability's SourceDieId); enforced by CombatEngine.
// DeclareBlockers, including the keyword's own cancellation cases
// (target KO'd, two Call Outs sharing a target, etc. - see
// CombatEngine.ActiveCallOutTargets). Always a WhenAttacks ability
// targeting an opposing Character die.
public sealed record SetCallOutTarget(TargetSpec Target) : EffectNode;
// Keyword Corrupt X - "Target player draws X dice from their bag
// (refilling from the Used Pile if necessary). Choose one die (no matter
// how many dice are drawn) and place it in that player's Used Pile, and
// the rest are returned to the bag." The X dice are a random bag draw
// (TurnEngine.DrawFromBag, same as Clear and Draw's own), not a target
// choice; which ONE of those specific dice then goes to the Used Pile IS
// a real choice, but the candidate set doesn't exist until this effect
// actually runs a draw - it can't be resolved upfront through the normal
// TargetSpec/LegalTargets pipeline like everything else in the tree, so
// EffectInterpreter calls ctx.ResolveTargets for it directly instead
// (validating the answer is actually one of the drawn dice).
public sealed record Corrupt(int Count, TargetSpec PlayerTarget) : EffectNode;
// Gambit ("Ace in the Hole", DPS032)'s own burst bonus - "draw 2 dice,
// Roll one and return the other to your bag." Same "draw N random from
// the bag, choose exactly one, do X with it, the rest go elsewhere"
// shape as Corrupt just above (down to reusing TurnEngine.DrawFromBag),
// just a different pair of destinations: the chosen die is rolled and
// stays in the Reserve Pool (not moved to the Used Pile), the rest
// return to the Bag (not the same "the rest are returned to the bag"
// wording Corrupt's own card text also happens to use, but a genuinely
// different mechanical outcome for the chosen one). Always a real
// choice once 2+ dice are drawn - the drawn dice are visible by card
// identity before rolling (rule 1.6.3's "unrolled" only means the FACE
// is unknown, not which card it is), so this can't be resolved upfront
// through the normal TargetSpec pipeline any more than Corrupt's own
// choice can.
public sealed record DrawAndChooseOneToRoll(int DrawCount) : EffectNode;
// A WhenDrawn "mulligan" effect (Cosmic Cube's "Infinite Possibilities"
// printing, Rip Hunter's "Navigate the Sands of Time" printing) - moves
// the chosen already-drawn dice (Target's zones should be DiceFromBag/
// DiceFromPrep, Own ownership) to ToZone (Out of Play for Cosmic Cube,
// Used Pile for Rip Hunter), then draws one replacement per die actually
// moved. Unlike DrawDice/Corrupt (both explicitly "outside Clear and
// Draw," rule 2.3.13 - roll immediately into the Reserve Pool), this
// happens *during* Clear and Draw itself, replacing dice that were part
// of that same step's own draw - so the replacements go through
// TurnEngine.DrawFromBag the same way the original draw did, landing
// unrolled in DiceFromBag to be rolled later, together with everything
// else, at Roll and Reroll - not rolled here.
public sealed record RedrawFromBag(TargetSpec Target, Model.Zone ToZone) : EffectNode;
public sealed record MoveDie(TargetSpec Target, Model.Zone ToZone) : EffectNode;
public sealed record ModifyStat(TargetSpec Target, int? AttackDelta, int? DefenseDelta) : EffectNode;
// Rogue "Strength Absorption" (DPS151): "target character die has 0A
// this turn" - a snapshot to an exact VALUE, unlike ModifyStat's relative
// delta. Still just an Applied ability underneath (rule 3.4.3.9 - "this
// turn" is its own duration statement, same default lifetime ModifyStat
// already has), so it's implemented as a computed one-time delta
// (Attack/Defense minus the target's current EffectiveAttack/
// EffectiveDefense) added as a Modifier - same storage, same Clean Up
// expiry, no separate "set value" bookkeeping needed. Null Attack/
// Defense means that stat is left alone, same convention ModifyStat uses.
public sealed record SetStat(TargetSpec Target, int? Attack, int? Defense) : EffectNode;
public sealed record Reroll(TargetSpec Target) : EffectNode;
// "Reroll target die(s); each that doesn't roll a character goes to the
// Used Pile" - confirmed a real recurring pattern across many sets, not a
// DPS-only one-off (Gambit "Unless I Got Someone to Play With"/DPS112,
// Psylocke "Advanced Telekinetic Combatant"/DPS150, Storm "Queen"/DPS132,
// plus non-DPS printings like Gambit "Cardsharp"/AVX107 and Storm
// "Wind-Rider"/AVX092 use the same text). DamagePerMovedToOpponent folds
// a follow-up like Psylocke/Storm's own "...deals 2 damage to your
// opponent for each die moved" into the same node rather than a separate
// Sequence step after it - the moved-die count only exists for the
// instant this node finishes rerolling, and nothing else in the DSL
// currently threads a live count from one Sequence step into the next.
// Zero (the default) means no such follow-up text exists at all (Gambit's
// own printing has none).
public sealed record RerollAndMoveUnlessCharacter(
    TargetSpec Target, Model.Zone ToZone, int DamagePerMovedToOpponent = 0) : EffectNode;
public sealed record Spin(TargetSpec Target, int LevelDelta) : EffectNode;
// "Spin [a/target] die to its [single/an] energy face" (Professor X
// "Uncanny Leadership"/DPS127, Iceman "Icy Interference"/DPS034) - a
// direct conversion, not a level-delta the way Spin above is (rule
// 3.7.5's own framing treats "spin to an energy face" as its own thing,
// distinct from spinning a Character die's level). Reuses the exact
// single-vs-double energy-face formula PlaceholderDiceRoller already
// uses for a natural Character-die roll (EnergyKind.Specific,
// ProvidedEnergyType from the target's own card, since Dice Masters
// energy faces are always the card's own printed type) rather than
// inventing a second one. Amount defaults to 1 (a "single" energy face)
// since that's the only amount either printing needs today - Professor
// X's own text says "single" explicitly; Iceman's just says "an energy
// face" with no double/opponent's-choice language, so 1 is the
// simplest reading rather than a real ambiguity worth modeling further.
public sealed record SpinToEnergyFace(TargetSpec Target, int Amount = 1) : EffectNode;
public sealed record DrawDice(int Count) : EffectNode;
public sealed record PrepDie(TargetSpec Source) : EffectNode;
// Level defaults to 1 (rule 2.6.3 note - dice fielded by an ability are
// fielded for free on level 1 unless stated otherwise); Jubilee
// "Rebellious Nature" (DPS036)'s own "field this die for free at level 2"
// is the first card that needed anything else.
public sealed record FieldDie(TargetSpec Target, bool Free, int Level = 1) : EffectNode;
public sealed record GainLife(int Amount) : EffectNode;
// Whose defaults to Own (rule 3.1.15 - "you" in card text means the
// ability's controller) - Ronan the Accuser's "your opponent loses 1
// life" (DPS050) is the first card needing Opposing instead.
public sealed record LoseLife(int Amount, TargetOwnership Whose = TargetOwnership.Own) : EffectNode;

// Rule 2.9.1's "switch life totals" card text (e.g. Cosmic Cube) - simple
// enough to be its own primitive rather than a generic "set stat" node.
public sealed record SwapLife : EffectNode;

// An ordered sequence, per rule 3.1.7 - non-global abilities with multiple
// effects resolve sequentially in text-box order.
public sealed record Sequence(IReadOnlyList<EffectNode> Steps) : EffectNode;

// Rule 3.1.17's "if you do" / "if [x], then [y]" pattern - see
// Conditional's own remarks just below for the full shape.
public enum EffectCondition
{
    TargetWasKOd,

    // Jean Grey ("Peaceful Coexistence", DPS035) - "if no character dice
    // were KO'd that turn." Reads GameState.AnyCharacterKOdThisTurn
    // directly rather than a resolved target's own state (unlike
    // TargetWasKOd) - CheckTarget is still required by Conditional's
    // shape, so callers pass TargetSpec.Self and the resolved id is
    // simply ignored.
    NoCharacterKOdThisTurn,

    // Magneto ("Idealist", DPS041)'s Global - "if you have no dice in
    // your Prep Area." Reads the ability controller's Prep Area directly,
    // same "ignores the resolved CheckTarget id" shape as
    // NoCharacterKOdThisTurn - use TargetSpec.Self here too.
    PrepAreaEmpty,

    // Jubilee ("Rebellious Nature", DPS036)'s Energize - "if you have
    // less life than your opponent." Reads both players' Life directly,
    // same "ignores the resolved CheckTarget id" shape as
    // NoCharacterKOdThisTurn/PrepAreaEmpty - use TargetSpec.Self here too.
    OwnLifeLessThanOpponent,

    // Burst symbols - the sheet's "*"/"**" ability-text marks (distinct
    // from the OLD misreading of them as "a different printing's text" -
    // see the "burst and double-burst" status update for how that got
    // corrected): "some abilities key off rolling that face" (per the
    // user, re: the stat line's own */** marks). Works for both Character
    // dice (CharacterFace, looked up by Level - DieStats.GetFace) and
    // Basic Action/Action dice (DieInstance.BurstStars directly, since
    // those have no "level" to derive a face from - see
    // EffectInterpreter.CurrentBurstStars for which source applies to
    // which). Unlike TargetWasKOd, THIS resolved die's own current face
    // is exactly what's being asked about, so CheckTarget is normally
    // TargetSpec.Self (the ability's own source die) rather than ignored.
    // A face can only ever be blank, single-, or double-burst (mutually
    // exclusive, never both), so these two never overlap for the same
    // die at the same time - "*/**" printed together (e.g. Take Cover)
    // is just both conditions checked independently, each firing on its
    // own face, not a combined third state.
    OnSingleBurstFace,
    OnDoubleBurstFace,

    // Phoenix ("Psionic Maelstrom", DPS086) - "if that character die is a
    // Villains character die, [...]." Unlike every condition above, this
    // one needs a parameter (which affiliation) - Conditional.
    // AffiliationParam carries it, checked via DieStats.HasAffiliation
    // against the resolved CheckTarget die (normally the ability's own
    // just-damaged/just-chosen target, not TargetSpec.Self).
    TargetHasAffiliation,

    // Iceman ("Frozen Fists of Fury", DPS074) - "if Wolverine is active,
    // [...]." Also needs a parameter (which card) - Conditional.
    // NamedCardParam carries it, same "active anywhere on the board,
    // either player's" check CardDef.GrantsSelfKeywordWhileNamedCardActive/
    // GrantsSelfStatBonusWhileNamedCardActive already use for a
    // continuously-applied grant; this is the same condition surfaced as
    // a one-shot Conditional instead, for card text where the "while
    // active" clause gates a triggered EFFECT rather than a continuous
    // grant. CheckTarget is ignored (TargetSpec.Self, matching every
    // other state-only condition above).
    NamedCardIsActive,

    // Corsair ("Criminal Record", DPS104) - "if your opponent has 4 or
    // more character dice in the Field Zone." Also needs a parameter
    // (the count threshold) - Conditional.CountParam carries it, same
    // "dieId ignored, reads state directly" shape as PrepAreaEmpty/
    // OwnLifeLessThanOpponent.
    OpponentHasAtLeastNCharacterDiceInFieldZone,
}

// Rule 3.1.17's "if you do" / "if [x], then [y]" pattern (e.g. Shocking
// Grasp: "if that character is KO'd by this damage, you may Prep this
// die"). CheckTarget is evaluated against When; Then only runs if it holds
// for at least one resolved die. Else (e.g. Gambit's "you may draw and
// roll a die. * Instead, draw 2 dice...") runs instead when it doesn't -
// null means "no else clause" (every condition before burst symbols only
// ever needed the Then half). AffiliationParam/NamedCardParam are only
// meaningful for their matching EffectCondition (TargetHasAffiliation/
// NamedCardIsActive respectively) - null otherwise, same "only meaningful
// for this one condition" shape as AbilityDef.KOdFilter/FieldedFilter.
public sealed record Conditional(
    TargetSpec CheckTarget, EffectCondition When, EffectNode Then, EffectNode? Else = null,
    string? AffiliationParam = null, string? NamedCardParam = null, int? CountParam = null) : EffectNode;

// Falcon's Global ("each player must field a Sidekick from their Used Pile
// if able") - a forced action on both players at once, not a chosen
// target. Sidekick dice are fungible, so "if able" is just "does one
// exist" - no real choice to make, so (like DrawDice) this bypasses the
// TargetSpec/ResolveTargets choice pipeline entirely rather than stretch
// TargetSpec to express "both players, no chooser, silently skip if none."
public sealed record FieldSidekickForEachPlayer : EffectNode;

// Starfire's Global ("if you purchased a die this turn, Prep a die from
// your bag") - the "if you..." check is against turn-scoped state
// (Player.PurchasedDieThisTurn), not a die's condition, so like
// FieldSidekickForEachPlayer this reads game state directly rather than
// going through Conditional/TargetSpec; the bag pick is fungible, same
// reasoning as DrawDice's. CharacterOnly checks Player.
// PurchasedCharacterDieThisTurn instead - Lilandra's own Global narrows
// Starfire's "a die" to "a character die" specifically, same mechanic
// otherwise, so this is a filter flag rather than a second node.
public sealed record PrepFromBagIfPurchasedThisTurn(bool CharacterOnly = false) : EffectNode;

// Ricochet's own Infiltrate follow-up ("draw a die from your bag and add
// it to your Prep Area") - the same bag-pick-to-Prep-Area mechanic as
// PrepFromBagIfPurchasedThisTurn, just unconditional.
public sealed record PrepFromBag : EffectNode;

// Keyword Loyalty's own grant side (e.g. Jean Grey's "put a Loyalty
// Counter on Jean Grey's card") - every printed instance of this puts a
// counter on the ability's OWN card, never a target choice, so like
// FieldSidekickForEachPlayer/PrepFromBag this bypasses TargetSpec
// entirely rather than modeling a single-choice-of-exactly-one-fixed-
// option Target. See GameState.LoyaltyCounters/DieStats.LoyaltyBonus
// for storage and payoff.
public sealed record GrantLoyaltyCounter : EffectNode;

// See GameState.PendingPurchaseDiscount's remarks - "the next die/action
// die you purchase this turn costs N less." Bypasses TargetSpec entirely,
// same reasoning as GrantLoyaltyCounter above: there's no target choice
// at all, just a fact recorded for TurnEngine.Purchase to consume later.
public sealed record GrantNextPurchaseDiscount(int Amount, Model.CardType? RequiredType = null) : EffectNode;

// "Target character die gains [keyword]" (Magik "Sorceress of Limbo"/
// DPS120, Psylocke "Telepath"/DPS088 both grant Overcrush this way) -
// rule 3.4.3.9 calls this kind of grant an Applied ability, same default
// lifetime as a numeric Applied stat modifier (until end of turn, unless
// otherwise stated), so it's stored on DieInstance.AppliedKeywords right
// alongside AppliedModifiers rather than as its own GameState-level
// turn-scoped set. Only meaningful for keywords DieStats.HasKeyword
// already resolves purely from state (Overcrush, etc.) - doesn't grant
// anything that needs its own AbilityDef wired up too.
public sealed record GrantKeyword(TargetSpec Target, string Keyword) : EffectNode;
