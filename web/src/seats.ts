// Which side of a game this browser holds, and the secret that proves it.
//
// A seat token is a bearer secret: whoever has it holds that side, the
// same model as a shared document link. Creating a game hands back BOTH
// seats, because the creator may be playing alone - holding both and
// passing the laptop - or about to send one to someone else.
//
// Session-scoped, not localStorage: a seat is a claim on a game in
// progress, and a stale one resurfacing in a later session would silently
// put someone back in a game they had left.

export interface Seat {
  playerId: string;
  token: string;
}

interface StoredSeats {
  gameId: string;
  seats: Seat[];
  /** Which of them this browser is currently playing as. */
  activePlayerId: string;
}

const KEY = "df2026:seats";

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
    // Storage blocked - seats then last only as long as this page does,
    // which still gets you through a game without reloading.
  }
}

export function rememberSeats(gameId: string, seats: Seat[], playAs?: string): void {
  write({ gameId, seats, activePlayerId: playAs ?? seats[0]?.playerId ?? "" });
}

/** The token to send for `gameId`, or null if this browser holds no seat. */
export function tokenFor(gameId: string): string | null {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return null;
  return stored.seats.find((s) => s.playerId === stored.activePlayerId)?.token ?? null;
}

/** Every seat this browser holds in `gameId` - two when playing alone. */
export function seatsFor(gameId: string): Seat[] {
  const stored = read();
  return stored && stored.gameId === gameId ? stored.seats : [];
}

/** Switch which held seat is playing - the hotseat case. */
export function playAs(gameId: string, playerId: string): void {
  const stored = read();
  if (!stored || stored.gameId !== gameId) return;
  if (!stored.seats.some((s) => s.playerId === playerId)) return;
  write({ ...stored, activePlayerId: playerId });
}

export function forgetSeats(): void {
  write(null);
}
