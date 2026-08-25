# Rebuilding the rules engine — what's changing

Quick recap since you already know the backstory: we're rebuilding the
ability system around a small, fixed set of reusable building blocks
instead of one-off code per card. Turn structure, combat, zones, etc.
are untouched. We validated the new vocabulary against your Orange Ban
list, and then against the *entire* Dark Phoenix Saga set we already
built (145 cards) to see how it held up at real scale: **75% fit
cleanly, no changes needed.** The examples below are real cards, not
made-up ones.

## a) Tweaked slightly, but still fully included

**Fungible-choice text → auto-resolved.**
Falcon: *"each player must field a Sidekick from their Used Pile if
able."* Real Sidekick dice are all identical, so "choose one" isn't
actually a decision — the engine just fields one instead of stopping
to ask "which of these identical dice do you want?"

**A timing restriction gets rounded up to "any time you could normally
use it."** Gladiator's Global (all three of his Orange Ban printings
share it): one raw-text version reads *"Pay Fist **when you attack**"*
for the "your dice can't be targeted by Action Dice or Globals"
effect. We're not tracking "did you declare an attack yet" as its own
piece of state, and every other Global in the game is already usable
during your Main Step or during the Attack Step's action window — so
this one just follows that same rule rather than getting a bespoke
"only mid-attack" restriction. Makes the ability very slightly more
flexible than a hyper-literal reading, not less.

That's actually a short list. Every genuine "you may" stays a genuine
choice — an earlier draft of this doc claimed a couple of "you may"
cards (Rogue's stat-swap, one of Moira's abilities) would just always
happen since declining "seemed pointless." Wrong: declining is a real
choice even with no cost attached — you might not want the effect, or
using it might hand your opponent a trigger for one of THEIR reactive
abilities. Fixed — both stay real choices, and that's now a standing
rule for how we build every card, not just those two.

**Good news found by checking the full DPS set**: Loyalty Counters
(the little per-card tally some abilities put on or read — Jean Grey,
Magneto, Gladiator, and others) turned out to have no home in the new
system at all — a real gap our smaller samples missed. It's common
enough (six-plus cards just in this one set) and cheap enough to add
properly that it's going in for real, not getting simplified.

## b) Genuinely doesn't fit the engine's architecture

This is a narrower, different question than "we haven't built that
piece yet" (see the next section) — these are cards whose text breaks
an assumption the whole engine is built on, not ones that just need
one more building block added to the set. Three real examples,
already identified as exactly this kind of problem back when we were
building the current engine (not new — we hit these before and set
them aside for the same reason):

- **Copying/impersonating another card.** Forge, "Reverse Engineer":
  *"if an opponent uses an action die, roll it — you may use its
  effect."* That means running someone else's card ability as if it
  were yours. Every part of the engine assumes an ability runs under
  its real owner; making that swappable mid-resolution isn't "one more
  ability type," it's a different rule about what's allowed to happen
  at all. (This is also exactly the shape a "Doppelganger"-style
  name-and-ability-copying card would hit.)
- **Canceling an ability that's already happening.** Blink, "Warp
  Portals": *"you may pay Mask and 1 life to cancel that [opponent's]
  Global Ability."* The engine can reduce, prevent, or redirect an
  effect's outcome — it has no notion of stopping an ability from
  running at all once it's been triggered. Different category, not a
  bigger version of what already exists.
- **Spend as much of a resource as you want, uncapped.** Explosion:
  *"you may spend any number of Bolt energy, for each that you do,
  deal 1 damage."* Every damage/effect amount elsewhere is determined
  by game state (how many dice match something, etc.) — here the
  player picks the pool size themselves with no ceiling, as part of
  using the ability. That's a structurally different kind of choice.

Cards like these three would need real, bespoke engine work
regardless of how the rest of the vocabulary evolves — they're not on
a "build this template eventually" list the way the ability-blanking
and live-number gaps from before are.

A fourth flavor, smaller but real: a handful of cards care about
**exactly which dice paid for something.** Bishop, "I'm Back": *"If
you spend this die as energy to field a character die, add this die
to your Prep Area."* The engine knows a purchase or fielding happened,
but not which specific dice covered the cost — and threading that
through is more machinery than these few cards are worth right now.
Candidates for the same treatment as the three above: simplified or
skipped rather than built.

## Where we'd love input

- For (b): any other cards you already suspect fall in that bucket?
  Better to hear about them now than discover them mid-build.
- Anything in (a) feel like it loses too much of the card?

Fan project, not affiliated with WizKids.
