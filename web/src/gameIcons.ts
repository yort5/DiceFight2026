// The printed symbols that appear on Dice Masters cards, as images.
//
// These are the old Teambuilder's own icon set (tb.dicecoalition.com),
// copied into src/assets so they resolve offline and get hashed by Vite.
// Its `iconid` map is the key to the filenames: e1-e4 are the four energy
// types, e5-eA the split "either" symbols, e10/e11/e33/e44 the doubles,
// eg0-eg9 + egx generic energy, and pawn/action/flip/burst/eq/d* the rest.
// Everything in the old set is copied in; the ones this file exports are
// the ones we render today.
//
// All are 17x17 PNGs well under Vite's 4KB inline limit, so they ship as
// data URIs - no extra requests, nothing to deploy alongside the bundle.
//
// See src/assets/README.md for the full inventory, including the symbols
// copied in but not yet rendered anywhere. GameIcon.tsx renders these.

import bolt from "./assets/energy-bolt.png";
import fist from "./assets/energy-fist.png";
import mask from "./assets/energy-mask.png";
import shield from "./assets/energy-shield.png";
import boltOrFist from "./assets/energy-bolt-or-fist.png";
import boltOrMask from "./assets/energy-bolt-or-mask.png";
import boltOrShield from "./assets/energy-bolt-or-shield.png";
import fistOrMask from "./assets/energy-fist-or-mask.png";
import fistOrShield from "./assets/energy-fist-or-shield.png";
import maskOrShield from "./assets/energy-mask-or-shield.png";
import sidekick from "./assets/sidekick.png";
import wild from "./assets/energy-wild.png";
import generic0 from "./assets/energy-generic-0.png";
import generic1 from "./assets/energy-generic-1.png";
import generic2 from "./assets/energy-generic-2.png";
import generic3 from "./assets/energy-generic-3.png";
import generic4 from "./assets/energy-generic-4.png";
import generic5 from "./assets/energy-generic-5.png";
import generic6 from "./assets/energy-generic-6.png";
import generic7 from "./assets/energy-generic-7.png";
import generic8 from "./assets/energy-generic-8.png";
import generic9 from "./assets/energy-generic-9.png";

export interface GameIcon {
  src: string;
  /** The word the icon replaces. Used as alt text, so the line still
   *  reads correctly when copied, read aloud, or when images fail. */
  label: string;
  /** True for the black-and-white symbols (generic energy, the wild
   *  face, the Sidekick pawn). The four energy types are coloured discs
   *  that read on any background; these are dark ink on white, so they
   *  have to be inverted in dark mode or they sink into the page. */
  mono?: boolean;
}

/** The four energy types, keyed by their lowercase name. */
export const ENERGY_ICONS: Record<string, GameIcon> = {
  bolt: { src: bolt, label: "Bolt" },
  fist: { src: fist, label: "Fist" },
  mask: { src: mask, label: "Mask" },
  shield: { src: shield, label: "Shield" },
};

/**
 * The split "either energy" symbols, keyed by both orderings of the pair
 * so a lookup never has to sort first. A card that costs one of two
 * energies prints this single two-colour symbol rather than two symbols,
 * which is the difference between "pay Bolt or Mask" and "pay Bolt and
 * Mask" - see the `X/Y` handling in CardText.tsx.
 *
 * OR is the only thing these mean here. The old Teambuilder also used
 * them to mark a dual-energy CHARACTER, where the same image has to be
 * read as "both"; the Energy column shows two separate symbols instead.
 */
export const SPLIT_ENERGY_ICONS: Record<string, GameIcon> = {
  "bolt/fist": { src: boltOrFist, label: "Bolt or Fist" },
  "bolt/mask": { src: boltOrMask, label: "Bolt or Mask" },
  "bolt/shield": { src: boltOrShield, label: "Bolt or Shield" },
  "fist/mask": { src: fistOrMask, label: "Fist or Mask" },
  "fist/shield": { src: fistOrShield, label: "Fist or Shield" },
  "mask/shield": { src: maskOrShield, label: "Mask or Shield" },
};
for (const key of Object.keys(SPLIT_ENERGY_ICONS)) {
  const [a, b] = key.split("/");
  SPLIT_ENERGY_ICONS[`${b}/${a}`] = SPLIT_ENERGY_ICONS[key];
}

/** Generic energy - any type will do. Printed as a numeral in a circle. */
export const GENERIC_ENERGY_ICONS: Record<string, GameIcon> = Object.fromEntries(
  [generic0, generic1, generic2, generic3, generic4, generic5, generic6, generic7, generic8, generic9].map(
    (src, n) => [String(n), { src, label: String(n), mono: true }],
  ),
);

export const SIDEKICK_ICON: GameIcon = { src: sidekick, label: "Sidekick", mono: true };
/** The "?" wild face, which counts as any energy type. */
export const WILD_ENERGY_ICON: GameIcon = { src: wild, label: "?", mono: true };
