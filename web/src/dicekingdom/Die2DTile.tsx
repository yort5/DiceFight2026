import { CHARACTER_ICONS, EnergyBadge, TardigradeIcon, type IconComponent } from "./icons";
import { dieEnergyType } from "./dieDisplay";
import type { CardDef, Die } from "./types";

/** Mobile's colored identity tile, shared by both layouts and every flat zone. */
export function Die2DTile({ die, cardsById, size, count = 1, showEnergyBadge = false, avatar }: {
  die: Die;
  cardsById: Map<string, CardDef>;
  size: number;
  count?: number;
  showEnergyBadge?: boolean;
  /** Swaps the art (ArtTestPage.tsx's style comparison). */
  avatar?: IconComponent;
}) {
  const Avatar = avatar ?? (die.cardId ? CHARACTER_ICONS[die.cardId] : undefined) ?? TardigradeIcon;
  const type = dieEnergyType(die, cardsById);
  const color = `var(--${type.toLowerCase()}, var(--wild))`;
  const discSize = Math.round(size * 0.5);
  return (
    <span className="dk-die-2d" aria-hidden="true" data-energy-type={type}
      style={{ width: size, height: size, borderColor: color }}>
      <span className="dk-die-identity" style={{ width: discSize, height: discSize, background: color }}>
        <Avatar size={Math.round(discSize * 0.62)} />
      </span>
      {showEnergyBadge && <span className="dk-die-type-badge">
        <EnergyBadge type={type} size={Math.round(size * 0.34)} />
      </span>}
      {count > 1 && <span className="dk-die-count" style={{ background: color }}>×{count}</span>}
    </span>
  );
}
