import type { GameState } from "./types";

// Root.tsx swaps <TeamBuilderPage> for <App> entirely on route change
// (see router.ts) rather than keeping both mounted, so there's no React
// state to hand a freshly-created game through directly. sessionStorage
// bridges that one gap: TeamBuilderPage's "Start Game" stashes the
// server's response here right before navigating to /game, and App reads
// it back once on mount (see readPendingGame) instead of falling back to
// its default "click New Game" empty state. Session-scoped (not
// localStorage) on purpose - a stale pending game from a closed tab
// should never resurrect itself in a later session.
const KEY = "df2026:pendingGame";

export function stashPendingGame(game: GameState): void {
  sessionStorage.setItem(KEY, JSON.stringify(game));
}

// Consumes (removes) the pending game so a later refresh of /game doesn't
// replay it - a refresh should re-fetch the same game by id instead, same
// as the rest of this app already does.
export function readPendingGame(): GameState | null {
  const raw = sessionStorage.getItem(KEY);
  if (!raw) return null;
  sessionStorage.removeItem(KEY);
  try {
    return JSON.parse(raw) as GameState;
  } catch {
    return null;
  }
}
