export interface CharacterFace {
  fieldingCost: number;
  attack: number;
  defense: number;
  burstStars: number | null;
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
