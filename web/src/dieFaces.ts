import { dieIconKind } from "./dieHelpers";
import type { IconKind } from "./DieIcon";
import type { CardDef, Die } from "./types";

// The six faces of a physical die, for the 3D cube in DieCube.tsx.
//
// WHAT WE ACTUALLY KNOW. No source of real per-card face layouts exists
// (see PlaceholderDiceRoller.cs, which says the same thing about the
// roll side). What we know, from the user:
//
//   - The typical composition is three character faces, two double-energy
//     and one single, all of the card's own energy type.
//   - Franklin's Galactus has FOUR character faces.
//   - A CROSSOVER character - one that costs two different energy types,
//     as every such card in GAF does - has a SPLIT double (Cosmic
//     Treadmill's is fist/mask) and a GENERIC single, not two faces of
//     one type.
//   - A card costing all four types has a WILD single, and a split double
//     whose pair follows no pattern - it is per card. All twenty such
//     cards are listed in FOUR_ENERGY_DOUBLES below.
//
// None of this is in the reference sheet, which records only what a card
// costs to buy, so it cannot be derived - it is written down here.
//
// So the set below is a plausible default, and the ONE face that is
// really true - the face the server says the die is showing - is forced
// into it by `facesFor`. Whatever the engine rolls, the face that ends up
// pointing at the player is exactly that result; the other five are
// scenery. That is the honest arrangement, and it means a future real
// face table can replace `defaultFaces` without touching anything else.

// Cube geometry. Where each face sits, and the cube rotation that brings
// face i forward - [rotateX, rotateY] in degrees. Here rather than in
// DieCube.tsx so the roll animation can aim at a face without importing
// a component.
export const FACE_TRANSFORMS = [
  "",
  "rotateY(180deg)",
  "rotateY(90deg)",
  "rotateY(-90deg)",
  "rotateX(90deg)",
  "rotateX(-90deg)",
];

export const FACE_ORIENTATIONS: readonly (readonly [number, number])[] = [
  [0, 0], [0, -180], [0, -90], [0, 90], [-90, 0], [90, 0],
];

export type CubeFace =
  | { kind: "character"; level: number; fieldingCost: number; attack: number; defense: number }
  // `secondIcon` is the other half of a split face - a Crossover
  // character's double shows both of its energy types in one symbol, not
  // two symbols side by side.
  | { kind: "energy"; icon: IconKind; secondIcon?: IconKind; amount: number }
  // The engine tracks a burst count per Action face, but DieDto does not
  // carry it, so an Action face is just an Action face on the client.
  | { kind: "action" };

const FACE_COUNT = 6;
const SIDEKICK_FACE: CubeFace = { kind: "character", level: 1, fieldingCost: 0, attack: 1, defense: 1 };

// Rule 1.6.8: one Level 1 character face and five single-energy faces,
// one of them Wild. This one we do know exactly.
const SIDEKICK_FACES: CubeFace[] = [
  SIDEKICK_FACE,
  { kind: "energy", icon: "Wild", amount: 1 },
  { kind: "energy", icon: "Fist", amount: 1 },
  { kind: "energy", icon: "Bolt", amount: 1 },
  { kind: "energy", icon: "Mask", amount: 1 },
  { kind: "energy", icon: "Shield", amount: 1 },
];

// Three Action faces (blank / single / double burst) - the half of an
// action die that is not energy. A BASIC Action die's other three are
// double-Generic (rule 1.3.10: "Basic Action dice provide generic
// energy"); a plain Action card has an energy type of its own and takes
// the same three faces a Character of that type would.
const ACTION_FACES: CubeFace[] = [{ kind: "action" }, { kind: "action" }, { kind: "action" }];

const BASIC_ACTION_ENERGY: CubeFace[] = [
  { kind: "energy", icon: "Generic", amount: 2 },
  { kind: "energy", icon: "Generic", amount: 2 },
  { kind: "energy", icon: "Generic", amount: 2 },
];

