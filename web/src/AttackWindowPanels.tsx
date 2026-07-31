import { useState } from "react";
import { dieLabel, hasKeyword } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { BlockAssignment, CardDef, Die, GameState, TagOutUse } from "./types";

// Keyword Infiltrate's own post-blockers window (Appendix 1) - only
// reachable when DeclareBlockers found at least one unblocked Infiltrate
// attacker (see CombatEngine.DeclareAttackers/DeclareBlockers's remarks),
// so anything rendered here is always a real, currently-eligible choice.
// Deliberately not built on the shared board `selection` state the other
// panels use - the eligible set is always a short, well-known list, so a
// local toggle-chip checklist is simpler than a click-to-select flow.
export function InfiltrateWindowPanel(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  combatAssignments: BlockAssignment[];
  busy: boolean;
  onConfirm: (infiltratingDieIds: string[]) => void;
}) {
  const { game, dice, cardsById, combatAssignments, busy } = props;
  const [chosen, setChosen] = useState<string[]>([]);

  const eligible = dice.filter(
    (d) =>
      d.zone === "AttackZone" &&
      d.controllerId === game.activePlayerId &&
      hasKeyword(d, cardsById, "Infiltrate") &&
      !combatAssignments.some((a) => a.attackerDieId === d.id),
  );

  function toggle(id: string) {
    setChosen((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  return (
    <div className="action-tray combat-panel">
      <p className="hint">
        These unblocked attacker(s) have Infiltrate - choose which ones deal 1 damage directly to your opponent and
        return to your Field Zone instead of finishing the attack normally.
      </p>

      {eligible.length === 0 && <p className="no-actions">No eligible Infiltrate attackers.</p>}
      <ul className="combat-attacker-list">
        {eligible.map((d) => (
          <li key={d.id}>
            <label>
              <input type="checkbox" checked={chosen.includes(d.id)} disabled={busy} onChange={() => toggle(d.id)} />
              {" "}
              {dieLabel(d, cardsById)}
            </label>
          </li>
        ))}
      </ul>

      <div className="tray-actions">
        <div className="tray-action">
          <button disabled={busy} onClick={() => props.onConfirm(chosen)}>
            Confirm Infiltrate ▶
          </button>
          <span className="hint">
            {chosen.length === 0 ? "None selected" : `${chosen.length} die(s) will Infiltrate`}
          </span>
        </div>
        <div className="tray-action">
          <button disabled={busy} onClick={() => props.onConfirm([])}>
            Decline All ▶
          </button>
          <span className="hint">Every eligible die finishes its attack normally instead</span>
        </div>
      </div>
    </div>
  );
}

// Keyword Tag Out's own post-blockers window (Appendix 1) - reachable
// whenever either player has a Tag Out die active in their Field Zone
// (see CombatEngine.NextSubStepAfterBlockers), not just the active
// player's. Reuses the shared board `selection` the same way
// DeclareBlockersPanel does: primary = the Tag Out die to Prep, secondary
// = its target - repeat "Add Tag Out" per use, then submit the whole
// batch at once.
export function TagOutWindowPanel(props: {
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  busy: boolean;
  onClearSelection: () => void;
  onConfirm: (uses: TagOutUse[]) => void;
}) {
  const { dice, cardsById, selection, busy } = props;
  const [uses, setUses] = useState<TagOutUse[]>([]);

  const eligibleTagOutDice = dice.filter(
    (d) => d.zone === "FieldZone" && hasKeyword(d, cardsById, "Tag Out") && !uses.some((u) => u.tagOutDieId === d.id),
  );
  const primaryDie = dice.find((d) => d.id === selection.primary) ?? null;
  const isTagOutSelected = primaryDie !== null && eligibleTagOutDice.some((d) => d.id === primaryDie.id);
  const targetId = selection.secondary[0];
  const targetDie = targetId ? (dice.find((d) => d.id === targetId) ?? null) : null;
  const isTargetSelected =
    targetDie !== null &&
    (targetDie.zone === "FieldZone" || targetDie.zone === "AttackZone") &&
    (targetDie.status === "Character" || targetDie.status === "SidekickCharacter");

  function addUse() {
    if (!isTagOutSelected || !isTargetSelected || !primaryDie || !targetDie) return;
    setUses((prev) => [...prev, { tagOutDieId: primaryDie.id, targetDieId: targetDie.id }]);
    props.onClearSelection();
  }

  function removeUse(tagOutDieId: string) {
    setUses((prev) => prev.filter((u) => u.tagOutDieId !== tagOutDieId));
  }

  return (
    <div className="action-tray combat-panel">
      <p className="hint">
        Either player may Prep a Tag Out die from their Field Zone to give some Character die +2A and +2D until end
        of turn. Click a Tag Out die, then its target, then "Add Tag Out". Repeat, then confirm.
      </p>

      {eligibleTagOutDice.length === 0 && uses.length === 0 && <p className="no-actions">No eligible Tag Out dice.</p>}
      {uses.length > 0 && (
        <ul className="combat-attacker-list">
          {uses.map((u) => {
            const tagOutDie = dice.find((d) => d.id === u.tagOutDieId);
            const target = dice.find((d) => d.id === u.targetDieId);
            return (
              <li key={u.tagOutDieId}>
                <span className="combat-attacker-name">{tagOutDie ? dieLabel(tagOutDie, cardsById) : u.tagOutDieId}</span>
                {" → "}
                <span className="secondary-chip">
                  {target ? dieLabel(target, cardsById) : u.targetDieId}
                  <button className="chip-remove" disabled={busy} onClick={() => removeUse(u.tagOutDieId)}>
                    x
                  </button>
                </span>
              </li>
            );
          })}
        </ul>
      )}

      <div className="selection-summary">
        <span className={primaryDie ? "primary-chip" : "empty-hint"}>
          {primaryDie ? dieLabel(primaryDie, cardsById) : "no Tag Out die selected"}
        </span>
        {targetDie && <span className="secondary-chip">{dieLabel(targetDie, cardsById)}</span>}
        <button className="clear-btn" onClick={props.onClearSelection}>
          Clear selection
        </button>
      </div>

      <div className="tray-actions">
        <div className="tray-action">
          <button disabled={busy || !isTagOutSelected || !isTargetSelected} onClick={addUse}>
            Add Tag Out
          </button>
          <span className="hint">Primary = Tag Out die, secondary = its target</span>
        </div>
        <div className="tray-action">
          <button disabled={busy} onClick={() => props.onConfirm(uses)}>
            Confirm Tag Out(s) ▶
          </button>
          <span className="hint">{uses.length === 0 ? "None queued" : `${uses.length} queued`}</span>
        </div>
        <div className="tray-action">
          <button disabled={busy} onClick={() => props.onConfirm([])}>
            Skip ▶
          </button>
          <span className="hint">No one Tags Out this combat</span>
        </div>
      </div>
    </div>
  );
}
