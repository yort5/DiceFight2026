namespace DiceFight.V2.Model;

// Rule 2.7 - Attack Step sub-steps, scoped to what V2_PLAN.md Phase 7
// actually asks for (declare attackers -> blockers -> action/global
// window -> assign damage -> KO resolution). v1's own AttackSubStep also
// has RangeWindow/InfiltrateWindow/TagOutWindow for keywords outside the
// closed vocabulary's current scope (Range/Infiltrate/Tag Out aren't
// CombatFlag/CombatRule-shaped grants - they're bespoke sub-windows) -
// deliberately not ported; those keywords go to V2_TAIL_POLICY.md if/when
// Phase 8's card migration needs them, same as any other card-specific
// mechanic the closed vocabulary doesn't cover.
public enum AttackSubStep
{
    DeclareAttackers,
    DeclareBlockers,
    ActionAndGlobalWindow,
    AssignCombatDamage,
    Done,
}
