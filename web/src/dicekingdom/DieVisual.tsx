import { DieCube, type CubeSpin } from "./DieCube";
import { Die2DTile } from "./Die2DTile";
import { displayModeFor, SIDE_TILE_ZONES, type DieDisplayMode } from "./dieDisplay";
import { facesFor } from "./dieFaces";
import { tileCues } from "./statusCues";
import { SpinFlash, useSpinFlash } from "./SpinFlash";
import type { CardDef, Die } from "./types";

/** Visuals only; zone wrappers own clicks, inspection, selection and flights. */
export function DieVisual({ die, cardsById, size, mine = true, zone = die.zone,
  mode = displayModeFor(die, zone), count, spin, turnOffset }: {
  die: Die;
  cardsById: Map<string, CardDef>;
  size: number;
  mine?: boolean;
  zone?: string;
  mode?: DieDisplayMode;
  count?: number;
  spin?: CubeSpin;
  turnOffset?: number;
}) {
  const inPlay = zone === "FieldZone" || zone === "AttackZone" || zone === "Intimidated";
  const spinFlash = useSpinFlash(die);
  return (
    <span className="dk-die-visual" data-display-mode={mode} style={{ width: size, height: size }}>
      {mode === "2d" ? <Die2DTile die={die} cardsById={cardsById} size={size} count={count}
        showEnergyBadge={SIDE_TILE_ZONES.has(zone)} /> : <>
        <DieCube {...facesFor(die, cardsById)} size={size} mine={mine} spin={spin} turnOffset={turnOffset}
          energyCorner={die.energySymbolId && die.energyAmount > 0 ? { type: die.energySymbolId, amount: die.energyAmount } : undefined}
          cues={inPlay ? tileCues(die, cardsById, mine) : undefined} />
        <SpinFlash flash={inPlay ? spinFlash : null} />
        {(die.damage ?? 0) > 0 && (zone === "FieldZone" || zone === "AttackZone") &&
          <span key={die.damage} className="dk-damage-badge" title={`${die.damage} damage marked - clears at the end of the turn`}>−{die.damage}</span>}
      </>}
    </span>
  );
}
