import { rememberSeats, tokenFor, type Seat } from "./seats";
import type { BlockAssignment, CardDef, DamageSplit, GameState, RangeAssignment, TagOutUse } from "./types";

// Relative on purpose: in production the API and built app share one
// origin (combined container), and in dev the Vite proxy (vite.config.ts)
// forwards this to the API dev server - no hardcoded host/CORS needed.
const BASE_URL = "/api";

// Every game action carries the seat token for that game - the server
// uses it to decide not just whether the move is legal but whether it is
// YOURS to make (see GamesController's Actor rules).
function seatHeader(path: string): Record<string, string> {
  const gameId = /^\/games\/([^/]+)/.exec(path)?.[1];
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
  getCards: () => request<CardDef[]>("/cards"),
  // Creating a game is the one call that hands back seat tokens; they
  // are stored here so nothing else has to think about them.
  createGame: async (teamCardIds?: string[]) => {
    const created = await request<{ game: GameState; seats: Seat[] }>("/games", {
      method: "POST",
      body: JSON.stringify({ teamCardIds: teamCardIds && teamCardIds.length > 0 ? teamCardIds : null }),
    });
    rememberSeats(created.game.gameId, created.seats);
    return created.game;
  },
  getGame: (id: string) => request<GameState>(`/games/${id}`),

  advanceStep: (id: string) => request<GameState>(`/games/${id}/advance-step`, { method: "POST" }),
  clearAndDraw: (id: string) => request<GameState>(`/games/${id}/clear-and-draw`, { method: "POST" }),
  roll: (id: string) => request<GameState>(`/games/${id}/roll`, { method: "POST" }),
  reroll: (id: string, rerollDieIds: string[]) =>
    request<GameState>(`/games/${id}/reroll`, {
      method: "POST",
      body: JSON.stringify({ rerollDieIds }),
    }),

  purchase: (id: string, dieId: string, energyDieIds: string[]) =>
    request<GameState>(`/games/${id}/purchase`, {
      method: "POST",
      body: JSON.stringify({ dieId, energyDieIds }),
    }),
  field: (id: string, dieId: string, energyDieIds: string[], targetDieIds: string[] = []) =>
    request<GameState>(`/games/${id}/field`, {
      method: "POST",
      body: JSON.stringify({ dieId, energyDieIds, targetDieIds }),
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
  declareAttackers: (id: string, attackerDieIds: string[], targetDieIds: string[] = []) =>
    request<GameState>(`/games/${id}/declare-attackers`, {
      method: "POST",
      body: JSON.stringify({ attackerDieIds, targetDieIds }),
    }),
  declareBlockers: (id: string, assignments: BlockAssignment[]) =>
    request<GameState>(`/games/${id}/declare-blockers`, {
      method: "POST",
      body: JSON.stringify({ assignments }),
    }),
  resolveInfiltrate: (id: string, assignments: BlockAssignment[], infiltratingDieIds: string[]) =>
    request<GameState>(`/games/${id}/resolve-infiltrate`, {
      method: "POST",
      body: JSON.stringify({ assignments, infiltratingDieIds }),
    }),
  resolveTagOut: (id: string, uses: TagOutUse[]) =>
    request<GameState>(`/games/${id}/resolve-tag-out`, {
      method: "POST",
      body: JSON.stringify({ uses }),
    }),
  // One side's assignments. Range is simultaneous, so the server collects
  // the active player's first and resolves both together when the
  // opponent answers - see GamesController.SubmitRange.
  submitRange: (id: string, assignments: RangeAssignment[]) =>
    request<GameState>(`/games/${id}/submit-range`, {
      method: "POST",
      body: JSON.stringify({ assignments }),
    }),
  assignCombatDamage: (id: string, assignments: BlockAssignment[], damageSplits: DamageSplit[]) =>
    request<GameState>(`/games/${id}/assign-combat-damage`, {
      method: "POST",
      body: JSON.stringify({ assignments, damageSplits }),
    }),

  cleanUp: (id: string) => request<GameState>(`/games/${id}/clean-up`, { method: "POST" }),

  resolvePendingChoice: (id: string, chosenDieIds: string[]) =>
    request<GameState>(`/games/${id}/resolve-pending-choice`, {
      method: "POST",
      body: JSON.stringify({ chosenDieIds }),
    }),
};
