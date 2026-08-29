import { ENERGY_ICONS, type GameIcon } from "./gameIcons";

// The components that put src/gameIcons.ts's symbols on the page.

/** Renders one printed symbol inline, sized to the surrounding text. */
export function Icon({ icon, label }: { icon: GameIcon; label?: string }) {
  const text = label ?? icon.label;
  const className = icon.mono ? "card-icon card-icon-mono" : "card-icon";
  return <img className={className} src={icon.src} alt={text} title={text} />;
}

/**
 * A card's energy type(s) as symbols, one per type. Dual-energy
 * characters get both symbols rather than the split "either" one: the
 * split symbol means "either energy" in a cost, and reusing it here for
 * "both energies" reads as the wrong thing - the same reason the handful
 * of four-energy characters show four symbols.
 */
export function EnergyTypes({ types }: { types: readonly string[] }) {
  if (types.length === 0) return null;
  return (
    <>
      {types.map((t) => {
        const icon = ENERGY_ICONS[t.toLowerCase()];
        return icon ? <Icon key={t} icon={icon} /> : <span key={t}>{t}</span>;
      })}
    </>
  );
}
