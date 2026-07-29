namespace DiceFight.Engine.Model;

// Rule 3.6 - a running attack/defense modifier from an Applied or Static
// ability. Kept separate rather than folded into the die so Static
// modifiers (3.6.6) can be recomputed live while Applied modifiers (3.6.5)
// persist independently until end of turn.
public sealed record Modifier(int AttackDelta, int DefenseDelta, string Source);

// A physical die in play. CardId is null for Sidekick dice (rule 1.3.9 -
// no corresponding card). VirtualCardId is the "which card does this die
// currently reference" override needed for Copying (rule 3.10) - left as
// a stub per the design doc's open questions.
public sealed class DieInstance
{
    public required string Id { get; init; }
    public string? CardId { get; init; }
    public string? VirtualCardId { get; set; }
    public required string OwnerId { get; init; }
    public required string ControllerId { get; set; }

    public Zone Zone { get; set; } = Zone.Bag;
    public DieStatus Status { get; set; } = DieStatus.Unrolled;

    // 1-based character level; 0/unused for non-character dice.
    public int Level { get; set; } = 1;

    public int Damage { get; set; }

    // Only meaningful when Status == Energy; derived by TurnEngine.ApplyRoll
    // from the die's type/card, not chosen freely (rule 1.3.10/1.4.2).
    public EnergyKind EnergyKind { get; set; } = EnergyKind.None;
    public EnergyType? ProvidedEnergyType { get; set; }

    // How much energy this face is worth (1, or 2 for a "double" face -
    // see the rulebook's Doubles rule). Only meaningful when Status ==
    // Energy. A double that's only partially spent "spins down" to a
    // single-energy face of the same kind/type by reducing this to 1 in
    // place, rather than moving zones - see TurnEngine's SpendEnergy.
    public int EnergyAmount { get; set; } = 1;

    // A bookkeeping stand-in for "virtual" generic energy (rule 1.4.4/
    // 1.4.5 - from a draw shortfall, or from partially spending a Generic
    // double that has no single-energy face to spin down to), represented
    // as a real spendable die in the Reserve Pool rather than a separate
    // counter, so it goes through the exact same selection/SpendEnergy
    // path as any other energy die. Not a physical die - see
    // TurnEngine.AddVirtualGenericEnergy/SpendEnergy for how it's created,
    // spent, and (unlike a real die) removed outright rather than moved to
    // a zone when it's used up, and never carried past Clean Up.
    public bool IsVirtualEnergy { get; set; }

    public List<Modifier> AppliedModifiers { get; } = [];
    public List<DieInstance> AttachedGear { get; } = [];

    // The raw, zone-independent physical fact: no card, not virtual
    // energy. Keyword Ally muddies this - an Ally Character die IS a real
    // card, so this is always false for one, even while the Field Zone
    // makes it count as a Sidekick for ability purposes. Use
    // DieStats.CountsAsSidekick(state, die) for that broader "is this
    // currently a legal Sidekick target/subject" question instead; this
    // property is for the narrower "is this a physical Sidekick die at
    // all" fact (e.g. Falcon's Global picking any physical Sidekick out
    // of the Used Pile, where Ally never applies anyway).
    public bool IsSidekick => CardId is null && !IsVirtualEnergy;

    // The rulebook's own "rolled dice" vs. "unrolled dice" distinction
    // ("More About Dice"): it's entirely zone-derived, not a separate
    // tracked state - "Dice in the Reserve Pool or the Field Zone
    // (including the Attack Zone) are considered to be whatever their
    // face is... Dice in the Prep Area, Used Pile, and bag are considered
    // 'unrolled dice,' and it doesn't matter what face happens to be
    // showing." Kept as a computed property for exactly that reason,
    // rather than a stored flag that could drift out of sync with Zone.
    public bool IsRolled => Zone is Zone.ReservePool or Zone.FieldZone or Zone.AttackZone;

    // Rule 1.6.8 - "when they are unrolled (in the Prep Area, Used Pile,
    // or the bag) they are not considered to be character dice, but just
    // Sidekick dice" - the same idea applies to any die, not just
    // Sidekicks: once it leaves a zone where its rolled face actually
    // matters (Reserve Pool/Field Zone/Attack Zone - see IsRolled), that
    // face is meaningless until the die is drawn and rolled fresh again.
    // Call this whenever a die lands in a genuinely dormant zone (Used
    // Pile, Bag, Prep Area, or back on its own Unpurchased card) so
    // nothing downstream has to guess whether a stale Status/Level/
    // EnergyKind is still real - grouping identical dice for display, or
    // an ability like Falcon's Global looking for "a Sidekick in the Used
    // Pile", both depend on this actually happening. Not called for the
    // transient Out of Play zone - what a die was just spent as is still
    // useful information mid-turn, so that staleness only gets cleaned up
    // once Out of Play is swept to the Used Pile at Clean Up.
    public void ResetToUnrolled()
    {
        Status = DieStatus.Unrolled;
        Level = 1;
        Damage = 0;
        EnergyKind = EnergyKind.None;
        ProvidedEnergyType = null;
        EnergyAmount = 1;
        AppliedModifiers.Clear();
    }
}
