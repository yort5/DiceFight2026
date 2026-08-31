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
  /** Printed rarity - display only, drives no rules. Null if the card
   *  is not on the reference sheet. */
  rarity: string | null;
  /** This card's id in the old Teambuilder (tb.dicecoalition.com), e.g.
   *  "135dps". Null for cards that tool never had. */
  oldTeamBuilderCode: string | null;
  /** The old Teambuilder's affiliation logo codes for this card, e.g.
   *  ["2"]. Display only; see AffiliationIcons.tsx for why these are not
   *  derived from `affiliations`. Empty when unknown. */
  affiliationIcons: string[];
  dieLimit: number;
  levels: CharacterFace[];
  rawText: string;
  keywords: string[];
  abilityTriggers: string[];
  globalAbilityCost: GlobalAbilityCost | null;
  globalAbilityNeedsTarget: boolean;
  isImplemented: boolean;
  whenFieldedNeedsTarget: boolean;
  set: string | null;
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
  /** The other half of a split double - a Crossover character's double
   *  provides one of each of its two energy types. Null otherwise. */
  secondProvidedEnergyType: string | null;
  energyAmount: number;
  isVirtualEnergy: boolean;
}

export interface GameLogEntry {
  seq: number;
  /** Who did it, or null for something the game itself did. */
  playerId: string | null;
  text: string;
}

export interface PlayerState {
  id: string;
  name: string;
  life: number;
}

export type AttackSubStep =
  | "NotInAttack"
  | "DeclareAttackers"
  | "RangeWindow"
  | "DeclareBlockers"
  | "InfiltrateWindow"
  | "TagOutWindow"
  | "ActionAndGlobalWindow"
  | "AssignCombatDamage"
  | "WhenDamagedAbilities"
  | "ResolveDamageAndWhenKOd"
  | "Done";

// Keyword Corrupt/RedrawFromBag (Cosmic Cube "Infinite Possibilities",
// Rip Hunter) - a mid-resolution choice the server can't answer in the
// same request that triggered it, since the candidate dice either don't
// exist until a random draw happens mid-effect or shouldn't be chosen
// blind before the player has actually seen what was drawn. Set on
// GameState whenever one is outstanding - every other action is
// rejected server-side until it's answered via api.resolvePendingChoice.
export interface PendingChoice {
  controllerId: string;
  description: string;
  candidateDieIds: string[];
  allowMultiple: boolean; // false: exactly one (Corrupt); true: any subset incl. none (RedrawFromBag)
}

export interface GameState {
  gameId: string;
  activePlayerId: string;
  currentStep: string;
  attackSubStep: AttackSubStep;
  isFirstTurn: boolean;
  epicBasicActionUsedThisTurn: boolean;
  playerOne: PlayerState;
  playerTwo: PlayerState;
  dice: Die[];
  pendingChoice: PendingChoice | null;
  /** Which side the caller holds - the board renders from this seat.
   *  Null only before a seat has been resolved. */
  yourPlayerId: string | null;
  /** Increments on every action - how the opponent's browser tells a real
   *  change from a quiet poll. */
  version: number;
  /** What has happened, oldest first. Written by the engine, so both
   *  players see the same account. Capped at the 200 most recent. */
  log: GameLogEntry[];
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

// Keyword Tag Out - either player's own Field Zone die with Tag Out,
// Prepped to give some Character/SidekickCharacter die (either side,
// Field or Attack Zone) +2A/+2D until end of turn.
export interface TagOutUse {
  tagOutDieId: string;
  targetDieId: string;
}

// Keyword Range X - either side's active Range dice each deal their own
// Range value to an opposing Character die of their choice; both sides'
// assignment lists are submitted together (see CombatEngine.ResolveRange).
export interface RangeAssignment {
  rangeDieId: string;
  targetDieId: string;
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
  "Intimidated",
] as const;
