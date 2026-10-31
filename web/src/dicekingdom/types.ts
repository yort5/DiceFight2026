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
  burst?: number;
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
  /** Display metadata; separate from game keywords. */
  hasErrata?: boolean;
  isImplemented?: boolean;
  type?: string | null;
  set?: string | null;
  affiliations?: string[] | null;
  /** A statless action card, either a shared Basic Action or a personal Action. */
  isAction?: boolean;
  /** The action's own text (rawText without its Global clause). */
  actionText?: string | null;
  global?: GlobalAbility | null;
  /** The energy its die's energy faces show (a Basic Action's purchase has no type). */
  dieEnergyType?: string | null;
  actionFaceBursts?: number[] | null;
  /** Damage this die takes is dealt to its controller's opponent too (Rhinoceros). */
  reflectsDamage?: boolean;
  /** Pack rarity - null for Basic Actions. */
  rarity?: "Common" | "Uncommon" | "Rare" | "SuperRare" | null;
  /** "Set 1" etc. (DiceKingdomSets) - null for Champions, Tardigrades and unfinished cards. */
  kingdomSet?: string | null;
}

export interface GlobalAbility {
  abilityIndex: number;
  text: string;
  cost: number;
  energyType: string | null;
  oncePerTurn: boolean;
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
  burst?: number;
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
  /** Showing an action face - a Basic Action die ready to use. */
  isActionFace?: boolean;
  /** Fielding cost right now, discounts included; null without a creature face. */
  fieldingCost?: number | null;
  /** Forced to block this turn (Hermit Crab). */
  mustBlock?: boolean;
  /** Can't be blocked this turn (Obscure) - nor can its lane-mates. */
  unblockable?: boolean;
  /** Every status cue on this die, with its source and duration (the
   *  server's DieStatuses). The tile and its explainer both read this. */
  statuses?: DieStatus[];
  /** The latest effect-caused spin - flashed once per new seq. */
  lastSpin?: SpinRecord | null;
}

export type DieStatusKind =
  | "mustBlock" | "cantBlock" | "unblockable" | "mustAttack" | "cantAttack" | "onlyBlocker"
  | "blanked" | "granted" | "deadly" | "intimidated" | "protected" | "targetProtected";

export interface DieStatus {
  kind: DieStatusKind;
  source: string | null;
  /** "turn" | "cleanup" | "whileActive", or null when unknown. */
  duration: string | null;
  /** For "granted": the keyword it was given. */
  keyword?: string | null;
  /** The server's explanation when a status has configurable rule scope. */
  description?: string | null;
}

export interface SpinRecord {
  seq: number;
  fromLevel: number | null;
  /** Null when it was spun to an energy face. */
  toLevel: number | null;
  source: string;
}

export interface LockedCard {
  playerId: string;
  cardId: string;
  sources: string[];
}

export interface PlayerState {
  id: string;
  name: string;
  life: number;
  champion: Champion | null;
  /** Great Horned Owl's Foresight is theirs and unused this turn (retired 2026-10-04). */
  foresightAvailable?: boolean;
  /** Their Champion's once-per-turn power (Wolf/Armadillo/Owl) can be used now. */
  championPowerUsable?: boolean;
  /** Golden Eagle's free field is unused and it's their Main Step. */
  freeFieldAvailable?: boolean;
  /** Generic energy from dice they couldn't draw this turn (deck-out) -
   *  the server spends it automatically before any die; gone after Main. */
  virtualEnergy?: number;
}

export interface PendingChoice {
  isConfirmation?: boolean;
  controllerId: string;
  description: string;
  candidateIds: string[];
  minCount: number;
  maxCount: number;
  /** "NameCard": the pick names a card (Pangolin's lockout), not a die. */
  intent?: "Unknown" | "Harmful" | "Beneficial" | "NameCard";
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
  /** Who may act in Main / the attack window right now (priority); null elsewhere. */
  priorityPlayerId?: string | null;
  /** What each card in the game costs YOU to buy, discounts included. */
  purchaseCosts?: Record<string, number> | null;
  /** The Active player's pass would hand priority to the other player (they
   *  could still use a Global) rather than close the window. */
  passGivesPriority?: boolean;
  /** Rule 2.9: someone's Life reached 0. winnerId null with gameOver = a tie. */
  gameOver?: boolean;
  winnerId?: string | null;
  /** Lanes holding an unblockable attacker - nothing can block there. */
  unblockableLanes?: number[];
  /** Cards a player can't buy or field right now (Pangolin), and why. */
  lockedCards?: LockedCard[];
  /** The seat the server plays itself - a boss battle's boss (2026-10-31). Only watched, never driven. */
  serverSeatId?: string | null;
}

export interface Seat {
  playerId: string;
  token: string;
}

export interface CreatedGame {
  game: GameState;
  seats: Seat[];
}

/** An online game opened with only the host's Champion (V2OpenGameDto). */
export interface OpenedGame {
  gameId: string;
  /** "" for a custom team without a Champion. */
  hostChampionId: string;
  seats: Seat[];
  /** What to call the host's team: "Wolf", "Wolf - custom team", "a custom team". */
  hostLabel: string;
}

/** Waiting for the invited player to pick, or started (V2LobbyDto). */
export interface LobbyStatus {
  gameId: string;
  hostChampionId: string;
  started: boolean;
  yourPlayerId: string | null;
  hostLabel: string;
}

export interface BlockAssignment {
  attackerDieId: string;
  blockerDieId: string;
}

// GET .../bot-decision - the shared computer opponent's next move
// (DiceFight.V2/Bot/DiceKingdomBot.cs, the same policy tools/Simulator
// plays). Only the fields `kind` uses are meaningful; bot.ts's
// botDecisionCall maps each kind onto the matching endpoint.
export interface BotDecision {
  kind:
    | "clearAndDraw"
    | "roll"
    | "reroll"
    | "finishRoll"
    | "foresight"
    | "field"
    | "purchase"
    | "useAction"
    | "useGlobal"
    | "pass"
    | "declareAttackers"
    | "declareBlockers"
    | "resolvePendingChoice"
    | "cleanUp"
    | "useChampionPower";
  reason: string;
  /** field: use Golden Eagle's free field. */
  free?: boolean;
  dieId: string | null;
  dieIds: string[];
  energyDieIds: string[];
  cardId: string | null;
  abilityIndex: number;
  skipAttack: boolean;
  attackers: { dieId: string; lane: number }[];
  assignments: BlockAssignment[];
}
