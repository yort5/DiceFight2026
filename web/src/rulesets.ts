// How many cards, dice and Basic Actions a team may have.
//
// This replaces the old `strictRules` checkbox, which could only be on or
// off. Off meant "no validation at all", which is a poor fit for what
// people actually do: house formats are usually the tournament rules with
// ONE number changed (10 characters, say), not an absence of rules. So a
// ruleset is a set of caps, and a house format is a first-class choice
// rather than an escape hatch.
//
// What is NOT a cap, and stays enforced under every ruleset: rule 2.1.5's
// "a team cannot have multiple cards with exactly the same card name" is
// a property of the cards, not a number, and a team that breaks it cannot
// be played with physically either - two identical cards are one card.
// Freeform relaxes the counting rules, not the game.

export type RulesetId = "standard" | "freeform" | "custom";

export interface Caps {
  /** Character/Action cards. Rule 2.1.1 - up to 8. */
  cards: number;
  /** Total dice on those cards. Rule 2.1.3 - at most 20. */
  dice: number;
  /** Basic Action cards. Rule 2.1.1 - 2 of them; their dice do not count
   *  toward the dice cap (rule 2.1.4). */
  basicActions: number;
}

export const STANDARD_CAPS: Caps = { cards: 8, dice: 20, basicActions: 2 };

/** No cap. Large enough to never bite, small enough to render as pips. */
const NO_CAP = Number.POSITIVE_INFINITY;

export const FREEFORM_CAPS: Caps = { cards: NO_CAP, dice: NO_CAP, basicActions: NO_CAP };

export interface RulesetOption {
  id: RulesetId;
  label: string;
  note: string;
}

export const RULESETS: readonly RulesetOption[] = [
  { id: "standard", label: "Standard", note: "8 cards · 20 dice · 2 BA" },
  { id: "freeform", label: "Freeform", note: "no limits — house rules" },
  { id: "custom", label: "Custom", note: "set your own caps" },
];

export function capsFor(ruleset: RulesetId, custom: Caps): Caps {
  if (ruleset === "freeform") return FREEFORM_CAPS;
  if (ruleset === "custom") return custom;
  return STANDARD_CAPS;
}

export function isCapped(value: number): boolean {
  return Number.isFinite(value);
}

/** How the team stands against a ruleset, for the legality strip. */
export interface Legality {
  overCards: boolean;
  overDice: boolean;
  overBasicActions: boolean;
  /** True when nothing is over - an INCOMPLETE team is still legal, it
   *  just is not finished. Rule-wise "up to 8" is a ceiling, not a quota. */
  ok: boolean;
  note: string;
}

export function legalityOf(
  ruleset: RulesetId,
  caps: Caps,
  counts: { cards: number; dice: number; basicActions: number },
): Legality {
  const overCards = counts.cards > caps.cards;
  const overDice = counts.dice > caps.dice;
  const overBasicActions = counts.basicActions > caps.basicActions;
  const ok = !overCards && !overDice && !overBasicActions;

  if (ruleset === "freeform") {
    return {
      overCards: false, overDice: false, overBasicActions: false, ok: true,
      note: "Freeform — no construction limits enforced. Fine for house formats and physical play.",
    };
  }
  if (!ok) {
    return {
      overCards, overDice, overBasicActions, ok,
      note: "Over the ruleset's limits — you can still share and print this team, but not start a game with it.",
    };
  }

  const cardsLeft = Math.max(0, caps.cards - counts.cards);
  const baLeft = Math.max(0, caps.basicActions - counts.basicActions);
  if (cardsLeft > 0 || baLeft > 0) {
    const parts: string[] = [];
    if (cardsLeft > 0) parts.push(`${cardsLeft} card slot${cardsLeft === 1 ? "" : "s"}`);
    if (baLeft > 0) parts.push(`${baLeft} basic action${baLeft === 1 ? "" : "s"}`);
    return { overCards, overDice, overBasicActions, ok, note: `Incomplete — ${parts.join(" and ")} left.` };
  }
  return { overCards, overDice, overBasicActions, ok, note: "Legal for this ruleset." };
}
