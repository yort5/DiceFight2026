import type { IconKind } from "./DieIcon";
import type { CardDef, Die } from "./types";

const SIDEKICK_FACE = { fieldingCost: 0, attack: 1, defense: 1 };

// Which face icon a die is currently showing, if any (rule 1.6.8's
// Sidekick face set, plus energy/Action faces generally) - a plain
// Character face (non-Sidekick) has no icon here since real character
// dice show card art, not a symbol, and we don't have card art.
export function dieIconKind(die: Die): IconKind | null {
  if (die.status === "Energy") {
    if (die.energyKind === "Wild") return "Wild";
    if (die.energyKind === "Generic") return "Generic";
    if (die.energyKind === "Specific" && die.providedEnergyType) {
      return die.providedEnergyType as IconKind;
    }
    return null;
  }
  if (die.status === "SidekickCharacter") return "Pawn";
  if (die.status === "Action") return "Action";
  return null;
}

// Mirrors DieStats.GetFace on the engine side (rule 1.3 key / 1.6.8).
export function getDieFace(die: Die, cardsById: Map<string, CardDef>) {
  if (!die.cardId) return SIDEKICK_FACE;
  const card = cardsById.get(die.cardId);
  const face = card?.levels[Math.max(0, die.level - 1)];
  return face ?? SIDEKICK_FACE;
}

// Only meaningful for a die currently showing a character face - the
// three stats printed on a real character die (fielding cost upper-left,
// attack upper-right, defense lower-right - see the reference card
// photos), used to draw a small die-face badge instead of plain text.
export interface CharacterFaceInfo {
  fieldingCost: number;
  attack: number;
  defense: number;
}

export function characterFaceInfo(die: Die, cardsById: Map<string, CardDef>): CharacterFaceInfo | null {
  if (die.status !== "Character" && die.status !== "SidekickCharacter") return null;
  const { fieldingCost, attack, defense } = getDieFace(die, cardsById);
  return { fieldingCost, attack, defense };
}

export function dieLabel(die: Die, cardsById: Map<string, CardDef>): string {
  if (die.isVirtualEnergy) return "Virtual Energy";
  if (!die.cardId) return "Sidekick";
  return cardsById.get(die.cardId)?.name ?? die.cardId;
}

// Whether the die's card carries a given keyword right now - `Die` itself
// has no keyword field, only `cardId`, so this always has to go through
// the catalog.
export function hasKeyword(die: Die, cardsById: Map<string, CardDef>, keyword: string): boolean {
  if (!die.cardId) return false;
  return cardsById.get(die.cardId)?.keywords.includes(keyword) ?? false;
}

// A short description of what a die is currently showing, for the chip.
export function dieStatusText(die: Die, cardsById: Map<string, CardDef>): string {
  if (die.status === "Energy") {
    const kind = die.energyKind === "Specific" && die.providedEnergyType ? die.providedEnergyType : die.energyKind;
    // A "double" face (rulebook's Doubles rule) is worth 2 - worth calling
    // out in the fallback text too, not just the enlarged rolled-zone
    // badge, since e.g. a partially-spent double still shows this way
    // once it's left the Reserve Pool.
    return die.energyAmount > 1 ? `${die.energyAmount} ${kind}` : kind;
  }
  if (die.status === "Character" || die.status === "SidekickCharacter") {
    // Attack/defense are printed on the die itself (DieCube) - this is
    // the text alongside it, and the whole description everywhere the
    // die is not drawn.
    const dmg = die.damage > 0 ? `, ${die.damage} dmg` : "";
    return `L${die.level}${dmg}`;
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

// Multiple cards can share a name across printings/reprints (different
// subtitle, cost, stats, and ability text) - the chip label alone can't
// tell them apart, so the hover tooltip leads with the subtitle and
// includes the full ability text (falls back to "(blank text box)" for
// the handful of real cards, like Colossus, that genuinely have none).
export function dieTooltip(die: Die, cardsById: Map<string, CardDef>): string | undefined {
  if (die.isVirtualEnergy) {
    return "Virtual generic energy - a stand-in for a physical die (from a draw shortfall, or the unspent half of a double). Lost at Clean Up if unspent.";
  }
  if (!die.cardId) return undefined;
  const card = cardsById.get(die.cardId);
  if (!card) return undefined;
  const header = card.subtitle ? `${card.name} — ${card.subtitle}` : card.name;
  return `${header}\n\n${card.rawText || "(blank text box)"}`;
}

export interface DieGroup {
  key: string;
  label: string;
  statusText: string;
  tooltip?: string;
  iconKind: IconKind | null;
  characterFace: CharacterFaceInfo | null;
  damage: number;
  energyAmount: number;
  count: number;
  ids: string[];
  /** One of the grouped dice - they are interchangeable by definition,
   *  so any of them answers "what is this showing". Kept so a consumer
   *  can derive more than the fields above (DieCube's face set). */
  die: Die;
}

function buildDieGroup(key: string, die: Die, cardsById: Map<string, CardDef>): DieGroup {
  return {
    key,
    label: dieLabel(die, cardsById),
    statusText: dieStatusText(die, cardsById),
    tooltip: dieTooltip(die, cardsById),
    iconKind: dieIconKind(die),
    characterFace: characterFaceInfo(die, cardsById),
    damage: die.damage,
    energyAmount: die.energyAmount,
    count: 1,
    ids: [die.id],
    die,
  };
}

// Collapses dice that are truly interchangeable right now (same card,
// level, damage, and face) into one chip with a count - mainly to keep
// the Unpurchased roster (up to 4 dice per card) and the Sidekick-heavy
// Bag/Used Pile zones from turning into a wall of identical chips.
// `collapse: false` (used for the rolled zones - Reserve Pool/Field
// Zone/Attack Zone - where each die gets its own visible face badge) skips
// this entirely, so e.g. two Sidekicks both showing a Bolt face render as
// two separate badges rather than one "Sidekick ×2" chip.
export function groupDice(dice: Die[], cardsById: Map<string, CardDef>, collapse: boolean = true): DieGroup[] {
  if (!collapse) {
    return dice.map((die) => buildDieGroup(die.id, die, cardsById));
  }

  const groups = new Map<string, DieGroup>();
  for (const die of dice) {
    const key = [die.cardId ?? "sidekick", die.level, die.damage, die.status, die.energyKind, die.providedEnergyType ?? "", die.energyAmount].join("|");
    const existing = groups.get(key);
    if (existing) {
      existing.count += 1;
      existing.ids.push(die.id);
    } else {
      groups.set(key, buildDieGroup(key, die, cardsById));
    }
  }
  return Array.from(groups.values());
}
