import { tokenFor } from "./seats";
import type { BlockAssignment, CardDef, Champion, CreatedGame, GameState } from "./types";

// v2 counterpart to ../api.ts - same relative-BASE_URL/seat-header/
// request<T> shape, pointed at api/v2/games instead of api/games. A
// separate client, not a parameterized version of the v1 one: the action
// list itself is smaller (no Range/Tag Out/Infiltrate/Continuous-die/
// Global-ability endpoints - see V2GamesController.cs's own remarks on
// why none of InstinctClashConfig's 8 Characters need them).
const BASE_URL = "/api/v2/games";

function seatHeader(path: string): Record<string, string> {
  const gameId = /^\/([^/]+)/.exec(path)?.[1];
  const token = gameId ? tokenFor(gameId) : null;
  return token ? { "X-Seat-Token": token } : {};
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...seatHeader(path),
      ...(options?.headers ?? {}),
    },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? `Request failed: ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export const api = {
  getChampions: () => request<Champion[]>("/champions"),
  getCards: () => request<CardDef[]>("/cards"),

  createGame: (playerOneChampionId: string, playerTwoChampionId: string) =>
    request<CreatedGame>("", {
      method: "POST",
      body: JSON.stringify({ playerOneChampionId, playerTwoChampionId }),
    }),
  getGame: (id: string) => request<GameState>(`/${id}`),

  clearAndDraw: (id: string) => request<GameState>(`/${id}/clear-and-draw`, { method: "POST" }),
  roll: (id: string) => request<GameState>(`/${id}/roll`, { method: "POST" }),
  reroll: (id: string, dieIds: string[]) =>
    request<GameState>(`/${id}/reroll`, { method: "POST", body: JSON.stringify({ dieIds }) }),
  finishRoll: (id: string) => request<GameState>(`/${id}/finish-roll`, { method: "POST" }),

  purchase: (id: string, dieId: string, energyDieIds: string[]) =>
    request<GameState>(`/${id}/purchase`, { method: "POST", body: JSON.stringify({ dieId, energyDieIds }) }),
  field: (id: string, dieId: string, energyDieIds: string[]) =>
    request<GameState>(`/${id}/field`, { method: "POST", body: JSON.stringify({ dieId, energyDieIds }) }),

  enterAttackStep: (id: string) => request<GameState>(`/${id}/enter-attack-step`, { method: "POST" }),
  skipAttackStep: (id: string) => request<GameState>(`/${id}/skip-attack-step`, { method: "POST" }),
  declareAttackers: (id: string, attackerDieIds: string[]) =>
    request<GameState>(`/${id}/declare-attackers`, { method: "POST", body: JSON.stringify({ attackerDieIds }) }),
  declareBlockers: (id: string, assignments: BlockAssignment[]) =>
    request<GameState>(`/${id}/declare-blockers`, { method: "POST", body: JSON.stringify({ assignments }) }),
  assignCombatDamage: (id: string, assignments: BlockAssignment[]) =>
    request<GameState>(`/${id}/assign-combat-damage`, { method: "POST", body: JSON.stringify({ assignments }) }),
  cleanUp: (id: string) => request<GameState>(`/${id}/clean-up`, { method: "POST" }),

  resolvePendingChoice: (id: string, chosenDieIds: string[]) =>
    request<GameState>(`/${id}/resolve-pending-choice`, {
      method: "POST",
      body: JSON.stringify({ chosenDieIds }),
    }),
};
