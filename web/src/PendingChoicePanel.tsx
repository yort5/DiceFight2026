import { useState } from "react";
import { dieLabel } from "./dieHelpers";
import type { CardDef, Die, PendingChoice } from "./types";

// Keyword Corrupt/RedrawFromBag (Cosmic Cube "Infinite Possibilities",
// Rip Hunter) - the server pauses mid-resolution whenever one of these
// fires (see GameState.PendingChoice's own remarks) and every other
// action is rejected until it's answered, so this panel pre-empts
// everything else in App.tsx's render (not Attack-Step-specific - it can
// happen during Clear & Draw or a WhenFielded ability just as easily,
// outside combat entirely). Same checklist shape as InfiltrateWindowPanel,
// but toggles between a single choice (radio, Corrupt) and any subset
// (checkbox, RedrawFromBag) based on `allowMultiple`.
export function PendingChoicePanel(props: {
  pendingChoice: PendingChoice;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  busy: boolean;
  onConfirm: (chosenDieIds: string[]) => void;
}) {
  const { pendingChoice, dice, cardsById, busy } = props;
  const [chosen, setChosen] = useState<string[]>([]);

  const candidates = pendingChoice.candidateDieIds
    .map((id) => dice.find((d) => d.id === id))
    .filter((d): d is Die => d !== undefined);

  function toggle(id: string) {
    if (pendingChoice.allowMultiple) {
      setChosen((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
    } else {
      setChosen([id]);
    }
  }

  return (
    <div className="action-tray combat-panel">
      <p className="hint">{pendingChoice.description}</p>

      <ul className="combat-attacker-list">
        {candidates.map((d) => (
          <li key={d.id}>
            <label>
              <input
                type={pendingChoice.allowMultiple ? "checkbox" : "radio"}
                name="pending-choice"
                checked={chosen.includes(d.id)}
                disabled={busy}
                onChange={() => toggle(d.id)}
              />
              {" "}
              {dieLabel(d, cardsById)}
            </label>
          </li>
        ))}
      </ul>

      <div className="tray-actions">
        <div className="tray-action">
          <button
            disabled={busy || (!pendingChoice.allowMultiple && chosen.length !== 1)}
            onClick={() => props.onConfirm(chosen)}
          >
            Confirm ▶
          </button>
          <span className="hint">{chosen.length === 0 ? "None selected" : `${chosen.length} chosen`}</span>
        </div>
        {pendingChoice.allowMultiple && (
          <div className="tray-action">
            <button disabled={busy} onClick={() => props.onConfirm([])}>
              None ▶
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
