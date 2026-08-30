import { dieIconKind } from "./dieHelpers";
import type { IconKind } from "./DieIcon";
import type { CardDef, Die } from "./types";

// The six faces of a physical die, for the 3D cube in DieCube.tsx.
//
// WHAT WE ACTUALLY KNOW. No source of real per-card face layouts exists
// (see PlaceholderDiceRoller.cs, which says the same thing about the
// roll side). What we know is the typical composition - three character
// faces, two double-energy, one single - and that real cards depart from
// it: Franklin's Galactus has FOUR character faces, and some characters
// that cost two energy types carry a generic side.
//
// So the set below is a plausible default, and the ONE face that is
// really true - the face the server says the die is showing - is forced
// into it by `facesFor`. Whatever the engine rolls, the face that ends up
// pointing at the player is exactly that result; the other five are
// scenery. That is the honest arrangement, and it means a future real
// face table can replace `defaultFaces` without touching anything else.

export type CubeFace =
  | { kind: "character"; level: number; fieldingCost: number; attack: number; defense: number }
  | { kind: "energy"; icon: IconKind; amount: number }
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

// A Basic Action die: three Action faces (blank / single / double burst)
// and three double-Generic energy faces.
const ACTION_FACES: CubeFace[] = [
  { kind: "action" },
  { kind: "action" },
  { kind: "action" },
  { kind: "energy", icon: "Generic", amount: 2 },
  { kind: "energy", icon: "Generic", amount: 2 },
  { kind: "energy", icon: "Generic", amount: 2 },
];

function energyIconFor(card: CardDef | undefined): IconKind {
  const type = card?.energyTypes[0];
  return type === "Bolt" || type === "Fist" || type === "Mask" || type === "Shield" ? type : "Generic";
}

function defaultFaces(die: Die, card: CardDef | undefined): CubeFace[] {
  if (!die.cardId) return SIDEKICK_FACES;
  if (card?.type === "BasicAction" || card?.type === "EpicBasicAction") return ACTION_FACES;

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

  // The rest are energy of the card's own type, doubles first - the
  // typical printing is two doubles to one single.
  const icon = energyIconFor(card);
  const remaining = FACE_COUNT - faces.length;
  for (let i = 0; i < remaining; i++) {
    faces.push({ kind: "energy", icon, amount: i < remaining - 1 ? 2 : 1 });
  }
  return faces;
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
  if (a.kind === "energy" && b.kind === "energy") return a.icon === b.icon && a.amount === b.amount;
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
