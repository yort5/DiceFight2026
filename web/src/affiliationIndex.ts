import type { CardDef } from "./types";

/** Where a logo lives. They are in public/ rather than bundled - see
 *  AffiliationIcons.tsx. */
export function affiliationIconUrl(code: string): string {
  return `${import.meta.env.BASE_URL}affiliations/a${code}.png`;
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
//      shows the SAME single logo. This is the rule that can hand back a
//      COMBINED image, since a card with two affiliations carries one
//      logo covering both - see OVERRIDES below, which pins a standalone
//      logo for every name that rule 2 resolves.
//   3. Anything still unresolved gets a generated badge - see
//      AffiliationBadge in AffiliationIcons.tsx. Currently 19 names,
//      most of them one-off misspellings of a name that does have one
//      ("X-men", "Avenger", "Zombies").

// Two kinds of choice the rules below cannot make for us.
//
// Where the same word is printed with more than one logo, the pick is
// ours. "Villains" is drawn as a red V in DC sets and as a different mark
// in Marvel ones; the V is the one people recognise. (The vote agrees -
// 219 to 131 - but pinning it means a future set cannot quietly flip it.)
//
// And where an affiliation only ever appears alongside a second one, the
// only logo the catalog can offer is the COMBINED image, which is
// literally two logos stacked in one picture: aKIUM is the Imperium eagle
// above the Ultramarines, aASS the Sinister Six "6" above the Villain
// mark. Right on a card that is both; wrong on a filter chip that means
// one. Each of these has a standalone logo in the same icon set, so the
// chip uses that instead.
const OVERRIDES: Record<string, string> = {
  Villains: "6",
  Villain: "6",
  // Warhammer: every card is a faction PLUS Imperium or Chaos, so the
  // only logo on offer is the stacked pair - and Imperium, which appears
  // stacked with two different factions, had no logo at all.
  Imperium: "KI",
  Ultramarine: "KUM",
  "Space Wolves": "KSW",
  Chaos: "KC",
  "Death Guard": "KDG",
  // Always printed alongside Villains, so likewise only ever stacked.
  "Sinister Six": "ASS1",
  "Orange Lanterns": "WO",
  "Black Lantern": "WKbw",
  "Black Lanterns": "WKbw",
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
    if (affiliations.length > 0 && affiliations.length === affiliationIcons.length) {
      affiliations.forEach((name, i) => vote(paired, name, affiliationIcons[i]));
    }
    if (affiliationIcons.length === 1) {
      for (const name of affiliations) {
        const seen = soleLogo.get(name) ?? new Set<string>();
        seen.add(affiliationIcons[0]);
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