const ENERGY_ICONS_BY_NAME: Record<string, IconKind> = {
  Bolt: "Bolt", Fist: "Fist", Mask: "Mask", Shield: "Shield",
};

// The double-energy face of a card that costs all four types. There is no
// rule deriving these - each card just prints a pair - so they are data,
// from the user. Every such card is its set's 121-124 slot, and all
// twenty are covered here; the names are the catalog's, which are longer
// than the ones players use for two of them ("Captain Britain Iron Man",
// "Charles Xavier, Juggernaut"). All are unique names.
const FOUR_ENERGY_DOUBLES: Record<string, [IconKind, IconKind]> = {
  // BAT
  "White Lantern Aquaman": ["Fist", "Shield"],
  "White Lantern Dove": ["Mask", "Shield"],
  "White Lantern Hal Jordan": ["Bolt", "Mask"],
  "White Lantern Superman": ["Bolt", "Fist"],
  // GAF
  "White Lantern Batman": ["Bolt", "Shield"],
  "White Lantern Deadman": ["Bolt", "Fist"],
  "White Lantern Sinestro": ["Fist", "Mask"],
  "White Lantern Wonder Woman": ["Mask", "Shield"],
  // DP
  "Captain America with Mjolnir": ["Bolt", "Shield"],
  "Charles Xavier, Juggernaut": ["Fist", "Shield"],
  "Phoenix Force Magneto": ["Bolt", "Mask"],
  "Wolverine Lord of Vampires": ["Fist", "Mask"],
  // GOTG
  "Captain Britain Iron Man": ["Bolt", "Mask"],
  "Groot Thor": ["Bolt", "Fist"],
  "King Black Bolt": ["Fist", "Shield"],
  "Punisher Sorcerer Supreme": ["Mask", "Shield"],
  // XFC
  "Blink In-Betweener": ["Bolt", "Mask"],
  "Cosmic X-23": ["Mask", "Shield"],
  "Czar Colossus": ["Fist", "Shield"],
  "Phoenix Storm": ["Bolt", "Fist"],
};

function energyIcons(card: CardDef | undefined): IconKind[] {
  return (card?.energyTypes ?? []).map((t) => ENERGY_ICONS_BY_NAME[t]).filter((i): i is IconKind => i != null);
}

/**
 * The three energy faces of a character die, from what its card costs.
 *
 * One type: two doubles and a single, all of that type. Two or three (a
 * Crossover): the doubles are split symbols covering both types and the
 * single is generic. Four: the single is Wild.
 */
function characterEnergyFaces(card: CardDef | undefined): CubeFace[] {
  const icons = energyIcons(card);
  if (icons.length <= 1) {
    const icon = icons[0] ?? "Generic";
    return [
      { kind: "energy", icon, amount: 2 },
      { kind: "energy", icon, amount: 2 },
      { kind: "energy", icon, amount: 1 },
    ];
  }
  if (icons.length >= 4) {
    // The Wild single is right for all of them; the pair is per card. The
    // fallback covers a four-energy card printed after this list was
    // written, rather than any card in the catalog today.
    const pair = (card && FOUR_ENERGY_DOUBLES[card.name]) ?? [icons[0], icons[1]];
    return [
      { kind: "energy", icon: pair[0], secondIcon: pair[1], amount: 2 },
      { kind: "energy", icon: pair[0], secondIcon: pair[1], amount: 2 },
      { kind: "energy", icon: "Wild", amount: 1 },
    ];
  }
  return [
    { kind: "energy", icon: icons[0], secondIcon: icons[1], amount: 2 },
    { kind: "energy", icon: icons[0], secondIcon: icons[1], amount: 2 },
    { kind: "energy", icon: "Generic", amount: 1 },
  ];
}

