// /dice-kingdom/bosses (2026-10-31): the boss ladder. Each boss is a team
// the user built in the Team Builder, played by the computer in its own
// style (server: V2/Data/DiceKingdomBosses.cs). The first is open to
// everyone; beating one opens the next. A first win pays points, coins and
// sometimes dice; a rematch pays less. The fight itself is an ordinary
// vs-computer game on the usual game screen.
// 2026-11-11: redrawn in the launch page's style (PageShell.tsx) - each boss
// a card under its own crowned painting, its team as faces, its prizes as chips.
import { useEffect, useMemo, useState, type CSSProperties } from "react";
import { navigate } from "../router";
import { hasPainting, paintingSrc } from "./cardArt";
import { ChampionPicker, championLabel } from "./ChampionPicker";
import { CustomTeamSetupChoice } from "./CustomTeamSetupChoice";
import { CUSTOM_TEAM_PICK, pendingCustomTeam } from "./customTeams";
import { api } from "./api";
import { energyOf, energyPlate } from "./FullArtCard";
import { CHAMPION_ICONS, CHARACTER_ICONS, TardigradeIcon } from "./icons";
import { BOSS_FACES, BuilderIcon, CheckIcon, CoinIcon, Crown, Line, LockIcon, tone } from "./kingdomArt";
import { KingdomPage, backPath } from "./PageShell";
import { players, type Boss, type BossLadder, type BossReward, type BossResult } from "./player";
import { describeSavedGame, rememberSeats } from "./seats";
import type { CardDef } from "./types";

const CHAMPIONS = [
  { id: "Wolf", energy: "Claw" },
  { id: "Armadillo", energy: "Shell" },
  { id: "GoldenEagle", energy: "Wing" },
  { id: "GreatHornedOwl", energy: "Eye" },
];

const RARITY_NAMES: Record<string, string> = { Common: "Common", Uncommon: "Uncommon", Rare: "Rare", SuperRare: "Super Rare" };

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;
const article = (word: string) => (/^[AEIOU]/i.test(word) ? `an ${word}` : `a ${word}`);

function percent(chance: number): string {
  return chance >= 1 ? "" : `${Math.round(chance * 1000) / 10}% chance of `;
}

/** "10 points · 15 coins · a Rare die" */
export function rewardText(reward: BossReward, cards: Map<string, CardDef>): string {
  const parts: string[] = [];
  if (reward.points) parts.push(plural(reward.points, "point"));
  if (reward.coins) parts.push(plural(reward.coins, "coin"));
  for (const drop of reward.drops) {
    const what = article(`${drop.cardId ? cards.get(drop.cardId)?.name ?? drop.cardId : RARITY_NAMES[drop.rarity ?? ""] ?? drop.rarity} die`);
    parts.push(percent(drop.chance) + what);
  }
  return parts.length ? parts.join(" · ") : "nothing more";
}

/** "+10 points · +15 coins · Koala" - what one battle paid. */
export function resultText(result: BossResult, cards: Map<string, CardDef>): string {
  const parts: string[] = [];
  if (result.points) parts.push(`+${plural(result.points, "point")}`);
  if (result.coins) parts.push(`+${plural(result.coins, "coin")}`);
  for (const id of result.dice) parts.push(`${cards.get(id)?.name ?? id} die`);
  return parts.join(" · ");
}

const dropName = (drop: BossReward["drops"][number], cards: Map<string, CardDef>) =>
  article(`${drop.cardId ? cards.get(drop.cardId)?.name ?? drop.cardId : RARITY_NAMES[drop.rarity ?? ""] ?? drop.rarity} die`);

/** A reward as chips: points, coins, and each die it can drop. */
function Prizes({ reward, cards }: { reward: BossReward | null; cards: Map<string, CardDef> }) {
  if (!reward || (!reward.points && !reward.coins && reward.drops.length === 0)) {
    return <span className="dkb-prize is-none">nothing more</span>;
  }
  return (
    <>
      {reward.points > 0 && <span className="dkb-prize is-points"><Crown />{plural(reward.points, "point")}</span>}
      {reward.coins > 0 && <span className="dkb-prize is-coins"><CoinIcon size={15} />{plural(reward.coins, "coin")}</span>}
      {reward.drops.map((drop, i) => {
        const rarity = drop.rarity ?? (drop.cardId ? cards.get(drop.cardId)?.rarity : null);
        return (
          <span key={i} className="dkb-prize is-die"
            style={rarity ? ({ "--rarity": `var(--rarity-${rarity})` } as CSSProperties) : undefined}>
            <i className="dkb-gem" />{percent(drop.chance)}{dropName(drop, cards)}
          </span>
        );
      })}
    </>
  );
}

