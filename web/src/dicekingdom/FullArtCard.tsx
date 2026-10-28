// The full-art card (2026-10-28): the painting as the hero, in a trading-card
// frame - the art test's "full-art card", made real after the user's "Can we
// make the cards on the collection page 'full size' so that you can actually
// see the art? ... And in the pack opening screen." Used by the Packs page's
// pack reveal, collection grid and the tapped card's close-up. A card with no
// painting yet shows its silhouette on its energy's plate in the same frame,
// and so does one you don't own yet in the collection (2026-10-28: "may just
// use the outline for cards you don't have yet").
import type { CSSProperties, ReactNode } from "react";
import { KeywordText } from "../KeywordPreview";
import { CHARACTER_ICONS, EnergyBadge } from "./icons";
import { Painting, hasPainting } from "./cardArt";
import type { Rarity } from "./player";
import type { CardDef } from "./types";

export function rarityLabel(rarity: Rarity | "Basic"): string {
  return rarity === "SuperRare" ? "Super Rare" : rarity === "Basic" ? "Basic Action" : rarity;
}

// A Basic Action's purchase has no energy type; its die's faces do.
export function energyOf(card: CardDef): string | undefined {
  return card.energyTypes[0] ?? card.dieEnergyType ?? undefined;
}

// The art sits on a plate of its energy's colour (2026-10-26 polish pass).
export function energyPlate(energy: string | undefined): CSSProperties | undefined {
  return energy ? ({ "--energy": `var(--${energy.toLowerCase()})` } as CSSProperties) : undefined;
}

// A name with a long single word (Hippopotamus, Deathstalker) gets smaller
// type on the small cards rather than splitting mid-word.
function longWord(name: string | undefined): boolean {
  return !!name && name.split(/\s+/).some((w) => w.length >= 10);
}

export function FullArtCard({ card, rarity, foil, large, outline, children }: {
  card: CardDef | undefined;
  rarity: Rarity | "Basic";
  foil?: boolean;
  large?: boolean; // the close-up: bigger, with the ability text and levels
  outline?: boolean; // the silhouette even when there's a painting (a card you don't own yet)
  children?: ReactNode; // the footer's right-hand side (dice owned, "New!")
}) {
  const Avatar = card ? CHARACTER_ICONS[card.id] : null;
  const energy = card && energyOf(card);
  const painted = !outline && hasPainting(card?.id);
  return (
    <div className={`dk-fullcard dk-rarity-${rarity}${foil ? " foil" : ""}${large ? " large" : ""}`} style={energyPlate(energy)}>
      <div className="dk-fullcard-head">
        <span className={`dk-fullcard-name${longWord(card?.name) ? " long" : ""}`}>{card?.name ?? "?"}</span>
        {card && (
          <span className="dk-fullcard-cost">
            {card.purchaseCost}
            {energy && <EnergyBadge type={energy} size={large ? 18 : 13} />}
          </span>
        )}
      </div>
      <div className={`dk-fullcard-art${painted ? " painted" : ""}`}>
        {painted ? <Painting cardId={card?.id} alt={card?.name ?? ""} /> : Avatar && <Avatar size={large ? 160 : 64} />}
      </div>
      {large && card && (
        <div className="dk-fullcard-text">
          <p><KeywordText text={card.rawText?.trim() || card.actionText?.trim() || "No ability text."} /></p>
          {card.levels.length > 0 && (
            <div className="dk-fullcard-levels">
              {card.levels.map((l, i) => (
                <span key={i}>
                  L{i + 1} <b>{l.attack}</b>/<b>{l.defense}</b>
                </span>
              ))}
            </div>
          )}
        </div>
      )}
      <div className="dk-fullcard-foot">
        <span className="dk-fullcard-rarity">{rarityLabel(rarity)}</span>
        {children}
      </div>
    </div>
  );
}
