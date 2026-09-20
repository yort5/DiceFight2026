import type { ReactElement } from "react";
import { CHARACTER_ICONS, TardigradeIcon } from "./icons";
import type { CardDef, Die } from "./types";

// The six faces of a physical die, for the 3D cube in DieCube.tsx.
// Ported from ../dieFaces.ts, but genuinely simpler to build than v1's
// copy: V2's Die already gives the true, modifier-inclusive
// effectiveAttack/effectiveDefense directly (no per-level card lookup,
// no energyKind/providedEnergyType/secondProvidedEnergyType to resolve),
// so this only has to answer "which of the six faces is up" - the
// numbers drawn on that face come straight from the die.
//
// MIRRORS src/DiceFight.V2/Data/DiceKingdomConfig.cs's TardigradeDie/
// CharacterDie, which are the authority. A Tardigrade die's six faces are
// a fixed, locked spec (v3/DESIGN_NOTES.md): two L1 (0A/1D), two L2
// (1A/1D), one L3 "Bulwark" (1A/3D), one "Surge" (a pure Wild-energy
// face, no character stats at all). A Character die (2026-09-07, later-
// Dice-Masters layout, was "three levels each printed twice, no energy
// at all"): three stat faces, one per level (no doubling), plus three
// energy faces of the card's own type - two double, one single.

// Cube geometry - identical to v1's, since this is pure 3D placement math
// with nothing Marvel- or animal-specific in it. Each slot's fixed LOCAL
// position within the cube - slot 0 is always the forward-facing slot
// (DieCube's `.front`, whichever face is the die's current value); slots
// 1-5 are the other five faces, shown as decorative fill during a tumble.
//
// Animation refresh (2026-09-16, design_handoff.../ANIMATIONS.md): the
// cube itself no longer carries a per-value RESTING rotation the way it
// used to (a removed FACE_ORIENTATIONS table rotated the whole cube so
// slot `index` ended up forward) - every tumble keyframe track (see
// useDiceRoll.ts) starts AND ends at a whole multiple of 360°, i.e.
// always visually identical to flat/no-rotation, so slot 0 is ALWAYS the
// forward slot at rest. Which value that slot shows is just "whatever
// `.front`'s content prop currently is" - a plain React re-render, not a
// rotation target - which is what makes decoupling the two safe: mid-
// tumble, slot 0 is rotated away from the camera anyway, so its content
// can update the instant new data arrives without the viewer ever seeing
// a mismatched face-vs-rotation frame; it's only readable again once the
// keyframes return it to flat, by which point it's already correct.
export const FACE_TRANSFORMS = [
  "",
  "rotateY(180deg)",
  "rotateY(90deg)",
  "rotateY(-90deg)",
  "rotateX(90deg)",
  "rotateX(-90deg)",
];

// `avatar` is optional on both variants and carries no gameplay meaning
// of its own - it's the SAME icon on every face of a given physical die
// (see defaultFaces below), just along for the ride so DieCube can put
// it in the face's spare center space. Direct feedback (2026-09-05):
// "I don't really know which dice are Tardigrades and which one is a
// Pangolin... Character stat faces should definitely have that
// character's symbol in the center" - without this, only a card's OWN
// stat faces carried any per-card identity at all (via the small
// external badge DieTile used to draw beside the cube, now removed as
// redundant); an energy face - Surge included - looked identical no
// matter which physical card it belonged to.
type Avatar = (p: { size?: number }) => ReactElement;

export type CubeFace =
  | { kind: "character"; level: number; fieldingCost: number; attack: number; defense: number; avatar?: Avatar }
  | { kind: "energy"; icon: string; amount: number; avatar?: Avatar };

const FACE_COUNT = 6;

