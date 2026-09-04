import type { CardDef, Die } from "./types";

export function dieLabel(die: Die, cardsById: Map<string, CardDef>): string {
  if (die.isTardigrade || !die.cardId) return "Tardigrade";
  return cardsById.get(die.cardId)?.name ?? die.cardId;
}

export interface CharacterFaceInfo {
  attack: number;
  defense: number;
}

// V2's Die already carries the true, modifier-inclusive stats directly -
// no per-level card lookup needed the way v1's dieHelpers.ts requires.
export function characterFaceInfo(die: Die): CharacterFaceInfo | null {
  if (die.effectiveAttack === null || die.effectiveDefense === null) return null;
  return { attack: die.effectiveAttack, defense: die.effectiveDefense };
}
