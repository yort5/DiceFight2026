// /dice-kingdom/art-test (2026-10-26): candidate art styles side by side,
// in the real pack card, collection tile and die tile, so the style is
// picked by eye in context. Images: public/dice-kingdom/art-test (made by
// tools/art/stylize.py from public-domain / CC0 sources, CREDITS.txt there).
// Not linked from anywhere - a page to look at, then delete once a style
// is chosen.
//
// Round 2 (2026-10-27), after the user's "the stylized photos could work if
// we didn't crop and kept the whole picture, but ... a bit more colorful.
// The vintage painting could also work if we either didn't crop or did
// something more interesting with the background": whole-picture art from
// tools/art/scene.py - a -full image for the full-art card's art window and
// a tighter -icon crop that fills the small round plates.
import { useEffect, useMemo, useState } from "react";
import { api } from "./api";
import { Die2DTile } from "./Die2DTile";
import { CHARACTER_ICONS, WolfIcon, type IconComponent } from "./icons";
import { CardFace, energyOf, energyPlate } from "./PacksPage";
import { ThemeToggle, useTheme } from "./ThemeToggle";
import type { CardDef, Die } from "./types";

const ANIMALS = [
  { id: "AI013", slug: "koala", rarity: "Rare" },
  { id: "DK-WING-03", slug: "hummingbird", rarity: "Common" },
  { id: "BFF033", slug: "snow-leopard", rarity: "SuperRare" },
] as const;

const STYLES = [
  { key: "silhouette", name: "Silhouette (today)", note: "The current hand-made SVG shapes." },
  {
    key: "photo",
    name: "Photo cutout",
    note: "A CC0 photo, background removed, with a sticker outline. Needs a clean photo per animal; mixed lighting shows.",
  },
  {
    key: "flat",
    name: "Auto-stylized from the photo",
    note: "The same cutout flattened to a few colours by a script. Cheap for all 125 cards, but it loses the face at small sizes.",
  },
  {
    key: "plate",
    name: "Vintage naturalist painting",
    note: "Public-domain 1800s plates (Lewin, Haeckel, Meyers), cut out. Lovely, but old plates vary by artist and don't exist for every animal.",
  },
] as const;

const ROUND2 = [
  {
    key: "painted",
    name: "Painted photo, whole picture",
    note: "The photo kept whole and run through a painting filter, with extra colour and a warm/cool grade. The small plates use a tighter crop on the face.",
  },
  {
    key: "scene",
    name: "Vintage painting, whole picture",
    note: "The painting as painted, background and all (Lewin's koala, Haeckel's hummingbirds). The snow leopard engraving is black and white, so its paper is washed with colour by script.",
  },
  {
    key: "rays",
    name: "Vintage painting on a sunburst",
    note: "The animal cut out of the painting and set on rays in its energy's colour (Koala Shell, Hummingbird Wing, Snow Leopard Claw).",
  },
  {
    key: "wash",
    name: "Vintage painting on a watercolour wash",
    note: "The same cut-outs on soft blobs of the energy's colour with warm accents.",
  },
] as const;

function imageIcon(src: string, alt: string): IconComponent {
  return function ImageIcon({ size = 24 }) {
    return <img src={src} alt={alt} width={size} height={size} style={{ objectFit: "contain" }} />;
  };
}

// Fills its round plate or die disc edge to edge, whatever size it's asked for.
function fillIcon(src: string, alt: string): IconComponent {
  return function FillIcon() {
    return <img className="dk-art-fill" src={src} alt={alt} />;
  };
}

function fakeDie(cardId: string): Die {
  return { id: `art-${cardId}`, cardId, ownerId: "", controllerId: "", zone: "ReservePool" } as Die;
}

