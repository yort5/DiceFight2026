// v2 counterpart to ../seats.ts - same bearer-token-in-sessionStorage
// model, kept as a separate module (not a shared one with a path param)
// specifically so a v1 /game session and a v3 /dice-kingdom session open
// in the same browser tab don't collide over one sessionStorage key.

export interface Seat {
  playerId: string;
  token: string;
}

interface StoredSeats {
  gameId: string;
  seats: Seat[];
  activePlayerId: string;
}

const KEY = "dicekingdom:seats";

let cached: StoredSeats | null = null;

function read(): StoredSeats | null {
  if (cached) return cached;
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as StoredSeats;
    if (typeof parsed?.gameId !== "string" || !Array.isArray(parsed.seats)) return null;
    cached = parsed;
    return parsed;
  } catch {
    return null;
  }
}

function write(value: StoredSeats | null): void {
  cached = value;
  try {
    if (value) sessionStorage.setItem(KEY, JSON.stringify(value));
    else sessionStorage.removeItem(KEY);
  } catch {
    // Storage blocked - seats then last only as long as this page does.
  }
  if (value) saveGame(value);
}

// --- Saved games (2026-10-08, user: "I lost one side of the game").
// sessionStorage above dies with its tab, and the seat tokens with it - a
// closed host tab left that seat unplayable. So every game's seats are
// ALSO kept here, in localStorage, which survives closing the tab and the
// browser; the start screen offers them back (savedGames/resumeSeats).
// The tab's CURRENT game stays in sessionStorage, so two tabs can still
// play the two sides of one game. Seats merge by player id: a browser
// that has held both sides keeps both.

const SAVED_KEY = "dicekingdom:games";
const KEEP_MS = 7 * 24 * 3600 * 1000; // the server forgets idle games well before this

export interface SavedGame {
  gameId: string;
  seats: Seat[];
  /** The seat this browser last played. */
  playAs: string;
  vsComputer?: boolean;
  /** e.g. "Wolf vs Armadillo", once the game has been seen. */
  label?: string;
  savedAt: number;
}

function readSaved(): Record<string, SavedGame> {
  try {
    const parsed = JSON.parse(localStorage.getItem(SAVED_KEY) ?? "{}") as Record<string, SavedGame>;
    return parsed && typeof parsed === "object" ? parsed : {};
  } catch {
    return {};
  }
}

function writeSaved(games: Record<string, SavedGame>): void {
  const now = Date.now();
  for (const [id, g] of Object.entries(games)) if (now - g.savedAt > KEEP_MS) delete games[id];
  try {
    localStorage.setItem(SAVED_KEY, JSON.stringify(games));
  } catch {
    // Storage blocked - nothing to resume later, same as before.
  }
}

function saveGame(value: StoredSeats): void {
  const named = value.seats.filter((s) => s.playerId);
  if (named.length === 0) return; // an invite link not yet resolved to a seat
  const games = readSaved();
  const prior = games[value.gameId];
  const seats = [...(prior?.seats ?? []).filter((s) => !named.some((n) => n.playerId === s.playerId)), ...named];
  games[value.gameId] = { ...prior, gameId: value.gameId, seats, playAs: value.activePlayerId || named[0].playerId, savedAt: Date.now() };
  writeSaved(games);
}

/** Games this browser holds a seat in, newest first. */
export function savedGames(): SavedGame[] {
  return Object.values(readSaved()).sort((a, b) => b.savedAt - a.savedAt);
}

/** Notes what a saved game is, for the resume list - and that it's still being played. */
export function describeSavedGame(gameId: string, label: string, vsComputer?: boolean): void {
  const games = readSaved();
  const g = games[gameId];
  if (!g) return;
  games[gameId] = { ...g, label, vsComputer: vsComputer ?? g.vsComputer, savedAt: Date.now() };
  writeSaved(games);
}

