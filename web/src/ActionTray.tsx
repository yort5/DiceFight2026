import { api } from "./api";
import { dieLabel } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die, GameState } from "./types";

interface ContextualAction {
  key: string;
  label: string;
  hint: string;
  run: () => Promise<GameState>;
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
  onRun: (action: () => Promise<GameState>) => void;
  onClear: () => void;
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
    actions.push({
      key: "field",
      label: "Field",
      hint: "Secondary selections = energy to spend",
      run: () => api.field(game.gameId, primaryDie.id, secondaryIds),
    });
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
    });
  }

  if (
    game.currentStep === "Attack" &&
    game.attackSubStep === "DeclareAttackers" &&
    primaryDie.zone === "FieldZone" &&
    isActiveController
  ) {
    actions.push({
      key: "declare-attackers",
      label: "Declare Attackers",
      hint: "Primary + secondary selections = every die attacking this turn",
      run: () => api.declareAttackers(game.gameId, [primaryDie.id, ...secondaryIds]),
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
            <button disabled={busy} onClick={() => onRun(a.run)}>
              {a.label}
            </button>
            <span className="hint">{a.hint}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
