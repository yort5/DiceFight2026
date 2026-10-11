import type { Die } from "./types";

/** Mirror TurnEngine.SpendEnergy/SpendDice eligibility for manually selected
 * Reserve dice. Virtual energy is generic: it reduces what dice owe, but
 * never satisfies a card's printed type requirement. The server stops
 * consuming offered dice once the remaining numerical cost is met. */
export function evaluateSelectedPayment(
  selected: Pick<Die, "energyAmount" | "energySymbolId">[],
  cost: number,
  requiredTypes: readonly string[],
  virtualEnergy = 0,
): { ready: boolean; typeMissing: boolean } {
  if (cost <= 0) return { ready: true, typeMissing: false };
  let adequateEnergy = false;
  for (let virtual = Math.min(cost, virtualEnergy); virtual >= 0; virtual--) {
    const remaining = cost - virtual;
    let amount = 0;
    const unmatched = new Set(requiredTypes);
    let wildPips = 0;
    for (const die of selected) {
      if (amount >= remaining) break;
      if (die.energyAmount <= 0) continue;
      amount += die.energyAmount;
      if (die.energySymbolId === "Wild") wildPips += die.energyAmount;
      else if (die.energySymbolId && die.energySymbolId !== "Generic") unmatched.delete(die.energySymbolId);
    }
    if (amount < remaining) continue;
    adequateEnergy = true;
    if (unmatched.size <= wildPips) return { ready: true, typeMissing: false };
  }
  return { ready: false, typeMissing: adequateEnergy && requiredTypes.length > 0 };
}
