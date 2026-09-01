using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Model;

// A one-shot applied modifier - Phase 2 reserved the storage shape;
// Phase 3 fills in Duration/expiry (V2_PLAN.md Phase 3 task 2) and the
// query pipeline that reads it. FieldingCostDelta was added alongside
// Attack/Defense once QueryEngine.GetFieldingCost needed a per-die
// component to sum, same "add it when a real consumer needs it" rule
// every other phase in this rewrite has followed. GrantedDuringPlayerId
// is only meaningful for Duration.UntilYourNextTurn (see TurnEngine.
// CleanUp's own expiry remarks) - null for EndOfTurn/Permanent.
public sealed record AppliedModifier(
    int AttackDelta,
    int DefenseDelta,
    int FieldingCostDelta,
    string Source,
    Duration Duration,
    string? GrantedDuringPlayerId = null);

// A physical die in play. Exactly one of CardId/PoolDieId is set: CardId
// for a card-owned die (Character/BasicAction/Token), PoolDieId (a
// GameConfig.BasicDicePool entry's DieDefinition.Id) for a basic/Sidekick-
// equivalent pool die - this is deliberately richer than v1's "CardId
// null = Sidekick" shape, since Direction C wants more than one
// interchangeable pool die type to be expressible (ARCHITECTURE_REVIEW.md
// Part 3's own dice-composition example).
//
// Unlike v1's DieInstance (which also stored Status/Level/EnergyKind/
// EnergyAmount/BurstStars, all DERIVED facts about whichever face is
// showing), v2 stores only CurrentFaceIndex - everything else is looked
// up from the die's own DieDefinition.Faces[CurrentFaceIndex] on demand
// (GameState.GetDieDefinition). This is only possible because v2 dice
// carry real per-die face data from the start (Appendix B), which v1
// never had (see DieFaces's own remarks) - not a simplified
// port, a genuine improvement the new data model enables.
public sealed class DieInstance
{
    public required string Id { get; init; }
    public string? CardId { get; init; }
    public string? PoolDieId { get; init; }
    public required string OwnerId { get; init; }
    public required string ControllerId { get; set; }

    public Zone Zone { get; set; } = Zone.Bag;

    // null = unrolled/dormant (Prep Area, Used Pile, Bag, Unpurchased) -
    // v1's IsRolled/ResetToUnrolled distinction, ported as "there is no
    // current face" rather than a separate stored flag that could drift.
    public int? CurrentFaceIndex { get; set; }

    public List<AppliedModifier> AppliedModifiers { get; } = [];

    // Phase 5 additions - damage marked on a character die (rule ~1.5;
    // v1's DieInstance.Damage, same "accumulates, checked against
    // GetDefense for KO" shape) and one-shot tag grants (GrantTag),
    // stored the same Duration/GrantedDuringPlayerId shape as
    // AppliedModifiers so TurnEngine.CleanUp's existing expiry sweep
    // extends to both with one added loop rather than a second mechanism.
    // Reset to 0/empty whenever a die leaves the Field/Attack Zone
    // (EffectInterpreter's own MoveToZone helper) - damage and granted
    // tags only mean anything while a character is actually in play.
    public int Damage { get; set; }
    public List<GrantedTag> GrantedTags { get; } = [];

    // Abilities granted to this die by something else (GrantAbility) -
    // Psylocke handing a die Overcrush, Lantern Ring handing one a whole
    // triggered ability. Distinct from the card's own list because
    // blanking does not touch these: rule 3.4.8.2 explains blanking as
    // abilities being lost "because dice refer to their card to initiate
    // or trigger their abilities", and a granted ability does not come
    // from the blanked card (V2_VOCABULARY.md Part 16, user ruling).
    public List<GrantedAbility> GrantedAbilities { get; } = [];

    // DIE-SCOPED blanking - this die's own text ignored, leaving other
    // copies of the same card alone. The default scope: Web Shooters,
    // Loki "Powerful Magic", Adam Warlock. Wolverine "No More
    // Distractions" prints "for all copies of that die" precisely because
    // that is NOT the default, which is the evidence the two scopes are
    // intended rather than wording drift (V2_VOCABULARY.md Part 21).
    //
    // Only one-shot blanks live here. A conditional blank is recomputed
    // on read - see QueryEngine.AbilitiesActive.
    public List<DieSuppression> Suppressions { get; } = [];

    // This-turn-only combat restrictions (CombatFlag effect template) -
    // no Duration param on the record itself (V2_VOCABULARY.md Part 1),
    // since every real CombatFlag use in Dice Masters card text is
    // "(this turn)" - always EndOfTurn scoped, cleared alongside
    // AppliedModifiers/GrantedTags at CleanUp. Read by Phase 7's combat,
    // not consulted anywhere yet.
    public HashSet<CombatFlagKind> CombatFlags { get; } = [];

    public bool IsSidekick => CardId is null;
}

public sealed record GrantedTag(string Tag, Duration Duration, string? GrantedDuringPlayerId = null);

public sealed record GrantedAbility(TriggeredAbility Ability, Duration Duration, string? GrantedDuringPlayerId = null);

public sealed record DieSuppression(Duration Duration, string? GrantedDuringPlayerId = null);
