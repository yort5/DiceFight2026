import type { Die } from "./types";

/** A rolled Reserve Pool die showing usable energy is payment while a
 * purchase or creature-fielding payment is underway, even if its face
 * also has Attack/Defense stats. Fielding itself starts with Field. */
export function isReservePaymentDie(
  candidate: Pick<Die, "id" | "zone" | "energyAmount">,
  primary: Pick<Die, "id" | "zone"> | null,
  fieldPaymentDieId: string | null,
): boolean {
  return primary !== null &&
    candidate.id !== primary.id &&
    candidate.zone === "ReservePool" &&
    candidate.energyAmount > 0 &&
    (primary.zone === "Unpurchased" ||
      (primary.zone === "ReservePool" && fieldPaymentDieId === primary.id));
}
