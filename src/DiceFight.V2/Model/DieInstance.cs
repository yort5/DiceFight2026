namespace DiceFight.V2.Model;

// A running attack/defense modifier, ported from v1's proven shape
// (Modifier(AttackDelta, DefenseDelta, Source)). The list starts empty and
// nothing populates or consumes it yet (V2_PLAN.md Phase 2 task 1) -
// Phase 3's query pipeline is where Duration/expiry and actual
// interception land; this is just the storage shape reserved now so
// DieInstance doesn't need reshaping later.
public sealed record AppliedModifier(int AttackDelta, int DefenseDelta, string Source);

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
// never had (see PlaceholderDiceRoller's own remarks) - not a simplified
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

    public bool IsSidekick => CardId is null;
}
