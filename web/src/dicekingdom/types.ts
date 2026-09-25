// Mirrors src/DiceFight.Api/V2Dtos.cs field-for-field (camelCase - ASP.NET
// Core's default JSON naming policy, same convention ../types.ts already
// relies on for v1). A separate module from ../types.ts on purpose: v2's
// shapes are genuinely smaller (no AttackSubStep/Range/BurstStars/
// VirtualEnergy - see V2Dtos.cs's own remarks), not a subset that could
// share one interface.

export interface CharacterFace {
  fieldingCost: number;
  attack: number;
  defense: number;
}

export interface CardDef {
  id: string;
  name: string;
  subtitle: string | null;
  purchaseCost: number;
  energyTypes: string[];
  dieLimit: number;
  levels: CharacterFace[];
  rawText: string;
  keywords: string[];
}

export interface Champion {
  id: string;
  name: string;
  energySymbolId: string;
  passiveText: string;
}

export interface StatModifier {
  label: string;
  delta: number;
}

export interface Die {
  id: string;
  cardId: string | null;
  ownerId: string;
  controllerId: string;
  zone: string;
  isTardigrade: boolean;
  level: number | null;
  effectiveAttack: number | null;
  effectiveDefense: number | null;
  energySymbolId: string | null;
  energyAmount: number;
  /** Which of the Attack Zone's four fixed lanes (0-3) this attacker was
   *  declared into - a display grouping only, null outside AttackZone. */
  lane: number | null;
  /** Printed base + named modifiers behind effectiveAttack/effectiveDefense -
   *  only present for an in-play (FieldZone/AttackZone) character die, so
   *  a "why is this number X" breakdown can be built without re-deriving
   *  champion/aura math client-side. See V2Dtos.cs's V2DieDto remarks. */
  baseAttack: number | null;
  baseDefense: number | null;
  attackModifiers: StatModifier[] | null;
  defenseModifiers: StatModifier[] | null;
  /** Damage already marked on this die (see V2DieDto.Damage). */
  damage?: number;
  /** Declaration order among attackers - a lane stacks (and takes combat
   *  damage) in this order. Null outside the Attack Zone. */
  attackOrder?: number | null;
}

export interface PlayerState {
  id: string;
  name: string;
  life: number;
  champion: Champion | null;
}

export interface PendingChoice {
  controllerId: string;
  description: string;
  candidateIds: string[];
  minCount: number;
  maxCount: number;
}

export interface GameLogEntry {
  seq: number;
  /** Who did it, or null for something the game itself did. */
  playerId: string | null;
  text: string;
  /** True for the marker line that opens each turn - drawn as a divider. */
  isTurnStart: boolean;
}

export interface GameState {
  gameId: string;
  activePlayerId: string;
  currentStep: string;
  currentStepId: string;
  playerOne: PlayerState;
  playerTwo: PlayerState;
  dice: Die[];
  pendingChoice: PendingChoice | null;
  log: GameLogEntry[];
  yourPlayerId: string | null;
  version: number;
  /** The blocks the defender declared this combat, in assignment order -
   *  the server's copy, so the attacker sees them too. Empty before
   *  blockers are declared. */
  blocks?: BlockAssignment[];
}

export interface Seat {
  playerId: string;
  token: string;
}

export interface CreatedGame {
  game: GameState;
  seats: Seat[];
}

export interface BlockAssignment {
  attackerDieId: string;
  blockerDieId: string;
}
