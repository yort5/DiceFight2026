import { DieIcon } from "./DieIcon";
import type { CardDef, Die } from "./types";
import { groupDice } from "./dieHelpers";

export interface Selection {
  primary: string | null;
  secondary: string[];
}

// Spatial layout follows the physical playmat's cross shape (see the mat
// reference image): Attack Zone spans the top, Used Pile/Field Zone/Prep
// Area sit side by side below it, Reserve Pool spans the middle ("roll
// dice here"), and the Bag sits at the bottom - matching where dice
// physically move to/from on the real mat rather than a flat stacked
// list. DiceFromBag/DiceFromPrep (this engine's transient pre-Roll
// staging zones - see the Zone enum remarks) aren't on the physical mat
// at all, so they're shown as a nested sub-zone right next to the
// physical zone they're about to join (Bag / Prep Area respectively).
// Out of Play and the Unpurchased roster stay off the mat grid, as
// low-traffic reference zones underneath it.
export function PlayerBoard(props: {
  title: string;
  isActive: boolean;
  life: number;
  virtualGenericEnergy: number;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
}) {
  const { dice, cardsById, selection, onGroupClick } = props;
  const zoneProps = { cardsById, selection, onGroupClick };
  const dicein = (zone: string) => dice.filter((d) => d.zone === zone);

  return (
    <div className={`board${props.isActive ? " active" : ""}`}>
      <div className="board-header">
        <h2>{props.title}</h2>
        <div className="life">{props.life} life</div>
        {props.virtualGenericEnergy > 0 && (
          <div className="virtual-energy">+{props.virtualGenericEnergy} virtual</div>
        )}
      </div>

      <div className="mat">
        <div className="mat-slot mat-attack">
          <ZoneSection zone="AttackZone" prominent dice={dicein("AttackZone")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-used">
          <ZoneSection zone="UsedPile" dice={dicein("UsedPile")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-field">
          <ZoneSection zone="FieldZone" prominent dice={dicein("FieldZone")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-prep">
          <ZoneSection zone="PrepArea" dice={dicein("PrepArea")} {...zoneProps} />
          <ZoneSection zone="DiceFromPrep" dice={dicein("DiceFromPrep")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-reserve">
          <ZoneSection zone="ReservePool" prominent dice={dicein("ReservePool")} {...zoneProps} />
        </div>
        <div className="mat-slot mat-bag">
          <ZoneSection zone="Bag" dice={dicein("Bag")} {...zoneProps} />
          <ZoneSection zone="DiceFromBag" dice={dicein("DiceFromBag")} {...zoneProps} />
        </div>
      </div>

      <div className="side-zones">
        <ZoneSection zone="OutOfPlay" dice={dicein("OutOfPlay")} {...zoneProps} />
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
};

// Loosely matches the physical mat's color-coded zones, so the shape of
// this layout reads the same way the printed mat does.
const ZONE_TINTS: Record<string, string> = {
  AttackZone: "attack",
  FieldZone: "field",
  ReservePool: "reserve",
  UsedPile: "used",
  PrepArea: "prep",
  DiceFromPrep: "prep",
  Bag: "bag",
  DiceFromBag: "bag",
};

function ZoneSection(props: {
  zone: string;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
  prominent?: boolean;
  bare?: boolean;
}) {
  const groups = groupDice(props.dice, props.cardsById);
  const content = (
    <div className="dice">
      {groups.length === 0 && props.prominent && <span className="empty-hint">empty</span>}
      {groups.map((group) => {
        const selectedCount = group.ids.filter(
          (id) => id === props.selection.primary || props.selection.secondary.includes(id),
        ).length;
        const isPrimary = group.ids.includes(props.selection.primary ?? "");
        return (
          <button
            key={group.key}
            className={`die-chip${selectedCount > 0 ? " selected" : ""}${isPrimary ? " primary" : ""}`}
            onClick={() => props.onGroupClick(group.ids)}
            title={group.tooltip}
          >
            <span className="chip-label">
              <DieIcon kind={group.iconKind} />
              {group.label}
              {group.count > 1 ? ` ×${group.count}` : ""}
            </span>
            {group.statusText && <span className="chip-status">{group.statusText}</span>}
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
