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
// Two codes have no image in the old tool at all (the Black Order and
// the Hand); those cards fall back to their affiliation text, as do the
// ~630 cards newer than that tool.

export function AffiliationIcons({ codes, names }: { codes: readonly string[]; names: readonly string[] }) {
  const label = names.join(", ");
  if (codes.length === 0) return <span>{label || "-"}</span>;
  return (
    <span className="affiliation-icons" title={label}>
      {codes.map((code) => (
        <img
          key={code}
          className="affiliation-icon"
          src={`${import.meta.env.BASE_URL}affiliations/a${code}.png`}
          alt={label}
          // A code whose image the old tool never had would otherwise
          // leave a broken-image glyph in the middle of the table.
          onError={(e) => { e.currentTarget.style.display = "none"; }}
        />
      ))}
    </span>
  );
}
