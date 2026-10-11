/** The single data contract between the Team Builder and Dice Kingdom setup. */
export interface CustomTeam {
  cards: { cardId: string; diceCount: number }[];
  tardigrades: { energyType: string; diceCount: number }[];
}

export const TARDIGRADE_TYPES = ["Claw", "Shell", "Wing", "Eye"] as const;
export type TardigradeType = (typeof TARDIGRADE_TYPES)[number];
export type TardigradeCounts = Record<TardigradeType, number>;
export const MAX_STARTING_TARDIGRADES = 8;

export function tardigradeTotal(counts: TardigradeCounts): number {
  return TARDIGRADE_TYPES.reduce((total, type) => total + counts[type], 0);
}

/** Clamp imported/saved counts to eight in total, keeping the original type order. */
export function capTardigrades(counts: Partial<TardigradeCounts>): TardigradeCounts {
  let remaining = MAX_STARTING_TARDIGRADES;
  const result = {} as TardigradeCounts;
  for (const type of TARDIGRADE_TYPES) {
    const value = counts[type];
    const valid = typeof value === "number" && Number.isSafeInteger(value) && value > 0 ? value : 0;
    result[type] = Math.min(valid, remaining);
    remaining -= result[type];
  }
  return result;
}

/** Do not add a die unless there is room across all four energy types. */
export function changeTardigradeCount(counts: TardigradeCounts, type: TardigradeType, delta: number): TardigradeCounts {
  const current = capTardigrades(counts);
  const available = MAX_STARTING_TARDIGRADES - tardigradeTotal(current) + current[type];
  return { ...current, [type]: Math.max(0, Math.min(available, current[type] + delta)) };
}
export const CUSTOM_TEAM_PICK = "__custom_team__";
const KEY = "dicekingdom:pendingCustomTeam";

let memoryTeam: CustomTeam | null = null; // storage blocked - lasts as long as the page

export function stageCustomTeam(team: CustomTeam): void {
  memoryTeam = team;
  try {
    sessionStorage.setItem(KEY, JSON.stringify(team));
  } catch {
    // Storage blocked (some privacy modes) - see memoryTeam.
  }
}
export function pendingCustomTeam(): CustomTeam | null {
  try {
    const value = sessionStorage.getItem(KEY);
    if (!value) return memoryTeam;
    const parsed: unknown = JSON.parse(value);
    if (!parsed || typeof parsed !== "object") return null;
    const team = parsed as CustomTeam;
    return Array.isArray(team.cards) && Array.isArray(team.tardigrades) ? team : null;
  } catch { return memoryTeam; }
}
