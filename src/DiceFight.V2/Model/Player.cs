namespace DiceFight.V2.Model;

// Trimmed to Phase 2's scope, deliberately - v1's Player also carries a
// pile of ability-hook bookkeeping (PurchasedDieThisTurn,
// SurchargedFirstPurchaseCardIds, ...) that nothing reads until the
// abilities that consult it exist (Phase 4+). Add fields when a real
// consumer needs them, not speculatively.
public sealed class Player
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Life { get; set; }
    public List<string> TeamCardIds { get; } = [];
}
