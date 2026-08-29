// A card's affiliation logos.
//
// The codes come from the card (see CardDef.AffiliationIcons) rather than
// from its affiliation text, because the two do not line up: one logo
// often stands for two affiliations at once - Doctor Octopus prints a
// single combined Sinister-Six-and-Villain mark, not two - and the same
// word is drawn with different logos in different universes ("Villains"
// is one logo in Marvel sets and another in DC). See
// scripts/extract_maxdice.py for where they are mined from.
//
// The images live in public/affiliations rather than being imported:
// there are 97 of them at ~154KB, which is more than half the JS bundle
// again if inlined, and only the handful of logos actually on screen ever
// gets fetched.
//
// A logo the file for which is not there yet falls back to the generated
// badge below, rather than being listed as missing anywhere in the code.
// Dropping the file into public/affiliations is all it takes for it to
// start showing; nothing here needs to change. That is how aBORDER
// (Black Order) and aHAND (the Hand) arrived - the old tool names both in
// its icon map but never drew them, and 404s on them to this day.
//
// Cards newer than that tool carry no codes at all; they fall back to a
// logo per affiliation NAME (see affiliationIndex.ts), and to a generated
// badge where even that comes up empty.

import { useState } from "react";
import { affiliationIconUrl } from "./affiliationIndex";

// A stand-in for the affiliations whose logo we do not have - either the
// old tool never drew one, or the name is a one-off misspelling of one
// that does. Drawn rather than shipped so that a name appearing for the
// first time still gets something recognisable in the filter, and so it
// is obvious at a glance which are real logos and which are waiting on
// one. Deliberately plain: initials on a disc, hue derived from the name
// so the same affiliation is the same colour every time.
function initials(name: string): string {
  // "a: Batman Family b: Villains" - the a:/b: markers are the sheet's
  // way of writing two affiliations in one cell, not part of the name.
  const words = name.replace(/\b[ab]:\s*/g, "").match(/[A-Za-z0-9]+/g);
  if (!words || words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[words.length - 1][0]).toUpperCase();
}

function hue(name: string): number {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360;
  return h;
}

export function AffiliationBadge({ name, code, label }: { name: string; code?: string; label?: string }) {
  const [imageFailed, setImageFailed] = useState(false);
  const text = label ?? name;
  if (code && !imageFailed) {
    return (
      <img
        className="affiliation-icon"
        src={affiliationIconUrl(code)}
        alt={text}
        title={text}
        onError={() => setImageFailed(true)}
      />
    );
  }
  const h = hue(name);
  return (
    <span
      className="affiliation-icon affiliation-icon-generated"
      title={`${text} (no logo yet)`}
      role="img"
      aria-label={text}
      style={{ background: `hsl(${h} 55% 42%)` }}
    >
      {initials(name)}
    </span>
  );
}

/**
 * One row's worth of logos. The card's own codes win where it has them -
 * that is the printing in front of you, combined marks and all. A card
 * the old tool never had falls back to a logo per affiliation NAME, from
 * the index the catalog is used to build, and to a generated badge for
 * the names that index cannot resolve.
 */
export function AffiliationIcons(
  { codes, names, index }: { codes: readonly string[]; names: readonly string[]; index: Record<string, string> },
) {
  const label = names.join(", ");
  if (names.length === 0 && codes.length === 0) return <span>-</span>;
  if (codes.length > 0) {
    return (
      <span className="affiliation-icons" title={label}>
        {codes.map((code) => (
          <AffiliationBadge key={code} name={names[0] ?? code} code={code} label={label} />
        ))}
      </span>
    );
  }
  return (
    <span className="affiliation-icons" title={label}>
      {names.map((name) => <AffiliationBadge key={name} name={name} code={index[name]} />)}
    </span>
  );
}
