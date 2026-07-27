export function HowToPlay(props: { onClose: () => void }) {
  return (
    <div className="modal-backdrop" onClick={props.onClose}>
      <div className="modal how-to-play" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>How to Play</h2>
          <button className="modal-close" onClick={props.onClose} aria-label="Close">
            ×
          </button>
        </div>

        <div className="modal-body">
          <p>
            This assumes you already know Dice Masters. It's a rundown of how this particular
            site maps onto the physical game - not a rules primer.
          </p>

          <h3>Selecting dice</h3>
          <p>
            Click a die (or a stack of identical dice) to make it the <strong>primary</strong>{" "}
            selection - it's highlighted orange. Click others to add them as{" "}
            <strong>secondary</strong> selections - highlighted blue. What secondary means depends
            on the action: energy to spend, ability targets, or extra attackers. The Action Tray
            at the top of the page shows your current selection and whatever actions are legal for
            it right now - if nothing's listed, that combination isn't a legal action in the
            current step. "Clear selection" resets both.
          </p>
          <p>
            Clicking a stack of identical dice again adds one more from that stack to the
            secondary selection; once every die in the stack is selected, clicking again removes
            the most recently added one. This is how you pick, say, "2 of these 3 identical
            Sidekicks."
          </p>

          <h3>Turn controls</h3>
          <p>
            The buttons under the header are the raw engine actions, one per button, run in the
            order the physical game's turn structure requires:
          </p>
          <ul>
            <li>
              <strong>Clear &amp; Draw</strong> - start of turn: clears used/attacking dice back to
              the bag as appropriate and draws for Prep Area.
            </li>
            <li>
              <strong>Advance Step</strong> - moves from Prep Area to Main Step, and later from Main
              Step into the Attack Step.
            </li>
            <li>
              <strong>Roll &amp; Reroll</strong> - rolls your Prep Area dice; any dice you've
              selected first are the ones rerolled instead of kept.
            </li>
            <li>
              <strong>Enter Attack Step</strong> / <strong>Skip Attack Step</strong> - choose
              whether to attack this turn.
            </li>
            <li>
              <strong>Declare Blockers</strong> / <strong>Assign Combat Damage</strong> - currently
              only support declaring no blockers; real blocker assignment isn't wired up in the UI
              yet (see the design doc's next-steps list).
            </li>
            <li>
              <strong>Clean Up</strong> - ends the turn.
            </li>
          </ul>
          <p>
            Most Main Step actions (Purchase, Field, Use Action Die) don't live in this row -
            select the die you want to act on and use the Action Tray instead.
          </p>

          <h3>The board</h3>
          <p>
            Each player's zones are laid out top-to-bottom roughly by how often you touch them:
            Attack Zone, Field Zone, and Reserve Pool are shown prominently since that's where
            most Main Step and Attack Step actions happen; Bag, Prep Area, Used Pile, and Out of
            Play sit below as reference; the Unpurchased roster is collapsed by default since it's
            just your remaining buy pool.
          </p>

          <h3>Known gaps</h3>
          <p>
            This is a work-in-progress dev console more than a polished game client. Global
            ability targeting, real per-attacker blocker assignment, and keyword behavior
            (Overcrush, Regenerate, etc.) aren't fully wired up yet - if an action you expect isn't
            available, that's likely why.
          </p>
        </div>
      </div>
    </div>
  );
}
