import type { CardDef, GameState } from "./types";

// Relative on purpose: in production the API and built app share one
// origin (combined container), and in dev the Vite proxy (vite.config.ts)
// forwards this to the API dev server - no hardcoded host/CORS needed.
const BASE_URL = "/api";

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options?.headers ?? {}) },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? `Request failed: ${res.status}`);
  }
  return res.json() as Promise<T>;
}

interface BlockAssignment {
  attackerDieId: string;
  blockerDieId: string;
}

interface DamageSplit extends BlockAssignment {
  amount: number;
}

export const api = {
  getCards: () => request<CardDef[]>("/cards"),
  createGame: () => request<GameState>("/games", { method: "POST" }),
  getGame: (id: string) => request<GameState>(`/games/${id}`),

  advanceStep: (id: string) => request<GameState>(`/games/${id}/advance-step`, { method: "POST" }),
  clearAndDraw: (id: string) => request<GameState>(`/games/${id}/clear-and-draw`, { method: "POST" }),
  roll: (id: string) => request<GameState>(`/games/${id}/roll`, { method: "POST" }),
  finishRoll: (id: string, rerollDieIds: string[]) =>
    request<GameState>(`/games/${id}/finish-roll`, {
      method: "POST",
      body: JSON.stringify({ rerollDieIds }),
    }),

  purchase: (id: string, dieId: string, energyDieIds: string[]) =>
    request<GameState>(`/games/${id}/purchase`, {
      method: "POST",
      body: JSON.stringify({ dieId, energyDieIds }),
    }),
  field: (id: string, dieId: string, energyDieIds: string[]) =>
    request<GameState>(`/games/${id}/field`, {
      method: "POST",
      body: JSON.stringify({ dieId, energyDieIds }),
    }),
  useActionDie: (id: string, dieId: string, targetDieIds: string[]) =>
    request<GameState>(`/games/${id}/use-action-die`, {
      method: "POST",
      body: JSON.stringify({ dieId, targetDieIds }),
    }),
  useGlobalAbility: (
    id: string,
    cardId: string,
    playerId: string,
    energyDieIds: string[],
    targetDieIds: string[],
  ) =>
    request<GameState>(`/games/${id}/use-global-ability`, {
      method: "POST",
      body: JSON.stringify({ cardId, playerId, energyDieIds, targetDieIds }),
    }),

  enterAttackStep: (id: string) => request<GameState>(`/games/${id}/enter-attack-step`, { method: "POST" }),
  skipAttackStep: (id: string) => request<GameState>(`/games/${id}/skip-attack-step`, { method: "POST" }),
  declareAttackers: (id: string, attackerDieIds: string[]) =>
    request<GameState>(`/games/${id}/declare-attackers`, {
      method: "POST",
      body: JSON.stringify({ attackerDieIds }),
    }),
  declareBlockers: (id: string, assignments: BlockAssignment[]) =>
    request<GameState>(`/games/${id}/declare-blockers`, {
      method: "POST",
      body: JSON.stringify({ assignments }),
    }),
  assignCombatDamage: (id: string, assignments: BlockAssignment[], damageSplits: DamageSplit[]) =>
    request<GameState>(`/games/${id}/assign-combat-damage`, {
      method: "POST",
      body: JSON.stringify({ assignments, damageSplits }),
    }),

  cleanUp: (id: string) => request<GameState>(`/games/${id}/clean-up`, { method: "POST" }),
};
