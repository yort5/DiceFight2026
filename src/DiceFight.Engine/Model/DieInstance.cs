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

    public bool IsSidekick => CardId is null && !IsVirtualEnergy;
}
