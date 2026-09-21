// A basic rule-based "computer" opponent for Dice Kingdom - lets someone
// play solo instead of needing a second person for pass-and-play. Not
// meant to play well: no lookahead, no combo awareness, no card-specific
// strategy. Each function below answers one narrow question ("what should
// I field/purchase right now", "who should block whom") using only
// what's visible on GameState - the same information a human player sees.
// DiceKingdomPage.tsx is the only caller; it drives the actual turn
// (calling the api and updating React state) using these as pure
// decision functions, same separation as its own selectionAction().
import type { BlockAssignment, CardDef, Die, GameState } from "./types";

// Same test ../DiceKingdomPage.tsx's own `rolled()` uses - moved here so
// both it and this module share one definition rather than two copies
// drifting apart.
export function rolled(d: Die): boolean {
  return d.effectiveAttack !== null || d.energySymbolId !== null;
}

function controlledBy(game: GameState, playerId: string, zone?: string): Die[] {
  return game.dice.filter((d) => d.controllerId === playerId && (!zone || d.zone === zone));
}

function ownedBy(game: GameState, playerId: string, zone?: string): Die[] {
  return game.dice.filter((d) => d.ownerId === playerId && (!zone || d.zone === zone));
}

// Whoever has to act next, or null if the current step runs on its own
// (an engine procedure with nothing for either player to decide). Mirrors
// the same precedence DiceKingdomPage's own `stepContent` ternary chain
// uses: a pending choice outranks the step, and Assign Blockers is the
// one step the INACTIVE player answers rather than the active one.
export function decisionOwner(game: GameState): string | null {
  if (game.pendingChoice) return game.pendingChoice.controllerId;
  if (game.currentStepId === "assign-blockers") {
    return game.activePlayerId === game.playerOne.id ? game.playerTwo.id : game.playerOne.id;
  }
  const decisionSteps = new Set([
    "start-of-turn",
    "roll-and-reroll",
    "main",
    "select-attackers",
    "action-global-window",
    "return-to-field",
  ]);
  return decisionSteps.has(game.currentStepId) ? game.activePlayerId : null;
}

function fieldingCost(die: Die, cardsById: Map<string, CardDef>): number {
  if (!die.cardId || die.level === null) return 0; // Tardigrade - free, matches costFor()
  return cardsById.get(die.cardId)?.levels[die.level - 1]?.fieldingCost ?? 0;
}

// Which reserve energy dice to spend on `cost`, with at least one pip matching
// `matchType` (or Wild) when the card has a type requirement - same rule as
// TurnEngine.SpendEnergy. Null if it can't be paid.
//
// The engine spends dice in the order offered and stops once the cost is met;
// only the LAST die can be overspent, and then it spins down to its own
// lower face (TurnEngine.TrySpinDown). What that leaves behind differs:
//   - Tardigrade double-energy -> its single-energy face, which still has
//     stats (1A/1D): best.
//   - Character double-energy -> a bare single-energy face, no stats: ok.
//   - anything else -> the leftover pip is simply lost: worst.
// So this tries every subset/last-die choice that exactly covers the cost
// and keeps the one whose leftover is most useful (then fewest dice).
export function pickEnergy(pool: Die[], cost: number, matchType: string | null): string[] | null {
  if (cost <= 0) return [];
  const dice = pool.filter((d) => d.energyAmount > 0).sort((a, b) => a.energyAmount - b.energyAmount);
  const matches = (d: Die) => !matchType || d.energySymbolId === matchType || d.energySymbolId === "Wild";
  if (dice.length > 14) return pickEnergyGreedy(dice, cost, matchType);

  let best: { ids: string[]; score: number; count: number } | null = null;
  for (let mask = 1; mask < 1 << dice.length; mask++) {
    const members = dice.filter((_, i) => mask & (1 << i));
    const sum = members.reduce((n, d) => n + d.energyAmount, 0);
    if (sum < cost || !members.some(matches)) continue;
    for (const last of members) {
      if (sum - last.energyAmount >= cost) continue; // engine would have stopped before `last`
      const overspend = sum - cost;
      let score = 4; // exact payment, nothing left over to protect
      if (overspend > 0) {
        const leftover = overspend; // pips still showing on the spun-down die
        score = last.isTardigrade && last.energyAmount === 2 && leftover === 1 ? 3
          : !last.isTardigrade && last.energyAmount === 2 && leftover === 1 ? 2
          : 0;
      }
      const count = members.length;
      if (!best || score > best.score || (score === best.score && count < best.count)) {
        best = { ids: [...members.filter((d) => d !== last), last].map((d) => d.id), score, count };
      }
    }
  }
  return best?.ids ?? null;
}

function pickEnergyGreedy(dice: Die[], cost: number, matchType: string | null): string[] | null {
  let rest = [...dice];
  const picked: string[] = [];
  let total = 0;
  if (matchType) {
    const idx = rest.findIndex((d) => d.energySymbolId === matchType || d.energySymbolId === "Wild");
    if (idx === -1) return null;
    picked.push(rest[idx].id);
    total += rest[idx].energyAmount;
    rest = rest.filter((_, i) => i !== idx);
  }
  for (const d of rest) {
    if (total >= cost) break;
    picked.push(d.id);
    total += d.energyAmount;
  }
  return total >= cost ? picked : null;
}

export type MainDecision =
  | { kind: "field"; dieId: string; energyDieIds: string[] }
  | { kind: "purchase"; dieId: string; energyDieIds: string[] }
  | { kind: "enterAttackStep" };