/** A card's face, small and round: its painting, or its silhouette on its energy's plate. */
function Face({ id, card }: { id: string; card: CardDef | undefined }) {
  const Avatar = CHARACTER_ICONS[id];
  return (
    <span className="dkb-face" style={energyPlate(card && energyOf(card))}>
      {hasPainting(id) ? <img src={paintingSrc(id, "icon")!} alt="" loading="lazy" /> : Avatar ? <Avatar size={22} /> : (card?.name ?? "?")[0]}
    </span>
  );
}

const PEEK = 7;

/** The boss's team: a row of faces that opens into the list, dice counts and all. */
function Team({ boss, cards }: { boss: Boss; cards: Map<string, CardDef> }) {
  const team = Object.entries(boss.cards);
  const tardigrades = Object.entries(boss.tardigrades).filter(([, n]) => n > 0);
  return (
    <details className="dkb-team">
      <summary>
        <span className="dkb-peek" aria-hidden="true">
          {team.slice(0, PEEK).map(([id]) => <Face key={id} id={id} card={cards.get(id)} />)}
          {team.length > PEEK && <span className="dkb-face is-more">+{team.length - PEEK}</span>}
        </span>
        <span className="dkb-team-label">Its team</span>
        <Line size={18}><path d="M6 9.5 12 15.5l6-6" /></Line>
      </summary>
      <ul>
        {team.map(([id, n]) => (
          <li key={id}>
            <Face id={id} card={cards.get(id)} />
            <span>{cards.get(id)?.name ?? id}</span>
            <b>×{n}</b>
          </li>
        ))}
        {tardigrades.map(([type, n]) => (
          <li key={type}>
            <span className="dkb-face is-tardigrade" style={tone(type)}><TardigradeIcon size={22} /></span>
            <span>{type} Tardigrades</span>
            <b>×{n}</b>
          </li>
        ))}
      </ul>
    </details>
  );
}

/** The boss's own card on its banner, crowned - or under lock. */
function BossCard({ boss }: { boss: Boss }) {
  const face = BOSS_FACES[boss.id] ?? Object.keys(boss.cards).find((id) => hasPainting(id));
  const src = face ? paintingSrc(face) : null;
  return (
    <span className="dkb-card" aria-hidden="true">
      {src && <img src={src} alt="" loading="lazy" />}
      {boss.status === "locked" ? <span className="dkb-card-lock"><LockIcon size={26} /></span> : <Crown />}
    </span>
  );
}

function statusText(boss: Boss): string {
  const record = `${boss.wins}W ${boss.losses}L`;
  if (boss.status === "beaten") return `Beaten · ${record}`;
  if (boss.status === "locked") return "Locked";
  return boss.losses ? `Open · ${boss.losses}L` : "Open";
}

