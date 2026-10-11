import { api } from "./api";
import { globalCardsInGame, pickEnergy, rolled } from "./bot";
import type { CardDef, Die, GameState, GlobalAbility } from "./types";

// A single authoritative frontend model for optional card/Champion abilities.
// The C# engine remains the final authority for legality and resolution.
export type AbilityCommand =
  | { kind: "global"; cardId: string; abilityIndex: number; energyDieIds: string[] }
  | { kind: "action"; dieId: string }
  | { kind: "foresight"; dieId: string };

export interface GlobalOption {
  card: CardDef;
  global: GlobalAbility;
  blocked: string | null;
  command: AbilityCommand | null;
}
export interface ActionDieOption {
  die: Die;
  blocked: string | null;
  command: AbilityCommand | null;
}
export interface AbilityOptions {
  globals: GlobalOption[];
  actionDice: ActionDieOption[];
  foresightDice: ActionDieOption[];
  foresightReady: boolean;
  actionsBlocked: string | null;
}

export function getAbilityOptions(
  game: GameState, cardsById: Map<string, CardDef>, viewerId: string, busy: boolean,
): AbilityOptions {
  const isYourTurn = game.activePlayerId === viewerId;
  const player = game.playerOne.id === viewerId ? game.playerOne : game.playerTwo;
  const reserve = game.dice.filter((d) => d.controllerId === viewerId && d.zone === "ReservePool");
  const havePriority = game.priorityPlayerId === viewerId;
  const actionWindow = game.currentStepId === "main" || game.currentStepId === "action-global-window";
  const blockedByChoice = game.pendingChoice ? "Finish the current choice first" : null;
  const actionsBlocked = busy ? "…" : blockedByChoice ?? (
    !isYourTurn ? "Action dice are only used on your own turn"
      : !actionWindow ? "Action dice may only be used in Main or the Attack action window"
      : havePriority ? null
      : game.priorityPlayerId ? "Your opponent has priority" : "Main Step or the attack window only"
  );
  const globalsTiming = busy ? "…" : blockedByChoice ?? (
    havePriority ? null : game.priorityPlayerId
      ? isYourTurn ? "Your opponent has priority" : "You'll get priority when they pass"
      : "Main Step or the attack window only"
  );
  const virtualEnergy = player.virtualEnergy ?? 0;
  const globals = globalCardsInGame(game, cardsById).map((card): GlobalOption => {
    const global = card.global!;
    const ids = pickEnergy(reserve, global.cost, global.energyType, virtualEnergy);
    const blocked = globalsTiming ?? (ids === null
      ? `Needs ${global.cost} ${global.energyType ?? "energy"} in your Reserve`
      : null);
    return {
      card, global, blocked,
      command: blocked === null && ids !== null
        ? { kind: "global", cardId: card.id, abilityIndex: global.abilityIndex, energyDieIds: ids }
        : null,
    };
  });
  const actionDice = reserve.filter((d) => d.isActionFace).map((die): ActionDieOption => ({
    die, blocked: actionsBlocked,
    command: actionsBlocked === null ? { kind: "action", dieId: die.id } : null,
  }));
  const foresightReady = !!player.foresightAvailable && isYourTurn &&
    game.currentStepId === "main" && havePriority && !game.pendingChoice;
  const foresightDice = foresightReady ? reserve.filter((d) => rolled(d) || d.isActionFace).map((die): ActionDieOption => ({
    die, blocked: busy ? "…" : null,
    command: busy ? null : { kind: "foresight", dieId: die.id },
  })) : [];
  return { globals, actionDice, foresightDice, foresightReady, actionsBlocked };
}

// Community Basic Action dice can be purchased by either player even when
// the physical unpurchased dice belong to the other Champion's pool.
export function purchasableDiceFor(game: GameState, cardsById: Map<string, CardDef>, viewerId: string): Die[] {
  return game.dice.filter((d) => d.zone === "Unpurchased" &&
    (d.controllerId === viewerId || (!!d.cardId && !!cardsById.get(d.cardId)?.isAction)));
}

export function basicActionStock(game: GameState, cardsById: Map<string, CardDef>): Array<{ card: CardDef; dice: Die[] }> {
  const grouped = new Map<string, Die[]>();
  for (const die of game.dice) {
    if (!die.cardId || !cardsById.get(die.cardId)?.isAction) continue;
    if (!grouped.has(die.cardId)) grouped.set(die.cardId, []);
    if (die.zone === "Unpurchased") grouped.get(die.cardId)!.push(die);
  }
  return [...grouped].map(([id, dice]) => ({ card: cardsById.get(id)!, dice }));
}

export function getPurchasePayment(
  game: GameState, card: CardDef, viewerId: string,
): string[] | null {
  const player = game.playerOne.id === viewerId ? game.playerOne : game.playerTwo;
  const reserve = game.dice.filter((d) => d.controllerId === viewerId && d.zone === "ReservePool");
  const cost = game.purchaseCosts?.[card.id] ?? card.purchaseCost;
  return pickEnergy(reserve, cost, card.energyTypes[0] ?? null, player.virtualEnergy ?? 0);
}

// Both layouts invoke the same action dispatcher. Do not duplicate API calls
// or recompute payments in click handlers.
export function executeAbility(gameId: string, command: AbilityCommand) {
  switch (command.kind) {
    case "global": return api.useGlobal(gameId, command.cardId, command.abilityIndex, command.energyDieIds);
    case "action": return api.useAction(gameId, command.dieId);
    case "foresight": return api.foresight(gameId, command.dieId);
  }
}