// v3's locked Tardigrade spec, straight from TardigradeDie in
// DiceKingdomConfig.cs - not derived from a CardDef, since a Tardigrade
// die has no cardId at all (isTardigrade instead).
const TARDIGRADE_FACES: CubeFace[] = [
  { kind: "character", level: 1, fieldingCost: 0, attack: 0, defense: 1, avatar: TardigradeIcon },
  { kind: "character", level: 1, fieldingCost: 0, attack: 0, defense: 1, avatar: TardigradeIcon },
  { kind: "character", level: 2, fieldingCost: 0, attack: 1, defense: 1, avatar: TardigradeIcon },
  { kind: "character", level: 2, fieldingCost: 0, attack: 1, defense: 1, avatar: TardigradeIcon },
  { kind: "character", level: 3, fieldingCost: 0, attack: 1, defense: 3, avatar: TardigradeIcon }, // Bulwark
  { kind: "energy", icon: "Wild", amount: 1, avatar: TardigradeIcon }, // Surge - dropped from 2 (2026-09-05 playtest experiment)
];

function defaultFaces(die: Die, card: CardDef | undefined): CubeFace[] {
  if (die.isTardigrade || !die.cardId) return TARDIGRADE_FACES;
  const levels = card?.levels ?? [];
  if (levels.length === 0) return TARDIGRADE_FACES;
  const energyType = card?.energyTypes[0] ?? "Wild";
  const avatar = CHARACTER_ICONS[die.cardId];
  const faces: CubeFace[] = levels.map((level, i) => ({
    kind: "character", level: i + 1, fieldingCost: level.fieldingCost, attack: level.attack, defense: level.defense, avatar,
  }));
  faces.push({ kind: "energy", icon: energyType, amount: 2, avatar });
  faces.push({ kind: "energy", icon: energyType, amount: 2, avatar });
  faces.push({ kind: "energy", icon: energyType, amount: 1, avatar });
  return faces.slice(0, FACE_COUNT);
}

/** The face the server says this die is showing, or null if it shows none. */
function currentFace(die: Die): CubeFace | null {
  if (die.level !== null && die.effectiveAttack !== null && die.effectiveDefense !== null) {
    return { kind: "character", level: die.level, fieldingCost: 0, attack: die.effectiveAttack, defense: die.effectiveDefense };
  }
  if (die.energySymbolId) {
    return { kind: "energy", icon: die.energySymbolId, amount: Math.max(1, die.energyAmount) };
  }
  return null;
}

export interface DieFaces {
  faces: CubeFace[];
  /** Which face is pointing at the player. Always shows the real result. */
  index: number;
}

export function facesFor(die: Die, cardsById: Map<string, CardDef>): DieFaces {
  const card = die.cardId ? cardsById.get(die.cardId) : undefined;
  const faces = defaultFaces(die, card).slice();
  const showing = currentFace(die);
  if (!showing) return { faces, index: 0 };

  // Real bug, direct feedback (2026-09-17): "both dice in the picture
  // showed zero attack on the screen" despite different real levels -
  // this used to write `showing` into whichever static-table slot
  // matched the die's level/kind (e.g. slot 2 for a level-2 face) and
  // report THAT slot as `index`. That was correct back when the cube
  // physically rotated to bring slot `index` forward (the old, now-
  // removed FACE_ORIENTATIONS), but DieCube.tsx's own header comment
  // states the model this file was never updated to match: post-
  // animation-refresh, the cube always rests at net-zero rotation, so
  // slot 0 is the ONLY slot ever physically facing the camera - every
  // other slot's `.front` class was correct DOM/data, but invisible.
  // Confirmed live (a Playwright screenshot, not just DOM text) showing
  // three different-level Tardigrades all rendering the same static
  // level-1 default (0/1) - slot 0's un-overwritten content - while
  // `.front`'s own (unseen) text already held the right numbers.
  const existing = faces[0];
  faces[0] = {
    ...showing,
    avatar: existing.avatar,
    ...(showing.kind === "character" && existing.kind === "character" ? { fieldingCost: existing.fieldingCost } : {}),
  };
  return { faces, index: 0 };
}
