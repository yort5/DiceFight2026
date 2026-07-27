import type { CardDef, Die } from "./types";
import { groupDice } from "./dieHelpers";

export interface Selection {
  primary: string | null;
  secondary: string[];
}

// Spatial layout loosely following the physical playmat (rule 1.5):
// Attack Zone and Field Zone are the "active" area shown prominently,
// Reserve Pool below them, then the quieter side zones, with the
// Unpurchased roster (the biggest, least-active zone - up to 4 dice per
// card) collapsed by default.
const PROMINENT_ZONES = ["AttackZone", "FieldZone", "ReservePool"] as const;
const SIDE_ZONES = ["Bag", "PrepArea", "DiceFromBag", "DiceFromPrep", "UsedPile", "OutOfPlay"] as const;

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

  return (
    <div className={`board${props.isActive ? " active" : ""}`}>
      <div className="board-header">
        <h2>{props.title}</h2>
        <div className="life">{props.life} life</div>
        {props.virtualGenericEnergy > 0 && (
          <div className="virtual-energy">+{props.virtualGenericEnergy} virtual</div>
        )}
      </div>

      {PROMINENT_ZONES.map((zone) => (
        <ZoneSection
          key={zone}
          zone={zone}
          prominent
          dice={dice.filter((d) => d.zone === zone)}
          cardsById={cardsById}
          selection={selection}
          onGroupClick={onGroupClick}
        />
      ))}

      <div className="side-zones">
        {SIDE_ZONES.map((zone) => (
          <ZoneSection
            key={zone}
            zone={zone}
            dice={dice.filter((d) => d.zone === zone)}
            cardsById={cardsById}
            selection={selection}
            onGroupClick={onGroupClick}
          />
        ))}
      </div>

      <details className="roster">
        <summary>Unpurchased roster ({dice.filter((d) => d.zone === "Unpurchased").length})</summary>
        <ZoneSection
          zone="Unpurchased"
          bare
          dice={dice.filter((d) => d.zone === "Unpurchased")}
          cardsById={cardsById}
          selection={selection}
          onGroupClick={onGroupClick}
        />
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
          >
            <span className="chip-label">
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
    <div className={`zone${props.prominent ? " prominent" : ""}`}>
      <h3>
        {ZONE_DISPLAY_NAMES[props.zone] ?? props.zone} ({props.dice.length})
      </h3>
      {content}
    </div>
  );
}
