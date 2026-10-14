namespace DiceFight.V2;

// Rule 2.7.2.2 - each blocker is assigned to a specific attacker, and
// multiple blockers may be assigned to the same attacker. Ported from v1
// nearly verbatim (V2_PLAN.md Phase 7 - "this part of v1 is good").
public sealed class CombatAssignment
{
    private readonly Dictionary<string, List<string>> _blockersByAttacker = new();
    private IReadOnlyDictionary<string, int?>? _attackerLanes;

    public void AssignBlocker(string attackerDieId, string blockerDieId)
    {
        if (!_blockersByAttacker.TryGetValue(attackerDieId, out var blockers))
        {
            blockers = [];
            _blockersByAttacker[attackerDieId] = blockers;
        }

        blockers.Add(blockerDieId);
    }

    // Every (attacker, blocker) pair in assignment order.
    public IEnumerable<(string AttackerDieId, string BlockerDieId)> Pairs =>
        _blockersByAttacker.SelectMany(kv => kv.Value.Select(b => (kv.Key, b)));

    // Lane combat engages each attacker with every blocker assigned to that lane.
    public void CaptureLanes(GameState state) => _attackerLanes = state.Dice
        .Where(d => d.ControllerId == state.ActivePlayerId && d.Zone == Model.Zone.AttackZone)
        .ToDictionary(d => d.Id, d => d.Lane);

    public IReadOnlySet<string> OpponentsOf(GameState state, string dieId)
    {
        if (_attackerLanes is null) CaptureLanes(state);
        // A KO clears a die's live lane; its original combat partners remain known.
        if (_attackerLanes!.TryGetValue(dieId, out var lane))
            return _attackerLanes.Where(a => a.Value == lane).SelectMany(a => BlockersOf(a.Key)).ToHashSet();
        var lanes = Pairs.Where(p => p.BlockerDieId == dieId && _attackerLanes.ContainsKey(p.AttackerDieId))
            .Select(p => _attackerLanes[p.AttackerDieId]).ToHashSet();
        return _attackerLanes.Where(a => lanes.Contains(a.Value)).Select(a => a.Key).ToHashSet();
    }

    public IReadOnlyList<string> BlockersOf(string attackerDieId) =>
        _blockersByAttacker.TryGetValue(attackerDieId, out var blockers) ? blockers : [];
}

public sealed record CombatResult(IReadOnlyList<string> KOdDieIds);
