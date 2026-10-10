import { seatsFor, tokenFor } from "./seats";
import { apiErrorMessage } from "./apiErrors";
import type { CustomTeam } from "./customTeams";
import type { BlockAssignment, BotDecision, CardDef, Champion, CreatedGame, GameState, LobbyStatus, OpenedGame } from "./types";

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

// `auto`: a move the client made on the player's behalf (nothing to block,
// nothing left to do in the attack window) - the game record marks it, so
// it isn't read as a decision (src/DiceFight.Api/Recording).
async function request<T>(path: string, options?: RequestInit, tokenOverride?: string | null, auto = false): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...seatHeader(path, tokenOverride),
      ...(auto ? { "X-Auto-Move": "1" } : {}),
      ...(options?.headers ?? {}),
    },
  });
  if (!res.ok) {
    const body: unknown = await res.json().catch(() => ({ title: res.statusText }));
    throw new Error(apiErrorMessage(res.status, body));
  }
  return res.json() as Promise<T>;
}

function makeClient(tokenOverride?: string | null, auto = false) {
  return {
    getChampions: () => request<Champion[]>("/champions", undefined, tokenOverride, auto),
    getCards: () => request<CardDef[]>("/cards", undefined, tokenOverride, auto),

    createGame: (playerOneChampionId: string, playerTwoChampionId: string) =>
      request<CreatedGame>(
        "",
        { method: "POST", body: JSON.stringify({ playerOneChampionId, playerTwoChampionId }) },
        tokenOverride,
        auto,
      ),
    createCustomGame: (playerOneCustomTeam: CustomTeam, playerTwoChampionId: string) =>
      request<CreatedGame>("", {
        method: "POST", body: JSON.stringify({ playerOneCustomTeam, playerTwoChampionId }),
      }, tokenOverride, auto),
    getGame: (id: string) => request<GameState>(`/${id}`, undefined, tokenOverride, auto),

    // Online game with only your own Champion picked - the invited player
    // picks theirs from the invite link (lobby.tsx).
    openGame: (championId: string) =>
      request<OpenedGame>("/open", { method: "POST", body: JSON.stringify({ championId }) }, tokenOverride, auto),
    getLobby: (id: string) => request<LobbyStatus>(`/${id}/lobby`, undefined, tokenOverride, auto),
    joinGame: (id: string, championId: string) =>
      request<GameState>(`/${id}/join`, { method: "POST", body: JSON.stringify({ championId }) }, tokenOverride, auto),

    clearAndDraw: (id: string) => request<GameState>(`/${id}/clear-and-draw`, { method: "POST" }, tokenOverride, auto),
    roll: (id: string) => request<GameState>(`/${id}/roll`, { method: "POST" }, tokenOverride, auto),
    reroll: (id: string, dieIds: string[]) =>
      request<GameState>(`/${id}/reroll`, { method: "POST", body: JSON.stringify({ dieIds }) }, tokenOverride, auto),
    finishRoll: (id: string) => request<GameState>(`/${id}/finish-roll`, { method: "POST" }, tokenOverride, auto),

    purchase: (id: string, dieId: string, energyDieIds: string[]) =>
      request<GameState>(`/${id}/purchase`, { method: "POST", body: JSON.stringify({ dieId, energyDieIds }) }, tokenOverride, auto),
    field: (id: string, dieId: string, energyDieIds: string[], free = false) =>
      request<GameState>(`/${id}/field`, { method: "POST", body: JSON.stringify({ dieId, energyDieIds, free }) }, tokenOverride, auto),

    enterAttackStep: (id: string) => request<GameState>(`/${id}/enter-attack-step`, { method: "POST" }, tokenOverride, auto),
    skipAttackStep: (id: string) => request<GameState>(`/${id}/skip-attack-step`, { method: "POST" }, tokenOverride, auto),
    // attackers: which lane (0-3) each declared attacker is placed into -
    // see DieInstance.Lane's own remarks. A lane is a display grouping
    // only; blocking below is still assigned per individual attacker.
    declareAttackers: (id: string, attackers: { dieId: string; lane: number }[]) =>
      request<GameState>(`/${id}/declare-attackers`, { method: "POST", body: JSON.stringify({ attackers }) }, tokenOverride, auto),
    declareBlockers: (id: string, assignments: BlockAssignment[]) =>
      request<GameState>(`/${id}/declare-blockers`, { method: "POST", body: JSON.stringify({ assignments }) }, tokenOverride, auto),
    assignCombatDamage: (id: string, assignments: BlockAssignment[]) =>
      request<GameState>(`/${id}/assign-combat-damage`, { method: "POST", body: JSON.stringify({ assignments }) }, tokenOverride, auto),
    // Pass priority - the Inactive player's "no Global" (Priority.cs).
    pass: (id: string) => request<GameState>(`/${id}/pass`, { method: "POST" }, tokenOverride, auto),
    // Great Horned Owl: reroll one Reserve Pool die, once per turn.
    championPower: (id: string) => request<GameState>(`/${id}/champion-power`, { method: "POST" }, tokenOverride, auto),
    foresight: (id: string, dieId: string) =>
      request<GameState>(`/${id}/foresight`, { method: "POST", body: JSON.stringify({ dieId }) }, tokenOverride, auto),
    useAction: (id: string, dieId: string) =>
      request<GameState>(`/${id}/use-action`, { method: "POST", body: JSON.stringify({ dieId }) }, tokenOverride, auto),
    useGlobal: (id: string, cardId: string, abilityIndex: number, energyDieIds: string[]) =>
      request<GameState>(
        `/${id}/use-global`,
        { method: "POST", body: JSON.stringify({ cardId, abilityIndex, energyDieIds }) },
        tokenOverride,
        auto,
      ),
    cleanUp: (id: string) => request<GameState>(`/${id}/clean-up`, { method: "POST" }, tokenOverride, auto),

    // The computer opponent's next move for this seat, or null when it
    // isn't this seat's decision (204). `skip`: dice already rejected
    // this turn - see V2GamesController.BotDecision.
    botDecision: async (id: string, skip: Iterable<string>): Promise<BotDecision | null> => {
      const query = [...skip].length ? `?skip=${encodeURIComponent([...skip].join(","))}` : "";
      const res = await fetch(`${BASE_URL}/${id}/bot-decision${query}`, {
        headers: seatHeader(`/${id}`, tokenOverride),
      });
      if (res.status === 204) return null;
      if (!res.ok) {
        const body = await res.json().catch(() => ({ error: res.statusText }));
        throw new Error(body.error ?? `Request failed: ${res.status}`);
      }
      return res.json() as Promise<BotDecision>;
    },

    resolvePendingChoice: (id: string, chosenDieIds: string[]) =>
      request<GameState>(
        `/${id}/resolve-pending-choice`,
        { method: "POST", body: JSON.stringify({ chosenDieIds }) },
        tokenOverride,
        auto,
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
export function apiAs(gameId: string, playerId: string, auto = false): ReturnType<typeof makeClient> {
  return makeClient(seatsFor(gameId).find((s) => s.playerId === playerId)?.token ?? null, auto);
}
