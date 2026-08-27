// Search-box matching for the card catalog, including the operator
// syntax the old Teambuilder supported (its `regexfilter`):
//
//   a & b   both must match
//   a | b   either may match  (lower precedence than &)
//   ~a      must NOT match
//   ^a      the card's NAME must START WITH a
//
// A query with none of those characters is a plain substring match, so
// the common case is unchanged and nothing needs escaping.
//
// Two deliberate differences from the old tool. It had separate "name"
// and "text" boxes, each with its own operators; this is one box, so a
// bare term is matched against everything printed on the card (name,
// subtitle, affiliations, rules text). And `^` there was a lookahead at
// the start of whichever field the box targeted - here it always means
// the card's name, which is what people mean by "starts with".

const OPERATOR_CHARS = /[&|~^]/;

export interface SearchableCard {
  name: string;
  subtitle: string | null;
  affiliations: string[];
  rawText: string;
}

/** Everything printed on the card, lowercased, for bare/`~` terms. */
function haystack(card: SearchableCard): string {
  return [card.name, card.subtitle ?? "", ...card.affiliations, card.rawText]
    .join(" ")
    .toLowerCase();
}

function matchesTerm(term: string, card: SearchableCard, hay: string): boolean {
  if (term.startsWith("^")) {
    const prefix = term.slice(1).trim();
    return prefix.length === 0 || card.name.toLowerCase().startsWith(prefix);
  }
  if (term.startsWith("~")) {
    const body = term.slice(1).trim();
    // An empty "~" excludes nothing rather than excluding everything -
    // otherwise a half-typed query blanks the table.
    return body.length === 0 || !hay.includes(body);
  }
  return hay.includes(term);
}

/**
 * True if `card` matches `query`. An empty query matches everything.
 * `query` is matched case-insensitively.
 */
export function matchesQuery(card: SearchableCard, query: string): boolean {
  const needle = query.trim().toLowerCase();
  if (needle.length === 0) return true;

  const hay = haystack(card);
  if (!OPERATOR_CHARS.test(needle)) return hay.includes(needle);

  // OR binds loosest, so split on it first; every AND group inside an
  // alternative must match for that alternative to succeed.
  return needle.split("|").some((alternative) => {
    const terms = alternative
      .split("&")
      .map((t) => t.trim())
      .filter((t) => t.length > 0);
    // "a |" - an empty alternative would otherwise match everything and
    // make the whole query a no-op mid-typing.
    if (terms.length === 0) return false;
    return terms.every((term) => matchesTerm(term, card, hay));
  });
}
