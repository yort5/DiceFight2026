// Pre-built format filters, ported from the reference Teambuilder
// (~/DiceMasters/Teambuilder/index.php - its `format_bans` map).
//
// That tool models a format as a BAN LIST: a card is legal unless its
// set code or its own card id appears in the format's list. Reading the
// real lists, the two formats worth having here are both simply
// release-order cutoffs (Silver Age's ban list is exactly the sets from
// AvX through TMNT - a contiguous prefix), so they are expressed that
// way instead. Shorter, and a newly-added set then becomes legal in the
// right formats automatically rather than needing every list edited.
//
// Deliberately NOT ported (user's call):
//  - Golden Era: bans only five individual cards, so it is effectively
//    "no filter" for team-building purposes.
//  - Modern Era / Global Escalation / Dice Fight Legacy: no longer used.

import { SET_NAMES } from "./sets";

// Set codes in release order - AvX (2013) first, MSW most recent. This
// is the order SET_NAMES is already written in, pulled out explicitly
// rather than relying on object key ordering, which is implicit and easy
// to break with an innocent-looking edit.
export const SET_RELEASE_ORDER: readonly string[] = [
  "AvX", "UXM", "YGO", "BFF", "JL", "AOU", "WOL", "ASM", "FUS", "WF",
  "CW", "TMNT", "GAF", "DRS", "DP", "HHS", "IMW", "DEF", "BAT", "SWW",
  "SMC", "GOTG", "XFC", "TOA", "THOR", "AI", "HQ", "KI", "JLL", "BFU",
  "ORK", "SW", "DOOM", "JUS", "MYST", "XMF", "XFO", "DXM", "AIW", "TIW",
  "ZHN", "WWE", "TAG", "BIT", "IG", "DPS", "SKC", "MSW",
];

// Cheap guard against the two lists drifting apart: a typo here would
// otherwise show up as a format silently missing a set.
const UNKNOWN_CODES = SET_RELEASE_ORDER.filter((code) => !(code in SET_NAMES));
if (UNKNOWN_CODES.length > 0) {
  console.warn(`formats.ts: set codes not present in SET_NAMES: ${UNKNOWN_CODES.join(", ")}`);
}

// Every set from `firstSet` onward, inclusive. PROMO spans every set and
// cannot be attributed to one release, so it stays legal in every format
// rather than being guessed at - the reference tool could be precise
// here because it modelled per-set promo codes (AvXop, JLop, ...), while
// this catalog has a single PROMO bucket.
function setsFrom(firstSet: string): ReadonlySet<string> {
  const start = SET_RELEASE_ORDER.indexOf(firstSet);
  if (start < 0) throw new Error(`formats.ts: unknown set code "${firstSet}" in a format definition.`);
  return new Set(["PROMO", ...SET_RELEASE_ORDER.slice(start)]);
}

export interface FormatDef {
  readonly id: string;
  readonly label: string;
  readonly description: string;
  readonly sets: ReadonlySet<string>;
}

export const FORMATS: readonly FormatDef[] = [
  {
    id: "silver",
    label: "Silver Age",
    description: "Green Arrow and The Flash onward.",
    sets: setsFrom("GAF"),
  },
  {
    id: "bronze",
    label: "Bronze Age",
    description: "Campaign Boxes onward - Avengers Infinity, Harley Quinn and later.",
    sets: setsFrom("AI"),
  },
];

// The Orange Ban list lives in ./orangeBan.ts - it is generated data
// (64 entries) rather than a hand-maintained constant, and it is applied
// ON TOP of whichever format is selected, since it is normally used in
// conjunction with one. That is why it is a separate checkbox rather
// than another entry in this dropdown.
