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
    bool Optional = false)
{
    // Rule 3.3.4/3.3.5 - only dice in the Field Zone (which includes the
    // Attack Zone) may be targeted, unless otherwise stated.
    public static readonly IReadOnlyList<Model.Zone> DefaultZones = [Model.Zone.FieldZone, Model.Zone.AttackZone];

    public static TargetSpec CharacterDie(
        string description,
        TargetOwnership ownership = TargetOwnership.Any,
        Model.EnergyType? energyType = null,
        int count = 1,
        IReadOnlyList<Model.Zone>? zones = null) =>
        new(ownership, CharacterDiceOnly: true, zones ?? DefaultZones, energyType, count, description);

    // optional: true models "you MAY target up to Count" (any number,
    // including zero, is a legal chosen count) rather than rule 3.3.11's
    // usual "as many as legally available, capped at Count" (which
    // requires choosing at least min(Count, legal.Count) - see Resolve).
    // Needed for card text like Cosmic Cube's "you may send ANY NUMBER of
    // them" - a voluntary selection, not a mandatory one just capped by
    // availability.
    public static TargetSpec AnyDie(
        string description, TargetOwnership ownership, IReadOnlyList<Model.Zone> zones, int count = 1,
        bool optional = false) =>
        new(ownership, CharacterDiceOnly: false, zones, RequiredEnergyType: null, count, description, Optional: optional);

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
        IReadOnlyList<Model.Zone>? zones = null) =>
        new(ownership, CharacterDiceOnly: false, zones ?? DefaultZones, RequiredEnergyType: null, count, description,
            SidekicksOnly: true);

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
public sealed record Reroll(TargetSpec Target) : EffectNode;
public sealed record Spin(TargetSpec Target, int LevelDelta) : EffectNode;
public sealed record DrawDice(int Count) : EffectNode;
public sealed record PrepDie(TargetSpec Source) : EffectNode;
public sealed record FieldDie(TargetSpec Target, bool Free) : EffectNode;
public sealed record GainLife(int Amount) : EffectNode;
public sealed record LoseLife(int Amount) : EffectNode;

// Rule 2.9.1's "switch life totals" card text (e.g. Cosmic Cube) - simple
// enough to be its own primitive rather than a generic "set stat" node.
public sealed record SwapLife : EffectNode;

// An ordered sequence, per rule 3.1.7 - non-global abilities with multiple
// effects resolve sequentially in text-box order.
public sealed record Sequence(IReadOnlyList<EffectNode> Steps) : EffectNode;

// Rule 3.1.17's "if you do" / "if [x], then [y]" pattern (e.g. Shocking
// Grasp: "if that character is KO'd by this damage, you may Prep this
// die"). CheckTarget is evaluated against When; Then only runs if it holds
// for at least one resolved die.
public enum EffectCondition
{
    TargetWasKOd
}

public sealed record Conditional(TargetSpec CheckTarget, EffectCondition When, EffectNode Then) : EffectNode;

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
// reasoning as DrawDice's.
public sealed record PrepFromBagIfPurchasedThisTurn : EffectNode;

// Ricochet's own Infiltrate follow-up ("draw a die from your bag and add
// it to your Prep Area") - the same bag-pick-to-Prep-Area mechanic as
// PrepFromBagIfPurchasedThisTurn, just unconditional.
public sealed record PrepFromBag : EffectNode;
