import { rolled } from "./bot";
import type { CardDef, Die } from "./types";

export type DieDisplayMode = "2d" | "3d";

export const ROLLED_ZONES = new Set(["ReservePool", "FieldZone", "AttackZone", "Intimidated"]);
export const SIDE_TILE_ZONES = new Set(["Bag", "UsedPile", "PrepArea", "OutOfPlay"]);

/** Piles show identity even when the DTO retains a previous rolled face. */
export function displayModeFor(die: Die, zone = die.zone): DieDisplayMode {
  return ROLLED_ZONES.has(zone) && rolled(die) ? "3d" : "2d";
}

/** The physical die's energy type, independent of its current rolled face. */
export function dieEnergyType(die: Die, cardsById: Map<string, CardDef>): string {
  if (die.isTardigrade || !die.cardId) {
    return /Tardigrade(Claw|Shell|Wing|Eye|Wild)(?:-|$)/.exec(die.id)?.[1] ?? "Wild";
  }
  const card = cardsById.get(die.cardId);
  return card?.dieEnergyType ?? card?.energyTypes[0] ?? "Wild";
}

/** Flat tiles may stack only when both their identity and energy badge match. */
export function dieIdentityKey(die: Die, cardsById: Map<string, CardDef>): string {
  return `${die.cardId ?? "tardigrade"}|${dieEnergyType(die, cardsById)}`;
}
