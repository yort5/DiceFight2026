namespace DiceFight.V2.Model;

// The five turn steps, ported unchanged from v1 (V2_PLAN.md Phase 2 task 3).
// Attack sub-steps (rule 2.7.0.1's six-plus-bookkeeping shape) are Phase 7
// (Combat) territory - not modeled yet, matching "no behavior" for this
// phase; SkipAttackStep is the only Attack-related action Phase 2 needs.
public enum TurnStep
{
    ClearAndDraw,
    RollAndReroll,
    Main,
    Attack,
    CleanUp,
}
