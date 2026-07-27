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

          <h3>Advance Step</h3>
          <p>
            The blue <strong>Advance Step ▶</strong> button next to the step/active-player readout
            is the one control you use every step, in every turn - it moves you to the next step.
            While you're in Roll &amp; Reroll it's disabled until you've rolled at least once; after
            that, clicking it keeps everything as currently rolled and moves on (rule 2.4.3 lets you
            reroll none).
          </p>

          <h3>Step actions</h3>
          <p>
            The dashed row below that holds the raw per-step engine actions - closer to admin/debug
            controls than a polished turn flow, since most of them only make sense (and only do
            something) during their own step:
          </p>
          <ul>
            <li>
              <strong>Clear &amp; Draw</strong> - start of turn: unspent Reserve Pool energy goes to
              the Used Pile, any dice already sitting in the Prep Area (from a KO or a Prep effect
              since your last Roll &amp; Reroll) move out to wait alongside this turn's draw, then
              you draw new dice from the Bag. The Prep Area itself stays empty afterward - it's
              reserved for whatever a card Preps from here on, which won't roll until next turn.
            </li>
            <li>
              <strong>Roll</strong> - only shows up during Roll &amp; Reroll, while there's still
              something unrolled. Rolls this turn's draw plus whatever carried over from the Prep
              Area at Clear &amp; Draw straight into the Reserve Pool - no need to select anything
              first, it's always all of them. After rolling, select any dice you want to reroll and
              use the Action Tray's "Reroll Selected," or just click Advance Step to keep everything
              as rolled. Anything a card Preps after this point sits in the Prep Area untouched
              until next turn's Clear &amp; Draw.
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