// Field the best available character if affordable, else buy the most
// expensive affordable card, else move on. "Best"/"most expensive" is a
// stand-in for real card evaluation - fine for a basic opponent, not
// meant to reflect actual card power. `skipIds` lets the caller rule out
// a candidate that the server already rejected once this turn (a
// legality rule this module doesn't model, e.g. a lockout ability) so
// the bot doesn't retry it forever.
export function decideMainAction(
  game: GameState,
  botId: string,
  cardsById: Map<string, CardDef>,
  skipIds: ReadonlySet<string>,
): MainDecision {
  const energyPool = controlledBy(game, botId, "ReservePool").filter((d) => d.energyAmount > 0);

  // Purchases first, about two turns in three when one is affordable -
  // previously fielding always ran first and spent the energy, so the bot
  // never bought anything. Random per decision, so it still fields
  // sometimes too. Cheaper-than-best candidates are fine: sorted by cost,
  // most expensive affordable wins.
  const purchaseCandidates = ownedBy(game, botId, "Unpurchased")
    .filter((d) => d.cardId && !skipIds.has(d.id))
    .map((d) => ({ die: d, card: cardsById.get(d.cardId!) }))
    .filter((x): x is { die: Die; card: CardDef } => !!x.card)
    .sort((a, b) => b.card.purchaseCost - a.card.purchaseCost);
  if (Math.random() < 0.67) {
    for (const { die, card } of purchaseCandidates) {
      const pay = pickEnergy(energyPool, card.purchaseCost, card.energyTypes[0] ?? null);
      if (pay) return { kind: "purchase", dieId: die.id, energyDieIds: pay };
    }
  }

  const fieldCandidates = controlledBy(game, botId, "ReservePool")
    .filter((d) => rolled(d) && d.effectiveAttack !== null && !skipIds.has(d.id))
    .sort((a, b) => (b.effectiveAttack! + (b.effectiveDefense ?? 0)) - (a.effectiveAttack! + (a.effectiveDefense ?? 0)));
  for (const die of fieldCandidates) {
    const pay = pickEnergy(energyPool.filter((d) => d.id !== die.id), fieldingCost(die, cardsById), null);
    if (pay) return { kind: "field", dieId: die.id, energyDieIds: pay };
  }

  // Nothing fielded and the coin flip skipped buying - still buy if possible.
  for (const { die, card } of purchaseCandidates) {
    const pay = pickEnergy(energyPool, card.purchaseCost, card.energyTypes[0] ?? null);
    if (pay) return { kind: "purchase", dieId: die.id, energyDieIds: pay };
  }

  return { kind: "enterAttackStep" };
}

// Attacks with every fielded character showing 2 or more Attack - direct
// feedback (2026-09-21): a 1A die swinging in is just a free kill for the
// defender's blocker. There's no defensive cost to attacking otherwise (it
// returns to the Field Zone at Clean Up either way and blocking
// eligibility doesn't depend on having attacked).
export const BOT_MIN_ATTACK = 2;
export function decideAttackers(game: GameState, botId: string): { dieId: string; lane: number }[] {
  const attackers = controlledBy(game, botId, "FieldZone").filter((d) => rolled(d) && (d.effectiveAttack ?? 0) >= BOT_MIN_ATTACK);
  return attackers.map((d, i) => ({ dieId: d.id, lane: i % 4 }));
}

// Greedily pairs the biggest attackers against the best available
// blocker, skipping a block entirely when nothing on the field would
// either kill the attacker or survive it - a pure numbers trade, no
// keyword/ability awareness (Unblockable, must-block, etc.); an illegal
// pairing here is expected to be caught and retried empty by the caller.
export function decideBlockers(game: GameState, botId: string): BlockAssignment[] {
  const attackers = game.dice
    .filter((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId)
    .sort((a, b) => (b.effectiveAttack ?? 0) - (a.effectiveAttack ?? 0));
  const available = new Map(
    controlledBy(game, botId, "FieldZone")
      .filter((d) => rolled(d) && d.effectiveAttack !== null)
      .map((d) => [d.id, d] as const),
  );

  const assignments: BlockAssignment[] = [];
  for (const attacker of attackers) {
    let best: Die | null = null;
    let bestScore = 0;
    for (const blocker of available.values()) {
      const kills = (blocker.effectiveAttack ?? 0) >= (attacker.effectiveDefense ?? 0);
      const survives = (blocker.effectiveDefense ?? 0) > (attacker.effectiveAttack ?? 0);
      // A blocker that neither kills nor survives still stops the attacker's
      // damage reaching the player (and a KO'd die just goes to Prep), so a
      // chump block beats taking it in the face: 0.5 baseline.
      const score = (kills ? 2 : 0) + (survives ? 1 : 0) || ((attacker.effectiveAttack ?? 0) > 0 ? 0.5 : 0);
      if (score > bestScore) {
        bestScore = score;
        best = blocker;
      }
    }
    if (best) {
      assignments.push({ attackerDieId: attacker.id, blockerDieId: best.id });
      available.delete(best.id);
    }
  }
  return assignments;
}

// A pending choice's content (what it's even choosing between) isn't
// modeled here - just satisfies the minimum count asked for, in whatever
// order the server offered candidates. Good enough for the choices a
// basic opponent will actually hit in this catalog; not a stand-in for
// understanding what the choice does.
export function decidePendingChoice(game: GameState): string[] {
  const choice = game.pendingChoice;
  if (!choice) return [];
  return choice.candidateIds.slice(0, Math.max(choice.minCount, 0));
}
