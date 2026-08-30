import { api } from "./api";
import { dieLabel } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die, GameState } from "./types";

interface ContextualAction {
  key: string;
  label: string;
  /** Dice this action deliberately rolls, so the roll animation plays
   *  even when a die lands on the face it was already showing. */
  rolledDieIds?: string[];
  hint: string;
  // Most actions fire one API call directly; Field on a WhenFielded-
  // targeting card instead hands off to App's own energy-then-target flow
  // (see onFieldNeedsTarget below), same shape as the Global ability flow.
  run?: () => Promise<GameState>;
  start?: () => void;
}

// Global abilities need two logically distinct secondary selections
// (energy to pay, then ability targets) that don't fit this tray's single
// secondary-selection model - left out of this pass, still reachable via
// the API directly. Everything below needs only one kind of secondary
// selection per action, which is what keeps a single "Primary + Secondary"
// selection model workable.
export function ActionTray(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  busy: boolean;
  onRun: (action: () => Promise<GameState>, rolledDieIds?: string[]) => void;
  onClear: () => void;
  onFieldNeedsTarget: (dieId: string, energyIds: string[]) => void;
}) {
  const { game, dice, selection, busy, onRun, onClear } = props;
  const primaryDie = dice.find((d) => d.id === selection.primary) ?? null;
  const secondaryIds = selection.secondary;

  if (!primaryDie) {
    return (
      <div className="action-tray empty">
        <p>
          Click a die to select it, then click others to add them as energy, ability targets, extra
          attackers, or (after rolling) dice to reroll.
        </p>
      </div>
    );
  }

  const inMain = game.currentStep === "Main";
  const inAttackWindow = game.currentStep === "Attack" && game.attackSubStep === "ActionAndGlobalWindow";
  const isActiveController = primaryDie.controllerId === game.activePlayerId;

  const actions: ContextualAction[] = [];

  if (primaryDie.zone === "Unpurchased" && inMain) {
    actions.push({
      key: "purchase",
      label: "Purchase",
      hint: "Secondary selections = energy to spend",
      run: () => api.purchase(game.gameId, primaryDie.id, secondaryIds),
    });
  }

  if (
    primaryDie.zone === "ReservePool" &&
    inMain &&
    isActiveController &&
    (primaryDie.status === "Character" || primaryDie.status === "SidekickCharacter")
  ) {
    const card = primaryDie.cardId ? props.cardsById.get(primaryDie.cardId) : undefined;
    if (card?.whenFieldedNeedsTarget) {
      actions.push({
        key: "field",
        label: "Field",
        hint: "Secondary = energy; you'll pick a target next",
        start: () => props.onFieldNeedsTarget(primaryDie.id, secondaryIds),
      });
    } else {
      actions.push({
        key: "field",
        label: "Field",
        hint: "Secondary selections = energy to spend",
        run: () => api.field(game.gameId, primaryDie.id, secondaryIds, []),
      });
    }
  }

  if (
    primaryDie.zone === "ReservePool" &&
    (inMain || inAttackWindow) &&
    isActiveController &&
    primaryDie.status === "Action"
  ) {
    actions.push({
      key: "use-action-die",
      label: "Use Action Die",
      hint: "Secondary selections = ability target(s), if its ability needs any",
      run: () => api.useActionDie(game.gameId, primaryDie.id, secondaryIds),
    });
  }

  if (game.currentStep === "RollAndReroll" && primaryDie.zone === "ReservePool" && isActiveController) {
    actions.push({
      key: "reroll-selected",
      label: "Reroll Selected",
      hint: "One-time decision - rerolls just the selected dice and immediately advances to Main",
      run: () => api.reroll(game.gameId, [primaryDie.id, ...secondaryIds]),
      rolledDieIds: [primaryDie.id, ...secondaryIds],
    });
  }

  return (
    <div className="action-tray">
      <div className="selection-summary">
        <span className="primary-chip">{dieLabel(primaryDie, props.cardsById)}</span>
        {secondaryIds.map((id) => (
          <span key={id} className="secondary-chip">
            {dieLabel(dice.find((d) => d.id === id)!, props.cardsById)}
          </span>
        ))}
        <button className="clear-btn" onClick={onClear}>
          Clear selection
        </button>
      </div>
      {actions.length === 0 && <p className="no-actions">No actions available for this die right now.</p>}
      <div className="tray-actions">
        {actions.map((a) => (
          <div key={a.key} className="tray-action">
            <button disabled={busy} onClick={() => (a.run ? onRun(a.run, a.rolledDieIds) : a.start?.())}>
              {a.label}
            </button>
            <span className="hint">{a.hint}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
