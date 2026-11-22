// Who goes first (2026-11-22, user: "I always go first... We should
// randomize that, ideally with an animation"). The server picks at random
// when the game is made; this shows it as a coin with each side's Champion
// on a face, flipping and landing on the one that starts. Shared by both
// Dice Kingdom pages. Shown once per game, before the first turn's draw,
// and dismissed by a tap or on its own a moment after it lands.
import { useEffect, useState } from "react";
import { CHAMPION_ICONS, ENERGY_ICONS } from "./icons";
import { tone } from "./kingdomArt";
import type { GameState, PlayerState } from "./types";

// The computer opponent waits while the flip is up (both pages' bot
// timers check this) - it used to start its turn behind the coin.
let showing = false;
export const firstFlipShowing = () => showing;

const SPIN_MS = 1900;
const HOLD_MS = 1600;
const seenKey = (gameId: string) => `dk-first-flip-${gameId}`;

function alreadySeen(gameId: string): boolean {
  try {
    return localStorage.getItem(seenKey(gameId)) === "1";
  } catch {
    return false;
  }
}

function markSeen(gameId: string) {
  try {
    localStorage.setItem(seenKey(gameId), "1");
  } catch {
    /* private window - it just shows again on a reload */
  }
}

function Face({ player, back }: { player: PlayerState; back?: boolean }) {
  const energy = player.champion?.energySymbolId ?? "Wild";
  const Icon = (player.champion && CHAMPION_ICONS[player.champion.id]) || ENERGY_ICONS[energy] || ENERGY_ICONS.Wild;
  return (
    <div className={`dk-flip-face${back ? " is-back" : ""}`} style={tone(energy)}>
      <Icon size={64} />
      <span className="dk-flip-name">{player.name}</span>
    </div>
  );
}

export function FirstPlayerFlip({ game, you }: { game: GameState; you: string | null }) {
  const pending = (game.turnNumber ?? 1) === 0 && !!game.firstPlayerId && !game.gameOver;
  const [open, setOpen] = useState(() => pending && !alreadySeen(game.gameId));
  const [landed, setLanded] = useState(false);

  useEffect(() => {
    if (!open) return;
    showing = true;
    markSeen(game.gameId);
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
    const land = window.setTimeout(() => setLanded(true), reduced ? 0 : SPIN_MS);
    const close = window.setTimeout(() => setOpen(false), (reduced ? 0 : SPIN_MS) + HOLD_MS);
    return () => {
      showing = false;
      window.clearTimeout(land);
      window.clearTimeout(close);
    };
  }, [open, game.gameId]);

  if (!open || !game.firstPlayerId) return null;
  const first = game.firstPlayerId === game.playerTwo.id ? game.playerTwo : game.playerOne;
  const headsUp = first.id === game.playerOne.id;
  const text = you ? (first.id === you ? "You go first!" : `${first.name} goes first`) : `${first.name} goes first`;
  return (
    <div className="dk-flip-backdrop" role="dialog" aria-label="Who goes first" onClick={() => setOpen(false)}>
      <div className="dk-flip">
        <p className="dk-flip-eyebrow">Flipping for first turn</p>
        <div className="dk-flip-stage">
          {/* Lands with player one up (an even number of half turns) or
              player two up (odd), after five full spins either way. */}
          <div className={`dk-flip-coin ${headsUp ? "lands-front" : "lands-back"}`}>
            <Face player={game.playerOne} />
            <Face player={game.playerTwo} back />
          </div>
        </div>
        <h2 className={`dk-flip-result${landed ? " is-shown" : ""}`} aria-live="polite">
          {landed ? text : " "}
        </h2>
        <p className="dk-flip-hint">Tap to continue</p>
      </div>
    </div>
  );
}