function Frames({ card, rarity, avatar, full, cardsById }: {
  card: CardDef;
  rarity: "Common" | "Uncommon" | "Rare" | "SuperRare";
  avatar: IconComponent;
  full?: string; // a whole picture for the full-art card, instead of the avatar
  cardsById: Map<string, CardDef>;
}) {
  const Avatar = avatar;
  return (
    <div className="dk-art-frames">
      <div className={`dk-pack-card dk-rarity-${rarity} up`}>
        <div className="dk-pack-card-inner">
          <div className="dk-pack-card-back" />
          <div className="dk-pack-card-front">
            <CardFace card={card} rarity={rarity} avatar={avatar} />
            <span className="dk-pack-owned">×1</span>
          </div>
        </div>
      </div>
      <div className={`dk-art-full dk-rarity-${rarity}`} style={energyPlate(energyOf(card))}>
        <span className={`dk-art-full-art${full ? " scene" : ""}`}>
          {full ? <img src={full} alt={card.name} /> : <Avatar size={150} />}
        </span>
        <span className="dk-art-full-name">{card.name}</span>
        <span className="dk-art-full-cost">Cost {card.purchaseCost}</span>
      </div>
      <div className="dk-art-side">
        <div className={`dk-coll-card dk-rarity-${rarity}`}>
          <span className="dk-coll-avatar" style={energyPlate(energyOf(card))}>
            <Avatar size={32} />
          </span>
          <span className="dk-coll-name">{card.name}</span>
        </div>
        <div className="dk-art-dice">
          <Die2DTile die={fakeDie(card.id)} cardsById={cardsById} size={56} avatar={avatar} />
          <Die2DTile die={fakeDie(card.id)} cardsById={cardsById} size={36} avatar={avatar} />
        </div>
      </div>
    </div>
  );
}

export function ArtTestPage() {
  const [theme, setTheme] = useTheme();
  const [cards, setCards] = useState<CardDef[]>([]);
  const byId = useMemo(() => new Map(cards.map((c) => [c.id, c])), [cards]);
  useEffect(() => {
    void api.getCards().then(setCards);
  }, []);

  const swallow = byId.get("DK-WING-02");
  return (
    <div className="dicekingdom dk-account dk-packs dk-art-test">
      <div style={{ float: "right" }}>
        <ThemeToggle theme={theme} setTheme={setTheme} />
      </div>
      <h1>Art test</h1>
      <p className="dk-acct-dim">
        Three animals in each candidate style, shown in the real pack card, a full-art card (what a card could look like
        if the art were the hero), the collection tile and the die tile (large and small).
        Rare and Super Rare foil is on. Missing here: AI illustrations in one fixed style - drop a few in and they'd go
        alongside.
      </p>
      {cards.length === 0 && <p className="dk-acct-dim">Loading…</p>}
      {cards.length > 0 && (
        <>
          <h2>Round 2: whole pictures</h2>
          <p className="dk-acct-dim">
            Keeping the background this time, and more colour. The full-art card shows the whole picture; the small
            plates and dice show a tighter crop of the same art.
          </p>
          {ROUND2.map((style) => (
            <section key={style.key} className="panel">
              <h3>{style.name}</h3>
              <p className="dk-acct-dim">{style.note}</p>
              <div className="dk-art-row">
                {ANIMALS.map((a) => {
                  const card = byId.get(a.id);
                  if (!card) return null;
                  const base = `/dice-kingdom/art-test/${a.slug}-${style.key}`;
                  return (
                    <Frames key={a.id} card={card} rarity={a.rarity} cardsById={byId}
                      avatar={fillIcon(`${base}-icon.webp`, card.name)} full={`${base}-full.webp`} />
                  );
                })}
              </div>
            </section>
          ))}
          <h2>Round 1: cut-outs</h2>
        </>
      )}
      {cards.length > 0 &&
        STYLES.map((style) => (
          <section key={style.key} className="panel">
            <h3>{style.name}</h3>
            <p className="dk-acct-dim">{style.note}</p>
            <div className="dk-art-row">
              {ANIMALS.map((a) => {
                const card = byId.get(a.id);
                if (!card) return null;
                const avatar =
                  style.key === "silhouette"
                    ? CHARACTER_ICONS[a.id]
                    : imageIcon(`/dice-kingdom/art-test/${a.slug}-${style.key}.png`, card.name);
                return <Frames key={a.id} card={card} rarity={a.rarity} avatar={avatar} cardsById={byId} />;
              })}
            </div>
          </section>
        ))}
      {swallow && (
        <section className="panel">
          <h3>Family drawings</h3>
          <p className="dk-acct-dim">
            Already in the game: the duck drawing stands in for the Barn Swallow, and the Wolf Champion portrait.
          </p>
          <div className="dk-art-row">
            <Frames card={swallow} rarity="Uncommon" avatar={CHARACTER_ICONS["DK-WING-02"]} cardsById={byId} />
            <div className="dk-art-champion">
              <WolfIcon size={120} />
              <span>Wolf (Champion)</span>
            </div>
          </div>
        </section>
      )}
    </div>
  );
}