export function BossesPage() {
  const [ladder, setLadder] = useState<BossLadder | null>(null);
  const [cards, setCards] = useState<CardDef[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [picking, setPicking] = useState<string | null>(null);
  const [team, setTeam] = useState<string | null>(() => (pendingCustomTeam() ? CUSTOM_TEAM_PICK : null));
  const [busy, setBusy] = useState(false);
  const byId = useMemo(() => new Map(cards.map((c) => [c.id, c])), [cards]);
  const back = backPath();

  useEffect(() => {
    players.bosses().then(setLadder, (e) => setError(e instanceof Error ? e.message : String(e)));
    api.getCards().then(setCards, () => {});
  }, []);

  async function fight(boss: Boss) {
    if (!team) return;
    setBusy(true);
    setError(null);
    try {
      const custom = team === CUSTOM_TEAM_PICK ? pendingCustomTeam() : null;
      const created = await api.createBossGame(boss.id, custom ?? team);
      rememberSeats(created.game.gameId, created.seats, created.game.playerOne.id);
      describeSavedGame(created.game.gameId, `Boss: ${boss.name}`, true);
      navigate(back, `?resume=${encodeURIComponent(created.game.gameId)}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  }

  const latest = ladder?.results[0];
  const latestBoss = latest && ladder?.bosses.find((b) => b.id === latest.bossId);
  const beaten = ladder?.bosses.filter((b) => b.status === "beaten").length ?? 0;

  return (
    <KingdomPage
      className="dk-bosses"
      title="Boss Ladder"
      dek="Beat each boss to open the next. A first win pays the most; rematches pay a little."
      hero={ladder && (
        <div className="dkb-score">
          <span className="dkb-rungs" aria-hidden="true">
            {ladder.bosses.map((b) => <Crown key={b.id} className={`is-${b.status}`} />)}
          </span>
          <span className="dkb-score-text">
            <b>{ladder.points}</b> boss {ladder.points === 1 ? "point" : "points"}
            <small>{beaten} of {ladder.bosses.length} bosses beaten</small>
          </span>
        </div>
      )}
    >
      {error && <p className="error">{error}</p>}
      {!ladder && !error && <p className="dkp-loading">Loading…</p>}
      {ladder && (
        <>
          {latest && latestBoss && (
            <p className={`dkp-note${latest.won ? " is-won" : ""}`}>
              {latest.won ? <CheckIcon size={18} /> : null}
              <span>
                <b>Last battle:</b> {latest.won ? `beat ${latestBoss.name}` : `lost to ${latestBoss.name}`}
                {latest.won && resultText(latest, byId) && ` - ${resultText(latest, byId)}`}
              </span>
            </p>
          )}
          <ol className="dkb-list">
            {ladder.bosses.map((boss, i) => {
              const Champion = boss.championId ? CHAMPION_ICONS[boss.championId] : null;
              return (
                <li key={boss.id} className={`dkb-boss is-${boss.status}`}>
                  <div className="dkb-banner">
                    <BossCard boss={boss} />
                    <span className="dkb-rank">Boss {i + 1}</span>
                    <h3>{boss.name}</h3>
                    <span className="dkb-status">
                      {boss.status === "beaten" ? <CheckIcon /> : boss.status === "locked" ? <LockIcon size={14} /> : null}
                      {statusText(boss)}
                    </span>
                  </div>
                  <div className="dkb-body">
                    <p className="dkb-blurb">{boss.blurb}</p>
                    <p className="dkb-style">
                      {boss.championId && <span>{Champion && <Champion size={18} />}{championLabel(boss.championId)}</span>}
                      <span>Plays: {boss.style}</span>
                    </p>
                    <Team boss={boss} cards={byId} />
                    <p className="dkb-reward">
                      <b>{boss.status === "beaten" ? "Rematch" : "First win"}</b>
                      <Prizes reward={boss.status === "beaten" ? boss.rematch : boss.firstWin} cards={byId} />
                    </p>
                    {boss.status === "locked" ? (
                      <p className="dkb-locked"><LockIcon /> Beat {ladder.bosses[i - 1]?.name} to open.</p>
                    ) : picking === boss.id ? (
                      <div className="dkb-pick">
                        <h4>Your team</h4>
                        {pendingCustomTeam()
                          ? <CustomTeamSetupChoice champions={CHAMPIONS} team={pendingCustomTeam()} value={team} onPick={setTeam} />
                          : <ChampionPicker champions={CHAMPIONS} value={team} onPick={setTeam} />}
                        <div className="dkb-pick-go">
                          <button type="button" className="btn is-go" disabled={!team || busy} onClick={() => void fight(boss)}>
                            Fight {boss.name}
                          </button>
                          <button type="button" className="btn ghost" onClick={() => setPicking(null)}>Cancel</button>
                        </div>
                      </div>
                    ) : (
                      <button type="button" className={`btn dkb-go ${boss.status === "beaten" ? "is-gold" : "is-boss"}`}
                        onClick={() => setPicking(boss.id)}>
                        {boss.status === "beaten" ? "Rematch" : "Challenge"}
                      </button>
                    )}
                  </div>
                </li>
              );
            })}
          </ol>
          <a className="dkp-linkcard" href="/teambuilder" onClick={(e) => { e.preventDefault(); navigate("/teambuilder"); }}>
            <BuilderIcon size={26} />
            <span>
              <b>Fight with your own team</b>
              Build it in the Team Builder and press "Play with this team", then come back here.
            </span>
          </a>
        </>
      )}
    </KingdomPage>
  );
}
