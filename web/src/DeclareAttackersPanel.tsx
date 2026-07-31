import { useState } from "react";
import { dieLabel, hasKeyword } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die, GameState } from "./types";

type Stage = "attackers" | "call-out-targets";

// Rule 2.7.1 - Declare Attackers, plus keyword Call Out's "target
// character die is the only character die that may block this character
// die" (a WhenAttacks-triggered target, per Appendix 1). Stage 1 reuses
// the shared board `selection` exactly like the old ActionTray action did
// (primary + secondary = every FieldZone die attacking this turn). If
// none of the chosen attackers have Call Out, that's the whole flow - one
// submit with no targets, same as before this panel existed. If one or
// more do, stage 2 asks for target(s) using the same click-to-select
// idiom the Global ability "targets" stage already uses.
//
// Known limitation, not fixed here: Drain (see GamesController) feeds one
// shared target list into every ability resolved from a single API call,
// so two *different* Call Out attackers declared in the same batch would
// both receive the same target selection - the live roster has exactly
// one Call Out card (Black Widow), so this can't come up today; same
// class of documented limitation as Casket of Ancient Winters.
export function DeclareAttackersPanel(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  busy: boolean;
  onClearSelection: () => void;
  onSubmit: (attackerDieIds: string[], targetDieIds: string[]) => void;
}) {
  const { game, dice, cardsById, selection, busy } = props;
  const [stage, setStage] = useState<Stage>("attackers");
  const [attackerIds, setAttackerIds] = useState<string[]>([]);

  if (stage === "call-out-targets") {
    const callOutAttackers = attackerIds
      .map((id) => dice.find((d) => d.id === id))
      .filter((d): d is Die => d !== undefined && hasKeyword(d, cardsById, "Call Out"));
    const chosenIds = selection.primary ? [selection.primary, ...selection.secondary] : [];

    return (
      <div className="action-tray combat-panel">
        <p className="hint">
          {callOutAttackers.map((d) => dieLabel(d, cardsById)).join(", ")} has Call Out - click its target (the only
          die allowed to block it).
        </p>
        <div className="selection-summary">
          {chosenIds.length === 0 && <span className="empty-hint">no target selected</span>}
          {chosenIds.map((id) => {
            const d = dice.find((x) => x.id === id);
            return d ? (
              <span key={id} className="secondary-chip">
                {dieLabel(d, cardsById)}
              </span>
            ) : null;
          })}
          <button className="clear-btn" onClick={props.onClearSelection}>
            Clear selection
          </button>
        </div>
        <div className="tray-actions">
          <div className="tray-action">
            <button
              disabled={busy || chosenIds.length === 0}
              onClick={() => props.onSubmit(attackerIds, chosenIds)}
            >
              Confirm Target ▶
            </button>
          </div>
        </div>
      </div>
    );
  }

  const primaryDie = dice.find((d) => d.id === selection.primary) ?? null;
  const secondaryIds = selection.secondary;
  const isActiveController = primaryDie?.controllerId === game.activePlayerId;
  const canDeclare = primaryDie !== null && primaryDie.zone === "FieldZone" && isActiveController;

  function declare() {
    if (!primaryDie) return;
    const chosen = [primaryDie.id, ...secondaryIds];
    const callOutIds = chosen.filter((id) => {
      const d = dice.find((x) => x.id === id);
      return d && hasKeyword(d, cardsById, "Call Out");
    });
    if (callOutIds.length === 0) {
      props.onSubmit(chosen, []);
      return;
    }
    setAttackerIds(chosen);
    setStage("call-out-targets");
    props.onClearSelection();
  }

  return (
    <div className="action-tray">
      <div className="selection-summary">
        {!primaryDie && <span className="empty-hint">no attacker selected</span>}
        {primaryDie && <span className="primary-chip">{dieLabel(primaryDie, cardsById)}</span>}
        {secondaryIds.map((id) => (
          <span key={id} className="secondary-chip">
            {dieLabel(dice.find((d) => d.id === id)!, cardsById)}
          </span>
        ))}
        <button className="clear-btn" onClick={props.onClearSelection}>
          Clear selection
        </button>
      </div>
      <div className="tray-actions">
        <div className="tray-action">
          <button disabled={busy || !canDeclare} onClick={declare}>
            Declare Attackers
          </button>
          <span className="hint">Primary + secondary selections = every die attacking this turn</span>
        </div>
      </div>
    </div>
  );
}
