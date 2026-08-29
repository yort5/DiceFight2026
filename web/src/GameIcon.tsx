import {
  ENERGY_ICONS,
  energyIcon,
  type GameIcon,
} from "./gameIcons";

// The components that put src/gameIcons.ts's symbols on the page.
/** Renders one printed symbol inline, sized to the surrounding text. */
export function Icon({ icon, label }: { icon: GameIcon; label?: string }) {
  const text = label ?? icon.label;
  return <img className="card-icon" src={icon.src} alt={text} title={text} />;
}

/**
 * A card's energy type(s) as symbols: one icon for a single type, the
 * split symbol for a dual-energy card, and one icon each for the handful
 * of cards that carry all four.
 */
export function EnergyTypes({ types }: { types: readonly string[] }) {
  if (types.length === 0) return null;
  const combined = energyIcon(types);
  // The split symbol means "either energy" in a cost but "both energies"
  // on a dual-energy character, so the column labels it by its types
  // rather than reusing the cost wording.
  if (combined) return <Icon icon={combined} label={types.join(" / ")} />;
  return (
    <>
      {types.map((t) => {
        const icon = ENERGY_ICONS[t.toLowerCase()];
        return icon ? <Icon key={t} icon={icon} /> : <span key={t}>{t}</span>;
      })}
    </>
  );
}
