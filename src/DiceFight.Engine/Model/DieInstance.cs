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

    // The other half of a SPLIT double - a Crossover character's double
    // face provides one of each of its two energy types rather than two
    // of one (see the Crossover glossary entry, and DieFaces). Null on
    // every other face, including a plain double.
    public EnergyType? SecondProvidedEnergyType { get; set; }

    // How much energy this face is worth (1, or 2 for a "double" face -
    // see the rulebook's Doubles rule). Only meaningful when Status ==
    // Energy. A double that's only partially spent "spins down" to a
    // single-energy face of the same kind/type by reducing this to 1 in
    // place, rather than moving zones - see TurnEngine's SpendEnergy.
    public int EnergyAmount { get; set; } = 1;

    // Only meaningful when Status == Action - which of a Basic Action/
    // Action die's 3 action faces (blank/single-/double-burst) got
    // rolled, per DieStats.HasKeyword's own "burst symbols" note (see
    // EffectCondition.OnSingleBurstFace/OnDoubleBurstFace). Unlike a
    // Character die (whose current face is always derivable from Level
    // via DieStats.GetFace - no separate storage needed), an Action die
    // has no "level" concept at all; which specific action face it landed
    // on is a genuinely random, persistent fact about THIS roll, decided
    // by the roller (see RolledFace's own remarks) and carried here until
    // the die is rerolled or swept back to a dormant zone
    // (ResetToUnrolled). null = blank face, 1 = single burst, 2 = double.
    public int? BurstStars { get; set; }

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

    // Rule 3.4.3.9 - "Character dice in your Reserve Pool gain Intimidate
    // (until end of turn)" is explicitly called an Applied ability by the
    // rulebook's own example, not a separate category from a numeric
    // Applied stat modifier - same default lifetime (until end of turn,
    // unless otherwise stated; lost early if the die leaves the Field
    // Zone), so it's cleared at every point AppliedModifiers already is,
    // rather than tracked as its own thing with its own lifecycle. See
    // DieStats.HasKeyword for where this is actually consulted, and the
    // GrantKeyword effect node for how a die ends up in this list.
    public List<string> AppliedKeywords { get; } = [];

    // Radicalization (DPS012)'s own Global - "target character die gains
    // X-Men or Brotherhood of Mutants (until end of turn)." Same Applied-
    // ability shape and lifetime as AppliedKeywords just above (rule
    // 3.4.3.9), just for affiliations instead of keywords - see
    // DieStats.HasAffiliation for where this is actually consulted, and
    // the GrantAffiliation effect node for how a die ends up in this list.
    public List<string> AppliedAffiliations { get; } = [];

    public List<DieInstance> AttachedGear { get; } = [];

    // Organic Steel (DPS010)'s own "prevent up to 2 damage to target
    // character die" - a one-shot shield consumed by the very next real
    // damage instance this die takes (DieStats.ApplyDamage), whatever
    // that amount turns out to be; not a running total that survives
    // multiple hits. Cleared at Clean Up (same "until end of turn"
    // default every other temporary Applied-style effect here already
    // uses) in case it's never actually consumed by any damage this turn.
    public int PendingDamagePrevention { get; set; }

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
        BurstStars = null;
        AppliedModifiers.Clear();
        AppliedKeywords.Clear();
        AppliedAffiliations.Clear();
        PendingDamagePrevention = 0;
    }
}
