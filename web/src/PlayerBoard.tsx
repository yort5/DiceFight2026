import { DieCube, type CubeSpin } from "./DieCube";
import { DieIcon } from "./DieIcon";
import { facesFor } from "./dieFaces";
import type { CardDef, Die } from "./types";
import { groupDice } from "./dieHelpers";

export interface Selection {
  primary: string | null;
  secondary: string[];
}

// Spatial layout follows the physical playmat's cross shape (per user
// feedback on the first pass): Attack Zone spans the top, Field Zone
// spans below it, then Used Pile/Reserve Pool/Prep Area sit side by side
// ("roll dice here" is the middle one) - Used Pile and Prep Area flank
// the Reserve Pool, not the Field Zone. Out of Play sits directly under
// Used Pile (familiar pairing), and the Bag spans the bottom. DiceFromBag/
// DiceFromPrep (this engine's transient pre-Roll staging zones - see the
// Zone enum remarks - not real physical zones) are paired with each
// other in their own row under the Bag, rather than nested under Bag/Prep
// Area individually. The Unpurchased roster stays off the mat grid, as a
// low-traffic reference zone underneath it.
export function PlayerBoard(props: {
  title: string;
  isActive: boolean;
  /** The local player's board - tints their dice apart from the
   *  opponent's, which is the only difference between the two. */
  mine: boolean;
  /** Roll animation state, by die id - see useDiceRoll.ts. */
  spins?: Record<string, CubeSpin>;
  turnOffsets?: Record<string, number>;
  /** True while a roll is in flight; shakes the Reserve Pool. */
  rolling?: boolean;
  life: number;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
  // While set, only these die ids are clickable in the Reserve Pool -
  // used during a Global ability's energy stage so a player can't select
  // a die that isn't legal payment (see App.tsx). Null/undefined means no
  // restriction, the normal case everywhere else.
  selectableEnergyIds?: Set<string> | null;
}) {
  const { dice, cardsById, selection, onGroupClick, selectableEnergyIds } = props;
  const zoneProps = {
    cardsById, selection, onGroupClick, selectableEnergyIds,
    mine: props.mine, spins: props.spins, turnOffsets: props.turnOffsets,
  };
  const dicein = (zone: string) => dice.filter((d) => d.zone === zone);

  return (
    <div className={`board${props.isActive ? " active" : ""}`}>
      <div className="board-header">
        <h2>{props.title}</h2>
        <div className="life">{props.life} life</div>
      </div>

      <div className="mat">
        <div className="mat-slot mat-attack">
          <ZoneSection zone="AttackZone" prominent dice={dicein("AttackZone")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-field">
          <ZoneSection zone="FieldZone" prominent dice={dicein("FieldZone")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-intimidated">
          <ZoneSection zone="Intimidated" dice={dicein("Intimidated")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-used">
          <ZoneSection zone="UsedPile" dice={dicein("UsedPile")} {...zoneProps} />
        </div>
        {/* The Reserve Pool is the dice tray, so it is the one zone that
            reacts to a roll - it shakes as the dice launch. */}
        <div className={`mat-slot mat-reserve${props.rolling ? " rolling" : ""}`}>
          <ZoneSection zone="ReservePool" prominent dice={dicein("ReservePool")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-prep">
          <ZoneSection zone="PrepArea" dice={dicein("PrepArea")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-outofplay">
          <ZoneSection zone="OutOfPlay" dice={dicein("OutOfPlay")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-bag">
          <ZoneSection zone="Bag" dice={dicein("Bag")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-drawn">
          <ZoneSection zone="DiceFromBag" dice={dicein("DiceFromBag")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-carried">
          <ZoneSection zone="DiceFromPrep" dice={dicein("DiceFromPrep")} {...zoneProps} />
        </div>
      </div>

      <details className="roster">
        <summary>Unpurchased roster ({dicein("Unpurchased").length})</summary>
        <ZoneSection zone="Unpurchased" bare dice={dicein("Unpurchased")} {...zoneProps} />
      </details>
    </div>
  );
}

const ZONE_DISPLAY_NAMES: Record<string, string> = {
  AttackZone: "Attack Zone",
  FieldZone: "Field Zone",
  ReservePool: "Reserve Pool",
  Bag: "Bag",
  PrepArea: "Prep Area",
  DiceFromBag: "Drawn This Turn",
  DiceFromPrep: "Carried From Prep",
  UsedPile: "Used Pile",
  OutOfPlay: "Out of Play",
  Unpurchased: "Unpurchased",
  Intimidated: "Intimidated",
};

// Loosely matches the physical mat's color-coded zones, so the shape of
// this layout reads the same way the printed mat does.
const ZONE_TINTS: Record<string, string> = {
  AttackZone: "attack",
  FieldZone: "field",
  ReservePool: "reserve",
  UsedPile: "used",
  OutOfPlay: "used",
  PrepArea: "prep",
  Bag: "bag",
  DiceFromBag: "staging",
  DiceFromPrep: "staging",
  Intimidated: "prep",
};

// The only zones where a die is actually showing a rolled face (rule 1.5)
// - everywhere else (Bag, Used Pile, Prep Area, Out of Play, Unpurchased,
// the transient staging zones) a die is unrolled, spent, or just sitting
// on its card, so the enlarged face-badge treatment doesn't apply there
// and stacks of identical dice can still collapse to a "×N" chip.
const ROLLED_ZONES = new Set(["ReservePool", "FieldZone", "AttackZone"]);

function ZoneSection(props: {
  zone: string;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
  selectableEnergyIds?: Set<string> | null;
  mine: boolean;
  spins?: Record<string, CubeSpin>;
  turnOffsets?: Record<string, number>;
  prominent?: boolean;
  bare?: boolean;
}) {
  const rolled = ROLLED_ZONES.has(props.zone);
  const groups = groupDice(props.dice, props.cardsById, !rolled);
  // Only the Reserve Pool is ever a source of energy to pay a cost with,
  // so that's the only zone where selectableEnergyIds narrows anything -
  // every other zone ignores it and behaves as it always has.
  const restrictToSelectable = props.zone === "ReservePool" && props.selectableEnergyIds != null;
  const content = (
    <div className="dice">
      {groups.length === 0 && props.prominent && <span className="empty-hint">empty</span>}
      {groups.map((group) => {
        const selectedCount = group.ids.filter(
          (id) => id === props.selection.primary || props.selection.secondary.includes(id),
        ).length;
        const isPrimary = group.ids.includes(props.selection.primary ?? "");
        const countSuffix = group.count > 1 ? ` ×${group.count}` : "";
        const showCube = rolled && (group.characterFace != null || group.iconKind != null);
        const isSelectable = !restrictToSelectable || group.ids.every((id) => props.selectableEnergyIds!.has(id));
        return (
          <button
            key={group.key}
            className={`die-chip${selectedCount > 0 ? " selected" : ""}${isPrimary ? " primary" : ""}${showCube ? " has-face" : ""}${isSelectable ? "" : " ineligible"}`}
            onClick={() => props.onGroupClick(group.ids)}
            disabled={!isSelectable}
            title={isSelectable ? group.tooltip : "Not eligible for what you're currently doing"}
            data-die-ids={group.ids.join(",")}
          >
            {showCube ? (
              <>
                {/* One cube per die in the zones where a face is really
                    up, so what the die is showing is the object itself
                    rather than a badge beside a label. */}
                <DieCube
                  {...facesFor(group.die, props.cardsById)}
                  size={40}
                  mine={props.mine}
                  damage={group.damage}
                  spin={props.spins?.[group.die.id]}
                  turnOffset={props.turnOffsets?.[group.die.id]}
                />
                <span className="chip-label">
                  {group.label}
                  {countSuffix}
                </span>
                {/* The cube is aria-hidden - a cube keeps all six faces
                    in the DOM - so the face it is showing has to be said
                    in text here, as it was when this was a flat badge. */}
                {group.statusText && <span className="chip-status">{group.statusText}</span>}
              </>
            ) : (
              <>
                <span className="chip-label">
                  <DieIcon kind={group.iconKind} />
                  {group.label}
                  {countSuffix}
                </span>
                {group.statusText && <span className="chip-status">{group.statusText}</span>}
              </>
            )}
            {selectedCount > 0 && <span className="chip-selected-badge">{selectedCount}</span>}
          </button>
        );
      })}
    </div>
  );

  if (props.bare) return content;

  return (
    <div className={`zone${props.prominent ? " prominent" : ""} zone-${ZONE_TINTS[props.zone] ?? "plain"}`}>
      <h3>
        {ZONE_DISPLAY_NAMES[props.zone] ?? props.zone} ({props.dice.length})
      </h3>
      {content}
    </div>
  );
}
