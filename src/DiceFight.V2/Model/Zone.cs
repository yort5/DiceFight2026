namespace DiceFight.V2.Model;

// Ten zones. V2_PLAN.md Phase 2 task 1 says "the same nine as v1" and
// names exactly nine - that undercounted v1's real zone list by one, found
// while implementing this phase: v1's Zone enum also has Unpurchased,
// where EVERY card's dice (Character and Action/Basic Action alike) sit
// until bought (see v1's TeamSetup.SetupTeamDice - only Sidekicks start
// directly in the Bag). It's not keyword-gated the way v1's own
// Intimidated zone is (that one stays out, deferred to Phase 7 when the
// Intimidate keyword itself is built), so leaving it out here would have
// made Purchase itself unrepresentable. Corrected in place rather than
// treated as a sign-off question - the plan's own stated intent was
// "same as v1," and this is a faithful-port correction, not a new design
// decision (same category as the purchase-cost-floor erratum, Part 1).
//
// DiceFromBag/DiceFromPrep are transient staging zones used only within a
// single Clear & Draw -> Roll & Reroll cycle - see v1's TurnEngine remarks
// for why the Prep Area needed splitting into a persistent zone plus these
// two staging zones (a Pepper Potts-shaped rules interaction). Declared
// here per the frozen list but not yet routed through (see TurnEngine's
// own remarks) - the interrupt window they exist for needs an ability
// layer this phase doesn't have.
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
    Unpurchased,
}
