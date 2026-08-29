import type { CardDef } from "./types";

/** Where a logo lives. They are in public/ rather than bundled - see
 *  AffiliationIcons.tsx. */
export function affiliationIconUrl(code: string): string {
  return `${import.meta.env.BASE_URL}affiliations/a${code}.png`;
}

// Two codes the old tool names but never actually drew - it 404s on them
// itself. Treated as "no logo" everywhere so they fall through to a
// generated badge instead of leaving a broken image in the table.
const NO_IMAGE = new Set(["BORDER", "HAND"]);

export function hasAffiliationIcon(code: string): boolean {
  return !NO_IMAGE.has(code);
}

// Picking one logo per affiliation NAME, for the filter.
//
// Cards carry their own logo codes (see CardDef.AffiliationIcons), which
// is the right thing per row - it is what that printing actually shows.
// The filter needs the other direction: one logo for the word
// "Villains". There is no such mapping in any source, because the two do
// not line up (one logo often covers two affiliations at once), so it is
// learned from the catalog instead. That means it keeps working as cards
// are added, with nothing to maintain.
//
//   1. Cards whose affiliation list and logo list are the same length
//      pair up one-to-one; each pairing is a vote. Most names are
//      settled here, and where printings disagree the majority wins.
//   2. Names left over take a logo only if every card carrying that name
//      shows the SAME single logo - which is how the combined marks get
//      picked up (Sinister Six, the Warhammer factions, Black Lanterns).
//   3. Anything still unresolved gets a generated badge - see
//      AffiliationBadge in AffiliationIcons.tsx. Currently 19 names,
//      most of them one-off misspellings of a name that does have one
//      ("X-men", "Avenger", "Zombies").

// Where the same word is printed with more than one logo, the choice is
// ours to make. "Villains" is drawn as a red V in DC sets and as a
// different mark in Marvel ones; the V is the one people recognise.
// (The vote agrees - 219 to 131 - but pinning it means a future set
// cannot quietly flip it.)
const OVERRIDES: Record<string, string> = {
  Villains: "6",
  Villain: "6",
};

export function buildAffiliationIconIndex(cards: readonly CardDef[]): Record<string, string> {
  const paired = new Map<string, Map<string, number>>();
  const soleLogo = new Map<string, Set<string>>();

  const vote = (into: Map<string, Map<string, number>>, name: string, code: string) => {
    const counts = into.get(name) ?? new Map<string, number>();
    counts.set(code, (counts.get(code) ?? 0) + 1);
    into.set(name, counts);
  };

  for (const card of cards) {
    const { affiliations, affiliationIcons } = card;
    const icons = affiliationIcons.filter(hasAffiliationIcon);
    if (affiliations.length > 0 && affiliations.length === icons.length) {
      affiliations.forEach((name, i) => vote(paired, name, icons[i]));
    }
    if (icons.length === 1) {
      for (const name of affiliations) {
        const seen = soleLogo.get(name) ?? new Set<string>();
        seen.add(icons[0]);
        soleLogo.set(name, seen);
      }
    }
  }

  const index: Record<string, string> = {};
  for (const [name, counts] of paired) {
    let best = "";
    let bestCount = 0;
    for (const [code, count] of counts) {
      if (count > bestCount) { best = code; bestCount = count; }
    }
    index[name] = best;
  }
  for (const [name, seen] of soleLogo) {
    if (!(name in index) && seen.size === 1) index[name] = [...seen][0];
  }
  return { ...index, ...OVERRIDES };
}
