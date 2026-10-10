import { CHARACTER_ICONS, EnergyBadge, TardigradePhotoIcon } from "./icons";
import { CueRows } from "./CueRows";
import { DieCube } from "./DieCube";
import { printedFacesFor, printedFacesForCard, type CubeFace } from "./dieFaces";
import type { ExplainRow } from "./statusCues";
import type { CardDef, Die } from "./types";

interface PurchaseAction {
  label: string;
  disabled: boolean;
  onClick: () => void;
}

interface Props {
  card?: CardDef;
  die?: Die;
  purchaseCost?: number;
  purchase?: PurchaseAction;
  cueRows?: ExplainRow[];
  placement?: "up" | "down";
  fallbackName?: string;
}

// One desktop card inspector for the roster and every die zone. Its caller
// decides whether purchasing is an available action, not which details exist.
export function CardDetailPopover({ card, die, purchaseCost, purchase, cueRows = [], placement = "down", fallbackName }: Props) {
  const Avatar = card ? CHARACTER_ICONS[card.id] : null;
  const name = card?.name ?? fallbackName ?? die?.cardId ?? "Tardigrade";
  const energyType = card?.energyTypes[0];
  const faces = card
    ? printedFacesForCard(card)
    : die
      ? orderTardigradeFaces(printedFacesFor(die, new Map()))
      : [];

  return (
    <div className={`card-popover ${placement} roster-detail`}>
      <div className="card-popover-head roster-detail-head">
        {Avatar ? <Avatar size={28} /> : !card && die && <TardigradePhotoIcon size={28} />}
        <div className="roster-detail-title">
          <div className="card-popover-name">{name}</div>
          {card && (
            <div className="card-popover-cost">
              Cost {purchaseCost ?? card.purchaseCost}
              {energyType && <EnergyBadge type={energyType} size={14} />}
            </div>
          )}
        </div>
        {purchase && (
          <button
            type="button"
            className="btn roster-detail-purchase"
            disabled={purchase.disabled}
            onClick={purchase.onClick}
          >
            {purchase.label}
          </button>
        )}
      </div>
      <CueRows rows={cueRows} />
      <p className="card-popover-text roster-detail-ability">
        {card
          ? card.rawText?.trim() || card.actionText?.trim() || "No ability text."
          : die && (!die.cardId || die.isTardigrade)
            ? "Two level-2 faces also provide Wild energy, one level-3 face has Bulwark, and three energy faces provide 2, 2 and 1 energy."
            : "No ability text."}
      </p>
      <div className="roster-detail-faces" aria-label="All six printed die faces">
        {faces.map((face, index, allFaces) => (
          <div
            className={`roster-detail-face${die?.level != null && face.kind === "character" && face.level === die.level ? " current" : ""}`}
            key={index}
            title={`Face ${index + 1}`}
          >
            <DieCube
              faces={[face, ...allFaces.filter((_, otherIndex) => otherIndex !== index)]}
              index={0}
              size={48}
              mine
              energyCorner={face.kind === "energy" ? { type: face.icon, amount: face.amount } : undefined}
            />
          </div>
        ))}
      </div>
    </div>
  );
}

function orderTardigradeFaces(faces: CubeFace[]): CubeFace[] {
  return [
    ...faces.filter((face) => face.kind !== "character").reverse(),
    ...faces.filter((face) => face.kind === "character"),
  ];
}
