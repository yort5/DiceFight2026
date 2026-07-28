export interface CharacterFace {
  fieldingCost: number;
  attack: number;
  defense: number;
  burstStars: number | null;
}

export interface GlobalAbilityCost {
  amount: number;
  requiredType: string | null;
}

export interface CardDef {
  id: string;
  name: string;
  subtitle: string | null;
  type: string;
  purchaseCost: number;
  energyTypes: string[];
  affiliations: string[];
  alignment: string | null;
  dieLimit: number;
  levels: CharacterFace[];
  rawText: string;
  keywords: string[];
  abilityTriggers: string[];
  globalAbilityCost: GlobalAbilityCost | null;
}

export interface Die {
  id: string;
  cardId: string | null;
  ownerId: string;
  controllerId: string;
  zone: string;
  status: string;
  level: number;
  damage: number;
  energyKind: string;
  providedEnergyType: string | null;
}

export interface PlayerState {
  id: string;
  name: string;
  life: number;
  virtualGenericEnergy: number;
}

export interface GameState {
  gameId: string;
  activePlayerId: string;
  currentStep: string;
  attackSubStep: string;
  isFirstTurn: boolean;
  epicBasicActionUsedThisTurn: boolean;
  playerOne: PlayerState;
  playerTwo: PlayerState;
  dice: Die[];
}

// Rule 2.7.2.2 - one pair per (attacker, blocker); a given attacker can
// have several of these (multiple blockers), a given blocker at most one
// (it can't block two attackers at once).
export interface BlockAssignment {
  attackerDieId: string;
  blockerDieId: string;
}

// Rule 2.7.4.3.4/2.7.4.3.5 - how much of a blocked attacker's full attack
// value lands on each of its blockers; the active player's choice, but
// must sum to exactly the attacker's attack value.
export interface DamageSplit extends BlockAssignment {
  amount: number;
}

export const ZONES = [
  "Unpurchased",
  "Bag",
  "PrepArea",
  "DiceFromBag",
  "DiceFromPrep",
  "ReservePool",
  "FieldZone",
  "AttackZone",
  "UsedPile",
  "OutOfPlay",
] as const;
