import { useState } from "react";
import { dieLabel, getDieFace } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { BlockAssignment, CardDef, DamageSplit, Die, GameState } from "./types";

// Rule 2.7.2 - Declare Blockers. Reuses the same board click-to-select
// `selection` the rest of the UI already uses: click an attacker (primary)
// then the Field Zone die(s) you want to block it with (secondary), and
// "Assign Selected Blocker(s)" appends those pairs to the running list
// below - repeat per attacker, then "Confirm Blockers" submits the whole
// list in one call. An attacker with nothing assigned to it is simply
// unblocked (rule 2.7.4.3.1), so an empty list is a legal "no blocks".
export function DeclareBlockersPanel(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  assignments: BlockAssignment[];
  busy: boolean;
  onAddAssignments: () => void;
  onRemoveAssignment: (blockerDieId: string) => void;
  onClearSelection: () => void;
  onConfirm: () => void;
}) {
  const { game, dice, cardsById, selection, assignments, busy } = props;
  const attackers = dice.filter((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId);
  const primaryDie = dice.find((d) => d.id === selection.primary) ?? null;
  const isAttackerSelected = primaryDie?.zone === "AttackZone" && primaryDie.controllerId === game.activePlayerId;
  const inactiveId = game.activePlayerId === game.playerOne.id ? game.playerTwo.id : game.playerOne.id;
  const blockerIds = selection.secondary.filter((id) => {
    const d = dice.find((x) => x.id === id);
    return d && d.zone === "FieldZone" && d.controllerId === inactiveId;
  });

  return (
    <div className="action-tray combat-panel">
      <p className="hint">
        Click an attacking die, then click the blocker(s) you want to assign to it, then "Assign Selected
        Blocker(s)". Repeat per attacker - anything left with no blockers is unblocked.
      </p>

      {attackers.length === 0 && <p className="no-actions">No attackers were declared.</p>}
      <ul className="combat-attacker-list">
        {attackers.map((a) => {
          const blockers = assignments.filter((x) => x.attackerDieId === a.id);
          return (
            <li key={a.id}>
              <span className="combat-attacker-name">{dieLabel(a, cardsById)}</span>
              {blockers.length === 0 ? (
                <span className="empty-hint"> unblocked</span>
              ) : (
                blockers.map((b) => {
                  const die = dice.find((d) => d.id === b.blockerDieId);
                  return (
                    <span key={b.blockerDieId} className="secondary-chip">
                      {die ? dieLabel(die, cardsById) : b.blockerDieId}
                      <button
                        className="chip-remove"
                        disabled={busy}
                        onClick={() => props.onRemoveAssignment(b.blockerDieId)}
                      >
                        x
                      </button>
                    </span>
                  );
                })
              )}
            </li>
          );
        })}
      </ul>

      <div className="selection-summary">
        <span className={primaryDie ? "primary-chip" : "empty-hint"}>
          {primaryDie ? dieLabel(primaryDie, cardsById) : "no attacker selected"}
        </span>
        {selection.secondary.map((id) => {
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
            disabled={busy || !isAttackerSelected || blockerIds.length === 0}
            onClick={props.onAddAssignments}
          >
            Assign Selected Blocker(s)
          </button>
          <span className="hint">Primary = attacker, secondary = blocker(s) for it</span>
        </div>
        <div className="tray-action">
          <button disabled={busy} onClick={props.onConfirm}>
            Confirm Blockers ▶
          </button>
          <span className="hint">
            {assignments.length === 0 ? "No blockers assigned - everything gets through" : `${assignments.length} blocker(s) assigned`}
          </span>
        </div>
      </div>
    </div>
  );
}

// Rule 2.7.4 - Assign Combat Damage. Only rendered when there's at least
// one real blocked attacker (an all-unblocked combat is a one-click
// "Assign Combat Damage (no blocks)" in the status bar instead - see
// App.tsx). Each blocked attacker's full attack value (rule 2.7.4.3.4)
// must be split across its blocker(s); local-only state until Confirm,
// since there's nothing to validate against the server until it's whole.
export function DamageSplitPanel(props: {
  dice: Die[];
  cardsById: Map<string, CardDef>;
  assignments: BlockAssignment[];
  busy: boolean;
  onConfirm: (splits: DamageSplit[]) => void;
}) {
  const { dice, cardsById, assignments, busy } = props;
  const [amounts, setAmounts] = useState<Record<string, number>>({});

  const attackerIds = [...new Set(assignments.map((a) => a.attackerDieId))];

  function setAmount(blockerDieId: string, value: number) {
    setAmounts((prev) => ({ ...prev, [blockerDieId]: Math.max(0, value) }));
  }

  let allValid = attackerIds.length > 0;
  const rows = attackerIds.map((attackerId) => {
    const attackerDie = dice.find((d) => d.id === attackerId);
    const attack = attackerDie ? getDieFace(attackerDie, cardsById).attack : 0;
    const blockers = assignments.filter((a) => a.attackerDieId === attackerId);
    const assigned = blockers.reduce((sum, b) => sum + (amounts[b.blockerDieId] ?? 0), 0);
    if (assigned !== attack) allValid = false;
    return { attackerId, attackerDie, attack, blockers, assigned };
  });

  return (
    <div className="action-tray combat-panel">
      <p className="hint">Split each blocked attacker's full attack value across its blocker(s).</p>
      {rows.map((row) => (
        <div key={row.attackerId} className="damage-split-row">
          <div className="combat-attacker-name">
            {row.attackerDie ? dieLabel(row.attackerDie, cardsById) : row.attackerId} - {row.attack} attack
            <span className={row.assigned === row.attack ? "hint" : "damage-split-warning"}>
              {" "}
              ({row.assigned}/{row.attack} assigned)
            </span>
          </div>
          {row.blockers.map((b) => {
            const blockerDie = dice.find((d) => d.id === b.blockerDieId);
            return (
              <label key={b.blockerDieId} className="damage-split-input">
                {blockerDie ? dieLabel(blockerDie, cardsById) : b.blockerDieId}
                <input
                  type="number"
                  min={0}
                  max={row.attack}
                  value={amounts[b.blockerDieId] ?? 0}
                  disabled={busy}
                  onChange={(e) => setAmount(b.blockerDieId, Number(e.target.value))}
                />
              </label>
            );
          })}
        </div>
      ))}
      <button
        disabled={busy || !allValid}
        onClick={() =>
          props.onConfirm(
            assignments.map((a) => ({ ...a, amount: amounts[a.blockerDieId] ?? 0 })),
          )
        }
      >
        Confirm Damage ▶
      </button>
    </div>
  );
}
