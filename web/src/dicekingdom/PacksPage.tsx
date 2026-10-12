// /dice-kingdom/packs (2026-10-12): coins, random packs, and the dice
// you've pulled. Packs are the server's (V2/Data/DiceKingdomPacks.cs):
// five dice - three Commons, an Uncommon, and an Uncommon/Rare/Super Rare -
// for coins from the starting grant and one visit a day. The Champion
// teams stay free starter rosters; the collection is for custom teams.
import { useEffect, useMemo, useState } from "react";
import { navigate } from "../router";
import { backPath } from "./AccountPage";
import { api } from "./api";
import { CHARACTER_ICONS, EnergyBadge } from "./icons";
import { players, type Collection, type PulledCard, type Rarity } from "./player";
import { ThemeToggle, useTheme } from "./ThemeToggle";
import type { CardDef } from "./types";

const RARITIES: Rarity[] = ["SuperRare", "Rare", "Uncommon", "Common"];

function rarityLabel(rarity: Rarity): string {
  return rarity === "SuperRare" ? "Super Rare" : rarity;
}

function nextDaily(iso: string): string {
  return new Date(iso).toLocaleString([], { weekday: "short", hour: "numeric", minute: "2-digit" });
}

function CardFace({ card, rarity }: { card: CardDef | undefined; rarity: Rarity }) {
  const Avatar = card ? CHARACTER_ICONS[card.id] : null;
  const energy = card?.energyTypes[0];
  return (
    <>
      <span className="dk-pack-rarity">{rarityLabel(rarity)}</span>
      <span className="dk-pack-avatar">{Avatar && <Avatar size={44} />}</span>
      <span className="dk-pack-name">{card?.name ?? "?"}</span>
      {card && (
        <span className="dk-pack-cost">
          Cost {card.purchaseCost} {energy && <EnergyBadge type={energy} size={14} />}
        </span>
      )}
    </>
  );
}

