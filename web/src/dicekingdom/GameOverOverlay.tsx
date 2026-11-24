// Rule 2.9 - the game ends the moment a player's Life reaches 0 (added
// 2026-09-30: a playtest game used to run on at -5 life). Shared by both
// Dice Kingdom pages: who won, both final Life totals, and a way back to
// the setup screen. The board stays visible underneath. A boss battle
// (2026-10-31) also shows what it paid, and a way back to the ladder.
import { useEffect, useRef, useState } from "react";
import { navigate } from "../router";
import { api } from "./api";
import { resultText } from "./BossesPage";
import { Crown } from "./kingdomArt";
import { players, type BossResult } from "./player";
import type { CardDef, GameState } from "./types";

// `onHome` (mobile, 2026-11-15, user: the win screen "doesn't give an
// option to go to the home page, only start a new game" - on mobile that
// button already went home, under the wrong name): with it, the buttons are
// "Play again" (onNewGame, when given) and "Home".
//
// A game that ends while you watch holds the board for END_PAUSE_MS first
// (user, 2026-11-24: "there should be a second or two pause at the end when
// damage resolves before transitioning to the You Win screen") - long
// enough for the last KO (dieFlights.ts, ~1.5 s) and the life totals to
// land. One that was already over when the page opened shows at once.
const END_PAUSE_MS = 2200;

export function GameOverOverlay({ game, you, onNewGame, onHome }: { game: GameState; you: string | null; onNewGame?: () => void; onHome?: () => void }) {
  const [boss, setBoss] = useState<{ name: string; result: BossResult; cards: Map<string, CardDef> } | null>(null);
  // Coins for finishing it (2026-11-15) - settled on the move that ended it.
  const [coins, setCoins] = useState<{ earned: number; mode: string } | null>(null);
  const overAtMount = useRef(game.gameOver);
  const [shown, setShown] = useState(game.gameOver);
  useEffect(() => {
    if (!game.gameOver) {
      overAtMount.current = false;
      setShown(false);
      return;
    }
    if (overAtMount.current) return;
    const t = window.setTimeout(() => setShown(true), END_PAUSE_MS);
    return () => window.clearTimeout(t);
  }, [game.gameOver, game.gameId]);
  useEffect(() => {
    if (!game.gameOver) return;
    let live = true;
    players.me().then((me) => {
      const played = me.games.find((g) => g.gameId === game.gameId);
      if (live && played && played.coinsEarned != null) setCoins({ earned: played.coinsEarned, mode: played.mode });
    }, () => {});
    // The server settles a boss battle on the move that ends it, so it's there by now.
    players.bosses().then(async (ladder) => {
      const result = ladder.results.find((r) => r.gameId === game.gameId);
      if (!result || !live) return;
      const cards = result.dice.length ? new Map((await api.getCards()).map((c) => [c.id, c])) : new Map<string, CardDef>();
      if (live) setBoss({ name: ladder.bosses.find((b) => b.id === result.bossId)?.name ?? "the boss", result, cards });
    }, () => {});
    return () => {
      live = false;
    };
  }, [game.gameOver, game.gameId]);

  if (!game.gameOver || !shown) return null;
  const winner = game.winnerId === game.playerOne.id ? game.playerOne : game.winnerId === game.playerTwo.id ? game.playerTwo : null;
  const title = !winner ? "It's a tie" : you && winner.id === you ? "You win!" : `${winner.name} wins`;
  const won = !!winner && !!you && winner.id === you;
  const ladderSearch = window.location.pathname === "/dice-kingdom/mobile" ? "?from=mobile" : "";
  return (
    <div className="dk-gameover-backdrop" role="dialog" aria-label="Game over">
      <div className={`dk-gameover${won ? " is-won" : winner ? " is-lost" : ""}`}>
        {/* Shown on mobile only (mobilePlay.css); hidden on desktop. */}
        {won && <Crown className="dk-gameover-crown" />}
        <p className="dk-gameover-eyebrow">Game over</p>
        <h2>{title}</h2>
        {/* Plain spans, so desktop reads exactly as before; mobile makes
            them two pills (2026-11-12). */}
        <p className="dk-gameover-life">
          {[game.playerOne, game.playerTwo].map((p, i) => (
            <span key={p.id}>
              {i > 0 && <span className="dk-gameover-sep"> · </span>}
              <span className={`dk-gameover-side${winner?.id === p.id ? " is-winner" : ""}`}>
                {p.name} <span className="dk-gameover-n">{p.life}</span>
              </span>
            </span>
          ))}
        </p>
        {boss && (
          <p className="dk-gameover-boss">
            {boss.result.won
              ? `${boss.result.firstWin ? `${boss.name} beaten for the first time!` : `${boss.name} beaten again.`} ${resultText(boss.result, boss.cards)}`
              : `${boss.name} holds. Try again from the Boss Ladder.`}
          </p>
        )}
        {coins && coins.mode !== "local" && (
          <p className="dk-gameover-boss dk-gameover-coins">
            {coins.earned > 0
              ? `+${coins.earned} coins for finishing the game.`
              : "No coins this time - today's 50 from games are already yours. More tomorrow."}
          </p>
        )}
        {onNewGame && (
          <button type="button" className="btn" onClick={onNewGame}>
            {onHome ? "Play again" : "New game"}
          </button>
        )}
        {onHome && (
          <button type="button" className={onNewGame ? "btn ghost" : "btn"} onClick={onHome}>
            Home
          </button>
        )}
        {boss && (
          <button type="button" className="btn ghost" onClick={() => navigate("/dice-kingdom/bosses", ladderSearch)}>
            Boss Ladder
          </button>
        )}
      </div>
    </div>
  );
}
