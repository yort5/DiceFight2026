import { useState } from "react";
import { dieLabel, hasKeyword } from "./dieHelpers";
import type { BlockAssignment, CardDef, Die, GameState } from "./types";

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