function OpenedPack({ pulled, cards }: { pulled: PulledCard[]; cards: Map<string, CardDef> }) {
  // Turned over one at a time; a tap turns the rest.
  const [shown, setShown] = useState(0);
  useEffect(() => {
    if (shown >= pulled.length) return;
    const timer = window.setTimeout(() => setShown((n) => n + 1), shown === pulled.length - 1 ? 700 : 350);
    return () => window.clearTimeout(timer);
  }, [shown, pulled.length]);

  return (
    <div className="dk-pack-opened" onClick={() => setShown(pulled.length)}>
      {pulled.map((p, i) => {
        const card = cards.get(p.cardId);
        const up = i < shown;
        return (
          <div key={i} className={`dk-pack-card dk-rarity-${p.rarity}${up ? " up" : ""}`} aria-hidden={!up}>
            <div className="dk-pack-card-inner">
              <div className="dk-pack-card-back" />
              <div className="dk-pack-card-front">
                <CardFace card={card} rarity={p.rarity} />
                <span className={`dk-pack-owned${p.new ? " new" : ""}`}>{p.new ? "New!" : `×${p.owned}`}</span>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

function CollectionGrid({ collection, cards }: { collection: Collection; cards: CardDef[] }) {
  const [selected, setSelected] = useState<string | null>(null);
  const characters = cards.filter((c) => c.rarity);
  const owned = characters.filter((c) => (collection.dice[c.id] ?? 0) > 0).length;
  const dice = Object.values(collection.dice).reduce((a, b) => a + b, 0);
  const pick = selected ? characters.find((c) => c.id === selected) : undefined;

  return (
    <div className="panel">
      <h3>Your collection</h3>
      <p className="dk-acct-dim">
        {owned} of {characters.length} characters · {dice} dice · {collection.packsOpened} packs opened. A team plays up
        to 4 dice of a character; extras are kept for trading in later.
      </p>
      {RARITIES.map((rarity) => {
        const tier = characters
          .filter((c) => c.rarity === rarity)
          .sort((a, b) => a.energyTypes[0].localeCompare(b.energyTypes[0]) || a.purchaseCost - b.purchaseCost);
        const have = tier.filter((c) => (collection.dice[c.id] ?? 0) > 0).length;
        return (
          <section key={rarity} className={`dk-coll-tier dk-rarity-${rarity}`}>
            <h4>
              {rarityLabel(rarity)}{" "}
              <small>
                {have}/{tier.length}
              </small>
            </h4>
            <div className="dk-coll-grid">
              {tier.map((card) => {
                const count = collection.dice[card.id] ?? 0;
                const Avatar = CHARACTER_ICONS[card.id];
                return (
                  <button
                    key={card.id}
                    type="button"
                    className={`dk-coll-card dk-rarity-${rarity}${count ? "" : " missing"}${selected === card.id ? " selected" : ""}`}
                    onClick={() => setSelected(selected === card.id ? null : card.id)}
                    aria-label={`${card.name}: ${count} dice`}
                  >
                    <span className="dk-coll-avatar">{Avatar && <Avatar size={32} />}</span>
                    <span className="dk-coll-name">{card.name}</span>
                    <span className="dk-coll-pips" aria-hidden>
                      {Array.from({ length: card.dieLimit }, (_, i) => (
                        <i key={i} className={i < count ? "on" : ""} />
                      ))}
                      {count > card.dieLimit && <small>+{count - card.dieLimit}</small>}
                    </span>
                  </button>
                );
              })}
            </div>
          </section>
        );
      })}
      {pick && (
        <div className="dk-coll-detail">
          <b>{pick.name}</b> · {rarityLabel(pick.rarity!)} · cost {pick.purchaseCost}{" "}
          <EnergyBadge type={pick.energyTypes[0]} size={14} /> · {collection.dice[pick.id] ?? 0} dice
          <div>{pick.rawText}</div>
          <div className="dk-coll-levels">
            {pick.levels.map((l, i) => (
              <span key={i}>
                L{i + 1} {l.attack}/{l.defense}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

export function PacksPage() {
  const [theme, setTheme] = useTheme();
  const [collection, setCollection] = useState<Collection | null>(null);
  const [daily, setDaily] = useState(0);
  const [cards, setCards] = useState<CardDef[]>([]);
  const [pulled, setPulled] = useState<{ n: number; cards: PulledCard[] } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const byId = useMemo(() => new Map(cards.map((c) => [c.id, c])), [cards]);

  useEffect(() => {
    players.collection().then(
      (c) => {
        setCollection(c);
        setDaily(c.dailyCoins);
      },
      (e) => setError(e instanceof Error ? e.message : String(e)),
    );
    api.getCards().then(setCards, (e) => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  async function open() {
    setBusy(true);
    setError(null);
    try {
      const res = await players.openPack();
      setCollection(res.collection);
      setPulled((p) => ({ n: (p?.n ?? 0) + 1, cards: res.cards }));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  const back = backPath();
  const canBuy = collection && collection.coins >= collection.packPrice;

  return (
    <div className="dicekingdom dk-account dk-packs">
      <div className="dk-titlebar-right" style={{ float: "right" }}>
        <ThemeToggle theme={theme} setTheme={setTheme} />
      </div>
      <p className="dk-acct-back">
        <a
          href={back}
          onClick={(e) => {
            e.preventDefault();
            navigate(back);
          }}
        >
          ← Dice Kingdom
        </a>
      </p>
      <h1>Packs</h1>
      {error && <p className="error">{error}</p>}
      {!collection && !error && <p className="dk-acct-dim">Loading…</p>}
      {collection && (
        <>
          <div className="panel dk-pack-shop">
            <div className="dk-pack-wallet">
              <span className="dk-pack-coins">{collection.coins}</span> coins
              {daily > 0 && <b className="dk-daily-coins"> +{daily} for today's visit</b>}
            </div>
            <button type="button" className="btn dk-pack-buy" disabled={!canBuy || busy} onClick={() => void open()}>
              Open a pack · {collection.packPrice} coins
            </button>
            <p className="dk-acct-dim">
              Each pack holds 5 dice: 3 Common, 1 Uncommon, and 1 more that's Uncommon, Rare or (1 in 20) Super Rare.
              Rarer isn't stronger - plenty of the best cards are Common. {collection.dailyAmount} more coins each day
              you visit; next from {nextDaily(collection.nextDailyUtc)}.
            </p>
            {pulled && <OpenedPack key={pulled.n} pulled={pulled.cards} cards={byId} />}
          </div>
          <CollectionGrid collection={collection} cards={cards} />
        </>
      )}
    </div>
  );
}
