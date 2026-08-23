# DiceFight v2 Vocabulary Spec

**This is the living, authoritative spec for the v2 closed template
vocabulary**, seeded from `V2_PLAN.md`'s Appendix A. Implementing
sessions code against THIS file, not the plan's appendix. Changing any
part of it requires the user's explicit sign-off (`V2_PLAN.md` ground
rule 2) — this file records both the adopted vocabulary and, in a
separate clearly-marked section, proposed-but-not-yet-adopted changes
found during validation.

Produced by Phase 0 (`V2_PLAN.md`) across an extended validation arc:
20 cards → 60 cards → the Orange Ban list → a scripted audit of the
full 145-card DPS set. Status: **FROZEN at the 2026-08-22 gate review
(Part 11), amendment chain F1-F14 all user-signed-off. Part 1 is the
single authoritative spec; ~82% of the DPS set fits it cleanly.**
Parts 2-10 are the validation/decision record in chronological order
(per-card verdicts in early parts predate later amendments); Part 11
is the freeze. Implementing sessions: code against Part 1, file
misfits to `V2_TAIL_POLICY.md`, never amend without sign-off.

---

## Part 1 — The adopted vocabulary (FROZEN 2026-08-22)

**This spec is frozen as of the final pre-implementation gate review
(Part 11).** Further changes route only through the two named design
spikes (ability-blanking incl. named-card lockout, live-value Amounts)
or `V2_TAIL_POLICY.md`, each with user sign-off — never ad-hoc during
implementation. Amendments relative to `V2_PLAN.md` Appendix A are
marked **[F#]** with the finding that introduced them (Parts 3-11).

### Targets — one filter shape, 11 fields

```
TargetFilter {
  Ownership: Any | Own | Opposing          // relative to controller
  Zones: Zone[]                            // default [FieldZone, AttackZone]
  Kind: AnyDie | CharacterDie | ActionDie | Player | DieOrPlayer
  Count: int                               // 0 = all matches (no choice)
  Tags: TagQuery?                          // see below
  Stat: (Attack|Defense|Level|PurchaseCost|FieldingCost|Counter(name), Min?, Max?)?  // ONE threshold [F3][F13]
  Optional: bool                           // "up to Count" vs "exactly"
  Self: bool                               // bypass: resolve to source die
  BindAs: string?                          // [F9] after resolution, remember chosen dice under this name
  Bound: string?                           // [F9] skip resolution; reuse dice bound earlier this ability.
                                           //      Reserved name "event" = the triggering event's subject die.
  AnsweredBy: Own | Opposing = Own         // [F14] who answers this choice (PendingChoice routing);
                                           //      distinct from Ownership, which picks WHICH dice qualify.
                                           //      Also on MayPay, for opponent-answered "you may" offers.
}
TagQuery { AnyOf: string[], NoneOf: string[] }
```

Tags unify v1's affiliations, keywords, card names, Sidekick-ness,
**and printed energy type [F4]**: a die's tag set = its card's
affiliations + keywords + its card name + "sidekick" if applicable +
its printed energy symbol id + granted tags. (Phase 1 validation warns
on symbol-id collisions with affiliation/keyword strings, since they
share a namespace.)

Bindings [F9] live in the interpreter's per-ability-resolution context
(a name → die-ids dictionary), created by `BindAs` at resolution time
and read by `Bound`. `TargetWasKOd`-style conditions are defined
against bindings: the condition's CheckTarget names the binding whose
dice are examined. Bindings are also the designated groundwork for the
deferred live-value-Amounts spike (a future `StatOf(binding)` captures
at bind time, giving rule-3.1.7 simultaneity for free).

### Amounts

```
Amount = Fixed(n) | PerMatch(TargetFilter, multiplier,
                             Distinct: bool = false,        // [F14] count DIFFERENT names/affiliations
                             Unit: Dice | EnergySymbols = Dice)  // [F14] count matching dice, or energy
                                                                 //       symbols shown on them
```

(A live-context value source — "this die's own stat," "the event's
damage amount" — is deliberately NOT adopted; it is the
live-value-Amounts design spike. See Part 4.)

### Durations

```
Duration = EndOfTurn | UntilYourNextTurn | Permanent   // [F14] middle value new
```

### Effect templates (18)

DealDamage, KO (param: `TriggersKOAbilities: bool`, false =
Sacrifice), MoveDie, DrawToZone, FieldDie, Reroll, Spin, SpinToEnergy,
ModifyStat, GrantTag, LifeChange (signed Amount: positive = gain,
negative = lose), PurchaseModifier, CombatFlag, Sequence, MayPay,
Conditional, **DrawAndChooseOne [F6]**, **GrantCounter [F13]**. Base
parameter list: `V2_PLAN.md` Appendix A, plus these adopted
amendments:

- **ModifyStat [F5]**: optional `SetAttack: int?` / `SetDefense: int?`,
  each mutually exclusive with its delta field — an absolute snapshot-
  to-value (implemented as a computed delta modifier, v1 `SetStat`'s
  proven approach), not new bookkeeping.
- **Reroll [F8]**: optional `NonCharacterMoveTo: Zone?` (each rerolled
  die that lands on a non-character face moves there) and
  `DamagePerMoved: int` (damage to the opponent per die so moved) —
  the per-die multi-target pattern (5 v1 users) folded into the node,
  since Sequence+Conditional cannot express per-die branching.
- **DrawAndChooseOne(Count, PlayerTarget, ChosenToZone, RestToZone)
  [F6]**: the target player draws Count dice from their bag; the
  ability's controller chooses exactly one; it goes to ChosenToZone,
  the rest to RestToZone (zones relative to the target player;
  ReservePool destination = rolled, per DrawToZone's convention).
  Covers both v1 `Corrupt` (opponent's bag, UsedPile/Bag) and
  `DrawAndChooseOneToRoll` (own bag, ReservePool/Bag).
- **DealDamage [F11]**: optional `Distribute: bool`. When true, Amount
  is resolved as repeated 1-point choices from Target instead of one
  lump application — the player picks a die, it takes 1 damage, the
  remaining pool decrements, repeat until the pool is exhausted (the
  same die may be chosen more than once). No new choice mechanism:
  this reuses the one player-decision pipeline (`PendingChoice`, Phase
  5) already committed to, just invoked N times instead of once — the
  client is free to offer a fast "hold to auto-fill the rest evenly"
  shortcut over the same repeated-choice API. Covers Cyclops, "Xavier's
  Dream" (DPS140)'s "X damage divided how you choose among any number
  of target character dice" precisely, preserving the real strategic
  choice (who takes damage) that the original round-2 write-up
  approximated away.
- **Spin [F12]**: optional `SetLevel: int?`, mutually exclusive with
  `LevelDelta` — sets the target die directly to an absolute character
  level, ported forward from v1's existing `SpinToCharacterLevel` node
  (never sampled in Phase 0's 60 cards, so never put through sign-off
  until now). Mirrors `ModifyStat`'s `SetAttack`/`SetDefense` [F5] —
  same absolute-vs-delta axis, applied to Level instead of a stat.
  Combined with target bindings [F9], closes Mutation (DPS009)
  cleanly: `Sequence([MoveDie(fieldDie, ToZone:UsedPile),
  MoveDie(usedPileDie, BindAs:"incoming", ToZone:FieldZone),
  Spin(Bound:"incoming", SetLevel:1)])`. (Part 4's claim that bindings
  alone closed Mutation was imprecise — the level-set gap survived
  bindings and needed this separate, small addition.)
- **GrantCounter(TargetFilter, CounterName, Amount) [F13]**: puts
  numeric counters on the resolved target's own *card* (all copies
  share the count — a per-`(player, cardId, counterName)` entry on
  `GameState`, the only card-scoped-not-die-scoped state in the
  model). Read back via `TargetFilter.Stat`'s `Counter(name)` kind and
  the ordinary `CountAtLeast` machinery. Covers Loyalty (6+ DPS
  cards) and almost certainly D&D-set Experience tokens.
- **CombatFlag [F14]**: flag list gains `Unblockable` (other dice may
  not choose to block this one — Falcon "Recon"), completing the set:
  MustBlock | CantBlock | MustAttack | CantAttack | OnlyBlocker |
  Unblockable.
- **MayPay [F14]**: gains `AnsweredBy: Own | Opposing = Own` — an
  opponent-answered offer (Black Widow "Tsarina"'s "your opponent can
  prevent this by spinning down"), routed through the same
  `PendingChoice` pipeline to the other player.

### Conditions (7 kinds)

CountAtLeast, TargetWasKOd, OnBurstFace, LifeComparison, NoKOsThisTurn,
TurnFact, **OnFaceKind(CharacterFace | EnergyFace) [F8]** — the
resolved CheckTarget die's current face kind (for branch-on-roll cards
like Making the Team, typically against a `Bound` die).

### Continuous templates (6)

StatAura, CostModifier, TagAura, CombatRule, DamageModifier,
TargetingProtection — all six gaining **`ActiveWhen: ConditionKind?`
[F2]** (a live board-state gate reusing the 7 condition kinds; absent
= unconditionally active while the source die is active), and
**DamageModifier gaining `Source: Ability | Combat | Any` [F10]**
(which damage it intercepts — the ability-vs-combat axis v1's
`DieStats.ApplyDamage` already proved out).

Further adopted [F14]:
- **CostModifier**: kind list grows to Purchase | Fielding |
  GlobalEnergy | **ActionDieUse**, and gains `Currency: Energy | Life
  = Energy` — covering both the energy tax on using Action dice
  (Lilandra "Freedom Fighter") and the life tax on Actions/Globals
  (Lilandra "Majestrix", Jinzo "Trap Destroyer"), all three proven as
  v1 flags.
- **DamageModifier**: mode list grows Reduce(n) | RedirectToSelf |
  PreventNonCombat | **Amplify(n)** | **Double** (Nick Fury "Patch",
  Cosmic Cube "Energy of the Beyonders", Jerry Lawler). **Ordering
  rule, fixed here since the physical game has no official layering
  rule to defer to: multipliers apply before flat reductions** —
  document at the damage-application choke point and never revisit
  per-card.

### Trigger events (10, per Phase 4 design as amended)

DieFielded, DieKOd, DieDamaged, DieAttacks, DieBlocks, DiceDrawn,
PurchaseMade, TurnStepEntered, DieUsed, **DieFaceChanged [F1]** — plus
paid Global activation as its own trigger kind (not an event).

```
DieFaceChanged {
  Die, PriorFace, NewFace,        // full Face payloads, not just kinds
  Cause: Roll | Reroll | Spin | Effect
}
```

Emitted from EVERY face-mutation site (roll, reroll, ability spin,
energy-face spin) — v1's CheckAwaken funnel comment is the design
precedent: a face-change source that skips emission is the
silently-never-fires bug class. Energize = EventFilter for NewFace
energy with symbol count ≥ 2 during Roll & Reroll; Awaken =
EventFilter for character-level increase, any Cause. Every event's
payload carries its subject die and event-specific values (DieDamaged
carries the damage amount — groundwork for the Amounts spike).

**EventFilter [F14]**: gains the same optional `Stat` threshold shape
as `TargetFilter` (checked against the event's subject die) — for
reactive triggers like Deathbird "Usurper"'s "when you KO an opposing
die with 3D or greater."

---

## Part 2 — 20 cards re-expressed

Source: `src/DiceFight.Engine/Data/SampleCards.cs` (v1, `main` @
`68a8ec9`). Ten common-node cards, five ex-single-use-node cards, five
ex-`Grants*`-flag cards, per `V2_PLAN.md` Phase 0 task 2.

### Bucket A — common-node cards (10)

**1. Power Bolt (DPS011)** — "Deal 2 damage to target character die or player."
```
Trigger: DieUsed (Self)
Effect: DealDamage(Fixed(2), TargetFilter{ Kind: DieOrPlayer })
```
**Verdict: Fit.** Textbook.

**2. Ronan the Accuser, "Treason!" (DPS050)** — "When fielded, lose 1
life. When KO'd, your opponent loses 1 life."
```
Abilities: [
  { Trigger: DieFielded (Self), Effect: LifeChange(Fixed(-1), Whose: Own) },
  { Trigger: DieKOd (Self),     Effect: LifeChange(Fixed(-1), Whose: Opposing) }
]
```
**Verdict: Fit.** Confirms LifeChange's signed-amount design covers
both v1 GainLife/LoseLife in one template.

**3. Storm, "Cloud Cover" (DPS092)** — "When fielded, target character
die with 3A or less can't block this turn."
```
Trigger: DieFielded (Self)
Effect: CombatFlag(TargetFilter{ Kind: CharacterDie, Stat: (Attack, Max: 3) }, CantBlock)
```
**Verdict: Fit.** Validates the single-stat-threshold field.

**4. Rally (DPS013)** — "Move up to 2 Sidekick dice from Used Pile to
Field Zone. \*\* Instead, up to 3."
```
Trigger: DieUsed (Self)
Effect: Conditional(OnBurstFace(double),
  Then: MoveDie(TargetFilter{ Kind: AnyDie, Zones:[UsedPile], Tags:{AnyOf:["sidekick"]}, Count:3, Optional:true }, FieldZone),
  Else: MoveDie(..., Count:2, Optional:true, ...))
```
**Verdict: Fit.** Confirms "Sidekick" as a plain tag composes with
Conditional/MoveDie exactly like any other filter — no special case
needed, unlike v1's dedicated `SidekicksOnly` bool.

**5. Jubilee, "Rebellious Nature" (DPS036)** — "Energize - if you have
less life than your opponent, you may field this die free at level 2."
```
Trigger: Energize (Self)   ⚠ see Finding 1
Effect: Conditional(LifeComparison(Own < Opponent),
  Then: FieldDie(TargetFilter{ Self:true }, Level:2, Free:true))
```
**Verdict: Fit (effect vocabulary); trigger-kind gap — see Finding 1.**

**6. Kitty Pryde, "Right of Passage" (DPS037)** — "Awaken - Prep a die
from your bag."
```
Trigger: Awaken (Self)   ⚠ see Finding 1
Effect: DrawToZone(Count:1, FromZone:Bag, ToZone:PrepArea)
```
**Verdict: Fit (effect vocabulary); same trigger-kind gap as #5.**

**7. Magneto, "Founder of the Brotherhood" (DPS146)** — "While active,
when one of your Brotherhood of Mutants dice is KO'd, KO target
opposing character die. Global: Pay Mask, once per turn, if no dice in
Prep Area, draw a die to Prep Area."
```
Abilities: [
  { Trigger: DieKOd, EventFilter:{ Ownership:Own, Tags:{AnyOf:["Brotherhood of Mutants"]} },
    Effect: KO(TargetFilter{ Kind:CharacterDie, Ownership:Opposing }) },
  { Trigger: Global, EnergyCost:{Mask:1}, OncePerTurn:true,
    Effect: Conditional(TurnFact(PrepAreaEmpty), Then: DrawToZone(1, Bag, PrepArea)) }
]
```
**Verdict: Fit.** "While active" needed no special modeling — a
triggered ability only listens while its source die is fielded, which
is already how the event-subscription registry works (Phase 4 task
3), not a continuous grant. Good confirmation this pattern is free.

**8. Master Mold, "Targeting Mutants" (DPS082)** — "When fielded, KO
target Brotherhood of Mutants character die."
```
Trigger: DieFielded (Self)
Effect: KO(TargetFilter{ Kind:CharacterDie, Tags:{AnyOf:["Brotherhood of Mutants"]} })
```
**Verdict: Fit.**

**9. Psylocke, "Telepath" (DPS088)** — "When fielded, target character
die gets Overcrush."
```
Trigger: DieFielded (Self)
Effect: GrantTag(TargetFilter{ Kind:CharacterDie }, ["Overcrush"], Duration:EndOfTurn)
```
**Verdict: Fit.** Keyword grants are just tag grants in v2 — no
separate `GrantKeyword` vs `GrantAffiliation` split needed.

**10. Spidey's Last Stand (ASM031)** — "Sacrifice a character to draw
and roll 2 dice."
```
Trigger: DieUsed (Self)
Effect: Sequence([
  KO(TargetFilter{ Kind:CharacterDie, Ownership:Own }, TriggersKOAbilities:false),
  DrawToZone(Count:2, FromZone:Bag, ToZone:ReservePool)
])
```
**Verdict: Fit.** Confirms the `TriggersKOAbilities` param cleanly
absorbs v1's separate `Sacrifice` node, and that `DrawToZone`'s
target-zone determines roll-or-not (landing in ReservePool = rolled,
landing in PrepArea/Bag = not) rather than needing a separate flag.

**Bucket A tally: 10/10 fit at the effect-vocabulary level; 2 (Jubilee,
Kitty Pryde) surface the same trigger-kind gap (Finding 1).**

### Bucket B — cards v1 gave a single-use EffectNode (5)

**11. Mutation (DPS009)** — WhenUsed: swap a Field-Zone die with a
non-Sidekick Used-Pile die, spin the swapped-in die to level 1. Global:
spin one own die down a level to spin another up.
```
Global (fits): Sequence([Spin(ownDie, -1), Spin(anotherDie, +1)])
WhenUsed (does not fit cleanly):
  Sequence([
    MoveDie(fieldDieFilter, ToZone:UsedPile),
    MoveDie(usedPileDieFilter, ToZone:FieldZone)
  ])   // achieves the swap...
  // ...but "spin the just-moved-in die to level 1" needs a
  // same-step reference to "the die MoveDie #2 just resolved" -
  // TargetFilter re-resolution afterward can't guarantee hitting
  // that specific die if more than one candidate now qualifies.
```
**Verdict: Misfit** (WhenUsed half). Nearest fix: give `MoveDie` an
optional `EnterLevel: int` parameter for moves into FieldZone
(parallel to `FieldDie`'s `Level`) — sidesteps the cross-step
reference entirely by folding the level-set into the move itself. Not
adopted here; candidate for the tail policy or a future sign-off ask.

**12. Archnemesis (DPS001)** — WhenUsed: two dice deal damage to each
other equal to their own attack. Global: target die's D = its own A.
```
Neither half fits: `DealDamage`'s Amount is Fixed | PerMatch(count),
neither can express "the resolved target's OWN current attack value."
ModifyStat's deltas are fixed too - can't express "set D = A."
```
**Verdict: Misfit.** Needs a third Amount source,
`SelfStat(Attack|Defense)` ("the amount is this resolved target's own
stat"), plus - for the "mutual" half specifically - a guarantee that
both amounts are captured before either damage applies (rule 3.1.7
simultaneity; a plain `Sequence` of two `DealDamage`s does not
guarantee this, since the second would read the first die's
already-changed defense/attack). Real gap, not just missing plumbing.

**13. Colossus, "Piotr" (DPS103)** — "End of your turn, each of your
level 2-3 character dice deals your opponent 2 damage (not per
Colossus die)."
```
Trigger: TurnStepEntered(EndOfTurn), EventFilter:{Ownership:Own}
Effect: DealDamage(
  PerMatch(TargetFilter{Kind:CharacterDie, Ownership:Own, Stat:(Level,Min:2)}, multiplier:2),
  TargetFilter{ Kind:Player, Ownership:Opposing })
```
**Verdict: Fit.** Validates PerMatch's fixed-multiplier-times-live-count
shape, and confirms `TurnStepEntered` needs to carry "whose turn" in
its event filter (already implied by Phase 4's EventFilter.Ownership,
just noting it's exercised here).

**14. Organic Steel (DPS010)** — Continuous: "Prevent up to 2 damage to
target character die and move this die to your Used Pile. If you have
an active X-Men character, also gain 1 life."
```
Conditional half fits: Conditional(CountAtLeast(TargetFilter{Kind:CharacterDie,
  Ownership:Own, Tags:{AnyOf:["X-Men"]}}, 1), Then: LifeChange(Fixed(1)))
Damage-prevention half does not fit: no one-shot "shield the next
instance of damage to this die" effect template exists. DamageModifier
is a continuous (aura) template, not a one-shot targeted effect.
```
**Verdict: Misfit** (damage-prevention half). Candidate fix: either a
new one-shot `PreventDamage(Target, Amount)` effect template, or give
`DamageModifier` a `OneShot: bool` so a continuous-shaped modifier can
also be spent as a single-use shield. Real, recurring Dice Masters
idiom (damage prevention/shielding) — worth a decision, not a one-off.

**15. Making the Team (DPS007)** — "Roll a character die from your
Used Pile. If it rolls a character face, field it for free. Otherwise,
Prep it."
```
Reroll(Target) fits the "roll" half, but no Condition kind expresses
"the just-rolled die landed on a character face vs. an energy face" -
the 6 Condition kinds have nothing like it (OnBurstFace checks burst
marks, not face KIND).
```
**Verdict: Misfit.** Candidate fix: a 7th condition,
`OnFaceKind(CharacterFace | EnergyFace)`, sibling to `OnBurstFace`.

**Bucket B tally: 1/5 fit (Colossus). Expected — this bucket was
deliberately the hard cases.**

### Bucket C — cards v1 modeled as a bespoke `Grants*` CardDef flag (5)

**16. Captain Marvel, "Alpha Flight" (MSW023)** — "While active, your
Character dice get +1A/+1D."
```
Continuous: StatAura(TargetFilter{Kind:CharacterDie, Ownership:Own}, AtkDelta:1, DefDelta:1)
```
**Verdict: Fit.** Exactly StatAura's purpose.

**17. Darkseid, "Force of Entropy" (BAT117)** — "While active, your
Sidekicks gain Swarm."
```
Continuous: TagAura(TargetFilter{Kind:AnyDie, Ownership:Own, Tags:{AnyOf:["sidekick"]}}, ["Swarm"])
```
**Verdict: Fit.** Confirms v1's own noted subtlety - a granted-Swarm
die's OWN card identity still governs Swarm's own match check - falls
out for free, since TagAura only grants the tag; it never touches how
Swarm-the-keyword itself is evaluated elsewhere.

**18. Deadpool, "Collect THIS!" (DPS108)** — "While active, your
character dice with fielding cost of 2 are free to field."
```
Continuous: CostModifier(Fielding, TargetFilter{Kind:CharacterDie, Ownership:Own,
  Stat:(FieldingCost, Max:2)}, Delta:-2)
```
**Verdict: Misfit (small).** `TargetFilter.Stat`'s kind list is
`Attack|Defense|Level|PurchaseCost` — `FieldingCost` is missing.
Candidate fix: add it as a 5th stat kind. Small, low-risk, narrowly
justified by this real card.

**19. Jean Grey, "Xavier's Dream" (DPS075)** — "While Jean Grey AND one
of your Sidekick dice are active, your opponents must pay 1 extra to
use a Global Ability."
```
Continuous: CostModifier(GlobalEnergy, TargetFilter{Ownership:Opposing}, Delta:+1)
  -- but gated on an EXTRA board-state condition ("own active
  Sidekick") that isn't a property of the target being modified at
  all, unlike everything else CostModifier's TargetFilter can express.
```
**Verdict: Misfit.** Continuous templates have no top-level
"active only when X" gate — every field in the template shapes are
either about scope/target or about the effect amount, none about
whether the whole grant is switched on. Candidate fix: give every
continuous template an optional `ActiveWhen: ConditionKind` gate,
reusing the 6 already-defined Condition kinds (no new condition
vocabulary, just letting the existing set gate a continuous grant too).

**20. Moira, "If It's Real" (DPS084)** — "While Wolverine is active,
Moira gets +1D. When fielded, X-Men get +1A this turn. When Moira is
KO'd, Prep a die from your Used Pile."
```
Continuous half (misfit, same gap as #19):
  StatAura(TargetFilter{Self:true}, DefDelta:1)
    ActiveWhen: CountAtLeast(TargetFilter{Tags:{AnyOf:["Wolverine"]}}, 1)
WhenFielded half (fits):
  ModifyStat(TargetFilter{Kind:CharacterDie, Ownership:Own, Tags:{AnyOf:["X-Men"]}, Count:0},
    AtkDelta:1, Duration:EndOfTurn)
WhenKOd half (fits):
  MoveDie(TargetFilter{Kind:AnyDie, Zones:[UsedPile], Ownership:Own, Count:1}, ToZone:PrepArea)
```
**Verdict: Partial.** 2 of 3 abilities fit cleanly (and `Count:0`
confirms it correctly replaces v1's separate `MatchAll` bool - one
less special case). The continuous "while named card active" self-buff
needs the same `ActiveWhen` gate as #19 - the same fix closes both.

**Bucket C tally: 2/5 clean fit (Captain Marvel, Darkseid), 1 small
independent gap (Deadpool), 2 sharing one gap (Jean Grey, Moira).**

---

## Part 3 — Round 2: expanding to 60 cards

The 20-card sample left the fit rate (13/20) in the ambiguous zone
between the plan's 12-card stop floor and 15-card target, and rested
big conclusions on single-card evidence. Expanded by 40 more cards -
weighted toward finishing off single-use-node coverage (6/23 tested in
round 1 → 22/23 after round 2) and much broader `Grants*` flag coverage
(5/39 → 21/39) - to see whether the round-1 findings hold up under more
evidence, and to surface anything round 1's small sample missed.
Entries below are terser than Part 2's, since the template shapes are
now established; only what's new or notable per card is called out.

### Bucket A round 2 — 10 more common-node cards (#21-30)

**21. Dazzler (MSW026)** — "When fielded, deal 4 damage to target [M]
(Mask-energy) character die." `DealDamage` fits the shape, but
targeting by a die's own **printed energy type** has no home in
`TargetFilter` - it's not in `Tags` (Part 1's tag list is
affiliations/keywords/name/sidekick - energy type was never added) and
there's no dedicated field either. **Misfit — new gap (Finding 4).**

**22. Shocking Grasp (MSW011)** — `Sequence([DealDamage(1, T),
Conditional(TargetWasKOd, Then: MayPay(Cost:none, Then:
MoveDie(Self,PrepArea)))])`. **Fit** — this is literally the card the
plan's own Phase 5 description names as `MayPay`'s motivating example.

**23. Cyclops, "First Class" (DPS025)** — `Trigger: DieFielded,
EventFilter:{Ownership:Own, Tags:{AnyOf:["Founder"]}}, Effect:
DealDamage(2, CharacterDie)`. **Fit** — confirms keyword-as-tag reactive
filtering, and that "while active" needs no modeling (a trigger only
listens while its source die is fielded).

**24. Phoenix, "Psionic Maelstrom" (DPS086)** — `DealDamage(3, T)` then
`Conditional(TargetHasAffiliation, CheckTarget: T, Then: DealDamage(3,
another))`, where **T must be the same resolved die in both steps.**
v1 achieves this by literally sharing one `TargetSpec` object reference
between the two nodes (resolve-once, reuse-the-answer). Appendix A's
`Sequence` has no equivalent - each step's `TargetFilter` re-resolves
independently. **Misfit — same root cause as Mutation (#11): effect
trees need a way for one step to refer to an already-resolved target
from an earlier step**, not just `Self`.

**25. Deathbird, "Treacherous" (DPS029)** — pure `Deadly` keyword, no
effect tree at all. **Fit**, trivially - keyword behavior is engine
code by design (Phase 7), free once the keyword is declared.

**26. Corsair, "Criminal Record" (DPS104)** — `Conditional(CountAtLeast
(opposing-field-zone-dice, 4), Then: Ko(Count:2), Else: Ko(Count:1))`.
**Fit** — good confirmation `CountAtLeast` composes with a Count-varying
`Then`/`Else`.

**27. Jubilee, "X-Men Field Leader" (DPS143)** — `Trigger: DieFielded
(unfiltered, own), Effect: Sequence([DealDamage(1,Player{Opposing}),
DealDamage(1,CharacterDie)])`. **Fit.**

**28. Lab Test (DPS005)** — Continuous Basic Action: `Trigger: DieUsed,
Effect: Reroll(CharacterDie in ReservePool, Own)`. **Fit** - "Continuous"
is just a keyword tag gating when/how the die can be activated, not a
different trigger event.

**29. Dark Phoenix, "Enemy of the Shi'ar" (DPS067)** — three abilities
(`Ko` by affiliation tag, `DealDamage` to player on attack, a Global
`Sequence([Ko(own), PurchaseModifier(Delta:-2)])`). **Fit**, all three -
good `PurchaseModifier` confirmation. *Side note, not a vocabulary
gap*: the card text says the discount floors at a **minimum of 1**, but
Phase 3's `GetPurchaseCost` design says "floor 0" - that's a real rules
mismatch (the physical game's purchase-cost floor is 1, not 0) worth
fixing in the Phase 3 write-up regardless of the vocabulary decision.

**30. Gambit, "Unless I Got Someone to Play With" (DPS112)** — "reroll
up to 2 opposing dice; each that doesn't roll a character face moves to
their Used Pile." Same shape as `RerollAndMoveUnlessCharacter` (a
common-tier node, used 5 times in v1) and the same missing condition
Making the Team (#15) already flagged - "did this die just roll onto a
character face or not." **Misfit — reinforces #15's gap with a second,
independent card**, and since the underlying v1 node has 5 total real
users (not 1), this upgrades from a tail curiosity to a systemic gap.

**Bucket A round 2 tally: 7/10 strict fit; 3 misfit, all mapping to
findings (2 new-but-cheap, 1 reinforcing an existing one).**

### Bucket B round 2 — 14 more ex-single-use-node cards (#31-44)

Brings single-use-node coverage from 6/23 to 22/23 v1 nodes tested.

**31. Cosmic Cube (MSW002)**, `SwapLife` — "switch life totals." No
Amount/effect shape reads-and-cross-assigns two live values. **Misfit**
- same value-source family as Archnemesis (#12).

**32. Rogue, "Mrs. X" (DPS049)**, `SwapAttack` — "swap Rogue's A with
target's A." **Misfit**, same family as #31/#12.

**33. Rogue, "Strength Absorption" (DPS151)**, `SetStat` — "target has
0A this turn." `ModifyStat` only has deltas, not an absolute set.
**Misfit — new gap (Finding 5)**, and it's exactly why v1 itself needed
a separate `SetStat` node alongside `ModifyStat`.

**34. Black Widow (GOTG005)**, `SetCallOutTarget` (Call Out keyword) —
`CombatFlag(opposing CharacterDie, OnlyBlocker)`. **Fit** - Appendix
A's own table already anticipated this mapping.

**35. Ronan the Accuser, "No Mercy" (DPS090)**, `OpponentKOsOwnCharacterDie`
— "each player KOs a character die THEY control," where the
opponent's half must be answered by the opponent, not the ability's
controller. `TargetFilter.Ownership` picks which dice qualify, not who
answers the choice. **Misfit** - narrow (1 v1 user), tail item.

**36. Corsair, "Recruiting a Crew" (DPS024)**, `GrantNextPurchaseGoesToBag`
— `PurchaseModifier(Delta:0, GoesToZone:Bag)`. **Fit** - Appendix A's
table already lists `GoesToZone?` as a parameter.

**37. Gambit, "I Like Solitaire" (DPS072)**, `GrantCantFieldCharacterDiceThisTurn`
— its `Conditional(TurnFact(FieldedNoOtherCharacterThisTurn))` half
fits directly (that `TurnFact` variant is already in Appendix A), but
the "no more fielding this turn" restriction itself has no template.
**Misfit** (the restriction half) - narrow, tail item.

**38. Invisible Woman (MSW032)**, `ForceBlock` — `CombatFlag(Target,
MustBlock)`. **Fit.**

**39. Falcon (MSW027)**, `FieldSidekickForEachPlayer` — "each player
fields A Sidekick if able," where Sidekicks are fungible so there's no
real choice, even at `Count:1`. `TargetFilter` has `Optional` (up-to-N)
but no "resolve arbitrarily, don't prompt" mode. **Misfit** - narrow,
tail item.

**40. Cyclops, "Xavier's Dream" (DPS140)**, `DividedDamageAmongChosenTargets`
— unbounded player-chosen target count with automatic even split (v1
already approximates the split). `TargetFilter.Count` is a fixed int,
not "1..unbounded, player's choice how many." **Misfit** - narrow (1
user, already an approximation in v1 itself), tail item.

**41. Black Manta, "Deep Sea Deviant" (JL078)**, `DealDamagePerActiveAffiliate`
— "damage = your active Villains," and "Villains" is one of Black
Manta's own two printed (fixed) affiliations, not a dynamic
self-reference. `DealDamage(PerMatch(CharacterDie{Own,Tags:AnyOf:
["Villains"]}, x1), Player{Opposing})`. **Fit** - looked harder than it
is; the affiliation is a literal tag, same as any other.

**42. Polaris (DXM010)**, `Corrupt` — "draw 2, choose 1 to Used Pile,
rest to Bag." Same "draw N, choose exactly 1, branch its destination"
shape as `DrawAndChooseOneToRoll` (#44 below), just different target
zones. **Misfit under the current spec, but notable**: these two v1
single-use nodes look like exactly one general template wearing two
costumes - see Finding 6.

**43. Mister Sinister, "Mutant Supremacist" (DPS083)** — `BlankOpposingTeamText`
(whole opposing side loses ability text) + `BlankTargetText` (one
targeted die). Neither template nor continuous list has an "abilities
don't fire" concept at all. **Misfit — real, structural gap (see
"Consider" below)**, and not a one-off: D'Ken (#46) and Vulcan Power
Suppression (#48) hit the same wall from the continuous side.

**44. Gambit, "Ace in the Hole" (DPS032)** — `Conditional(OnBurstFace,
Then: DrawAndChooseOneToRoll(2), Else: DrawDice(1))`. **Misfit** under
the current spec (same as #42); **fit** once Finding 6's unified
draw-and-choose template lands.

**Bucket B round 2 tally: 4/14 strict fit; 10 misfit (3 resolve via
recommended findings, 3 are the same bigger "Consider" gap, 4 are
narrow tail items).**

### Bucket C round 2 — 16 more ex-`Grants*` cards (#45-60)

Brings `Grants*` flag coverage from 5/39 to 21/39.

**45. Psylocke, "Heiress" (DPS128)**, `GrantsSelfAttackBonusPerMatchingDie`
— `StatAura(Self, AtkDelta: PerMatch(own X-Men dice in Prep Area, x2))`.
**Fit** - confirms `PerMatch` composes inside a continuous template's
delta, as Appendix A's own parenthetical already anticipated.

**46. D'Ken, "Shi'ar Civil War" (DPS141)**, `GrantsOpponentAbilityBlankWhileActive`
— opposing cheap dice "lose their abilities AND are free to field."
Two gaps in one card: ability-blanking (same as #43/#48) and a
cost-side "set fielding cost to 0" rather than a delta (same family as
Finding 5, but on `CostModifier` instead of `ModifyStat`). **Misfit**,
both known families - no new gap type.

**47. Dampening Collar (DPS002)**, `GrantsPreventsOpponentCharacterDiceFromSpinningUp`
— `CombatRule(CantSpinUp, Opposing)`. **Fit** - matches the table
directly.

**48. Vulcan, "Power Suppression" (DPS095)**, `GrantsIgnoresAbilitiesWhileEngaged`
— ability-blanking again, this time scoped to "dice currently engaged
in combat with Vulcan." **Misfit**, same structural gap as #43/#46 -
now 4 independent cards hitting it.

**49. Magneto, "Visionary" (DPS081)**, `GrantsMinimumBlockersRequirement`
— `CombatRule(MinBlockers:2, Tags:{AnyOf:["Brotherhood of Mutants"]})`.
**Fit** - matches the table. (Its Teamwatch/Global abilities also fit
cleanly via `PrepFromBag`/`Conditional`.)

**50. Blob, "Immovable" (DPS101)**, `GrantsBlocksMultipleAttackers` +
`GrantsReturnsKOdOpposingSidekickToBag` — the first half is a clean
`CombatRule(BlocksN:3, Tags:{AnyOf:["Blob"]})` fit. The second
("when THIS die KOs an opposing Sidekick in combat, return it to their
bag") needs a trigger effect to act on **the die from the triggering
event itself**, not the ability's own source die - `Self` doesn't cover
this, and it's not one of Appendix A's fields. **Misfit** (2nd half) —
**new gap (Finding 7)**, but likely near-universal: almost any reactive
trigger that "does something to the die that was just KO'd/fielded/
damaged" (not to the listening die itself) needs this, so it's probably
under-counted by "1 card" here.

**51. Bishop, "Tortured Timeline" (DPS019)**, `GrantsRerollOrSpinProtection`
— protected from reroll/spin specifically, not blanket untargetable
(still damageable). `TargetingProtection`'s `From` axis is
Global/Action/Both (WHO), not WHICH EFFECT TYPE. **Misfit** - narrow (2
v1 users total), tail item.

**52. Mystique, "Freedom Force" (DPS085)**, `GrantsOwnDamageReductionFromOpponentAbilities`
— reduces damage from **ability** sources only, not combat. v1's own
comment flags this exact ability-vs-combat distinction as something it
had to add specially. `DamageModifier` doesn't scope by damage source.
**Misfit** (continuous half only - its WhenKOd half fits cleanly via
`MinPurchaseCost`, already in the Stat kinds) - cheap, single-card, but
matches an already-established real rules distinction; noted under
Finding "DamageModifier scoping" below.

**53. Colossus, "Organic Steel" (DPS063)**, `GrantsFirstDamageRedirectToSelf`
— `DamageModifier(RedirectToSelf, Own)`, matches the table, but "the
**first** time each turn" is a usage limiter continuous templates don't
have (triggered Globals get `OncePerTurn`; continuous templates don't).
**Fit**, with a minor noted limitation rather than a hard blocker.

**54. Dark Phoenix, "Destructive Force" (DPS107)**, `GrantsRetaliatesEqualDamageToOpponentWhenDamagedByOpponent`
— actually a reactive trigger in disguise: `Trigger: DieDamaged(Self,
Source:Opposing), Effect: DealDamage(Amount:???, Player{Opposing})`
where the amount must equal **the damage just dealt in the triggering
event**. Same "need a live value source beyond Fixed/PerMatch" family
as Archnemesis (#12) and the two "swap" cards (#31/#32), but the value
here comes from the event's own payload, not a die's current stat.
**Misfit**, same family - broadens Finding "Amount needs a
context-value source" to cover event-payload values too, not just
own-stat reads.

**55. Angel, "Air Support" (DPS097)**, `GrantsGainLifeWhenOpponentTargetsOwnCharacterDie`
— another disguised trigger: "when opponent TARGETS your die, gain
life." None of the 9 planned events fire on target selection itself
(only on things that already happened - fielded, KO'd, damaged, etc).
**Misfit — new gap**, but structurally bigger than the others (it would
mean target resolution itself starts emitting events, blurring the
query/event split Phases 3 and 4 currently keep separate) - flagged
under "Consider," not "Recommended."

**56. Angel, "Xavier's Dream" (DPS137)**, `GrantsSidekickImmunityToOpponentGlobalTargeting`
— `TargetingProtection(Own, Tags:{AnyOf:["sidekick"]}, From:Global)`.
**Fit** - matches the table directly.

**57. Vulcan, "Aggession" (DPS135)**, `GrantsOpponentStatDebuff` —
"opponent's **non-fist** characters get -2D," needing `NoneOf:["Fist"]`
against a die's own energy type as a tag. **Misfit** - same gap as
Dazzler (#21), now confirmed by a second independent card.

**58. Rogue, "Unity Squad" (DPS129)**, `GrantsFieldingCostReduction` —
`CostModifier(Fielding, Tags:{AnyOf:["X-Men"]}, Delta:-1)`. **Fit** -
the "normal" affiliation-scoped case, contrasting cleanly with
Deadpool's stat-threshold-scoped one (#18) once `FieldingCost` is added
to `Stat` kinds (Finding 3).

**59. Kitty Pryde, "Headmistress" (DPS077)**, `cannotBeTargetedByOpponentWhileNamedCardActive`
— `TargetingProtection(Self, From:Both)` gated on Wolverine being
active. **Misfit only via the already-known `ActiveWhen` gap** - a
third independent card hitting Finding 2 (after Jean Grey, Moira),
otherwise confirms `TargetingProtection`'s own shape is fine.

**60. Corsair, "Leading the Starjammers" (DPS064)**, `GrantsMirrorsOwnStatIncreaseToOwnSidekick`
— "if Corsair's A or D is increased BY ANY EFFECT, mirror it onto a
Sidekick." Needs a "this die's own stat was just modified, by
whatever" reactive hook - not one of the 9 events, and structurally
awkward (every stat-modifying code path would need to also emit an
event). **Misfit — new gap**, but rare (1 user) and invasive enough to
implement that it belongs in "Consider" at best, likely tail.

**Bucket C round 2 tally: 6/16 strict fit; 10 misfit (2 resolve via
recommended findings — Finding 2 confirmed a 3rd time, Finding 7
confirmed — 4 are the ability-blanking/amount-source "Consider" gaps,
2 are narrow tail items).**

---

## Findings requiring a decision

Across 60 cards, eight refinements surfaced. All are cheap and
narrowly scoped - none is open-ended vocabulary growth, and each is
either reinforced by two or more independent real cards or is
trivially low-risk on its own. **Recommended for sign-off before Phase
4/6 implementation begins:**

1. **Trigger events need a roll-outcome kind.** Energize/Awaken fire on
   what face a die rolls to, not on any of the 9 planned events. Core,
   common keywords, not a tail case. *(2 cards: #5, #6.)* Add a 10th
   event, `DieRolled { FaceKind: Character | Energy, PriorFaceKind }`;
   Energize/Awaken become `EventFilter`s over it.
2. **Continuous templates need an `ActiveWhen` gate.** *(3 cards: #19,
   #20, #59.)* Add `ActiveWhen: ConditionKind?` to all 6 continuous
   templates, reusing the existing 6 Condition kinds.
3. **`TargetFilter.Stat` is missing `FieldingCost`.** *(1 card: #18,
   low-risk regardless.)* Add it as a 5th stat kind.
4. **A die's own energy type needs to be queryable as a tag (or a
   dedicated filter).** *(2 cards: #21, #57.)* Add each die's printed
   energy type to its auto-derived tag set (parallel to how
   affiliations already work), so `Tags:{AnyOf:["Mask"]}` /
   `NoneOf:["Fist"]` just works.
5. **`ModifyStat` needs an absolute-set mode, not just deltas.** *(2
   cards: #33 directly; also the underlying reason v1 needed a
   separate `SetStat` node at all.)* Add optional `SetAttack: int?` /
   `SetDefense: int?`, mutually exclusive with the delta fields.
6. **Unify `Corrupt` and `DrawAndChooseOneToRoll` into one template.**
   *(2 cards: #42, #44; both are "draw N, choose exactly 1, branch its
   destination," differing only in the two zones.)* Add
   `DrawAndChooseOne(Count, ChosenToZone, RestToZone)` - net **shrinks**
   the total template count relative to keeping either as a one-off.
7. **Effects need to target "the die from the triggering event,"
   distinct from `Self`.** *(1 card directly - #50 - but the pattern
   generalizes to almost any reactive trigger that acts on the event's
   subject rather than its own source die, so it's likely under-tested
   here, not over-claimed.)* Add `TargetFilter.EventSubject: bool`
   alongside `Self`.
8. **`OnFaceKind` condition (branch on rolled-to-character-vs-energy
   face).** *(Upgraded from round 1's "not recommended": 2 cards
   directly - #15, #30 - but the underlying v1 node
   (`RerollAndMoveUnlessCharacter`) has 5 total real users, all of
   which need this.)* Add a 7th condition kind,
   `OnFaceKind(CharacterFace | EnergyFace)`.

**Worth a deliberate design session, not a quick add** (real,
structural, reinforced by multiple cards - but each has more surface
area or architectural interaction than 1-8 above):
- **Ability-blanking.** 4 independent cards (#43 x2, #46, #48) hit
  this from both the continuous and one-shot sides. Real recurring
  Dice Masters mechanic, but touches ability-execution broadly - a
  `BlankAbilities` effect/continuous family is the likely shape, but
  deserves its own design pass rather than being bolted on here.
- **`Amount`/effect values need a live-context source beyond
  `Fixed`/`PerMatch`.** 4 cards (#12 x2, #31, #32, #54) want "this
  resolved die's own current stat" or "the triggering event's own
  payload value" as an amount. Real and recurring, but Archnemesis's
  simultaneity requirement (both amounts captured before either
  applies) makes the correct semantics non-trivial.
- **A `DieTargeted` trigger event.** 1 card (#55), but architecturally
  bigger than it looks - it means target resolution itself starts
  emitting events, blurring the Phase 3 (query) / Phase 4 (event)
  split that's otherwise clean.
- **`DamageModifier` needs a damage-source scope** (Ability vs. Combat).
  1 card (#52), cheap, and matches an already-known v1 rules
  distinction - lowest-stakes item in this tier, could reasonably move
  to "Recommended" if the user wants a 9th item.

**Tail** (rare - one confirming card each; defer to
`V2_TAIL_POLICY.md` when Phase 8 reaches them):
- Cross-player "opponent answers their own choice" (#35).
- Whole-turn "can't field any more character dice" flag (#37).
- No-choice/fungible single-target resolution at Count=1 (#39).
- Unbounded, player-chosen-count divided damage (#40).
- `TargetingProtection` narrowed to specific effect types, not just
  source (#51).
- Per-turn usage limiter on continuous templates (#53, minor).
- `StatModified` reactive hook (#60) - rare and the most invasive of
  the "new event" ideas (would require instrumenting every
  stat-modifying code path), likely never worth it for one card.

## Verdict tally (60 cards)

| Bucket | Strict fit | Misfit | n |
|---|---|---|---|
| A - common nodes | 15/20 | 5 | 20 |
| B - ex-single-use-node | 5/19 | 14 | 19 |
| C - ex-`Grants*` flag | 8/21 | 13 | 21 |
| **Total** | **28/60 (47%)** | **32** | **60** |

**If Findings 1-8 are adopted**, 15 of the 32 misfits resolve, bringing
the fit rate to **43/60 (72%)**. The remaining 17 split into 11
"Consider" cases (mostly the ability-blanking and live-amount-source
families, each reinforced by 3-4 cards) and 6 genuine one-off tail
items.

This second pass **confirms round 1's diagnosis rather than changing
it**: the common-node bucket holds up well (15/20, and every one of its
5 misfits maps to a cheap, reusable fix, not a one-off), while the two
"hard" buckets stay hard in a structured way - not evenly distributed
noise, but a small number of recurring root causes (ability-blanking,
live-value amounts, cross-step/cross-event target references) each hit
by multiple independent cards. That's a better sign than round 1's
smaller sample could show on its own: the vocabulary's failure modes
are a short, addressable list, not an open-ended one.

**Recommendation, updated**: adopt Findings 1-8 (all cheap, all
multiply-confirmed or trivially low-risk) before Phase 4/6. Treat
ability-blanking and live-value amounts as explicit design spikes
*before* Phase 8 reaches the cards that need them (D'Ken, Mister
Sinister, Vulcan Power Suppression, Archnemesis, Dark Phoenix
Destructive Force) rather than either building them speculatively now
or discovering the design problem mid-migration. Leave the 6 tail
items as `V2_TAIL_POLICY.md` entries when Phase 8 reaches them.

This is a recommendation, not an adoption - per ground rule 2, the
user decides.

---

## Part 4 — Architect review of the Round-2 findings (Fable, 2026-08-22)

A design-level evaluation of Part 3, requested by the user. Verdict
first: **the fieldwork holds up — the sample was well-chosen, the
triage tiers are correctly drawn, and none of the 8 findings should be
rejected.** But three technical corrections change how two of them
should be adopted, and one observation Sonnet made repeatedly without
promoting it turns out to be the single most valuable change on the
list. **SIGNED OFF by the user 2026-08-22 (full amended set)** — Part
1 above and the plan's Appendix A now reflect the adopted amendments.

### Corrections to the findings as written

**Correction A — Finding 1's `DieRolled` event is the wrong shape;
it must be `DieFaceChanged`.** Verified against v1 (`TurnEngine.cs`
`CheckEnergize`/`CheckAwaken`): Energize fires only on a **double**
energy face (`EnergyAmount >= 2`), so the proposed
`{FaceKind: Character | Energy}` payload is too coarse to express it.
Worse, Awaken fires from **every spin-up source alike** — v1's own
comment: "Amplify above, or EffectInterpreter's Spin case... all
funneled through this one check so Awaken can't silently miss a source
some future keyword adds." A roll-only event would re-introduce
exactly the silently-never-fires bug class v1 already paid to learn
about (the Awaken/Energize keyword-gate bug in DESIGN_LOG, cited by
plan ground rule 6). Adopt instead:

```
DieFaceChanged {
  Die, PriorFace, NewFace,           // full Face payloads, not kinds
  Cause: Roll | Reroll | Spin | Effect,
  Step                                // already standard event context
}
```

Energize = filter on NewFace being energy with symbol-count ≥ 2 during
Roll & Reroll; Awaken = filter on character-level increase with any
Cause. Same cost to implement as `DieRolled` (one choke point per
face-mutation site — v1 proves those sites already funnel), strictly
more correct.

**Correction B — Finding 8 alone does not actually close the cards it
claims.** `RerollAndMoveUnlessCharacter` is a *per-die* branch over a
multi-target reroll ("reroll up to 2; EACH that doesn't roll a
character moves"). `Sequence([Reroll(T), Conditional(OnFaceKind, ...)])`
can't express "each" — Conditional runs once, not per resolved die.
And Making the Team needs the branch to act on *the specific die just
rolled* (a cross-step reference, Correction C's territory). Adopt
Finding 8's `OnFaceKind` condition (needed for single-die branches),
**plus** fold the multi-die pattern into `Reroll` itself as two
optional params: `NonCharacterMoveTo: Zone?` and
`DamagePerMoved: int` (the Psylocke/Storm printings' "deals 2 damage
per die moved" rider — 5 total v1 users justify fold-in params under
the same ≥5-uses bar the original 16 templates met).

**Correction C — the cross-step target reference isn't a misfit
footnote; it's a ninth finding, and the most valuable one.** Sonnet
hit the same root cause four separate times — Mutation (#11), Phoenix
"Psionic Maelstrom" (#24), Making the Team (#15), and, unnoticed,
Shocking Grasp (#22): that card was counted *Fit*, but its
`TargetWasKOd` check is only well-defined if the Conditional can refer
to *the die damaged in step 1*, which is precisely the shared-target
mechanism #24 was ruled a misfit for lacking. v1 solves this with a
fragile trick (sharing one `TargetSpec` object reference between
nodes). v2 should solve it as a first-class, closed mechanism:

```
TargetFilter gains two fields:
  BindAs: string?    // after resolution, remember the chosen dice under this name
  Bound: string?     // skip resolution; reuse the dice previously bound to this name
Reserved binding "event" = the triggering event's subject die.
```

This one mechanism: closes #24 and Making the Team outright; closes
Mutation (#11) when combined with the `SpinToEnergy` level-set inverse
Appendix A already specifies; makes `TargetWasKOd`'s semantics
rigorous instead of implicit; and **subsumes Finding 7** — the
proposed `EventSubject: bool` becomes `Bound: "event"`, one mechanism
instead of two special cases. It also lays the exact groundwork the
"live-value Amounts" spike needs: an `Amount` that references a
binding's stat, *captured at bind time*, is a natural future solution
to Archnemesis's both-amounts-before-either-applies simultaneity
requirement — the spike stays deferred, but it lands on prepared
ground instead of requiring a retrofit.

### Verdicts on the eight findings

| # | Finding | Verdict |
|---|---|---|
| 1 | Roll-outcome event | **Adopt, amended** to `DieFaceChanged` (Correction A) |
| 2 | `ActiveWhen` gate on continuous templates | **Adopt as written** (3 independent cards; conditions are pure state reads, safe to evaluate at query time) |
| 3 | `FieldingCost` stat kind | **Adopt as written** |
| 4 | Energy type in the tag set | **Adopt as written**, plus one Phase-1 validation rule: warn when a config's symbol ids collide with its affiliation/keyword strings, since they now share a namespace |
| 5 | `ModifyStat` absolute-set mode | **Adopt as written** (v1's own ModifyStat/SetStat split is the proof it's a real axis) |
| 6 | Unified `DrawAndChooseOne` | **Adopt, amended**: needs a `PlayerTarget` param — Corrupt draws from the *target player's* bag (usually the opponent's), DrawAndChooseOneToRoll from your own; without it the merge only covers half its own motivating cards. Honest accounting: this is a 17th template, a shrink only relative to adding both one-offs — still clearly worth it (Corrupt is a multi-set recurring keyword) |
| 7 | `EventSubject` target flag | **Adopt, subsumed** into Correction C's bindings (`Bound: "event"`) |
| 8 | `OnFaceKind` condition | **Adopt, amended** per Correction B (condition + `Reroll` fold-in params) |

Plus two promotions:

- **Finding 9 (new): target bindings** (`BindAs`/`Bound`, Correction
  C). Elevated from Part 3's misfit notes to a recommended adoption.
- **Finding 10: `DamageModifier` gains `Source: Ability | Combat |
  Any`.** Sonnet parked this in "Consider" while noting it "could
  reasonably move to Recommended" — I agree it should: one enum
  parameter, and the ability-vs-combat damage distinction is already a
  proven, engine-level rules axis in v1 (`DieStats.ApplyDamage` was
  specifically reworked to carry it).

### Verdicts on the "Consider" tier — all agreed, with design notes

- **Ability-blanking: agree, defer to a design spike** — but record
  the likely shape now so the spike starts warm: it is naturally an
  **8th query**, `AbilitiesActive(die)`, consulted by the trigger
  registry before firing and by the Global/action activation paths —
  which means it *composes with* the Phase 3 spine rather than
  fighting it. The 4 confirming cards' variety (whole-side, single-die,
  cost-scoped, engagement-scoped) maps cleanly onto "who registers the
  interceptor," which is evidence the query shape is right. Not
  adopted now; the spike should also decide blanking's interaction
  with continuous templates (does a blanked die's own StatAura turn
  off? v1's answer: yes, via the GetCard choke point — v2 should
  match).
- **Live-value Amounts: agree, defer** — with Correction C's bindings
  explicitly named as the groundwork (see above). The spike's open
  question shrinks from "design a value-reference system" to "define
  `StatOf(binding)` / `EventValue` capture semantics."
- **`DieTargeted` event: agree, defer**, and I'd go further than
  Sonnet: this one may deserve *rejection* at the spike stage — it
  couples targeting (a pure query today) to the event stream for one
  card, and Angel "Air Support" approximated even in v1.
- **Tail items: agree with all seven placements.** No changes.

### One plan erratum outside the vocabulary

Sonnet's #29 side note is correct and is a plan bug: Phase 3 specifies
`GetPurchaseCost` "floor 0," but the game's own card text ("costs 2
less, **to a minimum of 1**") and v1's behavior floor purchase costs
at **1**. Fielding costs genuinely floor at 0 (printed-0 faces and
free-fielding exist). The plan text should read: purchase floor 1,
fielding floor 0.

### Impact on the plan if adopted

No phase is added, removed, or reordered; the architecture is
unchanged — this is parameter-level amendment, which is exactly what
Phase 0 existed to produce. Concretely:

- **Phase 1**: +1 validation rule (tag-namespace collisions).
- **Phase 3**: cost-floor erratum; note `AbilitiesActive` as the
  reserved 8th query pending its spike.
- **Phase 4**: 10 events (add `DieFaceChanged`); event payloads must
  carry the subject die and event-specific values (`DieDamaged`
  carries the damage amount — free now, groundwork for the Amounts
  spike).
- **Phase 5**: 17 templates (+`DrawAndChooseOne`); 7 conditions
  (+`OnFaceKind`); `ModifyStat` set-mode; `Reroll` fold-in params;
  binding table in the interpreter's execution context (small: a
  per-ability-resolution dictionary name → die ids).
- **Phase 6**: `ActiveWhen` on all six templates; `DamageModifier`
  source scope.
- **Phase 8**: two named design spikes (ability-blanking, live-value
  Amounts) inserted as explicit tasks before the DPS migration batches
  reach D'Ken/Mister Sinister/Vulcan and Archnemesis/Dark Phoenix
  respectively.

Projected fit rate with the amended adoption set: **~45/60 (75%)** —
Sonnet's 43 plus Mutation and Phoenix closed by bindings — and the
count is now honest where round 2's wasn't quite (Shocking Grasp was
a latent misfit; bindings make its Fit real).

### Sign-off record

The user signed off on the **full amended set** on 2026-08-22:
Findings 1–8 as amended above, target bindings (9), DamageModifier
source scope (10), and the cost-floor erratum. Part 1 of this spec and
`V2_PLAN.md` (Appendix A + affected phase descriptions) were amended
to match in the same commit. The deferred items stand as recorded:
ability-blanking and live-value Amounts are Phase 8 design spikes;
`DieTargeted` is deferred with a rejection lean; the seven tail items
await `V2_TAIL_POLICY.md` entries when Phase 8 reaches them.

**Addendum, same day**: reviewing a player-facing summary of this
document surfaced two more decisions — see Part 5. Both signed off and
folded into Part 1: `DealDamage.Distribute` [F11] and `Spin.SetLevel`
[F12].

---

## Part 5 — Post-sign-off refinements from player-facing review (2026-08-22)

The user drafted a short, plain-language summary of Parts 1–4 for
outside player feedback, and in reviewing it themself flagged two
items before it went out. Both are now adopted (folded into Part 1
above); this section is the rationale record.

**Divided/distributed damage.** Cyclops "Xavier's Dream" (DPS140) was
approximated in Part 2/3 as an automatic even split, on the reasoning
that a real per-target amount chooser "would need new interactive-
choice infrastructure this engine doesn't have yet." That premise was
wrong: the plan already commits (Phase 5 task 2) to routing every
player decision through one `PendingChoice`-style pipeline. A
"distribute N points across chosen targets" effect is just that same
pipeline invoked N times — choose one target, apply 1, decrement,
repeat — with no new server-side choice mechanism required. The user's
own proposed UI (tap per point of damage; hold to auto-fill evenly)
is exactly a fast client-side driver for that same repeated-choice
API, not a different capability. Adopted as `DealDamage.Distribute`
[F11]. Evidence caveat, stated plainly rather than inflated: this
pattern is confirmed by exactly one card in the 60-card sample
(Cyclops); it wasn't independently reinforced the way most other
findings were. Adopted anyway because the fix is cheap, reuses
existing architecture rather than adding to it, and the card text
itself ("X damage divided how you choose") makes clear the design
intent was always a real choice, not an approximation — the original
"no infrastructure for this" reasoning doesn't survive scrutiny.

**Mutation (DPS009).** Flagged by the user as commonly-played and
worth prioritizing. Part 4's claim that target bindings [F9] alone
closed this card was re-examined and found imprecise: bindings solve
the *reference* problem (naming "the die that just moved out of the
Used Pile" unambiguously across `Sequence` steps) but not the *action*
needed on it — setting its level to an absolute value (1), which nothing
in the adopted vocabulary could do; `Spin` was delta-only. v1 already
has the needed shape as its own node (`SpinToCharacterLevel`), simply
never sampled into either Phase 0 round, so it never reached a sign-off
decision on its own. Ported forward as `Spin.SetLevel` [F12], deliberately
mirroring `ModifyStat`'s already-adopted `SetAttack`/`SetDefense` [F5]
— same absolute-vs-delta axis, same precedent, applied to Level. With
both `Spin.SetLevel` and bindings [F9], Mutation's `WhenUsed` ability now
expresses cleanly; its Global ability (a level-for-level trade between
two dice) already fit in Part 2 without needing either.

No other findings, verdicts, or tallies in Parts 1–4 change as a
result of this addendum — these two items were specifically the ones
a human reviewer caught that the card-by-card pass had either
under-argued (Cyclops) or over-claimed as solved (Mutation).

---

## Part 6 — Validating against the "Orange Ban" list (2026-08-22)

The user's observation: random/convenient sampling (rounds 1-2) kept
surfacing new gaps round after round, which isn't a great validation
signal — grab a different handful of cards, find different problems,
repeat forever. Better idea: validate against the community's own
"Orange Ban" list (popular, powerful cards restricted in some formats
to encourage team variety — a few are also outright WizKids-banned).
These are specifically the cards players care most about, and power
outliers are likely to cluster around genuinely distinctive ability
patterns rather than being a random draw — a much more targeted
sample than "whatever we happened to have scripted already."

**Source**: `src/DiceFight.Engine/Data/BulkCards.json` (the full
~3,600-card reference sheet import, real printed text — see the
`dicefight2026-bulk-card-catalog` memory), cross-checked against our
own hand-curated `SampleCards.cs` for the handful of listed cards that
are also DPS/MSW/JL cards already in our engine.

**Result, upfront**: this WAS a better sample. Most of what it
surfaced either re-confirmed cards we'd already triaged correctly, or
reinforced findings already on the deferred list — genuinely NEW gap
*types* were a minority, not a repeat of "every round finds unrelated
new things." The two biggest deferred items (ability-blanking,
live-value amounts) came back far more often than in either prior
round, which is itself useful signal: those aren't edge cases, they're
concentrated in exactly the cards worth prioritizing.

### Already known — no new information

- **D'Ken, "Shi'ar Civil War"** (DPS141): still a misfit — ability-
  blanking, already deferred as a Phase 8 spike.
- **Vulcan, "Aggession"** (DPS135): was a misfit in round 2 (needed
  energy-type-as-tag) — **now fits**, adopted Finding 4 closes it.
  Good confirmation that fix does what it was supposed to.
- **Black Manta, "Deep Sea Deviant"** (JL078): already fit (round 2
  #41).
- **Master Mold, "Endless Sentinels"** (DPS147), **Gladiator** (all 3
  DPS printings): straightforward `PlaceToken` / `TargetingProtection`
  fits, nothing new.

### Existing deferred items — strongly reinforced

- **Ability-blanking** (Phase 8 spike, Part 4): confirmed by *Shriek*
  ("ignore that card's text"), *Magneto, "Magnetic Monster"* ("opposing
  characters lose their abilities"), on top of the already-known
  D'Ken/Mister Sinister/Vulcan Power Suppression. Now 6+ confirming
  cards, several from this specific power-outlier sample — raises this
  from "do a design spike before Phase 8 reaches it" to **do this
  spike early, it's clearly not a tail concern for the cards people
  actually play.**
- **Live-value Amounts** (Phase 8 spike): confirmed by *Mr. Fixit*
  ("+XA where X is his own printed A" — the self-stat-as-amount case)
  and *Vicious Struggle* ("1 damage for each damage you take" — the
  event-payload-as-amount case). Same recommendation: this spike
  matters more than "defer until Phase 8" implied.
- **Cross-player "opponent responds" choices** (tail item, Ronan No
  Mercy): reinforced by *Black Widow, "Tsarina"* ("opponent can
  prevent this by spinning one of their characters down a level") —
  a genuine interrupt/counter-offer shape, not just "opponent answers
  a forced choice." Worth a small bump from "rare tail" to "revisit if
  a third case turns up."

### New patterns, not seen in rounds 1-2

Roughly ranked by how cheap + well-confirmed they look:

1. **`CombatFlag` is missing "unblockable."** *Falcon, "Recon"*:
   "your Sidekicks can't be blocked." The existing flags
   (MustBlock/CantBlock/MustAttack/CantAttack/OnlyBlocker) are all
   about a die's own blocking behavior — none stop OTHER dice from
   choosing to block this one. Cheap, obvious complement to what's
   already there. **1 card, but clearly just a missing enum value.**
2. **`PerMatch` needs a "distinct" mode.** Several cards count
   *different* card names/affiliations, not just matching dice: *Team
   Up* ("+1A/+1D per different affiliation among your active dice"),
   *Half-Elf Bard, "Master"* ("+1A/+1D per other DIFFERENT character
   die"), *Hope Summers* ("per different active X-Men"). **3 cards.**
   Cheap: `PerMatch(Filter, multiplier, Distinct: bool)`.
3. **Energy is sometimes counted by symbol, not by die.** *Lantern
   Ring, "Limited Only by Imagination"* ("1 damage per energy symbol
   in your Reserve Pool matching type"), *Parallax* variants ("at
   least one of each energy type in Reserve Pool"). **2-3 cards.**
   `PerMatch`/`CountAtLeast` currently count dice via `TargetFilter`;
   this wants to count symbols shown, a different unit.
4. **A multi-turn duration, beyond End-of-Turn/Permanent.** *Swords of
   Revealing Light* ("can't attack until the start of your next
   turn"), *Vicious Struggle* ("until your next turn"). **2 cards.**
   `Duration` currently only has two values; needs a third
   (`UntilYourNextTurn` or similar).
5. **"Deny purchase/fielding of a specific named card."** *Magneto,
   "Magnetic Monster"* ("Professor X can't be fielded"), *Blob,
   "Appetite for Destruction"* ("opponent may not purchase or field
   that card's dice"), *Drax, "The Pacifist"* (same shape). **3
   cards**, all from this sample specifically — a real, recurring
   "lockout" pattern among powerful cards, not a one-off. Shape:
   probably a 7th continuous template, or a variant of `CostModifier`
   that can express "infinite cost" / outright prohibition.
6. **Damage-multiplier effects.** *Nick Fury, "Patch"* ("unblocked
   Avengers deal double combat damage"), *Cosmic Cube, "Energy of the
   Beyonders"* ("ability/action damage +2 this turn"), *Jerry Lawler*
   ("blocked/blocking Superstars deal double damage"). **3 cards**,
   concentrated in this sample. None of the 6 continuous templates nor
   17 effect templates multiply/boost damage — `DamageModifier` only
   reduces/redirects/prevents. Worth a `DamageModifier` amplify mode
   or a sibling template.
7. **Player-damage (not die-damage) as a trigger source.** *Hulk,
   "Green Goliath"*: "whenever either you OR Hulk takes damage" — our
   `DieDamaged` event fires on a die taking damage; nothing fires when
   the *player's life* drops. **1 card** so far, but "vengeance on
   life loss" is a recognizable, plausibly-recurring card-text idiom.
8. **"Pay life instead of energy" to use a Global/Action.** *Jinzo,
   "Trap Destroyer"* ("opponent must pay 2 life to use an action die
   or global ability") — matches a v1 flag
   (`GrantsOpponentPaysLifeToUseActionOrGlobal`) that existed but was
   never sampled into either Phase 0 round. **2 known instances.**
   `CostModifier` currently only touches Purchase/Fielding/
   GlobalEnergy cost; this is a different resource (life) entirely.

Smaller, single-card, likely-tail observations (not written up in
full — cheap to note, not worth a decision yet): `DamageModifier`'s
redirect-to-self should probably redirect to a chosen destination
(die OR player), not just "self" (*Jocasta, "Patterned After Janet"*);
`KO` may want an optional destination-zone override (*Lantern Ring,
"Energy Constructs"*: KO'd die goes to Used Pile, not the default);
continuous auras that grant a whole new triggered ability rather than
a tag or stat (*Green Lantern, "Human"*) look structurally bigger than
the others in this list — likely stays deferred rather than adopted.

### Good news: two recent fixes generalized past their motivating card

- **Batgirl, "Babs"** ("deal 4 damage divided any way you choose among
  target opposing characters") is the *second* real card needing
  exactly `DealDamage.Distribute` [F11] — the fix wasn't overfit to
  Cyclops.
- **Ring of Winter** ("move each Dragon die in Used Pile to Field Zone
  at level 3") is the *second* real card needing exactly the
  bindings + `Spin.SetLevel` [F12] combination that closed Mutation —
  same confirmation.

### Correction: four cards wrongly marked unavailable

The user caught this — Venom, Constantine, and the three Secret Wars
cards are all real. Two different mistakes, worth recording precisely
since one of them is a real data bug, not just an oversight in this
review:

1. **Venom, "Angelo Fortunato"**: a typo in my own search ("Fortunado"
   vs. the card's actual "Fortunato") — it was in our data the whole
   time (AVX124).
2. **`BulkCards.json` is stale for at least two sets.** Fetching the
   live reference sheet directly: Marvel Secret Wars (`MSW` — per the
   sheet's own SetInfo tab, NOT the `SW` code, which is a Warhammer
   40K set; this review's own earlier "Secret Wars isn't in the sheet"
   claim was wrong, from that same code confusion) has **153 rows
   live vs. 10 in our imported JSON**. Justice League is missing 14
   rows live-vs-imported, including all three Constantine printings.
   `DPS`'s low count (6) is expected — most DPS cards are hand-curated
   separately and correctly excluded from the bulk file — but MSW's
   and JL's gaps aren't explained by that and look like a genuine
   stale/incomplete import, worth a `python3 scripts/
   import_bulk_cards.py` re-run independent of this vocabulary work.

Real text, pulled directly from the live sheet, and their vocabulary
verdicts:

**Constantine, "Hellblazer" (JL137)**: *"While Constantine is active,
before your opponent's Clear and Draw Step, you may name a character.
If that character is fielded this turn, ignore its text until end of
turn and it cannot attack this turn."* Ability-blanking again — a 7th+
confirming card now, and unusual in targeting a **named-in-advance**
card rather than a filter match, which the deferred spike should plan
for. The "before opponent's Clear and Draw Step" timing and the
`CombatFlag(CantAttack)` half both look like clean fits on their own.

**Invisible Woman, "Interdimensional Adventurer" (MSW141)**: *"When
fielded, reroll 2 target character dice, and all of your character
dice get +3 attack until end of turn."* **Fit** — `Sequence([Reroll,
ModifyStat(Count:0)])`, nothing new.

**Black Panther, "Toppling Doomstadt" (MSW100)**: *"Energize - Your
Mask character dice get +2 attack this turn. While Black Panther is
active, when your opponent fields a character die, you may reroll one
of their other character dice."* **Fit** — the Energize half is
`ModifyStat` gated on the energy-type tag [F4]; the second half is
`DieFielded` with `EventFilter{Ownership:Opposing, ExcludeSelf}` (a
field Part 4's Finding 1 write-up already specified) wrapped in
`MayPay`.

**Terrax, "Namor's Cabal" (MSW131)**: *"While active, when one of your
character dice is KO'd, deal 4 damage to target character die."*
**Fit** — plain `DieKOd(Own)` → `DealDamage`, same shape as several
already-confirmed cards.

All four resolve cleanly against the current vocabulary except for
Constantine's ability-blanking half, which was already deferred.
**Doomcaliber Knight, Ring of Magnetism, and Typhoid Mary remain
genuinely unchecked** — not yet re-verified against the live sheet the
way these four were; worth doing before treating them as truly
missing, given how wrong that assumption turned out to be here.

### Recommendation

Findings 1-4 above (unblockable flag, distinct-count, symbol-counting,
multi-turn duration) look like the same shape as the earlier cheap,
well-justified batch — small, mechanical, each solving a real
confirmed card. Findings 5-8 (deny-a-named-card, damage multipliers,
player-damage trigger, life-cost-for-abilities) are a notch bigger and
would benefit from being decided together rather than piecemeal, the
same way the ability-blanking/live-value spikes were carved out
earlier — none is individually hard, but there are four of them this
round instead of two.

Not adopting anything in this pass without sign-off, per ground rule
2 — this section is findings only. Given how much sign-off back-and-
forth has already happened this session, suggest batching the next
decision rather than another round-trip per item: **either take the
whole set (1-8) in one sign-off now, or bank this document and decide
everything remaining in one pass right before Phase 4/5/6 actually
need it** (nothing here blocks Phase 1-3).

**The remaining three, re-verified against the live sheet (also
present — the earlier "unavailable" claim was wrong for these too):**

**Doomcaliber Knight** (three DPS... erratum, YGO printings —
"Doomcalibur" per the sheet, a second spelling difference from the ban
list). The ban-listed "Fiendish Fighter" (YGO048): *"While this
monster is active, it cannot be the target of action dice or
abilities. Global: ... Your monsters cannot be the target of action
dice or card abilities."* Mostly **fit** via `TargetingProtection`,
though "abilities" reads broader than the `From: Global | Action |
Both` axis currently covers (any triggered ability, not just paid
Global activations) — worth confirming scope when this card is
actually authored, not a new finding on its own. Its two siblings
("Skeletal Warrior," "Dark Cavalry") both **cancel an opponent's
ability/action die mid-resolution** — this isn't a new v2 gap, it's
the same **interrupt/cancel primitive** `RULES_ENGINE_DESIGN.md`
already named as one of four things v1 deliberately left unbuilt (see
next-steps item #15's "deliberately `isImplemented: false`" list). Good
to have it re-confirmed as relevant to v2 too, but it was already a
known gap, not a new one from this pass.

**Ring of Magnetism** (three YGO printings, all "Continuous. Play on a
monster" — attach to an opponent- or self-chosen die, then grant a
combat restriction *scoped to that attached die specifically*, e.g.
"your opponent can only block monsters affected by this die"). This is
structurally different from any of the 6 continuous templates: they
all gate on the granting card's own "while active" status; these gate
on a *separately chosen target's* status. A genuinely new pattern —
**attachable continuous auras** — though it may be specific to YGO's
own design language rather than broadly recurring; not urgent, but
distinct enough to name rather than fold into an existing item.

**Typhoid Mary** (three IG printings). "Red Rubber Boots": a
`CostModifier` gated by `ActiveWhen` [F2] (fits cleanly) plus another
ability-blanking case (further reinforcing that item). "Dissociative
Identity Disorder": fits via `FieldDie` + `CombatFlag` + bindings
[F9]. "Charming" prints a "Recruit" keyword not otherwise seen in our
data — not enough context to assess; flagged rather than guessed at.

**Takeaway**: every one of the four cards originally marked
"unavailable" was actually findable with more careful searching — the
lesson is about the search process, not the vocabulary. All fit
cleanly except where they hit already-known gaps (ability-blanking,
the pre-existing interrupt/cancel primitive). One genuinely new
pattern (attachable continuous auras) surfaced, likely low-priority.

---

## Part 7 — Two corrections from player-summary review, round 2 (2026-08-22)

### Correction: "you may" is never auto-collapsed to "always happens"

The player-facing summary claimed "you may [X]" with no attached cost
gets simplified to "always does X" on the reasoning that declining is
"never rational." The user rejected this outright: there ARE real
reasons to decline even a free, no-cost "you may" — you might simply
not want the effect, or accepting it might hand your opponent a
trigger for one of THEIR beneficial abilities (e.g. a reactive "when
your character's stat changes" effect). That's a real strategic
dimension a collapse silently deletes.

Checking how far this actually reached: exactly **two** v1 cards used
this collapse — Rogue, "Mrs. X" (`SwapAttack`, "you may swap Rogue's A
with target's A") and Moira, "It's Not a Dream" (DPS044, "they may
field it normally" after a forced reroll). Nothing in the already-
adopted v2 findings (F1-F12) depended on this premise — it's an
isolated authoring-policy error, not a vocabulary gap, and the fix is
free: `MayPay` already supports a real yes/no choice with **no** cost
attached (`MayPay(Cost: <no-op>, Then: <effect>)`) — this is exactly
the shape already used for Shocking Grasp's "if that character is
KO'd, you may Prep this die" in Part 2 (#22). The template never
needed a change; the authoring policy applying it inconsistently did.

**Adopted policy, effective immediately for all v2 authoring**: every
"you may [X]" in card text — cost-attached or not — is modeled as
`MayPay`, full stop. Never collapsed to "always happens" based on our
own judgment that declining seems pointless. No sign-off needed for
this one going forward per-card; it's a blanket authoring rule, not a
vocabulary change.

### Clarification: "doesn't fit the architecture" vs. "just needs a template we haven't built"

The user drew a sharper line than Part 6 did: the ability-blanking and
live-value-Amounts gaps are real, but they're *roadmapped* — they need
a new template/query/event, which is buildable within the existing
spine (queries, events, effect templates). That's different in kind
from an ability that breaks an assumption the whole architecture rests
on, no matter how the template list grows. Example given: a
"Doppelganger"-style card that copies another card's **name and
abilities** onto itself — every part of the engine (`GetKeywords`,
`GetFace`, tag resolution, event attribution) assumes a die's
underlying card identity is fixed for its lifetime; full identity
replacement isn't "one more template," it's a different assumption
about what a `DieInstance` is allowed to be.

v1's own design work already vetted three real cards in exactly this
category (deliberately left `isImplemented: false`, each flagged as
needing "a genuinely new class of engine capability," not a bigger
template) — worth carrying forward as the canonical v2 examples, since
they're independently vetted, not newly invented for this document:

- **Identity/control substitution** (the closest real match to
  Doppelganger). Forge, "Reverse Engineer" (DPS111): *"While Forge is
  active, if an opponent uses an action die, roll it. If it shows an
  action face you may use its effect."* Running a die's ability under
  a DIFFERENT controller than whoever actually used it means every
  template's `Own`/`Opposing` logic, every event's controller
  attribution, and every `PendingChoice`'s owner would need to accept
  an override for the duration of one resolution. Same family as
  Doppelganger: both break "this ability runs as its true owner /
  this die is its true card" as a fixed assumption.
- **Cancel an ability that's already running.** Blink, "Warp Portals"
  (DPS100): *"...you may pay Mask and 1 life to cancel that [opponent's]
  Global Ability."* Also Doomcaliber Knight's two non-ban-listed
  printings (Part 6). The ability queue (rule 3.2) only has one kind of
  interrupt — Prevent/Redirect effects that change an outcome. Stopping
  an already-queued ability from resolving AT ALL is a categorically
  different operation, not on that spectrum no matter how many
  Prevent/Redirect-shaped templates get added.
- **Open-ended, uncapped resource-to-effect loops.** Explosion (DPS003):
  *"You may also spend any number of Bolt energy, for each that you do
  you may deal 1 damage to target character die."* Every `Amount` in
  the adopted vocabulary (`Fixed`, `PerMatch`, even the deferred
  live-value idea) has a size determined BY GAME STATE. Here the
  player decides the pool size itself, uncapped, as part of resolving
  the ability — a different shape, not a bigger number.

One v1-flagged card, **D'Ken, "Obsessed" (DPS066)** ("you may use an
action die from either player's Used Pile"), was left in the same
"needs new capability" bucket at the time, but on reflection for v2
specifically it's a weaker fit for this category — it's a real,
substantial rework (which zones a die can be activated from), but
doesn't obviously break an architectural assumption the way the three
above do; it may turn out to be buildable as a normal `TargetFilter`
zone extension once someone sits down with it. Flagged as ambiguous
rather than confidently sorted.

**Why this distinction matters going forward**: Phase 8's tail-policy
list (`V2_TAIL_POLICY.md`) should keep these two categories visibly
separate — "needs a spike, then it's buildable" (ability-blanking,
live-value Amounts) vs. "needs the architecture itself to bend in a
new direction" (identity/control substitution, mid-resolution cancel,
uncapped resource loops) — so a future session doesn't quietly treat
the second category as "just another template to add."

---

## Part 8 — The complete list: every deliberate simplification across the full DPS set + Orange Ban (2026-08-22)

The user asked directly: beyond Gladiator's timing text and the two
corrected "you may" cases, are there other cards altered to fit — or
does the rest work as printed? Answered by sweeping the *entire*
`SampleCards.cs` (not just this session's samples) for every comment
marking a deliberate deviation from literal card text — v1's own
authoring policy required disclosing every one of these, so this list
is close to complete for cards v1 actually scripted (it does not
re-derive new v2-specific simplifications on the ~115 DPS cards v1
scripted with *no* noted deviation at all — those were only spot-
checked via this session's samples, not exhaustively re-verified
against v2 from scratch; see the scope note at the end).

Eight real cases beyond the two already discussed, each cross-checked
against where v2 currently stands:

1. **The Front Line (DPS015)**, Global: *"Target opposing character
   die can't block this turn **unless opponent pays 1 life**."* v1
   drops the "unless" escape hatch entirely (flat `CantBlock`,
   strictly stronger than the real card — no counter-play). **Still
   applies to v2 as currently designed** — `CombatFlag` has no
   "unless the target's controller pays a cost" branch; would need
   `CombatEngine`'s block-declaration step to accept a caller-supplied
   override, not a template addition. Genuinely unaddressed either way.

2. **Moira, "It's Not a Dream" (DPS044)**: *"...reroll it. If it lands
   on an Action face, **they may field it normally**."* A second "you
   may" wrongly collapsed to always-happens — same bug as Rogue "Mrs.
   X", just not yet re-fixed. **Needs the same correction as ground
   rule 8** — flagging for the same fix, not a new issue.

3. **Corsair, "Leading the Starjammers" (DPS064)**: *"...**you may**
   increase the A or D of a Sidekick die you control by the same
   amount."* Two stacked simplifications: the "you may" itself is
   auto-fired (third instance of the same bug — needs the ground-rule-8
   fix too), AND which Sidekick gets the boost is auto-picked (the
   first available one) rather than asked, specifically to avoid two
   `PendingChoice`s opening at once if several Corsair-grant dice get
   buffed simultaneously. That second half is a real, likely-permanent
   constraint (single-open-choice) unless choice-stacking gets built —
   different in kind from the "you may" bug. Moot until the deferred
   "your own stat was just modified" reactive hook (Part 6) exists at
   all — this card can't fire its trigger yet either way.

4. **Wolverine, "Hardened by Madripoor" (DPS096)**: *"When you have at
   least 3 active X-Men character dice, Wolverine **gains** 'Energize -
   Spin this die to level 1.'"* The card only HAS the Energize keyword
   conditionally; v1 prints Energize unconditionally and gates only the
   *effect* behind the count check via `Conditional` (functionally
   equivalent for this card alone, but a die that checks "does
   Wolverine have Energize" independent of whether the condition holds
   would see a difference). **Actually fixable cleanly in v2** now that
   `TagAura` has `ActiveWhen` [F2] — `TagAura(Self, ["Energize"],
   ActiveWhen: CountAtLeast(X-Men, 3))` grants the keyword itself only
   when the condition holds, matching the card's literal wording for
   the first time. Worth doing when this card is migrated, not a
   structural gap.

5. **D'Ken, "M'Kraan Crystal" (DPS106)**: *"...you take no more than 7
   damage during an opponent's turn (further damage is reduced to
   0)."* Left out entirely (the WhenAttacks half is scripted; this
   clause isn't) — a player-life damage CAP, and no choke point for
   player-life changes exists to intercept at. **Still applies to v2 as
   currently designed** — the 7 adopted Phase 3 queries don't include
   one for player life (`GetAttack`/`GetDefense`/etc. are all die-
   scoped); `LifeChange` is a one-shot effect, not an interceptable
   query. Worth a note for whoever designs Phase 3 in earnest: an
   analogous "life-change interception" query may be worth reserving
   the way `AbilitiesActive` already is, rather than discovering this
   gap again mid-migration.

6. **Magneto, "Master of Magnetism" (DPS121) and Mystique, "She Walks
   Among Us" (DPS149)**: *"...spin target opposing character die to an
   energy face **of your opponent's choice**."* Simplified to always
   land on the higher-value double-energy face, on the reasoning a
   rational opponent would pick that anyway. This is the SAME
   underlying gap as the cross-player "opponent answers their own
   choice" pattern from Part 6 (Ronan No Mercy, Black Widow "Tsarina")
   — a **third independent confirming card**. Given `PendingChoice`
   already supports routing a choice to a different controller (used
   for exactly this in Ronan No Mercy's v1 implementation), this looks
   cheaper than its "tail item" placement suggested — **worth
   reconsidering for promotion**: a `TargetFilter.AnsweredBy: Own |
   Opposing` field (distinct from `Ownership`, which picks *which
   dice* qualify, not *who* answers) would close all three cards with
   one small addition, not three separate special cases.

7. **Phoenix, "Psionic Maelstrom" (DPS086)**: card text's "**another**
   target character die" isn't enforced as actually different from the
   first-hit die (no distinctness check). Minor, likely-permanent —
   low stakes, no strong case for building a same-ability
   different-die constraint for this alone.

8. **Angel, "Air Support" (DPS097)**: the "when an opponent targets
   your die, gain life" trigger counts a target CHOICE offered inside
   an untaken `Conditional` branch, not just ones that actually
   resolved — a minor scope-widening approximation. Minor, likely-
   permanent, same reasoning as #7.

### What does NOT need altering

Everything else v1 scripted across the full DPS set carries no noted
deviation at all — including, notably, cards this session already
confirmed migrate cleanly to v2 (Colossus "Piotr," Magneto "Founder of
the Brotherhood," Master Mold's three printings, all three Gladiator
printings' Intimidate/targeting-immunity halves, Cyclops "First
Class," Jubilee "X-Men Field Leader," and everything else fit-checked
in Parts 2, 3, and 6). The Orange Ban list itself added no NEW
altered-not-skipped cases beyond Gladiator's already-covered timing
text — every other ban-listed card checked out as either a clean fit
or a genuine gap (Part 6/7's misfit lists), not an in-between
simplification.

### Scope honesty

This is a complete sweep of every simplification v1 **disclosed** —
close to exhaustive for that category, since disclosure was a standing
authoring requirement. It is **not** a fresh re-verification of all
~145 DPS cards against the v2 vocabulary from scratch; roughly 30 have
been individually checked across this session's three passes (Parts 2,
3, 6), and the other ~115 (scripted by v1 with no noted deviation) have
not been individually re-run against v2's specific template shapes. On
priors from the ones that HAVE been checked, most should port cleanly
— but that's an inference from a sample, not a verified claim about
the remaining ~115. A full sweep would be a genuinely large task (order
of magnitude bigger than any Phase 0 round so far); worth discussing
whether it's worth the cost before Phase 8 actually needs the answer,
versus finding out card-by-card during the real migration the way the
plan already anticipates.

---

## Part 9 — Full audit: all 145 DPS cards against the v2 vocabulary (2026-08-22)

The user asked to verify the "relatively few gaps" impression against
the whole pool rather than the ~30-card sample checked so far. Rather
than manually re-derive a verdict per card (expensive at this scale),
this was done with a script: parse every `DPS*` `CardDef` out of
`SampleCards.cs`, extract which `EffectNode` types and `Grants*` flags
each one uses, and classify each against the same fit/`newgap`/
`consider`/`tail` categories established across Parts 1-8. The
classification table itself (which v1 node/flag maps to which v2
status) is hand-built from everything this session already
established — the script just applies it at scale instead of re-
deriving it per card.

**A real bug was caught and fixed along the way**: the first extraction
pass split the file on each card's declaration line, which
accidentally swept the *next* card's leading comment into the
*current* card's text — e.g. Explosion's "deliberately left
`isImplemented: false`" disclaimer sits between The Front Line's code
and Explosion's own declaration, so it was misread as The Front Line's
own status. Fixed by parsing actual statement boundaries (paren-
balanced from each `CardDef(` to its matching `)`), not text between
declaration lines. Before the fix: 8 cards misreported as
`isImplemented: false`. After: 5, which is the real number.

### Headline result

| Bucket | Count | Share |
|---|---|---|
| **Fits cleanly** (with the F1-F12 amendments + Part 7/8 corrections already adopted) | **109** | **75%** |
| New gap, found by this sweep (not previously identified) | 14 | 10% |
| Already-known deferred item (`Consider` tier — ability-blanking, live-value Amounts, cross-player choice, pay-life-not-energy) | 11 | 8% |
| Narrow/tail (already catalogued in Parts 2/3/6) | 6 | 4% |
| Already vanilla in v1 itself — not a v2 regression, same 5 architecturally-hard cards from Part 7 | 5 | 3% |

**This confirms the "relatively few gaps" read, with real numbers
behind it**: three in four DPS cards need nothing beyond what's
already been decided. The `Consider`/tail/vanilla buckets (22 cards,
15%) are all cards already known about, not new surprises — the
recommended findings from Parts 3-8 are doing real, broad work, not
just patching the handful of cards that happened to get sampled.

### The 14 new gaps — but they're really 4 root causes, not 14 unrelated ones

1. **Loyalty Counters have no place in the adopted vocabulary at all
   (6 cards)**: `JeanGreyPeacefulCoexistence` (DPS035), `Magneto`
   (DPS041), `Gladiator, "The Empire Must Stand"` (DPS073), `Moira,
   "Strength of Foresight"` (DPS124), `Supreme Intelligence` (DPS053),
   `Madelyne Pryor, "Sisterhood"` (DPS079). Loyalty is a real,
   moderately-widespread per-**card** (not per-die) numeric counter
   system — plus a targeting filter (`RequiresLoyaltyCounter`) and a
   condition (`OwnTeamWideLoyaltyCounterCountAtLeast`) that read it.
   Nothing in Appendix A's data model (Appendix B) or effect templates
   accounts for counters at all — tags are boolean present/absent,
   counters are numeric and stack. **This is a real correction to
   earlier work**: round 1's own write-up marked `Gladiator, "The
   Empire Must Stand"` a clean fit without questioning the
   `GrantLoyaltyCounter` node it uses — that verdict was too hasty.
   Given 6+ confirmed users just in DPS (D&D sets' "Experience" tokens
   are almost certainly the same underlying shape), this is a real
   candidate for a **Recommended**-tier addition: a per-card counter
   dict in the data model, a `GrantCounter(TargetFilter, Name, Amount)`
   effect, and a counter-count Stat/Condition to read it back — not
   fundamentally hard, just missed until this full sweep.
2. **Events don't expose what was spent to pay for the thing that just
   happened (4 cards)**: `Bishop, "Time Traveller"` (DPS099, needs to
   know which *card* the spent energy came from), `Bishop, "I'm Back"`
   (DPS059), `Forge, "More Than Firepower"` (DPS031), `Professor X,
   "Dreamer"` (DPS047, both need to know the *energy type* spent to pay
   a fielding/purchase cost). `PurchaseMade`/fielding aren't event
   kinds with rich payloads in the current design — they'd need to
   carry which specific dice paid for the action, not just that it
   happened. Real, moderately common (4 confirmed cards), worth adding
   to the events design when Phase 4 is actually implemented.
3. **`EventFilter` can't filter by the triggering die's own stat, or
   by combat-vs-ability cause (1 card)**: `Deathbird, "Usurper"`
   (DPS069) — "when you KO an opposing die **with 3D or greater**."
   `TargetFilter` has a stat threshold; `EventFilter` doesn't. Narrow
   on its own, but pairs naturally with fix #2 above (both are "event
   payloads need to be richer than currently specced") — worth solving
   together, not as two separate patches.
4. **Two more single-card gaps**, each narrow enough to leave for Phase
   8's tail policy rather than design now: `Lilandra, "Grand Admiral of
   the Guard"` (DPS118, needs the engine to know an attacker was
   unblocked at the moment its `DieAttacks` event fires) and `Madelyne
   Pryor, "Aspiring"` (DPS119, needs to know a draw was an *extra* one,
   not the normal turn draw). `Lilandra, "Freedom Fighter"` (DPS078)
   is a fifth, slightly different one — `CostModifier` covers
   Purchase/Fielding/GlobalEnergy but not "cost to use an Action die,"
   a 4th cost kind worth adding alongside the other three.

### The `Consider` bucket, at DPS scale

11 cards, all mapping to already-known deferred items — nothing new,
but useful to see the real distribution: ability-blanking (3: Vulcan
"Power Suppression," Mister Sinister "Mutant Supremacist," D'Ken
"Shi'ar Civil War"), the live-value-Amounts family (5: Archnemesis,
Rogue "Mrs. X," Cable "High Stakes," Iceman "Mr. Ice Guy," Dark
Phoenix "Destructive Force" — the last three via `DoublePrintedAttackOfEach`,
each target getting its OWN printed stat as its own delta, same
"needs a live per-die value" shape as the others), pay-life-not-energy
(1: `Lilandra, "Majestrix"` — a genuine correction: this card was
reported "not found anywhere" during the Orange Ban investigation;
it's real, in our own hand-curated catalog, at DPS145 — a third
instance of a wrongly-reported-missing card, worth remembering that
"not found" needs real verification every time, not just a second
grep), `DieTargeted` (1: Angel "Air Support"), and one not previously
bucketed (`Organic Steel`'s one-shot damage-prevention shield, `Consider`-
adjacent, matches Part 3's original write-up).

### Scope note

This is now a complete pass over v1's entire hand-curated DPS set (145
cards) — the gap this session's earlier "scope honesty" note flagged
(only ~30 individually checked) is closed. The Orange Ban list itself
was already fully checked in Parts 6-8. Between the two, essentially
everything this project has real card text for has now been checked
against the adopted v2 vocabulary. What's NOT covered: the ~3,600-card
bulk catalog beyond the Orange Ban list and DPS — no claim is made
about fit rate there.

---

## Part 10 — Two more decisions from Part 9's findings (2026-08-22)

### Adopted: Loyalty Counters (Finding 13)

**Adopted** — a real, recurring gap (6+ confirmed DPS users, plus the
same shape almost certainly covers D&D-set "Experience" tokens),
missed until Part 9's full sweep because round 1 marked a
Loyalty-using card "fit" without questioning the counter mechanism
itself. Design, closely matching v1's own proven shape:

- **Data model**: a per-`(player, cardId, counterName)` integer count,
  living on `GameState` — counters belong to a *card* (all copies of
  it share the count), not to one die, unlike everything else in the
  adopted model. `Appendix B`'s `GameConfig`/`CardDef`/`DieInstance`
  triad gains this as a new piece of `GameState` itself, not a new
  per-card or per-die field.
- **Grant**: `GrantCounter(TargetFilter, CounterName: string, Amount: int)`
  — a 18th effect template. Always grants to the resolved target's own
  *card*, mirroring `GrantLoyaltyCounter`'s v1 behavior.
- **Read**: extend `TargetFilter.Stat` with a `Counter(name)` kind
  alongside the existing fixed stat kinds (Attack/Defense/Level/
  PurchaseCost/FieldingCost) — `Stat: (Counter("Loyalty"), Min: 1)`
  reads as "at least one Loyalty Counter," reusing `CountAtLeast` and
  ordinary target-filtering rather than inventing a parallel query/
  condition system just for counters.

Small, closely modeled on a mechanism v1 already proved works. Folded
into Part 1 as adopted.

### Reclassified: "payment-source visibility" (Bishop x2, Forge,
Professor X) — presented to players as an alter-or-skip candidate, not
a roadmap item

Part 9 filed this as a `newgap` — buildable by giving purchase/
fielding events richer payloads (which specific dice paid), the same
kind of extension `DieDamaged`'s damage-amount payload or
`DieFaceChanged`'s `Cause` field already are. The user's call: present
these to players alongside Part 7's architecturally-bespoke examples
(Forge, Blink, Explosion) as cards that might get altered or skipped,
not cards on a "we're building this" list.

**Worth recording the nuance, not just the decision**: technically
this looks more like the payload-richness gaps already adopted
elsewhere than like Part 7's structural walls (identity substitution,
mid-queue cancellation, uncapped resource loops) — nothing about it
breaks an architectural assumption the way those three do. But
whether to spend the engineering effort building it is a separate,
legitimate call from whether it's *possible* — four confirmed cards is
thin evidence next to Loyalty's six-plus, and "present as a candidate
for alteration" is a reasonable product decision independent of the
technical classification. Recorded as the user's explicit choice, not
a technical reassessment — if a future session decides to build it
after all, the design sketch in Part 9's finding #2 is still there.

---

## Part 11 — Architect gate review and spec freeze (Fable, 2026-08-22)

Final review before v2 implementation begins, requested by the user
after the session's long run of validation rounds, adoptions, and
corrections. Three outputs: documentation drift found and fixed, the
outstanding decision backlog resolved in one batch (user signed off),
and the spec frozen.

### Drift found and fixed

Ten parts of accretion left the authoritative Part 1 out of sync in
one real way and the plan in two smaller ways — exactly the kind of
rot that would have confused a fresh implementing session:

1. **Part 10 claimed Finding 13 (Loyalty Counters) was "folded into
   Part 1" — it wasn't.** Part 1 still said 17 templates, no
   `GrantCounter`, no `Counter(name)` stat kind. The plan's Appendix A
   addendum had it; the file implementing sessions actually code
   against didn't. Fixed — Part 1 now carries all of F13.
2. **The plan's Phase 5 implementation-order list** didn't include
   `GrantCounter`, and **Phase 2's `GameState` task** didn't mention
   the counter store F13 places there (the only card-scoped state in
   the model — easy for an implementer to miss). Both fixed in
   `V2_PLAN.md`.
3. Minor: the Part 4 sign-off record's addendum chain listed F11/F12
   but not F13; superseded by this part's consolidated record.

### The final batch (F14) — adopted with user sign-off

The decision backlog banked across Parts 6, 8, and 9 ("decide in one
pass before implementation"), resolved under the session's standing
bar (multiply-confirmed or trivially cheap → adopt; real design
surface → spike; single-card → tail):

**Adopted (folded into Part 1 as [F14]):**
- `CombatFlag.Unblockable` (Falcon "Recon" — completes the flag set).
- `PerMatch.Distinct` (3 cards counting different names/affiliations).
- `PerMatch.Unit: Dice | EnergySymbols` (2-3 cards counting Reserve
  Pool symbols).
- `Duration.UntilYourNextTurn` (2 cards).
- `CostModifier` kind `ActionDieUse` + `Currency: Energy | Life` (4
  cards, all proven v1 flags).
- `AnsweredBy: Own | Opposing` on `TargetFilter` and `MayPay` (4
  confirming cards — Ronan "No Mercy," Black Widow "Tsarina," Magneto
  DPS121, Mystique DPS149; upgrades the latter two from "simplified"
  to faithful, retiring Part 8 finding #6's deliberate approximation).
- `EventFilter.Stat` threshold (Deathbird "Usurper").
- `DamageModifier` modes `Amplify(n)` / `Double` (3 popular cards),
  with the ordering rule fixed at adoption time: **multipliers before
  flat reductions** — a deliberate house ruling, since the physical
  game defines no layering; decided once here so no per-card
  relitigating.

**Deferred into the ability-blanking spike (scope note added):**
deny-named-card lockout (Blob "Appetite for Destruction," Drax "The
Pacifist," Magneto "Magnetic Monster"). Reason: its hard half —
"choose an opposing card when fielded and REMEMBER the choice" — is
the identical per-die chosen-card memory Shriek's ability-blanking
already needs. One mechanism, two payoffs; building lockout alone now
would preempt half the spike's design space.

**Tailed:** player-life-damage-as-trigger (Hulk "Green Goliath," 1
card), unblocked-at-attack event payload (Lilandra "Grand Admiral," 1
card), extra-draw event flag (Madelyne Pryor "Aspiring," 1 card).

### Where the numbers land

With F13 + F14, the full-DPS audit's buckets shift: the six Loyalty
cards, Deathbird, Lilandra "Freedom Fighter," and Lilandra
"Majestrix" all flip to fit, and Ronan "No Mercy" leaves the tail —
**~119/145 (82%) of the DPS set fits the frozen vocabulary cleanly**,
with the remainder concentrated in: the two spikes (~9 cards:
ability-blanking family incl. lockout, live-value family), the 5
architecturally-alien cards (Part 7 — same in v1), the payment-source
group (4, user-designated alter-or-skip), and ~8 genuine tail
singletons. Nothing unaccounted for.

### Freeze declaration and readiness verdict

The vocabulary is **frozen** at: 11-field TargetFilter, 18 effect
templates, 7 conditions, 6 continuous templates (with their adopted
kinds/modes/gates), 10 events, bindings, and the F1-F14 amendment set.
Ground rules 1-8 stand. Changes from here route only through the two
spikes or `V2_TAIL_POLICY.md`, with sign-off — an implementing session
finding a misfit card during Phases 1-8 files it and moves on (ground
rule 2), full stop.

**Verdict: ready to implement.** Phase 0 ran far past its original
20-card design — 60 sampled + the full 145-card DPS audit + the
Orange Ban list — and the last two rounds produced only consolidations
and parameter-level additions, not structural changes: the vocabulary
has converged. Phase 1 (scaffolding + data model) is next; its
Appendix B blueprint plus F13's GameState counter store are the
complete data-model input. Per the plan's handoff design, any capable
session can execute it — the spec now answers the questions it would
otherwise have had to invent answers for.

---

## Part 12 — The two design spikes: PROPOSALS AWAITING SIGN-OFF (2026-08-24)

**Nothing in this Part is adopted.** V2_PLAN.md Phase 8 task 3 requires
each spike to be written up, signed off, and only then implemented.
Both write-ups below are grounded in v1's actual implementations and in
what v2 has actually built, and both are honest about what they do NOT
close.

Reading guide: each spike states the cards that need it, the shape
proposed, what it costs in vocabulary terms, what it closes, and what
it leaves tailed.

### Spike A — Ability-blanking + named-card lockout

**Cards**: D'Ken "Shi'ar Civil War" (DPS141), Mister Sinister "Mutant
Supremacist" (DPS083), Vulcan "Power Suppression" (DPS095), Shriek
(SMC016); plus the named-card lockout family folded in at the Part 11
freeze — Blob (XFC087), Drax (IG107), Magneto (AOU139).

#### What v1 does, and the one detail worth copying exactly

v1 funnels every "what does THIS die's card grant" lookup through a
single choke point, `DieStats.GetCard`, which returns `null` when the
die is blanked. The detail worth copying verbatim is its *scope*, which
v1's own comment spells out: blanking hides the **rules-text box**
(keywords, triggered abilities, static grants) and NOT fixed printed
attributes — face stats, affiliations, energy type all survive. Nor
does it apply when a *different* die's card is consulted for identity
or board presence ("is a die named X active" for someone else's
condition). A blanked Wolverine is still named Wolverine, still X-Men,
still 4A — he just does nothing.

#### Proposed v2 shape

1. **Implement the reserved 8th query**: `QueryEngine.AbilitiesActive(state, die) -> bool`.
   The plan reserved this name and explicitly said not to build it
   early; this is what it was reserved for.

2. **Consulted at exactly three sites**, each already existing:
   - `EventBus.Fire`'s listener scan — a blanked die's triggered
     abilities never enqueue.
   - `TurnEngine.UseGlobal` / `UseAction` — a blanked die's Global
     can't be activated.
   - `ContinuousRegistry.ActiveSourceDice` — a blanked die's
     continuous templates switch off. (The plan asks this question
     explicitly; v1's answer is yes, via the same choke point. Match it.)

3. **Deliberately NOT consulted** by `GetAttack`/`GetDefense`/
   `GetFieldingCost`, by `GetTags`' affiliation/name/energy
   contributions, or by `TargetResolver`'s identity filters — mirroring
   v1's scope decision above.

4. **The already-queued case, which the rule-3.2.5 decision already
   settled.** If ability X blanks a die whose ability Y is *already in
   the queue*, Y still fires and resolves with no text to do anything
   (the Dwarf Wizard / Shriek behavior). Concretely:
   `EffectInterpreter.ResolveQueued` must re-check `AbilitiesActive` on
   the source die at *resolution* time and no-op if it has since been
   blanked. This falls straight out of per-ability snapshots dissolving
   between queue entries — no extra mechanism, but it must be written
   down or it will be missed.

#### Vocabulary cost

| Addition | Kind | Covers |
|---|---|---|
| `AbilityBlank(Target, ActiveWhen?)` | continuous template (7th) | D'Ken (Target: opposing character dice, `Stat: PurchaseCost Max 3`) |
| `BlankText(Target, Duration)` | effect template (19th) | Mister Sinister's Global (single die, end of turn) |
| `RememberCard(Target, MemoryName)` | effect template (20th) | the "choose an opposing card when fielded" memory both families share |
| `PurchaseLock` / `FieldLock` modes on `CostModifier`, or a `Lockout(MemoryName)` continuous | continuous | Blob / Drax / Magneto AOU139 "can't purchase/field that card" |

#### What it does NOT close (be honest about this before signing)

- **Mister Sinister's side-wide half** — "ignore all text on opposing
  character *cards*" is **card**-scoped, not die-scoped: it covers
  copies not yet in play. `AbilityBlank`'s `TargetFilter` resolves to
  *dice*. Closing it needs a card-scoped store keyed by
  `(player, cardId)` — exactly the shape `GameState.Counters` already
  established for F13, so the precedent exists, but it is a second
  mechanism, not a free rider on the first.
- **Vulcan's engagement scoping** — "blocking or blocked by Vulcan" is
  a combat-*engagement* relationship. `TargetFilter` has no concept of
  "engaged with the source die," and v1 doesn't express it through
  targeting either (it populates the blank set from inside
  `CombatEngine.DeclareBlockers`). Vulcan likely stays tailed even
  after this spike, unless engagement becomes a `TargetFilter` notion —
  which would be a much larger ask.

**Assessment**: this is the bigger of the two spikes — three or four
vocabulary additions plus a card-scoped store, and it still leaves two
of its five motivating cards partly open. Worth doing for the lockout
family and D'Ken; worth going in with eyes open about Sinister and
Vulcan.

### Spike B — Live-value Amounts

**Cards**: Archnemesis (DPS001), Cosmic Cube (MSW002), Rogue "Mrs. X"
(DPS049), Dark Phoenix "Destructive Force" (DPS107).

#### Proposed v2 shape

Extend `Amount` with two binding-referencing sources, per the plan:

```
Amount = Fixed(n)
       | PerMatch(...)                       // unchanged
       | StatOf(binding, Attack|Defense|Level|...)   // NEW
       | EventValue                                  // NEW
```

**`StatOf` captures at BIND time, not at use time.** This is the whole
point, and it is what makes Archnemesis's rule-3.1.7 simultaneity fall
out for free rather than needing special-casing: both dice are bound
(and their stats snapshotted) before either `DealDamage` applies, so
neither reads the other's already-applied damage.

#### Which value `StatOf` reads — settled (user ruling, 2026-08-24)

The game distinguishes two categories of stat modifier, and they
behave differently under read-and-copy effects:

- **Applied** — attaches to the die itself (a Global that gives +1A).
  It IS part of the die's own value: a 4A die with an applied +1A,
  targeted by Archnemesis's "D equal to its A," gets **D 5, not 4**.
- **Static** — a conditional aura recomputed from what the die
  currently is (Lois Lane: other SuperFriends get +1A *while
  attacking*). It is NOT part of the die's own value; it re-derives
  after any change.

The user's worked example: Lois active; an attacking 4A SuperFriend
shows 5A. Swap its attack with a 1A Sidekick. Result — the Sidekick
becomes **4A**, and the SuperFriend becomes **2A**: the 1A swapped in,
plus Lois's +1A again, because it is still a SuperFriend and still
attacking.

**So `StatOf` reads the `GetBase*` queries (printed face + applied
modifiers), never the static-inclusive ones.** v2 already has exactly
this split — `GetBaseAttack`/`GetBaseDefense`/`GetBaseStatValue` vs
`GetAttack`/`GetDefense`/`GetStatValue` — built in Phase 6 for an
unrelated reason (breaking a self-referential-aura recursion). It
turns out to be the game's own applied-vs-static line, so this spike
inherits it for free rather than needing a new concept.

**This ruling already found and fixed a live bug**, independent of the
spike: `ModifyStat`'s `SetAttack`/`SetDefense` were computing their
delta against the static-INCLUSIVE `GetAttack`, which cancels the aura
out and re-adds it — landing the Lois example on 1A instead of 2A.
Corrected to the `GetBase*` queries, with the user's own scenario as
the regression test (verified failing against the old code first).

Implementation consequence worth stating up front:
`EffectContext.Bindings` is currently `name -> dieId`. Bind-time
capture means it becomes `name -> (dieId, capturedStats)` (or gains a
parallel capture dictionary). Small, but it touches the binding table
every template already uses.

`EventValue` reads the triggering event's own numeric payload —
`DamageDealtPayload.Amount` already exists and `QueuedAbility` already
carries the event subject, so this is mostly plumbing the payload into
the same capture table.

#### One additional change these cards force

`ModifyStat.SetAttack` / `SetDefense` are `int?`. Archnemesis's Global
("target die has D equal to its A") and Rogue's attack-swap both need
them to accept an `Amount` instead. That is a **type change to an
existing frozen template**, not just a new `Amount` case — call it out
now rather than discover it mid-implementation.

#### Coverage

| Card | Closed? | Notes |
|---|---|---|
| Archnemesis — WhenUsed mutual damage | **Yes** | `Sequence` of two `DealDamage`s over bound dice; bind-time capture gives the simultaneity |
| Archnemesis — Global (D = its own A) | **Yes**, given Amount-typed `SetDefense` | |
| Rogue "Mrs. X" — attack swap | **Yes**, same caveat | And note ground rule 8: v1 wrongly collapsed its "you may" to always-swap. v2 must wrap it in `MayPay` |
| Dark Phoenix "Destructive Force" | **Yes** | `EventValue` off `DieDamaged`'s existing payload |
| Cosmic Cube — swap life totals | **No** | Needs a `LifeOf(player)` amount source *and* a set-mode on `LifeChange` (it takes a signed delta, not an absolute). Two further additions for one card — recommend leaving tailed |

**Assessment**: the cheaper and better-defined spike. Two `Amount`
cases plus one type widening closes four of five motivating clauses
cleanly, and the bind-time-capture design has a real payoff beyond
these cards (it is the general answer to "read a value before the
ability mutates it"). Recommend doing this one first.

### Recommended order

Spike B first (smaller, self-contained, immediate card payoff), then
Spike A — and, if Spike A is approved, decide the Mister Sinister
card-scoped store and the Vulcan engagement question explicitly at
sign-off rather than during implementation.

Also outstanding and unrelated to either spike, from batch 1:
`EventFilter.Step` (see `V2_TAIL_POLICY.md`) — a one-field addition
blocking every end-of-turn/start-of-turn card in the catalog.

---

## Part 13 — Spike C: the timing model. ADOPTED AND IMPLEMENTED (2026-08-24)

**Signed off by the user and built the same day** (see the
implementation note at the end of this Part).** Supersedes the `EventFilter.Step` parameter proposal
(see `V2_TAIL_POLICY.md`, kept there marked superseded).

Design direction set by the user: **one flat, ordered, extensible list
of steps.** Abilities that need their own timing window get their own
entry in that list rather than nesting inside a broader step — e.g.
"Resolve any Range abilities" becomes a peer step after "Select
attackers / resolve effects due to attacking", skipped entirely when
no Range dice are active, rather than being handled *inside* the
resolve-effects step.

### Why this is right, not just workable

1. **The rulebook itself is a flat ordered list.** The starter
   rulebook's TURN SUMMARY presents "Any abilities that take place *at
   the start of your turn*" as a **peer entry preceding Clear and
   Draw**, not as a property of it. So "before X" needs no before/at/
   after modelling at all — it is simply its own entry. That removes
   the most complicated part of the earlier proposal.
2. **v1 already converged here under pressure.** v1's `AttackSubStep`
   carries `RangeWindow`, `InfiltrateWindow`, and `TagOutWindow` as
   first-class peer values, with `NextSubStepAfterBlockers` skipping
   whichever have nothing to offer. The design has survived contact
   with real cards once already.
3. **It is the only shape that makes timing addressable.** An ability
   naming its window is just naming a value in the list — which is
   what the frozen `EventFilter` needs in order to discriminate at all.
4. **Direction C.** If the list is *data*, a variant game reorders,
   removes, or inserts steps with zero engine change. That is a
   materially bigger Direction-C win than the config work done so far.

### The rulebook's own step list (starter rulebook TURN SUMMARY)

```
At the start of your turn        (abilities only)
Clear and Draw    - move energy from Reserve Pool to Used Pile
                  - draw 4 (refill bag from Used Pile if needed)
Roll and Reroll   - roll drawn dice + Prep Area
                  - reroll any of them, all at once
Main              - field / use action dice / purchase / Globals (both players)
                  - at END of step: move unfielded character dice to Used Pile
Attack            - select attackers; resolve effects due to attacking
                  - assign blockers; resolve effects due to blocking
                  - Action/Global window (active, then inactive)
                  - assign and resolve damage; unblocked attackers Out of Play
                  - resolve effects due to damage or KO
                  - return remaining Attack Zone dice to Field Zone
Cleanup           - end all effects, clear all damage
                  - unused Action dice to Used Pile
                  - end turn; Out of Play to Used Pile
```

### Proposed shape

- **`TurnStepDef { Id, Phase, NeedsInput }`**, and the game's step list
  is an ordered `TurnStepDef[]` on `GameConfig` — same "engine knows
  behavior by Id, config declares which exist" contract keywords
  already use (Phase 7's note). `TurnStep` stops being a 5-value enum.
- **`Phase`** is the grouping label (ClearAndDraw / Main / Attack /
  Cleanup). Flattening the *order* loses containment, which real code
  needs — `UseGlobal` allows "Main or Attack", `CleanUp` requires
  Attack. Flat ordering **plus** a grouping tag keeps both.
- **`NeedsInput`** distinguishes the two genuinely different kinds of
  entry the summary contains: **decision windows** where a player
  chooses (Main, Action/Global window, select attackers, Range /
  Infiltrate / Tag Out) versus **engine procedures** that simply run
  (move energy to Used Pile, return dice to Field Zone, clear damage).
  Both are steps; only the first can pause. Phase 9's API needs this
  distinction to know when it must wait for a client.
- **Skip predicates are engine code keyed to keyword id** — "skip
  RangeWindow unless a Range die is active" — matching Phase 7's
  existing stated model and v1's `hasRangeTrigger` / `hasInfiltrateChoice`
  / `hasTagOutChoice`.
- **Abilities address a window by naming its step id**, via the
  existing `TurnStepEntered` event plus a step discriminator on
  `EventFilter`. That field is still needed — it is just now naming a
  rich list rather than a 5-value enum.

### Guardrail (recommend adopting as a ground rule)

**A step may be added for a KEYWORD or for rulebook structure. Never
for a single card's text.** The keyword set is closed and small (~24
declared); cards are unbounded. A step-per-card would reproduce
precisely the v1 failure this whole rewrite exists to escape — 39
one-per-card `Grants*` flags with zero reuse (ARCHITECTURE_REVIEW.md's
central finding).

### Concrete payoff beyond unblocking cards

`Fast` is currently implemented as a two-wave loop inside
`CombatEngine.AssignCombatDamage`, selected by a `bool fast`
parameter. Under this model it becomes two ordinary steps — "assign
and resolve Fast damage", then "assign and resolve normal damage" —
which is both a closer reading of the rulebook (its single "assign and
resolve damage" line simply happens twice) and a plainer implementation
than a boolean-parameterised private method. Range / Infiltrate /
Tag Out / Call Out / Intimidate all become expressible for the first
time, which un-tails five of the currently-tailed keyword cards.

### Fidelity gaps this spike would also close

Reading the TURN SUMMARY against the current engine surfaced three
real differences, none previously logged:

1. **Main Step has no end-of-step sweep.** The rulebook moves unfielded
   character dice to the Used Pile at the end of Main; v2 leaves them
   in the Reserve Pool until Clean Up.
2. **Reserve Pool clears at the wrong time.** The rulebook (and v1,
   which follows it — "rule 2.3.1") clears energy from the Reserve
   Pool during *Clear and Draw*, i.e. at the start of your next turn.
   v2's `CleanUp` sweeps it at end of turn instead. Same eventual
   destination, different observable window — which matters precisely
   for "at the start of your turn" abilities.
3. **The Attack Step is missing three of its six entries** — "resolve
   effects due to attacking" and "due to blocking" are not distinct
   windows, and "return remaining Attack Zone dice to the Field Zone"
   is folded inside `AssignCombatDamage` rather than being its own step.

### Sizing

Larger than Spike B, smaller than Spike A, but it is **load-bearing for
both the tailed combat keywords and the Phase 9 API shape** (the client
needs to know what step it is in and whether it must respond). Doing it
before Phase 9 avoids designing that API twice.

### Implementation note (2026-08-24)

Built as described, with one scope line: the step list contains only
the steps the engine actually runs. `StepIds` names every entry from
the TURN SUMMARY (plus the keyword windows), but `TurnStepDefs.Standard`
lists the ten currently implemented, following the same "declare it
when it has a consumer" rule Phase 4 used for unwired events.

Shipped:
- `TurnStep` becomes the PHASE tag (gaining `StartOfTurn`);
  `TurnStepDef { Id, Phase, NeedsInput }`; `GameConfig.Steps` as an
  ordered list defaulting to `TurnStepDefs.Standard`.
- `GameState` carries a cursor (`CurrentStepIndex` / `CurrentStepId`),
  with `CurrentStep` kept readable AND settable as a phase - setting it
  parks on that phase's first step. That is what kept the refactor
  small: ~290 call sites reference `CurrentStep`, and only three needed
  touching, all of them the now-deleted `AttackSubStep`.
- `AttackSubStep` is deleted. Attack sub-steps are ordinary entries in
  the one list, so `CombatEngine` just asks "am I standing on this step".
- `GameEvent.Step` is a step id; `EventFilter.Step` filters on it.
- The turn now opens on `start-of-turn` (the TURN SUMMARY's own first
  entry) before `clear-and-draw`, so a Pepper-Potts-shaped card has a
  window to name. Nothing occupies it yet.

Colossus "Piotr" is un-tailed, with a test asserting both halves of
what made it tailed: it fires at Clean Up, and does NOT fire when its
controller enters their own Attack Step.
