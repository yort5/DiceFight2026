// /dice-kingdom/packs (2026-10-12): coins, random packs, and the dice
// you've pulled. Packs are the server's (V2/Data/DiceKingdomPacks.cs):
// six dice - three Commons, an Uncommon, an Uncommon/Rare/Super Rare, and a
// rarity-less Basic Action (2026-10-20) - for coins from the starting grant and one visit a day. The Champion
// teams stay free starter rosters, and each joins the collection once a
// game with it is finished. Spare dice trade up a rarity (TradeIns).
import { useEffect, useMemo, useState } from "react";
import { navigate } from "../router";
import { backPath } from "./AccountPage";
import { championLabel } from "./ChampionPicker";
import { api } from "./api";
import { CHARACTER_ICONS, EnergyBadge } from "./icons";
import { players, type Collection, type PulledCard, type Rarity } from "./player";
import { ThemeToggle, useTheme } from "./ThemeToggle";
import type { CardDef } from "./types";

const RARITIES: Rarity[] = ["SuperRare", "Rare", "Uncommon", "Common"];

function rarityLabel(rarity: Rarity | "Basic"): string {
  return rarity === "SuperRare" ? "Super Rare" : rarity === "Basic" ? "Basic Action" : rarity;
}

// A Basic Action's purchase has no energy type; its die's faces do.
function energyOf(card: CardDef): string | undefined {
  return card.energyTypes[0] ?? card.dieEnergyType ?? undefined;
}

function nextDaily(iso: string): string {
  return new Date(iso).toLocaleString([], { weekday: "short", hour: "numeric", minute: "2-digit" });
}