/** Makes a saved game this tab's current one; returns it, or null if unknown. */
export function resumeSeats(gameId: string): SavedGame | null {
  const g = readSaved()[gameId];
  if (!g) return null;
  write({ gameId, seats: g.seats, activePlayerId: g.playAs });
  return g;
}

/**
 * A game the server says this player holds seats in (player.ts) - added to
 * this browser's saved list if it isn't there, so a game started on
 * another device can be resumed here.
 */
export function adoptGame(gameId: string, seats: Seat[], playAs: string, label: string, vsComputer: boolean): void {
  const games = readSaved();
  if (games[gameId] || seats.length === 0 || dismissed().includes(gameId)) return;
  games[gameId] = { gameId, seats, playAs, label, vsComputer, savedAt: Date.now() };
  writeSaved(games);
}

export function forgetSavedGame(gameId: string): void {
  const games = readSaved();
  delete games[gameId];
  writeSaved(games);
  // ...and don't let adoptGame bring it back from the server.
  try {
    localStorage.setItem(DISMISSED_KEY, JSON.stringify([gameId, ...dismissed().filter((id) => id !== gameId)].slice(0, 100)));
  } catch {
    // Storage blocked.
  }
}

const DISMISSED_KEY = "dicekingdom:dismissed";

function dismissed(): string[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(DISMISSED_KEY) ?? "[]");
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

/** A link back into YOUR OWN seat - to keep, or to carry on from another device. */
export function myLink(gameId: string, path = "/dice-kingdom"): string | null {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return null;
  const mine = stored.seats.find((s) => s.playerId === stored.activePlayerId) ?? stored.seats[0];
  if (!mine?.token) return null;
  const url = new URL(window.location.href);
  url.pathname = path;
  url.search = `?g=${encodeURIComponent(gameId)}&s=${encodeURIComponent(mine.token)}`;
  url.hash = "";
  return url.toString();
}

export function rememberSeats(gameId: string, seats: Seat[], playAs?: string): void {
  write({ gameId, seats, activePlayerId: playAs ?? seats[0]?.playerId ?? "" });
}

export function tokenFor(gameId: string): string | null {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return null;
  return (stored.seats.find((s) => s.playerId === stored.activePlayerId) ?? stored.seats[0])?.token ?? null;
}

export function seatsFor(gameId: string): Seat[] {
  const stored = read();
  return stored && stored.gameId === gameId ? stored.seats : [];
}

export function playAs(gameId: string, playerId: string): void {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return;
  if (!stored.seats.some((s) => s.playerId === playerId)) return;
  write({ ...stored, activePlayerId: playerId });
}

/** Leaving a game for good (New Game after it ended): this tab and the saved list. */
export function forgetSeats(): void {
  const stored = read();
  if (stored) forgetSavedGame(stored.gameId);
  write(null);
}

export function inviteLink(gameId: string, path = "/dice-kingdom"): string | null {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return null;
  const other = stored.seats.find((s) => s.playerId !== stored.activePlayerId);
  if (!other) return null;
  const url = new URL(window.location.href);
  url.pathname = path;
  url.search = `?g=${encodeURIComponent(gameId)}&s=${encodeURIComponent(other.token)}`;
  url.hash = "";
  return url.toString();
}

export function claimSeatFromUrl(): { gameId: string; token: string } | null {
  const params = new URLSearchParams(window.location.search);
  const gameId = params.get("g");
  const token = params.get("s");
  if (!gameId || !token) return null;

  write({ gameId, seats: [{ playerId: "", token }], activePlayerId: "" });
  window.history.replaceState(null, "", window.location.pathname);
  return { gameId, token };
}

export function nameClaimedSeat(gameId: string, playerId: string): void {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return;
  if (stored.seats.length !== 1 || stored.seats[0].playerId !== "") return;
  write({ gameId, seats: [{ ...stored.seats[0], playerId }], activePlayerId: playerId });
}