// Appends energy faces until the die has six, whatever the card's own
// three are. More than three slots to fill means repeating a double
// (a card with fewer than three levels); fewer means dropping one.
function fillEnergy(faces: CubeFace[], energy: CubeFace[]): CubeFace[] {
  while (faces.length < FACE_COUNT) {
    const remaining = FACE_COUNT - faces.length;
    faces.push(remaining <= energy.length ? energy[energy.length - remaining] : energy[0]);
  }
  return faces;
}

function defaultFaces(die: Die, card: CardDef | undefined): CubeFace[] {
  if (!die.cardId) return SIDEKICK_FACES;
  if (card?.type === "BasicAction" || card?.type === "EpicBasicAction") {
    return [...ACTION_FACES, ...BASIC_ACTION_ENERGY];
  }
  // A plain Action card is an action die too, but with its own energy.
  if (card?.type === "Action") return fillEnergy([...ACTION_FACES], characterEnergyFaces(card));

  // Character faces come first and in level order, so a face's index IS
  // its level - which is what lets a spin be a quarter-turn of the cube.
  // The count is the card's own, not a hardcoded three: the day the
  // catalog carries Galactus's fourth face, this picks it up. (It does
  // not today - every sheet row has exactly three, and the engine's
  // roller caps at three as well.)
  const levels = card?.levels ?? [];
  const faces: CubeFace[] = levels.slice(0, FACE_COUNT - 1).map((face, i) => ({
    kind: "character",
    level: i + 1,
    fieldingCost: face.fieldingCost,
    attack: face.attack,
    defense: face.defense,
  }));
  if (faces.length === 0) faces.push(SIDEKICK_FACE);

  // The rest are energy. A four-level card (Galactus) has room for only
  // two of the three, and loses a double rather than its single.
  return fillEnergy(faces, characterEnergyFaces(card));
}

/** The face the server says this die is showing, or null if it shows none. */
function currentFace(die: Die, card: CardDef | undefined): CubeFace | null {
  if (die.status === "Energy") {
    const icon = dieIconKind(die);
    return icon ? { kind: "energy", icon, amount: Math.max(1, die.energyAmount) } : null;
  }
  if (die.status === "Action") return { kind: "action" };
  if (die.status === "SidekickCharacter") return SIDEKICK_FACE;
  if (die.status === "Character") {
    const face = card?.levels[Math.max(0, die.level - 1)];
    return face
      ? { kind: "character", level: die.level, fieldingCost: face.fieldingCost, attack: face.attack, defense: face.defense }
      : null;
  }
  return null;
}

function sameFace(a: CubeFace, b: CubeFace): boolean {
  if (a.kind !== b.kind) return false;
  if (a.kind === "character" && b.kind === "character") return a.level === b.level;
  if (a.kind === "energy" && b.kind === "energy") {
    return a.icon === b.icon && a.secondIcon === b.secondIcon && a.amount === b.amount;
  }
  return a.kind === "action" && b.kind === "action";
}

export interface DieFaces {
  faces: CubeFace[];
  /** Which face is pointing at the player. Always shows the real result. */
  index: number;
}

export function facesFor(die: Die, cardsById: Map<string, CardDef>): DieFaces {
  const card = die.cardId ? cardsById.get(die.cardId) : undefined;
  const faces = defaultFaces(die, card).slice();
  const showing = currentFace(die, card);
  if (!showing) return { faces, index: 0 };

  const found = faces.findIndex((face) => sameFace(face, showing));
  if (found >= 0) return { faces, index: found };

  // The default set does not contain what the die is really showing - a
  // generic side on a dual-energy character, say. The server wins:
  // overwrite a face of the same kind if there is a spare one, else the
  // last face, so the cube can land on the truth. Deterministic, so the
  // set does not shuffle between renders.
  const spare = faces.findIndex((face, i) => face.kind === showing.kind && i > 0);
  const slot = spare >= 0 ? spare : faces.length - 1;
  faces[slot] = showing;
  return { faces, index: slot };
}

/** How many faces of this die are character faces (what a spin can reach). */
export function characterFaceCount(faces: CubeFace[]): number {
  return faces.filter((face) => face.kind === "character").length;
}
