namespace DiceFight.V2;

// Rule 2.7.2.2 - each blocker is assigned to a specific attacker, and
// multiple blockers may be assigned to the same attacker. Ported from v1
// nearly verbatim (V2_PLAN.md Phase 7 - "this part of v1 is good").
public sealed class CombatAssignment
{
    private readonly Dictionary<string, List<string>> _blockersByAttacker = new();

    public void AssignBlocker(string attackerDieId, string blockerDieId)
    {
        if (!_blockersByAttacker.TryGetValue(attackerDieId, out var blockers))
        {
            blockers = [];
            _blockersByAttacker[attackerDieId] = blockers;
        }

        blockers.Add(blockerDieId);
    }

    public IReadOnlyList<string> BlockersOf(string attackerDieId) =>
        _blockersByAttacker.TryGetValue(attackerDieId, out var blockers) ? blockers : [];
}

public sealed record CombatResult(IReadOnlyList<string> KOdDieIds);
