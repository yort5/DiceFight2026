import { useState } from "react";
import type { GameState } from "./types";

// The rail beside the table: who has how much life, where the turn is,
// and what to do next.
//
// The turn sequence is deliberately READ-ONLY. Rule 2.2.4 forbids going
// back a step, so the steps are a progress display and the only way to
// move is the legal action in the Now panel below them - which comes
// from the same `can*` guards App.tsx already computes, so a button
// appears only when the server would accept it.

const STEPS = [
  { key: "ClearAndDraw", label: "Clear & Draw", hint: "Sweep the pool, draw four." },
  { key: "RollAndReroll", label: "Roll & Reroll", hint: "Roll, then reroll once." },
  { key: "Main", label: "Main", hint: "Buy, field, spin, act." },
  { key: "Attack", label: "Attack", hint: "Declare, block, deal damage." },
  { key: "CleanUp", label: "Clean Up", hint: "Damage clears, dice retire." },
];

// What the player is actually being asked for, one line. The Attack Step
// is the only one with sub-steps worth naming - the rest are a whole
// step at a time.
const ATTACK_SUB_STEPS: Record<string, string> = {
  DeclareAttackers: "Choose which of your fielded dice attack.",
  RangeWindow: "Range dice may deal their damage before blockers.",
  DeclareBlockers: "The defender assigns blockers - any number may gang up on one attacker.",
  InfiltrateWindow: "Unblocked Infiltrate dice may act now.",
  TagOutWindow: "Tag Out may swap a die out of combat.",
  ActionAndGlobalWindow: "Last window for action dice and Global abilities before damage.",
  AssignCombatDamage: "Split each attacker's damage among the dice blocking it.",
  WhenDamagedAbilities: "Resolving abilities that trigger on damage.",
  ResolveDamageAndWhenKOd: "Resolving damage and anything that triggers on a knock-out.",
  Done: "Combat is finished - move on to Clean Up.",
};

const STEP_GUIDANCE: Record<string, string> = {
  ClearAndDraw: "Spent dice go to the Used Pile, then draw back up to four.",
  RollAndReroll: "Roll everything drawn. You get one reroll decision, and taking it ends the step.",
  Main: "Buy dice, field what you rolled, spin dice up or down, and use action dice.",
  CleanUp: "Damage wears off and used dice retire. Then it is the other player's turn.",
};

export interface RailAction {
  key: string;
  label: string;
  onClick: () => void;
  disabled?: boolean;
}

export function TurnRail(props: {
  game: GameState;
  /** The seat this browser holds - its life panel is the amber one. */
  nearPlayerId: string;
  /** The legal ways forward right now, primary first. */
  actions: RailAction[];
  /** Shown under the guidance when the next move is a board selection
   *  rather than a button. */
  note?: string;
  /** Link that hands the other seat to someone else - null when this
   *  browser holds only one seat. */
  inviteLink?: string | null;
}) {
  const { game } = props;
  const you = props.nearPlayerId === game.playerTwo.id ? game.playerTwo : game.playerOne;
  const them = props.nearPlayerId === game.playerTwo.id ? game.playerOne : game.playerTwo;
  const currentIndex = STEPS.findIndex((s) => s.key === game.currentStep);
  // The other player is mid-turn: say so, rather than leaving the panel
  // looking like something is stuck.
  const waiting = game.activePlayerId !== props.nearPlayerId;
  const inAttack = game.currentStep === "Attack" && game.attackSubStep !== "NotInAttack";
  const title = inAttack ? `Attack · ${spaced(game.attackSubStep)}` : (STEPS[currentIndex]?.label ?? game.currentStep);
  const guidance = inAttack
    ? (ATTACK_SUB_STEPS[game.attackSubStep] ?? "")
    : (STEP_GUIDANCE[game.currentStep] ?? "");

  return (
    <div className="turn-rail">
      <div className="life-panels">
        <div className="life-panel theirs">
          <span className="life-label">{them.name}</span>
          <span className="life-value">{them.life}</span>
        </div>
        <div className="life-panel yours">
          <span className="life-label">{you.name}</span>
          <span className="life-value">{you.life}</span>
        </div>
      </div>

      <div className="rail-panel">
        <h3>Turn sequence</h3>
        <ol className="turn-steps">
          {STEPS.map((step, i) => {
            const state = i < currentIndex ? "past" : i === currentIndex ? "current" : "future";
            return (
              <li key={step.key} className={`turn-step ${state}`} aria-current={state === "current" || undefined}>
                <span className="turn-step-dot" aria-hidden="true" />
                <span className="turn-step-body">
                  <span className="turn-step-label">{step.label}</span>
                  <span className="turn-step-hint">{step.hint}</span>
                </span>
              </li>
            );
          })}
        </ol>
      </div>

      {props.inviteLink && <InvitePanel link={props.inviteLink} />}

      <div className="rail-panel now-panel">
        <span className="now-eyebrow">Now</span>
        <h3 className="now-title">{title}</h3>
        {guidance && <p className="now-guidance">{guidance}</p>}
        {/* What IsFirstTurn actually changes, per TurnEngine: the draw.
            It has nothing to do with attacking. */}
        {game.isFirstTurn && (
          <p className="now-note">First turn - you draw 3, and a 4th die goes Out of Play (rule 2.3.3).</p>
        )}
        {props.note && <p className="now-note">{props.note}</p>}
        {props.actions.map((action, i) => (
          <button
            key={action.key}
            className={i === 0 ? "now-button primary" : "now-button"}
            disabled={action.disabled}
            onClick={action.onClick}
          >
            {action.label}
          </button>
        ))}
        {props.actions.length === 0 && (
          <p className="now-note">
            {waiting
              ? `Waiting for ${them.name} to move.`
              : "Nothing to advance right now - finish what is on the board."}
          </p>
        )}
      </div>
    </div>
  );
}

// The invite is the only way a second person gets into the game, so it
// sits in the rail until someone takes it - not behind a menu.
function InvitePanel({ link }: { link: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="rail-panel invite-panel">
      <h3>Invite an opponent</h3>
      <p className="now-note">
        Send this link. Whoever opens it takes the other side - so send it to one person.
      </p>
      {/* Deliberately NOT .now-button: that class means "a move you can
          make in this step", and copying a link is not one. */}
      <button
        className="rail-button"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(link);
            setCopied(true);
            window.setTimeout(() => setCopied(false), 2000);
          } catch {
            // Clipboard blocked (insecure origin, denied permission) -
            // the link is selectable below either way.
          }
        }}
      >
        {copied ? "Copied!" : "Copy invite link"}
      </button>
      <input className="invite-link" readOnly value={link} onFocus={(e) => e.currentTarget.select()} />
    </div>
  );
}

function spaced(subStep: string): string {
  return subStep.replace(/([a-z])([A-Z])/g, "$1 $2");
}
