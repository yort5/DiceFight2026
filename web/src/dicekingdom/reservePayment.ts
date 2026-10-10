import type { Die } from "./types";

/** Only consume Reserve dice as payment after the player explicitly starts
 * Field or Purchase; otherwise clicking a different character selects it.
 * Character faces with energy remain valid payment choices. */
export function isReservePaymentDie(
  candidate: Pick<Die, "id" | "zone" | "energyAmount">,
  primary: Pick<Die, "id" | "zone"> | null,
  paymentForId: string | null,
): boolean {
  return primary !== null &&
    candidate.id !== primary.id &&
    candidate.zone === "ReservePool" &&
    candidate.energyAmount > 0 &&
    paymentForId === primary.id &&
    (primary.zone === "Unpurchased" || primary.zone === "ReservePool");
}
