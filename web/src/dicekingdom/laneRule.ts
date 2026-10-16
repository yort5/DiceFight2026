// Stacking rule (2026-10-16), the same check as CombatEngine.CanAddToLane /
// LanesAllowed: a lane takes another attacker only if some other lane
// already has as many. An empty lane is always open. Whatever the order,
// that comes to: the fullest lane is at most one ahead of the next-fullest.

export const LANE_COUNT = 4;

// Attackers per lane from a pending dieId -> lane map, leaving out `without`
// (the die being placed or moved, so its current spot doesn't count).
export function laneCounts(pending: Record<string, number>, without?: string | null): number[] {
  const counts = Array.from({ length: LANE_COUNT }, () => 0);
  for (const [dieId, lane] of Object.entries(pending)) if (dieId !== without) counts[lane]++;
  return counts;
}

export function canAddToLane(counts: number[], lane: number): boolean {
  return counts[lane] === 0 || counts.some((c, i) => i !== lane && c >= counts[lane]);
}

export function lanesAllowed(pending: Record<string, number>): boolean {
  const sorted = laneCounts(pending).sort((a, b) => b - a);
  return sorted[0] <= sorted[1] + 1;
}

// Where "Declare" puts a die: the targeted lane if it can take it, else the
// emptiest lane (lowest number on a tie), which always can.
export function declareLane(counts: number[], preferred: number): number {
  if (canAddToLane(counts, preferred)) return preferred;
  const fewest = Math.min(...counts);
  return counts.indexOf(fewest);
}
