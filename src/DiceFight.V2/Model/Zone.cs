namespace DiceFight.V2.Model;

// The nine zones, ported unchanged from v1 (see V2_PLAN.md Phase 2 task 1).
// DiceFromBag/DiceFromPrep are transient staging zones used only within a
// single Clear & Draw -> Roll & Reroll cycle - see v1's TurnEngine remarks
// for why the Prep Area needed splitting into a persistent zone plus these
// two staging zones (a Pepper Potts-shaped rules interaction).
public enum Zone
{
    Bag,
    PrepArea,
    ReservePool,
    FieldZone,
    AttackZone,
    UsedPile,
    OutOfPlay,
    DiceFromBag,
    DiceFromPrep,
}
