// /dice-kingdom/bosses (2026-10-31): the boss ladder. Each boss is a team
// the user built in the Team Builder, played by the computer in its own
// style (server: V2/Data/DiceKingdomBosses.cs). The first is open to
// everyone; beating one opens the next. A first win pays points, coins and
// sometimes dice; a rematch pays less. The fight itself is an ordinary
// vs-computer game on the usual game screen.
import { useEffect, useMemo, useState } from "react";
import { navigate } from "../router";
import { backPath } from "./AccountPage";
import { ChampionPicker, championLabel } from "./ChampionPicker";
import { CustomTeamSetupChoice } from "./CustomTeamSetupChoice";
import { CUSTOM_TEAM_PICK, pendingCustomTeam } from "./customTeams";
import { api } from "./api";
import { players, type Boss, type BossLadder, type BossReward, type BossResult } from "./player";
import { describeSavedGame, rememberSeats } from "./seats";
import { ThemeToggle, useTheme } from "./ThemeToggle";
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

function teamSummary(boss: Boss, cards: Map<string, CardDef>): string {
  const names = Object.entries(boss.cards).map(([id, n]) => `${cards.get(id)?.name ?? id} ×${n}`);
  const tardigrades = Object.entries(boss.tardigrades).filter(([, n]) => n > 0).map(([t, n]) => `${n} ${t}`);
  return `${names.join(", ")}${tardigrades.length ? ` · Tardigrades: ${tardigrades.join(", ")}` : ""}`;
}

export function BossesPage() {
  const [theme, setTheme] = useTheme();
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

  return (
    <div className="dicekingdom dk-account dk-bosses">
      <div className="dk-titlebar-right" style={{ float: "right" }}>
        <ThemeToggle theme={theme} setTheme={setTheme} />
      </div>
      <p className="dk-acct-back">
        <a href={back} onClick={(e) => { e.preventDefault(); navigate(back); }}>← Dice Kingdom</a>
      </p>
      <h1>Boss Ladder</h1>
      <p className="dk-acct-dim">
        Beat each boss to open the next. A first win pays the most; rematches pay a little.
      </p>
      {error && <p className="error">{error}</p>}
      {!ladder && !error && <p className="dk-acct-dim">Loading…</p>}
      {ladder && (
        <>
          <div className="panel dk-boss-score">
            <span className="dk-boss-points">{ladder.points}</span> boss points ·{" "}
            {ladder.bosses.filter((b) => b.status === "beaten").length} of {ladder.bosses.length} bosses beaten
            {latest && latestBoss && (
              <p className={latest.won ? "dk-acct-ok" : "dk-acct-dim"}>
                Last battle: {latest.won ? `beat ${latestBoss.name}` : `lost to ${latestBoss.name}`}
                {latest.won && resultText(latest, byId) && ` - ${resultText(latest, byId)}`}
              </p>
            )}
          </div>
          <ol className="dk-boss-list">
            {ladder.bosses.map((boss, i) => (
              <li key={boss.id} className={`panel dk-boss dk-boss-${boss.status}`}>
                <div className="dk-acct-row">
                  <span>
                    <small className="dk-acct-dim">Boss {i + 1}</small>
                    <h3>{boss.name}</h3>
                  </span>
                  <span className="dk-boss-status">
                    {boss.status === "beaten" ? `Beaten · ${boss.wins}W ${boss.losses}L` : boss.status === "open" ? (boss.losses ? `${boss.losses}L` : "Open") : "Locked"}
                  </span>
                </div>
                <p>{boss.blurb}</p>
                <p className="dk-acct-dim dk-boss-team">
                  {boss.championId ? `${championLabel(boss.championId)} · ` : ""}Plays: {boss.style}
                  <br />
                  {teamSummary(boss, byId)}
                </p>
                <p className="dk-boss-reward">
                  <b>{boss.status === "beaten" ? "Rematch:" : "First win:"}</b>{" "}
                  {boss.status === "beaten" ? (boss.rematch ? rewardText(boss.rematch, byId) : "nothing more") : rewardText(boss.firstWin, byId)}
                </p>
                {boss.status === "locked" ? (
                  <p className="dk-acct-dim">Beat {ladder.bosses[i - 1]?.name} to open.</p>
                ) : picking === boss.id ? (
                  <div className="dk-boss-pick">
                    <h3>Your team</h3>
                    {pendingCustomTeam()
                      ? <CustomTeamSetupChoice champions={CHAMPIONS} team={pendingCustomTeam()} value={team} onPick={setTeam} />
                      : <ChampionPicker champions={CHAMPIONS} value={team} onPick={setTeam} />}
                    <div className="dk-acct-row">
                      <button type="button" className="btn" disabled={!team || busy} onClick={() => void fight(boss)}>
                        Fight {boss.name}
                      </button>
                      <button type="button" className="btn ghost" onClick={() => setPicking(null)}>Cancel</button>
                    </div>
                  </div>
                ) : (
                  <button type="button" className="btn" onClick={() => setPicking(boss.id)}>
                    {boss.status === "beaten" ? "Rematch" : "Challenge"}
                  </button>
                )}
              </li>
            ))}
          </ol>
          <p className="dk-acct-dim">
            To fight with your own team, build it in the{" "}
            <a href="/teambuilder" onClick={(e) => { e.preventDefault(); navigate("/teambuilder"); }}>Team Builder</a>{" "}
            and press "Play with this team", then come back here.
          </p>
        </>
      )}
    </div>
  );
}
