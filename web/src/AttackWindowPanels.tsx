import { useState } from "react";
import { dieLabel, hasKeyword } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { BlockAssignment, CardDef, Die, GameState, RangeAssignment, TagOutUse } from "./types";

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

// Keyword Range X - fires as soon as any attacker has Range (before
// Declare Blockers even runs, unlike the other two windows), and lets
// *both* sides' active Range dice deal damage, not just the active
// player's (Appendix 1 clarification 5). Reuses the shared board
// `selection` the same way TagOutWindowPanel does: primary = a Range
// die (either side), secondary[0] = its target (must belong to the
// Range die's own opponent, not necessarily the game's active player).
// "Add" auto-buckets into the active-vs-inactive assignment list by
// comparing the Range die's own controller to the active player, so
// The Range window is the only place both players act at once, so this
// panel is one SIDE's view of it, not the referee's. You assign your own
// Range dice and nobody else's; the server holds the active player's
// assignments until the opponent answers, then fires them together.
//
// Which means the panel has three faces: assigning, waiting on the
// opponent, and being waited on. A player with no Range dice never sees
// it at all - the server resolves past them.
export function RangeWindowPanel(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  selection: Selection;
  busy: boolean;
  you: string;
  onClearSelection: () => void;
  onConfirm: (assignments: RangeAssignment[]) => void;
}) {
  const { game, dice, cardsById, selection, busy, you } = props;
  const [assignments, setAssignments] = useState<RangeAssignment[]>([]);

  const youAreActive = you === game.activePlayerId;
  const yourRangeDice = dice.filter(
    (d) =>
      d.controllerId === you &&
      (d.zone === "FieldZone" || d.zone === "AttackZone") &&
      hasKeyword(d, cardsById, "Range"),
  );

  // Submitted and waiting: the active player has spoken and the window is
  // holding for the opponent.
  if (youAreActive && game.rangeSubmittedByActivePlayer) {
    return (
      <div className="action-tray combat-panel">
        <p className="hint">
          Range assigned. Waiting for your opponent to assign theirs - all Range damage is dealt at once.
        </p>
      </div>
    );
  }

  // Your turn to answer, but only after the active player has gone. They
  // hold priority (rule 2.6.5.7), and going first is what that buys them.
  if (!youAreActive && !game.rangeSubmittedByActivePlayer) {
    return (
      <div className="action-tray combat-panel">
        <p className="hint">Waiting for the active player to assign Range.</p>
      </div>
    );
  }

  if (yourRangeDice.length === 0) {
    return (
      <div className="action-tray combat-panel">
        <p className="hint">You control no Range dice. Waiting for the Range window to resolve.</p>
      </div>
    );
  }

  const primaryDie = dice.find((d) => d.id === selection.primary) ?? null;
  const isRangeSelected = primaryDie !== null && yourRangeDice.some((d) => d.id === primaryDie.id);
  const targetId = selection.secondary[0];
  const targetDie = targetId ? (dice.find((d) => d.id === targetId) ?? null) : null;
  const isTargetSelected =
    targetDie !== null &&
    targetDie.controllerId !== you &&
    (targetDie.zone === "FieldZone" || targetDie.zone === "AttackZone") &&
    (targetDie.status === "Character" || targetDie.status === "SidekickCharacter");

  function addAssignment() {
    if (!isRangeSelected || !isTargetSelected || !primaryDie || !targetDie) return;
    setAssignments((prev) => [...prev, { rangeDieId: primaryDie.id, targetDieId: targetDie.id }]);
    props.onClearSelection();
  }

  function removeAssignment(index: number) {
    setAssignments((prev) => prev.filter((_, i) => i !== index));
  }

  return (
    <div className="action-tray combat-panel">
      <p className="hint">
        {youAreActive
          ? "Each of your active Range dice may deal its Range value to an opposing Character die. You assign first; your opponent answers, and both sides' damage lands together."
          : "The active player has assigned their Range dice. Assign yours - all Range damage is dealt at once, so theirs is already locked in."}
      </p>

      <p className="hint">Your Range assignments</p>
      {assignments.length === 0 && <p className="no-actions">None yet.</p>}
      <ul className="combat-attacker-list">
        {assignments.map((a, i) => {
          const rangeDie = dice.find((d) => d.id === a.rangeDieId);
          const target = dice.find((d) => d.id === a.targetDieId);
          return (
            <li key={`${a.rangeDieId}-${a.targetDieId}-${i}`}>
              <span className="combat-attacker-name">
                {rangeDie ? dieLabel(rangeDie, cardsById) : a.rangeDieId}
              </span>
              {" → "}
              <span className="secondary-chip">
                {target ? dieLabel(target, cardsById) : a.targetDieId}
                <button className="chip-remove" disabled={busy} onClick={() => removeAssignment(i)}>
                  x
                </button>
              </span>
            </li>
          );
        })}
      </ul>

      <div className="selection-summary">
        <span className={primaryDie ? "primary-chip" : "empty-hint"}>
          {primaryDie ? dieLabel(primaryDie, cardsById) : "no Range die selected"}
        </span>
        {targetDie && <span className="secondary-chip">{dieLabel(targetDie, cardsById)}</span>}
        <button className="clear-btn" onClick={props.onClearSelection}>
          Clear selection
        </button>
      </div>

      <div className="tray-actions">
        <div className="tray-action">
          <button disabled={busy || !isRangeSelected || !isTargetSelected} onClick={addAssignment}>
            Add Range Damage
          </button>
          <span className="hint">Primary = your Range die, secondary = its target</span>
        </div>
        <div className="tray-action">
          <button disabled={busy || assignments.length === 0} onClick={() => props.onConfirm(assignments)}>
            Confirm Range ▶
          </button>
          <span className="hint">
            {assignments.length === 0 ? "None queued" : `${assignments.length} queued`}
          </span>
        </div>
        <div className="tray-action">
          <button disabled={busy} onClick={() => props.onConfirm([])}>
            Skip (no Range damage) ▶
          </button>
          <span className="hint">You deal no Range damage this combat</span>
        </div>
      </div>
    </div>
  );
}
