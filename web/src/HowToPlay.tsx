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

          <h3>Advance to:</h3>
          <p>
            The status bar's "Advance to:" buttons are always exactly whatever's legal to click
            right now, in plain-language order - so it's never a guessing game between "click a
            named action" and "click a generic Advance Step." Concretely, per step:
          </p>
          <ul>
            <li>
              <strong>Clear &amp; Draw step</strong> - first "Clear &amp; Draw" (unspent Reserve
              Pool energy goes to the Used Pile, any dice already sitting in the Prep Area from a
              KO or a Prep effect move out to wait alongside this turn's draw, then you draw new
              dice from the Bag), then once that's done, "Roll &amp; Reroll ▶".
            </li>
            <li>
              <strong>Roll &amp; Reroll step</strong> - first "Roll (N dice)" (rolls this turn's
              draw straight into the Reserve Pool - no need to select anything, it's always all of
              them). Once rolled, you get exactly one reroll decision (rule 2.4.3/2.4.4): either
              select any dice you want to reroll and use the Action Tray's "Reroll Selected", or
              select none and click "Main ▶" directly if you're happy with the roll. Either way you
              land in Main immediately after - "Reroll Selected" advances the step for you the
              moment it resolves, since nothing else is legal here afterward.
            </li>
            <li>
              <strong>Main step</strong> - two options, since this is a real fork: "Attack ▶" or
              "Clean Up (skip attack) ▶". Purchase/Field/Use Action Die/Global abilities aren't
              "advancing" - select the die you want to act on and use the Action Tray instead.
            </li>
            <li>
              <strong>Attack step</strong> - declaring attackers isn't a step-advance button:
              select the attacking die/dice on the board and use the Action Tray's "Declare
              Attackers." Once they're declared, the tray is replaced by the blocker builder:
              click an attacker, then the blocker(s) for it, then "Assign Selected Blocker(s)" -
              repeat per attacker (an attacker left with nothing assigned is unblocked), then
              "Confirm Blockers ▶". If anything was actually blocked, a damage-split form appears
              alongside the Action Tray - split each blocked attacker's full attack value across
              its blocker(s), then "Confirm Damage ▶"; an all-unblocked attack skips straight to
              the one-click "Assign Combat Damage (no blocks) ▶" instead.
            </li>
            <li>
              <strong>Clean Up step</strong> - "End Turn ▶".
            </li>
          </ul>
          <p>
            The collapsed "Manual step actions (advanced)" panel underneath has the same actions
            spelled out individually and always visible, each disabled unless it's actually legal
            right now - only worth opening if you want the raw controls rather than the guided
            "Advance to:" flow.
          </p>

          <h3>The board</h3>
          <p>
            Each player's zones follow the shape of the physical playmat: Attack Zone across the
            top, Field Zone below it, then Used Pile/Reserve Pool/Prep Area side by side (Reserve
            Pool is the middle one - where dice actually land when rolled), Out of Play under Used
            Pile, and the Bag at the bottom - color-tinted the same way the mat is. The Unpurchased
            roster sits underneath as a low-traffic reference zone, collapsed by default since it's
            just your remaining buy pool. Unpurchased dice show their purchase cost and required
            energy type(s) right on the chip. A die actually showing a rolled face - anything in
            the Reserve Pool, Field Zone, or Attack Zone, the only zones where that's meaningful -
            gets a face badge instead of plain text: a character face shows fielding cost
            upper-left/attack upper-right/defense lower-right/damage taken (if any) lower-left,
            mirroring a real character die/card, while an energy or Action face just shows its
            symbol enlarged. Those zones also stop collapsing identical dice into a "×N" chip, so
            e.g. two Sidekicks that both rolled Bolt show as two separate badges. Everywhere else
            dice stay compact (small icon + text, collapsed by count) since nothing there is
            showing a face that matters. Hover any die (in any zone) for its full subtitle and
            ability text - useful since the same character name can have several
            differently-worded printings.
          </p>

          <h3>Global Abilities</h3>
          <p>
            The sidebar on the right lists every card with a scripted Global ability and its energy
            cost - Global abilities aren't tied to a die selection the way everything else is
            (either player can trigger any card's Global, whether or not they own or control a die
            of it), so they get their own flow instead of a spot in the Action Tray. Click{" "}
            <strong>Use</strong>, pick who's paying, click that player's Reserve Pool energy on the
            board and <strong>Confirm Energy</strong>, then click a target on the board and{" "}
            <strong>Confirm Target(s)</strong> (or <strong>Skip</strong> if the ability doesn't need
            one) - the Action Tray is replaced by a status line while this is in progress.{" "}
            <strong>Cancel</strong> backs out entirely. Only cards with a fully scripted Global show
            up here; a card's plain-text Global ability (not yet wired into the engine) won't.
          </p>

          <h3>Known gaps</h3>
          <p>
            This is a work-in-progress dev console more than a polished game client. Keyword
            behavior (Overcrush, Regenerate, etc.) isn't simulated yet - it's tracked as data on a
            card but has no effect on combat - if an action you expect isn't available, that's
            likely why.
          </p>
        </div>
      </div>
    </div>
  );
}
