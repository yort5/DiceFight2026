// Rule 2.9 - the game ends the moment a player's Life reaches 0 (added
// 2026-09-30: a playtest game used to run on at -5 life). Shared by both
// Dice Kingdom pages: who won, both final Life totals, and a way back to
// the setup screen. The board stays visible underneath. A boss battle
// (2026-10-31) also shows what it paid, and a way back to the ladder.
import { useEffect, useState } from "react";
import { navigate } from "../router";
import { api } from "./api";
import { resultText } from "./BossesPage";
import { players, type BossResult } from "./player";
import type { CardDef, GameState } from "./types";

export function GameOverOverlay({ game, you, onNewGame }: { game: GameState; you: string | null; onNewGame: () => void }) {
  const [boss, setBoss] = useState<{ name: string; result: BossResult; cards: Map<string, CardDef> } | null>(null);
  useEffect(() => {
    if (!game.gameOver) return;
    let live = true;
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

  if (!game.gameOver) return null;
  const winner = game.winnerId === game.playerOne.id ? game.playerOne : game.winnerId === game.playerTwo.id ? game.playerTwo : null;
  const title = !winner ? "It's a tie" : you && winner.id === you ? "You win!" : `${winner.name} wins`;
  const ladderSearch = window.location.pathname === "/dice-kingdom/mobile" ? "?from=mobile" : "";
  return (
    <div className="dk-gameover-backdrop" role="dialog" aria-label="Game over">
      <div className="dk-gameover">
        <p className="dk-gameover-eyebrow">Game over</p>
        <h2>{title}</h2>
        <p className="dk-gameover-life">
          {game.playerOne.name} {game.playerOne.life} · {game.playerTwo.name} {game.playerTwo.life}
        </p>
        {boss && (
          <p className="dk-gameover-boss">
            {boss.result.won
              ? `${boss.result.firstWin ? `${boss.name} beaten for the first time!` : `${boss.name} beaten again.`} ${resultText(boss.result, boss.cards)}`
              : `${boss.name} holds. Try again from the Boss Ladder.`}
          </p>
        )}
        <button type="button" className="btn" onClick={onNewGame}>
          New game
        </button>
        {boss && (
          <button type="button" className="btn ghost" onClick={() => navigate("/dice-kingdom/bosses", ladderSearch)}>
            Boss Ladder
          </button>
        )}
      </div>
    </div>
  );
}
