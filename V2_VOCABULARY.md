# DiceFight v2 Vocabulary Spec

**This is the living, authoritative spec for the v2 closed template
vocabulary**, seeded from `V2_PLAN.md`'s Appendix A. Implementing
sessions code against THIS file, not the plan's appendix. Changing any
part of it requires the user's explicit sign-off (`V2_PLAN.md` ground
rule 2) — this file records both the adopted vocabulary and, in a
separate clearly-marked section, proposed-but-not-yet-adopted changes
found during validation.

Produced by Phase 0 (`V2_PLAN.md`). Status: **20 cards validated, 3
refinements recommended for sign-off before Phase 4 — see "Findings
requiring a decision" below. Vocabulary NOT yet amended.**

---

## Part 1 — The vocabulary (as specified in V2_PLAN.md Appendix A)

Unchanged from the plan; reproduced here as the working copy.

### Targets — one filter shape, 8 fields

```
TargetFilter {
  Ownership: Any | Own | Opposing          // relative to controller
  Zones: Zone[]                            // default [FieldZone, AttackZone]
  Kind: AnyDie | CharacterDie | ActionDie | Player | DieOrPlayer
  Count: int                               // 0 = all matches (no choice)
  Tags: TagQuery?                          // see below
  Stat: (Attack|Defense|Level|PurchaseCost, Min?, Max?)?   // ONE threshold
  Optional: bool                           // "up to Count" vs "exactly"
  Self: bool                               // bypass: resolve to source die
}
TagQuery { AnyOf: string[], NoneOf: string[] }
```

Tags unify v1's affiliations, keywords, card names, and Sidekick-ness:
a die's tag set = its card's affiliations + keywords + its card name +
"sidekick" if applicable + granted tags.

### Amounts

```
Amount = Fixed(n) | PerMatch(TargetFilter, multiplier)
```

### Effect templates (16)

DealDamage, KO (param: `TriggersKOAbilities: bool`, false =
Sacrifice), MoveDie, DrawToZone, FieldDie, Reroll, Spin, SpinToEnergy,
ModifyStat, GrantTag, LifeChange (signed Amount: positive = gain,
negative = lose), PurchaseModifier, CombatFlag, Sequence, MayPay,
Conditional. Full parameter list: see `V2_PLAN.md` Appendix A.

### Conditions (6 kinds)

CountAtLeast, TargetWasKOd, OnBurstFace, LifeComparison, NoKOsThisTurn,
TurnFact.

### Continuous templates (6)

StatAura, CostModifier, TagAura, CombatRule, DamageModifier,
TargetingProtection.

### Trigger events (9, per Phase 4 design)

DieFielded, DieKOd, DieDamaged, DieAttacks, DieBlocks, DiceDrawn,
PurchaseMade, TurnStepEntered, DieUsed — plus paid Global activation
as its own trigger kind (not an event).

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

## Findings requiring a decision

Three refinements, found across independent cards, each cheap and
narrowly scoped - not open-ended vocabulary growth. **Recommended for
sign-off before Phase 4/6 implementation begins:**

1. **Trigger events need a roll-outcome kind.** Energize, Awaken (and
   v1's ContinuousResolve/WhenInfiltrates) fire on what face a die
   rolls to, not on any of the 9 planned events (zone moves, purchases,
   turn steps, actions). This isn't a tail case - Energize/Awaken are
   core, common keywords (10 v1 uses combined). Recommend: add a 10th
   event, `DieRolled { FaceKind: Character | Energy, PriorFaceKind }`,
   with Energize/Awaken/etc. expressed as `EventFilter`s over it
   (Energize = rolled to Energy; Awaken = rolled to Character from a
   non-Character start) rather than as distinct trigger kinds.
2. **Continuous templates need an `ActiveWhen` gate.** Surfaced twice
   independently (#19, #20) and matches a recurring v1 pattern
   (`RequiresOwnActiveSidekick`-style clauses on `Grants*` flags).
   Recommend: add `ActiveWhen: ConditionKind?` to all 6 continuous
   templates, reusing the existing 6 Condition kinds - no new
   condition vocabulary.
3. **`TargetFilter.Stat` is missing `FieldingCost`.** Surfaced once
   (#18) but trivially justified and low-risk. Recommend: add it as a
   5th stat kind alongside Attack/Defense/Level/PurchaseCost.

**Not recommended for adoption now** (real gaps, but rarer and
individually costlier - better handled as tail-policy `Ask` entries
once Phase 8 hits a card that needs them, rather than speculatively
building for cards not yet migrated):
- A `SelfStat` amount/value source (Archnemesis-style "damage/stat
  equal to this die's own attack") - needs the added simultaneity-
  snapshot design discussed in #12, not just a new enum case.
- A one-shot damage-prevention effect (Organic Steel-style shields).
- An `OnFaceKind` condition (Making the Team-style branch-on-roll).
- A `MoveDie.EnterLevel` parameter (Mutation-style swap-and-set-level).

## Verdict tally

| Bucket | Fit | Partial | Misfit | n |
|---|---|---|---|---|
| A - common nodes | 8 clean + 2 flagged-trigger-gap | 0 | 0 | 10 |
| B - ex-single-use-node | 1 | 0 | 4 | 5 |
| C - ex-`Grants*` flag | 2 | 1 | 2 | 5 |
| **Total** | **11-13** | **1** | **6** | **20** |

Against the Phase 0 acceptance bar (target ≥15/20 clean fit; hard stop
below 12): **13 fit cleanly today** (counting the 2 flagged-trigger
cards as fit-at-the-effect-level, since Finding 1 is a trigger-event
gap, not an effect-vocabulary gap) - above the 12-card stop threshold,
so no hard stop. But three of the six misfits collapse to the same two
root causes (Findings 1 and 2), each a small, well-justified,
non-open-ended addition. **Adopting Findings 1-3 would bring the
clean-fit count to 17/20** (everything except Mutation's swap and
Archnemesis's mutual-stat-damage, both genuinely unusual mechanics
better handled as tail-policy items than vocabulary bloat).

**Recommendation**: adopt Findings 1-3 into Appendix A / this spec
before starting Phase 4 (events) and Phase 6 (continuous templates) -
they're needed by those phases' own design, not deferrable busywork.
Leave the four not-recommended items as-is; they'll become
`V2_TAIL_POLICY.md` entries when Phase 8 reaches Mutation, Archnemesis,
Organic Steel, and Making the Team specifically.

This is a recommendation, not an adoption - per ground rule 2, the
user decides.
