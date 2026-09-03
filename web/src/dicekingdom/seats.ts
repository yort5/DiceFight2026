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

export function forgetSeats(): void {
  write(null);
}

export function inviteLink(gameId: string): string | null {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return null;
  const other = stored.seats.find((s) => s.playerId !== stored.activePlayerId);
  if (!other) return null;
  const url = new URL(window.location.href);
  url.pathname = "/dice-kingdom";
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
