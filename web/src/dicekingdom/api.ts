import { seatsFor, tokenFor } from "./seats";
import type { BlockAssignment, CardDef, Champion, CreatedGame, GameState } from "./types";

// v2 counterpart to ../api.ts - same relative-BASE_URL/seat-header/
// request<T> shape, pointed at api/v2/games instead of api/games. A
// separate client, not a parameterized version of the v1 one: the action
// list itself is smaller (no Range/Tag Out/Infiltrate/Continuous-die/
// Global-ability endpoints - see V2GamesController.cs's own remarks on
// why none of DiceKingdomConfig's 8 Characters need them).
const BASE_URL = "/api/v2/games";

// undefined = "look up whichever seat this browser is currently playing
// as" (seats.ts's own tokenFor - the normal case); null/a string = use
// exactly this token instead, regardless of that. Only apiAs (below)
// passes the latter.
function seatHeader(path: string, tokenOverride?: string | null): Record<string, string> {
  if (tokenOverride !== undefined) return tokenOverride ? { "X-Seat-Token": tokenOverride } : {};
  const gameId = /^\/([^/]+)/.exec(path)?.[1];
  const token = gameId ? tokenFor(gameId) : null;
  return token ? { "X-Seat-Token": token } : {};
}

async function request<T>(path: string, options?: RequestInit, tokenOverride?: string | null): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...seatHeader(path, tokenOverride),
      ...(options?.headers ?? {}),
    },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? `Request failed: ${res.status}`);
  }
  return res.json() as Promise<T>;
}

function makeClient(tokenOverride?: string | null) {
  return {
    getChampions: () => request<Champion[]>("/champions", undefined, tokenOverride),
    getCards: () => request<CardDef[]>("/cards", undefined, tokenOverride),

    createGame: (playerOneChampionId: string, playerTwoChampionId: string) =>
      request<CreatedGame>(
        "",
        { method: "POST", body: JSON.stringify({ playerOneChampionId, playerTwoChampionId }) },
        tokenOverride,
      ),
    getGame: (id: string) => request<GameState>(`/${id}`, undefined, tokenOverride),

    clearAndDraw: (id: string) => request<GameState>(`/${id}/clear-and-draw`, { method: "POST" }, tokenOverride),
    roll: (id: string) => request<GameState>(`/${id}/roll`, { method: "POST" }, tokenOverride),
    reroll: (id: string, dieIds: string[]) =>
      request<GameState>(`/${id}/reroll`, { method: "POST", body: JSON.stringify({ dieIds }) }, tokenOverride),
    finishRoll: (id: string) => request<GameState>(`/${id}/finish-roll`, { method: "POST" }, tokenOverride),

    purchase: (id: string, dieId: string, energyDieIds: string[]) =>
      request<GameState>(`/${id}/purchase`, { method: "POST", body: JSON.stringify({ dieId, energyDieIds }) }, tokenOverride),
    field: (id: string, dieId: string, energyDieIds: string[]) =>
      request<GameState>(`/${id}/field`, { method: "POST", body: JSON.stringify({ dieId, energyDieIds }) }, tokenOverride),

    enterAttackStep: (id: string) => request<GameState>(`/${id}/enter-attack-step`, { method: "POST" }, tokenOverride),
    skipAttackStep: (id: string) => request<GameState>(`/${id}/skip-attack-step`, { method: "POST" }, tokenOverride),
    // attackers: which lane (0-3) each declared attacker is placed into -
    // see DieInstance.Lane's own remarks. A lane is a display grouping
    // only; blocking below is still assigned per individual attacker.
    declareAttackers: (id: string, attackers: { dieId: string; lane: number }[]) =>
      request<GameState>(`/${id}/declare-attackers`, { method: "POST", body: JSON.stringify({ attackers }) }, tokenOverride),
    declareBlockers: (id: string, assignments: BlockAssignment[]) =>
      request<GameState>(`/${id}/declare-blockers`, { method: "POST", body: JSON.stringify({ assignments }) }, tokenOverride),
    assignCombatDamage: (id: string, assignments: BlockAssignment[]) =>
      request<GameState>(`/${id}/assign-combat-damage`, { method: "POST", body: JSON.stringify({ assignments }) }, tokenOverride),
    cleanUp: (id: string) => request<GameState>(`/${id}/clean-up`, { method: "POST" }, tokenOverride),

    resolvePendingChoice: (id: string, chosenDieIds: string[]) =>
      request<GameState>(
        `/${id}/resolve-pending-choice`,
        { method: "POST", body: JSON.stringify({ chosenDieIds }) },
        tokenOverride,
      ),
  };
}

export const api = makeClient();

// A client bound to one specific seat's own token, rather than whichever
// seat this browser is currently "playing as" (seats.ts's tokenFor,
// which is one shared flag - see its own remarks). The computer opponent
// needs to act as Player Two regardless of which seat the human has
// selected, and without disturbing that selection for their own next
// click - see bot.ts / DiceKingdomPage's performBotAction, the only
// caller. Both seats' tokens are already in local storage for a vs-
// computer game (rememberSeats stores both, same as ordinary pass-and-
// play), so this needs no server round trip.
export function apiAs(gameId: string, playerId: string): ReturnType<typeof makeClient> {
  return makeClient(seatsFor(gameId).find((s) => s.playerId === playerId)?.token ?? null);
}
