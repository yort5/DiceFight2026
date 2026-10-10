import { rolled } from "./bot";
import type { Die, GameState } from "./types";

// Only changes to the die's physical face warrant a spin. Effective
// Attack/Defense, fielding costs, and statuses can change without its
// printed face changing (e.g. buffs and Globals).
export function physicalFaceChanged(before: Die, after: Die): boolean {
  return before.level !== after.level ||
    before.energySymbolId !== after.energySymbolId ||
    before.energyAmount !== after.energyAmount ||
    before.isActionFace !== after.isActionFace;
}

// A remote roll can be seen after more than one backend action has happened
// between polls. In particular, Clear & Draw -> Roll may complete before
// the observing client ever sees Roll & Reroll as its previous step.
// Only dice that really arrived in Reserve from the roll staging zones,
// or whose faces changed during a confirmed reroll, should tumble.
export function remoteRolledIds(previous: GameState, next: GameState): string[] {
  const before = new Map(previous.dice.map((d) => [d.id, d]));
  const previousSeq = previous.log?.reduce((n, e) => Math.max(n, e.seq), 0) ?? 0;
  const reportedRollers = new Set(
    (next.log ?? [])
      .filter((e) => e.seq > previousSeq && e.playerId && /\b(?:rolls\.|rerolls?\b)/i.test(e.text))
      .map((e) => e.playerId),
  );
  const wasRolling = previous.currentStepId === "roll-and-reroll";
  return next.dice.filter((die) => {
    const was = before.get(die.id);
    if (!was || die.zone !== "ReservePool" || !rolled(die)) return false;
    // Roll() moves newly rolled dice from these two zones into Reserve.
    // This remains reliable even if the polling client missed the step.
    if (was.zone === "DiceFromBag" || was.zone === "DiceFromPrep") return true;
    // A poll can also miss Clear & Draw: use the authoritative new roll
    // log in that case rather than interpreting ordinary actions as rolls.
    if (was.zone !== "ReservePool") {
      return reportedRollers.has(die.controllerId);
    }
    // RerollOwn() keeps dice in Reserve. Don't animate stat-only changes
    // or arbitrary Main-phase effects as randomized rolls.
    return wasRolling && die.controllerId === previous.activePlayerId && physicalFaceChanged(was, die);
  }).map((d) => d.id);
}

export function classifyDieMotion(
  previous: GameState,
  next: GameState,
  rolledDieIds: readonly string[] = [],
): { tumbles: string[]; flips: string[] } {
  const before = new Map(previous.dice.map((d) => [d.id, d]));
  const explicit = new Set(rolledDieIds);
  const tumbles: string[] = [];
  const flips: string[] = [];
  for (const die of next.dice) {
    const was = before.get(die.id);
    if (!was || !rolled(die)) continue;
    if (explicit.has(die.id)) {
      tumbles.push(die.id);
    } else if (was.zone === die.zone && physicalFaceChanged(was, die)) {
      // Do not animate ordinary moves between zones or stat-only updates.
      flips.push(die.id);
    }
  }
  return { tumbles, flips };
}
