import type { CardDef, Die } from "./types";

const SIDEKICK_FACE = { fieldingCost: 0, attack: 1, defense: 1 };

// Mirrors DieStats.GetFace on the engine side (rule 1.3 key / 1.6.8).
export function getDieFace(die: Die, cardsById: Map<string, CardDef>) {
  if (!die.cardId) return SIDEKICK_FACE;
  const card = cardsById.get(die.cardId);
  const face = card?.levels[Math.max(0, die.level - 1)];
  return face ?? SIDEKICK_FACE;
}

export function dieLabel(die: Die, cardsById: Map<string, CardDef>): string {
  if (!die.cardId) return "Sidekick";
  return cardsById.get(die.cardId)?.name ?? die.cardId;
}

// A short description of what a die is currently showing, for the chip.
export function dieStatusText(die: Die, cardsById: Map<string, CardDef>): string {
  if (die.status === "Energy") {
    return die.energyKind === "Specific" && die.providedEnergyType
      ? die.providedEnergyType
      : die.energyKind;
  }
  if (die.status === "Character" || die.status === "SidekickCharacter") {
    const face = getDieFace(die, cardsById);
    const dmg = die.damage > 0 ? `, ${die.damage} dmg` : "";
    return `L${die.level} · ${face.attack}A/${face.defense}D${dmg}`;
  }
  if (die.status === "Action") return "Action";
  if (die.status === "Unrolled" && die.cardId) {
    // Unpurchased dice: what it costs to buy, and which energy type(s)
    // that cost requires at least one of each of (rule 2.6.2.3) - Basic
    // Actions have no type requirement (rule 1.2.4/1.3.10), just a cost.
    const card = cardsById.get(die.cardId);
    if (card) {
      const energy = card.energyTypes.length > 0 ? ` · ${card.energyTypes.join("/")}` : "";
      return `Cost ${card.purchaseCost}${energy}`;
    }
  }
  return "";
}

export interface DieGroup {
  key: string;
  label: string;
  statusText: string;
  count: number;
  ids: string[];
}

// Collapses dice that are truly interchangeable right now (same card,
// level, damage, and face) into one chip with a count - mainly to keep
// the Unpurchased roster (up to 4 dice per card) and the Sidekick-heavy
// Bag/Used Pile zones from turning into a wall of identical chips.
export function groupDice(dice: Die[], cardsById: Map<string, CardDef>): DieGroup[] {
  const groups = new Map<string, DieGroup>();
  for (const die of dice) {
    const key = [die.cardId ?? "sidekick", die.level, die.damage, die.status, die.energyKind, die.providedEnergyType ?? ""].join("|");
    const existing = groups.get(key);
    if (existing) {
      existing.count += 1;
      existing.ids.push(die.id);
    } else {
      groups.set(key, {
        key,
        label: dieLabel(die, cardsById),
        statusText: dieStatusText(die, cardsById),
        count: 1,
        ids: [die.id],
      });
    }
  }
  return Array.from(groups.values());
}
