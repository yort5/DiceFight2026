using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// Cards migrated one-off, on request, outside both systematic sweeps -
// CardCatalog.cs's curated teams and DpsCards.cs's 145-card DPS set. Kept
// separate so neither of those counts (nor their own catalog-wide
// invariant tests, once Task 5 lands) silently picks up an extra card.
// Source of truth for stats/text is the bulk catalog
// (src/DiceFight.Engine/Data/BulkCards.json), not SampleCards.cs - this
// card was never in v1's curated set at all.
public static class BonusCards
{
    // Domino, "Not Really A Party Girl" (XFO010) - user-requested
    // 2026-09-01, right after the Energize unlock (DpsCards.cs Batch 4)
    // made her trivial: same Sequence(DealDamage, Reroll(Self)) shape as
    // Cyclops "Defending the Phoenix", just damage-to-player instead of
    // damage-to-a-character-die. Reuses DpsCards.Energize rather than
    // re-deriving the same signed-off shape.
    public static readonly CardDef DominoNotReallyAPartyGirl = new(
        Id: "XFO010", Name: "Domino", Subtitle: "Not Really A Party Girl", Set: "XFO", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("XFO010Die", "Shield", (0, 2, 2), (1, 3, 2), (1, 5, 2)),
        DieLimit: 4, Affiliations: ["X-Men"], Keywords: ["Energize"],
        RawText: "Energize - Deal 1 damage to target opponent and reroll this die.",
        Abilities: [DpsCards.Energize(new Sequence(
        [
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            new Reroll(new TargetFilter(Self: true)),
        ]))],
        Continuous: []);

    public static IReadOnlyList<CardDef> All => [DominoNotReallyAPartyGirl];
}
