// Who's playing (2026-10-10; server: src/DiceFight.Api/Accounts). Every
// browser registers once, anonymously, and keeps the token it gets back in
// localStorage; every Dice Kingdom request sends it as X-Player-Token, so
// the server can keep this player's games. Naming the player makes it an
// account; a link code moves another device onto it (AccountPage.tsx).

const KEY = "dicekingdom:player";
const HEADER = "X-Player-Token";

export interface PlayedSeat {
  seatId: string;
  championId: string | null;
  /** Only on this player's own seats. */
  token: string | null;
  bot: boolean;
}

export interface PlayedGame {
  gameId: string;
  mode: "computer" | "online" | "local";
  status: "waiting" | "playing" | "finished";
  startedUtc: string;
  endedUtc: string | null;
  result: "win" | "loss" | null;
  winnerSeatId: string | null;
  opponentName: string | null;
  seats: PlayedSeat[];
}

export interface PlayerDevice {
  id: string;
  label: string | null;
  addedUtc: string;
  lastSeenUtc: string;
  thisDevice: boolean;
}

export interface Me {
  id: string;
  name: string | null;
  devices: PlayerDevice[];
  games: PlayedGame[];
  coins: number;
  /** Coins just given for today's first visit (0 if already had). */
  dailyCoins: number;
}

export type Rarity = "Common" | "Uncommon" | "Rare" | "SuperRare";

/** Coins and the dice pulled from packs (PacksPage.tsx). */
export interface Collection {
  coins: number;
  dailyCoins: number;
  dailyAmount: number;
  packPrice: number;
  packsOpened: number;
  /** Card id -> dice owned, past the card's die limit too. */
  dice: Record<string, number>;
  nextDailyUtc: string;
  tradeIns: number;
  /** Champions whose starter roster has been added. */
  starters: string[];
  /** "Common" -> 5: dice of that rarity for one of the next. */
  tradeInCost: Partial<Record<Rarity, number>>;
}

export interface PulledCard {
  cardId: string;
  /** "Basic" for a Basic Action, which has no rarity. */
  rarity: Rarity | "Basic";
  /** Dice of it now, this pack's included. */
  owned: number;
  /** The first of it. */
  new: boolean;
}

function storedToken(): string | null {
  try {
    return localStorage.getItem(KEY);
  } catch {
    return null;
  }
}

let memoryToken: string | null = null; // storage blocked - lasts as long as the page
let registering: Promise<string | null> | null = null;

function setToken(token: string | null) {
  memoryToken = token;
  try {
    if (token) localStorage.setItem(KEY, token);
    else localStorage.removeItem(KEY);
  } catch {
    // Storage blocked - see memoryToken.
  }
}

/** This browser's player token, registering one first if there isn't one. Null if the server can't be reached. */
export function playerToken(): Promise<string | null> {
  const known = storedToken() ?? memoryToken;
  if (known) return Promise.resolve(known);
  registering ??= fetch("/api/players", { method: "POST" })
    .then(async (res) => {
      if (!res.ok) return null;
      const body = (await res.json()) as { playerId: string; token: string };
      setToken(body.token);
      return body.token;
    })
    .catch(() => null)
    .finally(() => {
      registering = null;
    });
  return registering;
}

export async function playerHeader(): Promise<Record<string, string>> {
  const token = await playerToken();
  return token ? { [HEADER]: token } : {};
}

async function call<T>(path: string, init?: RequestInit, retried = false): Promise<T> {
  const res = await fetch(`/api/players${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(await playerHeader()), ...(init?.headers ?? {}) },
  });
  // The server doesn't know this token (forgotten, or this device was
  // signed out elsewhere): start over as a new anonymous player.
  if (res.status === 401 && !retried) {
    setToken(null);
    return call<T>(path, init, true);
  }
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? `Request failed: ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export const players = {
  me: () => call<Me>("/me"),
  setName: (name: string | null) => call<Me>("/me/name", { method: "PUT", body: JSON.stringify({ name }) }),
  linkCode: () => call<{ code: string; expiresUtc: string }>("/me/link-code", { method: "POST" }),
  link: (code: string) => call<Me>("/me/link", { method: "POST", body: JSON.stringify({ code }) }),
  collection: () => call<Collection>("/me/collection"),
  openPack: () => call<{ cards: PulledCard[]; collection: Collection }>("/me/packs", { method: "POST" }),
  /** One card id per die given. */
  tradeIn: (give: string[]) =>
    call<{ card: PulledCard; collection: Collection }>("/me/trade-in", { method: "POST", body: JSON.stringify({ give }) }),
  removeDevice: (deviceId: string) => call<Me>(`/me/devices/${encodeURIComponent(deviceId)}`, { method: "DELETE" }),
  /** Signs this device out: it forgets its token and becomes a new anonymous player. */
  signOut: async (me: Me) => {
    const mine = me.devices.find((d) => d.thisDevice);
    if (mine) await call<Me>(`/me/devices/${encodeURIComponent(mine.id)}`, { method: "DELETE" });
    setToken(null);
  },
};
