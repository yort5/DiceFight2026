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
// Only dice that really arrived in Reserve, or whose faces changed during
// a reroll, should tumble.
export function remoteRolledIds(previous: GameState, next: GameState): string[] {
  const before = new Map(previous.dice.map((d) => [d.id, d]));
  const wasRolling = previous.currentStepId === "roll-and-reroll";
  return next.dice.filter((die) => {
    const was = before.get(die.id);
    if (!was || die.zone !== "ReservePool" || !rolled(die)) return false;
    // Every way into the Reserve Pool rolls the die: the turn's Roll (from
    // DiceFromBag/DiceFromPrep), and abilities that draw or move a die there
    // (EffectInterpreter's DrawToZone/MoveDie roll on arrival). So arriving
    // from any other zone is a roll - even when the poll missed Clear & Draw
    // and Roll in between - without reading the log's wording.
    if (was.zone !== "ReservePool") return true;
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