function CardFace({ card, rarity }: { card: CardDef | undefined; rarity: Rarity | "Basic" }) {
  const Avatar = card ? CHARACTER_ICONS[card.id] : null;
  const energy = card && energyOf(card);
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

const NEXT: Partial<Record<Rarity, Rarity>> = { Common: "Uncommon", Uncommon: "Rare", Rare: "SuperRare" };

/** The spare dice (past a card's die limit) a trade of `rarity` would give, most-duplicated first. */
function tradePlan(rarity: Rarity, cost: number, collection: Collection, cards: CardDef[]): string[] | null {
  const spares = cards
    .filter((c) => c.rarity === rarity)
    .map((c) => ({ id: c.id, spare: Math.max(0, (collection.dice[c.id] ?? 0) - c.dieLimit) }))
    .filter((s) => s.spare > 0);
  const give: string[] = [];
  while (give.length < cost) {
    spares.sort((a, b) => b.spare - a.spare);
    if (!spares.length || spares[0].spare === 0) return null;
    give.push(spares[0].id);
    spares[0].spare--;
  }
  return give;
}

function spareCount(rarity: Rarity, collection: Collection, cards: CardDef[]): number {
  return cards
    .filter((c) => c.rarity === rarity)
    .reduce((n, c) => n + Math.max(0, (collection.dice[c.id] ?? 0) - c.dieLimit), 0);
}

function TradeIns({
  collection,
  cards,
  byId,
  onTraded,
}: {
  collection: Collection;
  cards: CardDef[];
  byId: Map<string, CardDef>;
  onTraded: (c: Collection) => void;
}) {
  const [got, setGot] = useState<{ n: number; card: PulledCard } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function trade(give: string[]) {
    setBusy(true);
    setError(null);
    try {
      const res = await players.tradeIn(give);
      onTraded(res.collection);
      setGot((g) => ({ n: (g?.n ?? 0) + 1, card: res.card }));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="panel">
      <h3>Trade in</h3>
      <p className="dk-acct-dim">
        Trade spare dice - past the most a team can play of that card (usually 4) - up a rarity. You get a random card of
        the next rarity, picked from the ones you don't have a full set of yet.
      </p>
      {(["Common", "Uncommon", "Rare"] as Rarity[]).map((rarity) => {
        const cost = collection.tradeInCost[rarity];
        const next = NEXT[rarity];
        if (!cost || !next) return null;
        const spare = spareCount(rarity, collection, cards);
        const plan = tradePlan(rarity, cost, collection, cards);
        const names = plan
          ? [...new Set(plan)].map((id) => `${byId.get(id)?.name ?? id}${plan.filter((g) => g === id).length > 1 ? ` ×${plan.filter((g) => g === id).length}` : ""}`).join(", ")
          : null;
        return (
          <div key={rarity} className={`dk-trade-row dk-rarity-${rarity}`}>
            <span className="dk-acct-game-label">
              <b>
                {cost} {rarityLabel(rarity)} → 1 {rarityLabel(next)}
              </b>
              <small>
                {" "}
                · {spare} spare{names && <> · gives {names}</>}
              </small>
            </span>
            <button type="button" className="btn" disabled={!plan || busy} onClick={() => plan && void trade(plan)}>
              Trade
            </button>
          </div>
        );
      })}
      {error && <p className="error">{error}</p>}
      {got && (
        <div className="dk-trade-result">
          <OpenedPack key={got.n} pulled={[got.card]} cards={byId} />
        </div>
      )}
    </div>
  );
}

function CollectionGrid({ collection, cards }: { collection: Collection; cards: CardDef[] }) {
  const [selected, setSelected] = useState<string | null>(null);
  // Every collectible card is in a set (DiceKingdomSets); each set gets its rarity tiers.
  const collectible = cards.filter((c) => c.kingdomSet);
  const sets = [...new Set(collectible.map((c) => c.kingdomSet!))].sort();
  const dice = Object.values(collection.dice).reduce((a, b) => a + b, 0);
  const pick = selected ? collectible.find((c) => c.id === selected) : undefined;

  return (
    <div className="panel">
      <h3>Your collection</h3>
      <p className="dk-acct-dim">
        {dice} dice · {collection.packsOpened} {collection.packsOpened === 1 ? "pack" : "packs"} opened
        {collection.tradeIns > 0 && (
          <>
            {" "}
            · {collection.tradeIns} {collection.tradeIns === 1 ? "trade-in" : "trade-ins"}
          </>
        )}
        . A team plays up to a card's die limit (usually 4); extras can be traded in.
      </p>
      <p className="dk-acct-dim">
        {collection.starters.length
          ? `Starter rosters added: ${collection.starters.map(championLabel).join(", ")}. `
          : ""}
        Finish a game with a Champion to add its starter roster.
      </p>
      {sets.map((set) => {
        const inSet = collectible.filter((c) => c.kingdomSet === set);
        const characters = inSet.filter((c) => c.type === "Character");
        const ownedCharacters = characters.filter((c) => (collection.dice[c.id] ?? 0) > 0).length;
        const owned = inSet.filter((c) => (collection.dice[c.id] ?? 0) > 0).length;
        return (
          <section key={set} className="dk-coll-set">
            <h4 className="dk-coll-set-name">
              {set}{" "}
              <small>
                {ownedCharacters}/{characters.length} characters · {owned}/{inSet.length} cards
              </small>
            </h4>
            {[...RARITIES, "Basic" as const].map((rarity) => {
              const tier = (
                rarity === "Basic" ? inSet.filter((c) => !c.rarity) : inSet.filter((c) => c.rarity === rarity)
              ).sort((a, b) => (energyOf(a) ?? "").localeCompare(energyOf(b) ?? "") || a.purchaseCost - b.purchaseCost);
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
          </section>
        );
      })}
      {pick && (
        <div className="dk-coll-detail">
          <b>{pick.name}</b> · {pick.kingdomSet} {rarityLabel(pick.rarity ?? "Basic")} · cost {pick.purchaseCost}{" "}
          {energyOf(pick) && <EnergyBadge type={energyOf(pick)!} size={14} />} · {collection.dice[pick.id] ?? 0} dice
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
              Open a {collection.packSet} pack · {collection.packPrice} coins
            </button>
            <p className="dk-acct-dim">
              Each {collection.packSet} pack holds 6 dice: 3 Common, 1 Uncommon, 1 more that's Uncommon, Rare or (1 in
              20) Super Rare, and a Basic Action. Rarer isn't stronger - plenty of the best cards are Common.{" "}
              {collection.dailyAmount} more coins each day you visit; next from {nextDaily(collection.nextDailyUtc)}.
            </p>
            {pulled && <OpenedPack key={pulled.n} pulled={pulled.cards} cards={byId} />}
          </div>
          <TradeIns collection={collection} cards={cards} byId={byId} onTraded={setCollection} />
          <CollectionGrid collection={collection} cards={cards} />
        </>
      )}
    </div>
  );
}
