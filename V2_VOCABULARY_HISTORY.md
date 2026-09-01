# DiceFight v2 — vocabulary history

**This is the archive, not the spec.** `V2_VOCABULARY.md` states what the
vocabulary IS today, derived from the code. This file keeps how it got
there: 28 parts written between 2026-08-22 and 2026-09-01, covering the
validation arc (20 cards, then 60, then the Orange Ban list, then a
scripted audit of all 145 DPS cards), the gate review and freeze, the
three design spikes, and each implementation increment.

Read it when you want to know WHY something is the way it is, or whether
a shape was already tried and rejected. Do not read it to find out what
the vocabulary currently is - several of these parts amend earlier ones,
and the last word on any subject is the code.

Two known traps if you do read it:

- **Part 1's "FROZEN" spec is out of date.** Parts 15-28 amended it
  repeatedly (affiliations left the tag namespace, blanking arrived, face
  kinds became declared, the amounts and conditions grew). It is kept in
  `V2_VOCABULARY.md` as a historical note only.
- **Part 12's Spike A proposal was superseded** by Part 19 and then
  amended again by Parts 20-21. Part 25 records what was actually built.

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

**57. Vulcan, "Aggression" (DPS135)**, `GrantsOpponentStatDebuff` —
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
- **Vulcan, "Aggression"** (DPS135): was a misfit in round 2 (needed
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

> **SUPERSEDED by Part 19 (2026-09-01). Do not sign off on this
> version.** Parts 15, 16 and 17 each amended it after it was written -
> most importantly Part 16's ruling that blanking removes only a card's
> OWN printed text - and Part 16 surfaced a whole gap (granted abilities
> have nowhere to live) that this write-up does not mention. Part 19 is
> the current proposal; what follows is kept for the reasoning that
> still holds.

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

### Spike B — Live-value Amounts *(ADOPTED AND IMPLEMENTED 2026-08-24)*

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

---

## Part 14 — Spike B implementation note (2026-08-24)

Built as designed. `Amount` gains `StatOf(binding, stat)` and
`EventValue`; `ModifyStat.SetAttack`/`SetDefense` widened from `int?` to
`Amount?`; `EffectContext` gained a `CapturedStats` table and a `Bind`
method that snapshots a die's BASE stats at bind time.

Two implementation choices worth recording:

- **`Bind` lives on `EffectContext`, not on `EffectInterpreter`.** The
  first draft had it as a private interpreter helper, and a test that
  seeded `Bindings["self"]` directly then silently skipped capture -
  the failure surfaced immediately, but the same trap would have caught
  any future caller. Binding-and-capturing is context state, so it is
  now impossible to seed a binding without capturing it.
- **`StatOf`/`EventValue` resolve in `EffectInterpreter`, not in
  `AmountResolver`.** Both are meaningful only inside an ability's own
  resolution, and `AmountResolver` is shared with `ContinuousRegistry`,
  which has neither bindings nor a triggering event. Both throw rather
  than reading zero when referenced out of context.

**Closed**: Rogue "Mrs. X" (DPS049) is migrated and implemented - the
attack swap, with its "you may" restored to a real `MayPay` choice
(ground rule 8; v1 collapsed it, and V2_PLAN.md names it as one of the
two cards v1 got wrong). Archnemesis's Global shape (`SetDefense:
StatOf(target, Attack)`) and `EventValue` are both covered by tests.

**Two further findings, neither anticipated in the write-up** - see
`V2_TAIL_POLICY.md` for both:

1. **Archnemesis's WhenUsed half needs a bind-only step.** The write-up
   asserted this half closed, writing it as
   `Sequence([DealDamage(StatOf("b",...), Bound "a"), ...])` without
   saying where "a" and "b" get bound. They cannot: a `TargetFilter`
   binds only as a side effect of the node that uses it, so the first
   `DealDamage` would need "b" bound before "b" has been resolved. A
   no-op `ModifyStat(..., AtkDelta: 0)` *does* work as a bind step, but
   encoding it that way is an obscure idiom to propagate across cards.
   Proposed instead: a `Bind(TargetFilter)` effect template. Not added -
   ground rule 2.
2. **Globals are card-scoped, not die-scoped.** Rule 2.6.5.2 (and v1's
   own `UseGlobalAbility`, which keys on `(cardId, playerId)`): a Global
   is usable by card ownership alone, with no die of it active - and the
   TURN SUMMARY says *both* players may use Globals. v2's
   `TurnEngine.UseGlobal` requires an active fielded die owned by the
   ACTIVE player, so a Global on a Basic Action card (Archnemesis) can
   never be used at all. A pre-existing Phase 4 gap, unrelated to this
   spike but blocking the same card.

---

## Part 15 — Rules validation pass (2026-08-24)

Requested by the user before taking the outstanding vocabulary asks one
at a time. Read against both source documents (see the rules-references
memory for the extraction recipe):

- *Dice Masters Comprehensive Rules* (4.11.2023)
- the X-Men starter rulebook (its TURN SUMMARY, already used for Spike C)

### Assumptions confirmed correct

| Assumption | Rule |
|---|---|
| KO'd character dice go to the Prep Area | 1.5.3.2 |
| Damage clears at Clean Up for dice that were not KO'd | 2.8.1 |
| "Once blocked, always blocked" | 2.7.x (stated verbatim twice) |
| Purchase needs at least one energy matching the card's type | 2.6.2.3 |
| Purchase cost can never be reduced below 1 | 2.6.2.4 |
| Fielding cost is payable with any energy type | 2.6.3.2 |
| "When fielded" fires immediately on entering the Field Zone | 2.6.3.6 |
| Globals are usable by either player | Glossary, "Global" |
| Purchased dice go to the Used Pile; spent energy goes Out of Play | 2.6.2.6 |

Also rule-cited at last: the Reserve Pool clears **at Clear and Draw**
(2.3.1 - "At the start of this step, the Active player will CLEAR all
dice in their Reserve Pool to the Used Pile"), confirming the fidelity
gap already logged against `TurnEngine.CleanUp`.

### Finding 1 — the tag collapse, and where it actually hurts

The user's instinct was right, and the rules are more specific than the
heuristic. **The rules define a closed list of card attributes** (1.2,
and the 1.2 Key):

> Card Attributes: Name/Title, Subtitle, Purchase Cost, Energy Type,
> Affiliation, Alignment

Keywords are NOT in that list. They are a class of *ability* -
"3.4.7 Keyword Abilities... shorthand for special abilities that a card
may have". And 1.2.7 explicitly separates a third group ("Such items
are not affiliations"): Alignment, Emotional Conduit, Equipment.

So the rules have three distinct concepts where `GetTags` has one
string set: **attributes**, **keyword abilities**, and **die kind**
(Sidekick, 1.3.8/1.3.9). The user's "if it is important enough to
filter on in the Team Builder" heuristic maps almost exactly onto the
rules' own "attribute".

**Where this actually bites - blanking (Spike A).** The rules
distinguish blanking a *component* from blanking an *ability* from
blanking an *attribute*:

- 3.4.8.1 - "Abilities that blank (or ignore) a specified **attribute,
  ability, or component**"
- 3.4.7.2 - "Abilities that blank or ignore the card's text box will
  also blank the **Keyword ability**"
- 3.4.8.2 - "When a card's text box is ignored, **all abilities** that
  pertain to the dice from that card are lost"

A blanked die therefore loses its keywords but keeps its affiliation,
name and energy type - those are printed outside the text box. Against
a single flat tag set that distinction cannot be drawn.

**The good news: this needs no vocabulary change to fix.** `CardDef`
already stores `Affiliations` and `Keywords` as separate lists;
`QueryEngine.GetTags` is the only place they are merged, and provenance
is fully recoverable there. Spike A can simply have `GetTags` omit the
`Keywords`-derived entries when `AbilitiesActive(die)` is false. This
should be written into Spike A's design before it is built.

**What a vocabulary change WOULD buy**, and is a separate decision:
attribute-level *addressing* - letting a card say "target a die with
the X-Men **affiliation**" as distinct from "a die tagged X-Men". That
matters for (a) name collisions between an affiliation, a keyword and a
card title, which validation can only partly catch, and (b) 3.4.8.1's
"blank a specified attribute", which cannot be expressed at all today.
Candidate shape: `TargetFilter.Affiliations: TagQuery?` alongside the
existing catch-all `Tags`. **Recommend deferring this until a real card
needs it** - the blanking correctness fix above is the urgent half, and
it is free.

### Finding 2 — `EnergySymbolId` is singular; the rules allow several

`CardDef.EnergySymbolId` is a single `string?`. The rules have
Crossover characters:

> 1781 - "Crossover: Crossover characters have two or more types of
> energy. At least one of each type of energy they [require]..."
> 2.6.2.3 example (2) - "To purchase a 3-cost bolt-fist Crossover
> Character die, you can spend any combination of 3 energy but 2 of
> those energy types must be a bolt and a fist"

v1 got this right - its `CardDef.EnergyTypes` is a list. v2 narrowed it
to one, which makes Crossover cards unrepresentable and makes
`SpendEnergy`'s single `requiredSymbolId` check incomplete. No migrated
card is Crossover yet, so nothing is currently wrong on disk - but this
is a data-model narrowing rather than a missing feature, and it gets
more expensive to widen the longer the catalog grows.

### Finding 3 — ability-fielded dice and level 1 (NEEDS A RULING)

This one bears directly on the `FieldDie` change made earlier today.
The Glossary's "Field" entry says:

> "...or an ability directing a Character die to be fielded from the
> **Used Pile, Prep Area, or bag**... Other abilities can field
> Character dice, and dice fielded this way are considered **fielded
> for free on level 1**, unless otherwise stated."

Two readings, and they disagree about Making the Team:

1. **Literal**: ability-fielded dice are level 1 unless the card says
   otherwise. Making the Team says only "field it for free", so it
   fields at level 1 - and the roll matters solely for deciding
   *whether* it is fielded or Prepped. This is what `FieldDie`'s
   original `Level = 1` default did.
2. **As ruled today**: the level-1 default exists *because* the three
   zones named are all dormant - a die there has no face and therefore
   no level. Making the Team is unusual in rolling the die first, which
   gives it a real level, so it fields at that level.

Reading 2 is defensible and arguably the better reading of intent - the
named zones are all dormant, which is exactly when a default is needed.
But reading 1 is what the sentence literally says. Current code
implements reading 2. Flagging rather than quietly switching: this is a
rules call, not an engineering one.

### Finding 4 — Alignment and Equipment have no representation

Both are card attributes per 1.2.7 and the 1.2 Key (Alignment on D&D
sets; Equipment used with the Equip keyword). `CardDef` models neither.
Nothing in the DPS set needs them, so this is a note for whenever a D&D
set is migrated, not an action item.

---

## Part 16 — Blanking is provenance-aware; granted abilities are a gap (2026-08-24)

User correction to Part 15's Finding 1, and it changes Spike A's design
before it is built.

**Blanking removes only a card's OWN printed text - never text granted
to that die by another ability.** Worked examples the user gave:

- Psylocke "Telepath" grants a die Overcrush. If Shriek then blanks that
  die, **it keeps Overcrush**.
- Lantern Ring is the classic case: it grants a die "deal 1 damage to
  target player for each energy symbol in your Reserve Pool that matches
  [this die's] type". A die granted that ability keeps it through
  blanking.

The rules support this reading: 3.4.8.2 explains blanking as "all
abilities that pertain to the dice from that card are lost **because
dice refer to their card to initiate or trigger their abilities**". A
granted ability does not come from the blanked card, so nothing severs
it.

### Consequences for v2

1. **Part 15's blanking note was too coarse.** It said a blanked die
   "loses its keywords but keeps affiliation/name/energy". Correct
   version: a blanked die loses only the entries `GetBaseTags` derives
   from `card.Keywords` - it keeps `DieInstance.GrantedTags` (from
   `GrantTag`) and anything a live `TagAura` contributes, as well as all
   printed ATTRIBUTES. The provenance needed is still fully recoverable
   inside `QueryEngine`, so this remains a no-vocabulary-change fix -
   just a more careful one than first written.

2. **Granted ABILITIES have no representation at all.** `GrantTag`
   grants tags; nothing in the closed vocabulary grants a whole
   triggered ability to a die. Lantern Ring needs exactly that, and
   `EventBus.Fire` currently scans only `state.CardCatalog[cardId].Abilities` -
   there is nowhere for a granted ability to live, let alone survive
   blanking. This is a real gap independent of Spike A; Spike A merely
   makes it visible, because "which abilities does this die have" stops
   being answerable from the card alone.

   Candidate shape: a per-die granted-ability store (mirroring
   `GrantedTags`, with the same `Duration` handling) plus a
   `GrantAbility` effect template, and `EventBus.Fire` unioning it with
   the card's own list. Needs sign-off; not implemented.

3. **The ruling is changeable, but the mechanism is not optional.** The
   user noted that the granted-survives-blanking ruling could itself be
   simplified away if it bought enough. It would not buy much: even if
   granted text were blanked alongside printed text, v2 would still need
   somewhere for granted abilities to live in order to have Lantern Ring
   at all. So build the store either way; the ruling only decides
   whether blanking filters it.

### Also settled this pass

- **`CardDef.EnergySymbolIds` is now a list** (implemented). Rule
  2.6.2.3 requires one energy of EACH of a card's types; Crossover
  characters carry two or more and some carry all four. v1's
  `EnergyTypes` was a list all along. `SpendEnergy` now tracks an
  outstanding-requirement set, with wild satisfying any of them.
- **Making the Team keeps its RawText verbatim.** The user offered
  rewording the card text so the glossary's "unless otherwise stated"
  is satisfied, with the caveat that it might confuse future Google
  Sheet syncs. It would: `import_bulk_cards.py` skips ids it finds
  already hand-curated, so nothing mechanical would clobber it, but an
  edited RawText would read as a data error on any later cross-check.
  Instead the divergence is recorded in the card's own comment and in
  its expression, leaving the data verbatim and auditable.

---

## Part 17 — The bound-die predicate condition (supersedes the `HasTag` proposal, 2026-08-24)

The user asked two questions about the proposed `HasTag` condition that
between them reshaped it, and a third that is still open.

### `HasTag` was too narrow - it should be a general bound-die predicate

The question was whether `HasTag` would also cover Phoenix "Eternal
Flame" (DPS126), whose text gates on attack value rather than a tag.
Investigating it separated two things that are easy to conflate:

- **Selection filtering** - "which dice does this effect apply to".
  `TargetFilter` already carries both `Tags` AND `Stat`. Eternal Flame
  is entirely this: "opposing character dice with less than 4A can't
  block" is `CombatFlag(TargetFilter{Opposing, CharacterDie,
  Stat:(Attack, Max:3), Count: 0}, CantBlock)`. **It needed nothing new
  and is now migrated and tested.**
- **Branching on an already-chosen die** - `Conditional` over a bound
  die. v2 can test that die's KO state (`TargetWasKOd`), burst level
  (`OnBurstFace`) and face kind (`OnFaceKind`)... but neither its tags
  nor its stats. Phoenix "Psionic Maelstrom" is this second kind.

So the gap is not "tags specifically", it is "predicates about one
bound die". A narrow `HasTag` would close one case and leave the
identical stat-shaped case ("if that die has 3D or greater") open for a
second addition later.

**Revised proposal**: one condition carrying nullable predicate fields,
so only what is set is checked:

```
BoundDieMatches(
    CheckBinding: string,
    Tags: TagQuery?      = null,
    Stat: StatThreshold? = null,
    Kind: TargetKind?    = null,
    Ownership: TargetOwnership? = null)
```

All-nullable is what makes it safe: it avoids the trap that killed the
`Bound`-composes-with-`TargetFilter` idea, where `Kind` defaulting to
`CharacterDie` silently excluded energy-faced dice.

### Sizing, from v1's own usage

v1's `TargetHasAffiliation` - the direct equivalent - has **2 users**
in the curated set (Phoenix "Psionic Maelstrom" DPS086, and Dark
Phoenix's "if that die was X-Men" clause). Small, but the shape
generalises, and v2 already ships three sibling conditions that address
a bound die the same way.

Most of v1's other condition kinds are *counting* conditions that v2's
`CountAtLeast` already subsumes (`OwnActiveAffiliationOrKeywordCountAtLeast`,
`OwnSidekickActive`, `OwnCharacterDiceInFieldZoneAtLeast`,
`OpponentHasAtLeastNCharacterDiceInFieldZone`, and the Loyalty-counter
one) - so the bound-die predicate is genuinely the main uncovered
condition shape, not one of many.

### STILL OPEN: is Affiliation a tag, or first-class?

Naming this condition `HasTag` would have quietly settled a question
the user raised earlier and which Part 15 deliberately deferred: the
rules define a **closed list of card attributes** (Name/Title, Subtitle,
Purchase Cost, Energy Type, Affiliation, Alignment) in which keywords do
not appear, and the user's heuristic - "if it is important enough to
filter on in the Team Builder, it is important enough" - lines up with
that list.

This is a genuine fork and should be decided once, not drifted into:

- **(a) Keep affiliations merged into tags.** Cheapest now. Every card
  authored against the merged path makes unpicking it later more
  expensive - the same trajectory `EnergySymbolIds` was on before it was
  widened.
- **(b) Give Affiliation first-class addressing.** `CardDef` already
  stores `Affiliations` separately, so this is a QUERY-surface change:
  an `Affiliations` predicate on both `TargetFilter` and the new
  condition, with `Tags` left for genuinely tag-ish things (Alignment,
  Equipment, card names, "sidekick").

**Recommendation: decide (a) vs (b) before adding the condition**, and
apply the answer to `TargetFilter` and the condition together. Adding a
tag-only condition first and an affiliation predicate later would leave
the two query surfaces inconsistent, which is the worst of both.

---

## Part 18 — Analysis: affiliation as a tag (2026-08-24, user leaning yes)

The user's own case, and my analysis on request. **Not yet decided** -
they are still thinking. Recorded now so the reasoning survives.

### The reframing that dissolves most of the tension

`CardDef.Affiliations` is already its own field and stays either way.
Team Builder filtering, deck validation, UI grouping, and blanking
provenance all read that structured field directly. The choice is only
about how ABILITY TARGETING addresses affiliations - a query-surface
decision, not a data-model one. Most arguments for "first-class" turn
out to be arguments for keeping the data structured, which nothing
threatens.

### For tags (the user's case, and I find it persuasive)

1. **Direction C.** Privileging "Affiliation" hardcodes a Dice Masters
   taxonomy into an engine whose stated purpose is expressing a
   different game as data (ARCHITECTURE_REVIEW.md Part 3). Energy
   symbols and keywords are already config-declared with no special
   status.
2. **Founder is the game telling on itself.** Dice Masters shipped a
   KEYWORD that behaves like an affiliation because the closed
   affiliation list could not stretch. v2 already proves the point:
   Cyclops "First Class" filters `Tags: AnyOf ["Founder"]` and is
   indistinguishable from an affiliation filter. An engine enforcing a
   line the designers route around will keep losing.
3. **WWE generalises the lesson** - a taxonomy load-bearing in one IP
   and vestigial in another should not be structural.
4. **Other games went this way** (Lorcana, Marvel Puzzle Quest):
   multi-axis, open-ended classifications rather than one closed
   affiliation field.

### Against, stated honestly

1. **Reversibility runs slightly the other way.** Choosing tags and
   later wanting attributes means re-authoring every card that used
   `Tags` for an affiliation check - ~8-10 of the 28 migrated so far,
   far worse at 145. The reverse costs nothing, since `Tags` would
   still exist. Small asymmetry, but it argues against drifting into
   either answer by default.
2. **One rules capability is lost.** 3.4.8.1 permits blanking "a
   specified ATTRIBUTE" - "ignore that die's affiliation" is
   inexpressible if the engine cannot tell which tags are affiliations
   at query time. No such card is known in DPS; a risk to note, not a
   blocker.
3. **Collisions get worse** - addressed below.

### Guardrail implemented regardless of the decision

Tag unification puts affiliations, keywords, CARD NAMES, "sidekick" and
energy symbol ids in one namespace. `ValidateCatalog` checked
affiliations against symbols/keywords but **never checked card names at
all** - despite names being the one part of that namespace nobody picks
with the collision in mind. A filter for affiliation "X" would silently
also match a card merely NAMED "X" (Kitty Pryde "Headmistress" already
relies on name-tags: "while Wolverine is active").

Now validated in both directions - a card name colliding with a
keyword/symbol, and an affiliation colliding with a card name - with
tests. This is protective whichever way the decision goes, and it is
the concrete price of unification being paid up front.

### Recommendation

Go with tags, for reason 2 above more than any other - the Founder
precedent is the game's own designers demonstrating that the closed
list does not hold. Keep `CardDef.Affiliations` structured (it already
is), keep the collision validation, and treat an `Affiliations`
predicate on `TargetFilter` as an available additive move if a future
card genuinely needs attribute-level addressing.

---

## Part 19 — Spike A, consolidated for sign-off (2026-09-01)

**Supersedes Part 12's Spike A.** That write-up was amended three times
after it was written — Part 15 (blanking loses keywords, keeps printed
attributes), Part 16 (the user's correction: blanking removes only a
card's OWN text, plus the granted-abilities gap it exposed), and Part 17
(the affiliation fork, which changes how much surgery the tag path
needs). A proposal spread across four Parts, where the first says things
the later three overrule, is not something anyone should be asked to
sign. This is one current statement of what would be built.

Everything below was re-checked against the code as it stands today, not
against the notes. Where a note and the code disagreed, the code won.

### What it is for

**Ability-blanking**: D'Ken "Shi'ar Civil War" (DPS141), Mister Sinister
"Mutant Supremacist" (DPS083), Vulcan "Power Suppression" (DPS095),
Shriek (SMC016).

**Named-card lockout**, folded in at the Part 11 freeze because it shares
the same "choose an opposing card when fielded, remember the choice"
memory: Blob (XFC087), Drax (IG107), Magneto (AOU139).

### What blanking actually removes

Three rules define the scope, and they draw a finer line than "the die
does nothing":

- 3.4.8.1 — abilities may blank a specified **attribute, ability, or
  component** (three different things).
- 3.4.7.2 — blanking the text box also blanks **keyword** abilities.
- 3.4.8.2 — when the text box is ignored, all abilities that pertain to
  the card's dice are lost **"because dice refer to their card to
  initiate or trigger their abilities"**.

That last clause is the load-bearing one, and it is what the user's
Part 16 correction turns on: a **granted** ability does not come from
the blanked card, so nothing severs it. Psylocke grants a die Overcrush;
Shriek blanks that die; **it keeps Overcrush**. Lantern Ring is the same
case and the reason it matters.

So a blanked die loses: its card's keywords, its card's triggered
abilities, its card's Globals, and its card's continuous templates.

It keeps: every printed **attribute** (name, subtitle, purchase cost,
energy type, affiliation, alignment — the closed list at rules 1.2), its
face stats, its die kind, and **anything another live ability granted
it**. A blanked Wolverine is still named Wolverine, still X-Men, still
4A, still has whatever Psylocke gave him. He just has no text of his own.

### The build, site by site

**1. Implement the reserved 8th query.** `QueryEngine.AbilitiesActive(
state, die) -> bool`. The name is reserved and the file carries an
explicit note not to build it early; this is what it was reserved for.

**2. Consulted at four sites.** Three are from the original write-up and
still stand; the fourth is Part 16's correction, and it is the fiddly one.

| Site | Change | Effect |
|---|---|---|
| `EventBus.Fire` (its `card.Abilities` scan, EventBus.cs:41) | skip when blanked | a blanked die's triggers never enqueue |
| `TurnEngine.UseGlobal` / `UseAction` | reject when blanked | a blanked die's Global can't be activated |
| `ContinuousRegistry.ActiveSourceDice` | exclude when blanked | a blanked die's auras switch off |
| `QueryEngine.GetBaseTags` | drop **only** the `card.Keywords` loop | a blanked die loses keywords, keeps everything else |

That fourth row is the whole of Part 16 in one line, and it is worth
being precise because `GetBaseTags` currently merges five sources into
one flat set:

```
sidekick kind  +  card.Affiliations  +  card.Keywords  +  card.Name
               +  card.EnergySymbolIds  +  die.GrantedTags
```

Blanking removes the **third** of those and nothing else. `GetTags`'
`TagAura` union on top also survives untouched — a continuously-granted
tag is granted, not printed. The provenance is fully recoverable inside
`QueryEngine`, so this stays a no-vocabulary-change fix; it is just a
more careful one than "blank the tags".

**3. Deliberately NOT consulted** by `GetAttack` / `GetDefense` /
`GetFieldingCost`, nor by `TargetResolver`'s identity filters. This
mirrors v1's own scope decision (`DieStats.GetCard`'s choke point) and
rules 1.2's attribute list. "Is a die named X active" asked by *someone
else's* condition must still see a blanked die.

**4. The already-queued case.** If ability X blanks a die whose ability Y
is already in the queue, Y still fires and resolves with no text to do
anything. `EffectInterpreter.ResolveQueued` re-checks `AbilitiesActive`
on the source die at **resolution** time and no-ops if it has since been
blanked. This falls out of the rule-3.2.5 per-ability snapshot decision
already made — no new mechanism — but it must be written down or it will
be missed.

### The gap Spike A exposes: granted abilities have nowhere to live

This is the significant change from Part 12, and it is not optional.

`GrantTag` grants tags. **Nothing in the closed vocabulary grants a whole
triggered ability to a die**, and `EventBus.Fire` scans only
`state.CardCatalog[cardId].Abilities` — so there is no place a granted
ability could be stored, let alone survive blanking. Lantern Ring needs
exactly that.

Spike A does not cause this gap; it makes it visible, because "which
abilities does this die have" stops being answerable from the card alone.

Candidate shape, mirroring `GrantedTags` (which already exists on
`DieInstance` with `Duration` handling):

- a per-die granted-ability store on `DieInstance`
- a `GrantAbility` effect template
- `EventBus.Fire` unioning that store with the card's own list

**The store is needed either way.** The user noted the
granted-survives-blanking ruling could be simplified away if it bought
enough. It would not: even if granted text were blanked alongside
printed text, v2 would still need somewhere for granted abilities to
live in order to have Lantern Ring at all. The ruling only decides
whether the blanking filter skips that store — one boolean, not the
mechanism.

### Vocabulary cost

| Addition | Kind | Covers |
|---|---|---|
| `AbilityBlank(Target, ActiveWhen?)` | continuous template (7th) | D'Ken (Target: opposing character dice, `Stat: PurchaseCost Max 3`) |
| `BlankText(Target, Duration)` | effect template | Mister Sinister's Global (single die, end of turn) |
| `RememberCard(Target, MemoryName)` | effect template | the "choose an opposing card when fielded" memory both families share |
| `Lockout(MemoryName)` continuous **or** `PurchaseLock`/`FieldLock` modes on `CostModifier` | continuous | Blob / Drax / Magneto AOU139 |
| `GrantAbility(Target, Ability, Duration)` | effect template | the gap above; Lantern Ring |

Plus one non-vocabulary addition: the per-die granted-ability store.

### What it still does NOT close

- **Mister Sinister's side-wide half.** "Ignore all text on opposing
  character *cards*" is **card**-scoped, covering copies not yet in play.
  `AbilityBlank`'s `TargetFilter` resolves to *dice*. Closing it needs a
  store keyed by `(player, cardId)` — the same shape `GameState.Counters`
  already established, so there is precedent, but it is a second
  mechanism rather than a free rider.
- **Vulcan's engagement scoping.** "Blocking or blocked by Vulcan" is a
  combat-*engagement* relationship. `TargetFilter` has no notion of
  "engaged with the source die", and v1 does not express it through
  targeting either — it populates the blank set from inside
  `CombatEngine.DeclareBlockers`. Vulcan likely stays tailed even after
  this spike.

### Interaction with the affiliation fork (Parts 17-18)

These should be decided together, because one simplifies the other. If
affiliation becomes first-class — leaving `Tags` for genuinely tag-ish
things — then `GetBaseTags` stops merging attributes with abilities at
all, and Spike A's fourth site becomes close to trivial instead of a
provenance-filtering exercise inside a five-source union. Part 17 already
recommends deciding the fork before adding the bound-die predicate; the
same argument applies here, more strongly.

### What sign-off needs to decide

1. **The core** — `AbilitiesActive` plus the four sites plus the
   resolution-time re-check. Approve as written?
2. **The granted-ability store and `GrantAbility`.** Approve? And confirm
   the Part 16 ruling holds: granted abilities survive blanking.
3. **Mister Sinister's card-scoped half** — build the `(player, cardId)`
   store now, or tail the side-wide clause and migrate only his
   die-scoped Global?
4. **Vulcan** — accept that he stays tailed, or is engagement-as-a-
   `TargetFilter`-notion worth opening as its own spike later?
5. **The lockout shape** — `Lockout(MemoryName)` as its own continuous,
   or modes on the existing `CostModifier`?
6. **The affiliation fork** (Parts 17-18) — worth settling in the same
   pass, per the interaction above.

**Assessment, unchanged from Part 12 and now better founded**: this is
the larger spike. Four or five vocabulary additions plus a per-die store,
and it still leaves two of its motivating cards partly open. It is worth
doing for the lockout family, D'Ken, and — the part Part 12 could not see
— because the granted-ability store it forces is a real gap that blocks
Lantern Ring independently of any blanking card.

---

## Part 20 — Spike A: sign-off answers, and what the catalog said (2026-09-01)

User decisions on Part 19's six questions, plus two findings from
checking the catalog rather than the notes. **The scope grew and the
design got simpler**, because three things I had proposed separately
turn out to be one mechanism.

### Decisions

| # | Question | Decision |
|---|---|---|
| 1 | The core (`AbilitiesActive` + sites + re-check) | **Approved**, with the storage refinement below |
| 2 | Granted-ability store + `GrantAbility` | **Approved.** Part 16's ruling stands: granted abilities survive blanking |
| 3 | Mister Sinister's card-scoped half | **Build it** — the user's instinct that it is not one card is correct; see below |
| 4 | Vulcan | **Tailed** for now |
| 5 | Lockout shape | Reshaped — see "one store, three flags" |
| 6 | Affiliation first-class (Parts 17-18) | **Adopted.** The user's note: "I can always ignore it when we come to v3" |

### Finding 1 — blanking is 22 cards, not 4

Part 12 named four. A scan of the whole catalog for text that ignores or
blanks text/abilities finds **22**, and the user was right on both of
their specific recollections: there is a second Shriek (SMC017 "Dark
Empathy") and Prismatic Spray (BFF064 "Lesser Spell", *"All of your
opponent's characters lose all their card text until the end of the
turn"*).

They split by **scope**, and the split is the design:

- **Card-scoped** (~9): Scarlet Witch MSW125, Shriek SMC017, Shriek
  SMC016, Prismatic Spray BFF064, Typhoid Mary IG135, Kryptonite WF054,
  Wolverine MSW136 (*"for all copies of that die"*), Scarlet Spider
  ASM102/ASM131. Mister Sinister is a member of this family, not an
  exception to it.
- **Die-scoped** (~7): Adam Warlock GOTG081, Loki IG037, the three Web
  Shooters, Wonder Woman SKC152, D'Ken.
- **Ability-class-scoped** (~4): Angela IG058 (*"ignore your opponents
  'When fielded' abilities"*) blanks a **trigger kind** across the
  board; Ant-Man 10M2016 and Dormammu DRS011 ignore a single ability
  *instance*. This is a third shape neither Part 12 nor Part 19 saw.
  **Recommend tailing it** — it is not what the store below is for.

They also split by **duration**, and this is what answers the user's
boolean question: **14 are one-shot with a duration** ("until end of
turn"), only **8 are continuous** ("while X is active").

### Finding 2 — 34 cards say their text cannot be ignored

Nothing in Parts 12-19 recorded this. Thirty-four cards carry an
explicit immunity, and it is **clause-level, not card-level**:

> King Black Bolt GOTG123 — "You may not use ? energy to purchase this
> die, **this text may not be ignored**."
> Ziraj ZHN022 — "This effect **can't be ignored or swapped**."
> Strahd 1WKO16D — "Strahd doesn't count as an Adventurer, **this text
> cannot be ignored**."

In each case one *clause* is immune while the rest of the card is not.
So immunity is a flag on the **ability / continuous definition**, not on
`CardDef`. It is one bool, but it has to be designed in — discovering it
during migration would mean revisiting every blanking site.

(The four-energy White Lantern family and several D&D sets are in this
list, so it is not an exotic corner.)

### The user's question: why not just a boolean?

> *"Would it simplify things to simply add a boolean to the card that
> indicates whether or not the text is blanked? ... Or is that already
> kind of what you're doing with `EffectInterpreter.ResolveQueued`
> checking `AbilitiesActive`?"*

**Yes to the second half — that is exactly what `AbilitiesActive` is.**
One choke point, consulted everywhere, so managing blanking becomes
managing one thing. The re-check at resolution is not a separate
mechanism, just one of the places that single query gets asked.

**On the first half: right for 14 of the cards, wrong for the other 8.**

- A **one-shot with a duration** ("ignore that card's text until end of
  turn") is a fact with an expiry. Nothing to recompute. A stored entry
  is exactly right, and v2 already has this pattern — `DieInstance.
  GrantedTags` carries `Duration` and is swept the same way.
- A **continuous** blank is conditional, and a stored boolean would have
  to be *maintained*. D'Ken blanks "opposing character dice with
  purchase cost 3 or lower"; Adam Warlock blanks dice "of a lower level
  than Adam Warlock". Every field, KO, spin, level change or cost
  modifier could flip the answer for any die. Storing it means finding
  and re-flipping every affected boolean on every such event — which is
  the bookkeeping the continuous registry exists to avoid. It is the
  same reason no die stores its current attack.

So `AbilitiesActive` **folds both**: stored one-shot suppressions plus a
live registry query. The user gets the single thing to manage; that
thing is a query rather than a field, and the cheap stored half happens
to cover most of the cards.

### One store, three flags — which also answers the lockout question

> *"Maybe they also need Booleans like 'IsFieldable' or 'IsPurchasable'
> that can be flipped to false by an ability?"*

Same answer, same shape — and following it through collapses what Part
19 treated as two separate mechanisms into one.

The lockout family is **card-scoped and per-player**, exactly like
Sinister's half:

> Blob XFC087 — "choose an opposing card... **your opponent may not
> purchase or field that card's dice** until Blob leaves the Field Zone."
> Drax IG107 — same shape.
> Magneto AOU139 — "**Professor X can't be fielded**" (a *named* card;
> no choice step needed) — and separately, "opposing characters with
> Purchase Cost 3 or lower lose their abilities", which is D'Ken's
> die-scoped continuous shape. Magneto needs both halves.

So there is one **card-scoped suppression store**, keyed `(player,
cardId)` — the shape `GameState.Counters` already established — carrying
three independent flags:

| Flag | Set by | Asked by |
|---|---|---|
| `TextIgnored` | Sinister, Scarlet Witch, both Shrieks, Prismatic Spray, Typhoid Mary, Kryptonite, Wolverine, Scarlet Spider ×2 | the card-scoped arm of `AbilitiesActive` |
| `CantPurchase` | Blob, Drax | `TurnEngine.Purchase` |
| `CantField` | Blob, Drax, Magneto AOU139 | `TurnEngine.Field` |

and each entry is either a stored one-shot with a `Duration` or a live
continuous registration, resolved by the same fold as above.

**This is why decision 3 flipped to "build it".** The `(player, cardId)`
store is not a mechanism for one awkward card; it serves nine
card-scoped blankers and the whole lockout family. Part 12 called it "a
second mechanism, not a free rider" — correct, but it earns its keep
many times over, which Part 12 could not see with four cards in view.

### The shape to build

Four derived queries, all folding *(stored one-shots with Duration)* +
*(live continuous registrations)*, all honouring the clause-level
immunity flag:

```
AbilitiesActive(state, die)              -> bool   // die-scoped ∪ card-scoped
CardTextActive(state, player, cardId)    -> bool
CanPurchase(state, player, cardId)       -> bool
CanField(state, player, cardId)          -> bool
```

Plus, unchanged from Part 19: the four consultation sites
(`EventBus.Fire`, `TurnEngine.UseGlobal`/`UseAction`,
`ContinuousRegistry.ActiveSourceDice`, and `GetBaseTags` dropping *only*
its `card.Keywords` loop), the resolution-time re-check in
`ResolveQueued`, and the granted-ability store with `GrantAbility`.

### Revised vocabulary cost

| Addition | Kind | Note |
|---|---|---|
| `AbilityBlank(Target, ActiveWhen?)` | continuous | die-scoped; D'Ken, Magneto's first half, Adam Warlock |
| `BlankText(Target, Duration)` | effect | die-scoped one-shot; Web Shooters, Loki |
| `BlankCardText(Target, Duration)` | effect | card-scoped one-shot; Scarlet Witch, Shriek SMC017, Prismatic Spray |
| `RememberCard(Target, MemoryName)` | effect | the shared "choose an opposing card" memory |
| `Lockout(MemoryName \| CardId, Purchase\|Field)` | continuous | Blob, Drax, Magneto's Professor X clause |
| `GrantAbility(Target, Ability, Duration)` | effect | approved; Lantern Ring |
| `CannotBeIgnored: bool` | **field on ability/continuous defs** | Finding 2; 34 cards |

### Still tailed after this

- **Vulcan** (engagement scoping) — user's call, unchanged.
- **The ability-class-scoped family** (Angela, Ant-Man, Dormammu,
  Captain Cold's Cold Gun) — blanking a *trigger kind* or a single
  ability *instance* is a third shape; recommend tailing until one of
  them is actually wanted.
- **Prismatic Spray "Greater Spell"** (BFF096) — "treated as if they had
  1A and 1D regardless of bonuses" is a stat override that outranks
  modifiers, not blanking. Different mechanism, separate question.

---

## Part 21 — Blanking: the declared model, and PermanentText (2026-09-01)

Two clarifications from the user before sign-off. The second is adopted
as proposed and makes the design smaller; the first grants an authority
worth writing down explicitly, and I want to be honest about where I
would and would not use it.

### The authority: the engine declares how blanking works

> *"Card blanking is one area where the designers continually iterated
> over the years... taking all the existing text at face value can be
> confusing or lead to splintering. We can declare, via the rules
> engine, that THIS is how blanking works, and that can supersede what
> might be written in the text, especially on some of the older cards.
> This is also kind of what the comprehensive rules document tried to
> do."*

Adopted, and it is the right call — it is the difference between one
mechanism and a mechanism per printing. **The declared model is
authoritative; card text is normalized onto it, and anything that
genuinely will not normalize gets tailed rather than growing the
vocabulary a seventh shape.**

#### The declared model

1. Blanking suppresses **abilities**, never **attributes**. Name,
   subtitle, purchase cost, energy type, affiliation and alignment
   always survive (rules 1.2's own closed list), as do face stats and
   die kind.
2. Blanking never removes abilities **granted** to a die by something
   else (Part 16's ruling).
3. Blanking never removes **permanent** abilities (below).
4. Blanking has exactly **two scopes** — die and card — and exactly two
   durations — a stored one-shot with an expiry, or a live continuous
   registration.
5. Every printed wording maps onto that. "Ignore the text", "blank",
   "lose all their card text", "ignore all text on opposing character
   cards" are the same two templates at different scopes; no card gets
   its own mechanism for saying it differently.

### Where I would NOT use the authority: the two scopes are load-bearing

The obvious simplification the clarification invites is collapsing die
scope and card scope into one. I looked at it and recommend against it,
for two concrete reasons rather than caution:

**Globals force card scope to exist.** v2 already ruled (2026-08-24)
that Globals are card-scoped, not die-scoped — a Global is addressed as
`(cardId, abilityIndex)` in `TurnEngine.UseGlobal` and needs no die at
all. A purely die-scoped blank could therefore never turn a Global off,
and four of these cards go out of their way to say it does: Shriek
SMC016 and SMC017, Scarlet Witch MSW125 and Kryptonite WF054 all say
"including Global Abilities". Collapsing to die scope makes those
clauses unimplementable.

**The designers' own text says die scope is the default.** Wolverine
MSW136 spells out *"ignore that character die's card text **for all
copies of that die**"*. That clause is only worth printing if the
default is otherwise — which is evidence the distinction is intended
rather than accidental wording drift. Collapsing to card scope would
make Wolverine's clause redundant and silently strengthen ~7
die-scoped cards (the Web Shooters family, Loki, Adam Warlock) by
blanking every copy a player owns instead of the one targeted.

And the saving would be small: die-scoped and card-scoped suppression
are the *same store keyed differently*, not two mechanisms. Keeping both
costs one extra dictionary, not a second design.

### Where I WOULD use it

- **The ability-class family gets normalized or tailed, not modelled.**
  Angela IG058 ("ignore your opponents' 'When fielded' abilities"),
  Ant-Man 10M2016 and Dormammu DRS011 (ignore one ability *instance*)
  are a third shape. Under the declared model they are simply **not
  blanking** — they are trigger suppression, which is a different
  question. Tailed, with no third mechanism grown for them.
- **Cosmetic wording differences are normalized on import**, not
  preserved. A card that says "lose all their card text" and one that
  says "ignore all text on opposing character cards" compile to the same
  expression.
- **Older cards whose wording contradicts the model lose.** The model
  wins; the divergence is recorded in the card's comment, the way
  "Making the Team" already keeps its RawText verbatim while its
  expression diverges (Part 16).

### PermanentText — adopted, and it replaces the immunity flag

> *"Worst case we could add another property on the card for
> 'PermanentText' — if it's separate from the CardText, we don't have to
> worry about it getting ignored, and the resolver could just add that
> in, even though most of the time it would be blank?"*

**Better than Part 20's `CannotBeIgnored: bool`, and adopted instead.**
The flag version makes immunity something every filtering site has to
remember to check; a missed check silently blanks text that should have
survived. Separate collections make immunity structural — the blanking
filter only ever sees `Abilities`, and `PermanentAbilities` is simply
not in that code path. Nothing to forget.

`CardDef` gains two collections alongside the existing ones:

```
Abilities            // blankable      (unchanged)
Continuous           // blankable      (unchanged)
PermanentAbilities   // never blanked  NEW - usually empty
PermanentContinuous  // never blanked  NEW - usually empty
```

The 34 immune clauses decompose into these cleanly, because each is
already a self-contained clause: King Black Bolt's "you may not use ?
energy to purchase this die" is one continuous restriction; Strahd's
"doesn't count as an Adventurer" is one static tag denial.

#### One refinement, which is what makes it actually simpler

Two collections are only safer than a flag if **no site enumerates the
raw collections**. There are exactly four today, so this is cheap to
close now and expensive later:

| Site | Reads |
|---|---|
| `EventBus.Fire` | `card.Abilities` (EventBus.cs:41) |
| `TurnEngine.UseGlobal` | `card.Abilities[abilityIndex]` (TurnEngine.cs:251-254) |
| `ContinuousRegistry` | `card.Continuous` (ContinuousRegistry.cs:36) |

All of them route through one accessor instead:

```
QueryEngine.AbilitiesOf(state, die)  ->  card.PermanentAbilities        (always)
                                       + card.Abilities                 (unless blanked)
                                       + die.GrantedAbilities           (always - Part 16)
```

This is not extra work: the granted-ability store already forces
`EventBus.Fire` to stop reading `card.Abilities` directly, so the choke
point has to exist regardless. `PermanentAbilities` then rides in for
free, and "which abilities does this die have" has exactly one answer in
exactly one place — which is the property the user was after in asking
about a single boolean in the first place.

**Implementation hazard, worth writing down now**: `UseGlobal` addresses
a Global by its **index into `card.Abilities`**. `PermanentAbilities`
must therefore be a *separate* list, never spliced into that one, or
every Global's address shifts. The accessor above returns abilities for
*resolution*; Global *addressing* stays an index into `card.Abilities`
alone. (A permanent Global would need its own addressing; none of the 34
immune clauses is a Global, so this is not needed yet — but it is
exactly the kind of thing that silently breaks during migration.)

### Net effect on Part 20's brief

- `CannotBeIgnored: bool` — **dropped**, replaced by
  `PermanentAbilities` / `PermanentContinuous`.
- `QueryEngine.AbilitiesOf(state, die)` — **added** as the single
  ability-resolution choke point.
- Two scopes and the four derived queries — **unchanged**.
- The ability-class family — **tailed by declaration**, not modelled.

---

## Part 22 — Affiliation is first-class: IMPLEMENTED (2026-09-01)

Part 18's question, adopted at the Part 20 sign-off and built here.
Part 17's open fork is settled by the same change.

**`TargetFilter` and `EventFilter` each gain `Affiliations: TagQuery?`**
alongside `Tags`, and `QueryEngine.GetAffiliations(state, die)` joins the
query surface. `GetBaseTags` no longer merges `card.Affiliations` into
the tag set.

What stays a tag: **keywords** (they are abilities — 3.4.7 — and blanking
has to be able to take them away), the **card name**, **energy symbol
ids**, and **"sidekick"** (die kind, 1.3.8). What leaves: affiliations
only. That matches rules 1.2's own closed list of card *attributes*,
which is the line Part 15 identified and could not draw against a single
flat string set.

### Why it had to happen before Spike A

Spike A's fourth consultation site is "`GetBaseTags` drops only the
`card.Keywords` loop". With affiliations still merged in, that site is a
provenance-filtering exercise inside a five-source union, and every
future reader has to know which of the five survive blanking. With the
split it is one loop, guarded. The split does not make blanking possible;
it makes it legible.

Cyclops "First Class" (DPS025) is the case that shows it: he is
`Affiliations: ["X-Men"], Keywords: ["Founder"]`. Under blanking he keeps
X-Men and loses Founder. Before this change both were the string
`"X-Men"`/`"Founder"` in one set with nothing to tell them apart.

### Migration

Six card filters moved from `Tags:` to `Affiliations:` — Brotherhood of
Mutants (×3), X-Men (×2), Shi'ar, Villains. Four deliberately did NOT
move, and each is a small proof the split is drawn in the right place:

| Left as `Tags:` | Because |
|---|---|
| `"Founder"` (Cyclops DPS025) | a keyword, not an affiliation |
| `"Blob"` (Magneto's clause) | a card name |
| `"Mask"` (CardCatalog) | an energy symbol id |
| `"sidekick"` | die kind |

Two existing tests failed on the change and both were right to: they
addressed an affiliation through `Tags`. That is the entire blast radius.

### The validator changed meaning, not strictness

`ValidateCatalog` used to report an affiliation colliding with a **card
name**. That is now legal, and it matters — the real catalog is full of
characters named after their own team, and every one of them was an
error under tag unification.

What it reports instead: an affiliation that doubles as something still
*in* the tag namespace (a keyword or an energy symbol id). Nothing is
ambiguous any more, but `Tags:` and `Affiliations:` are near-identical
filter fields that blanking treats oppositely, so writing one where you
meant the other silently matches nothing. This is the only place that can
warn about it.

### Known follow-on: granting an affiliation

Nothing grants an affiliation today — the one `GrantTag` in the migrated
catalog grants a keyword (Overcrush), and `GetAffiliations` reads printed
affiliations only. Loki "Chains of Destiny" (AI032, *"when fielded,
choose an affiliation..."*) is the case in the wider catalog. When it is
migrated it needs its own granted store, **not** a reuse of
`GrantedTags` — routing it back through tags would reopen exactly the
ambiguity this Part closes. Recorded in `GetAffiliations`' own remarks so
it is found at the point of temptation.

There is also no `includeContinuous` split on the affiliation path, for
the same reason: nothing grants an affiliation continuously, so there is
no self-referential aura to break the way `TagAura`s forced `GetBaseTags`
into existence. Adding granted affiliations later means revisiting that.

---

## Part 23 — Spike A, increment 1: the choke point (2026-09-01)

The first of Spike A's increments, and deliberately the one with **no
blanking in it**. What it builds is the seam blanking will need, plus the
two things that must never be blanked — so that when suppression arrives,
exactly one line changes and no site has to be revisited.

### `QueryEngine.AbilitiesOf(state, die)`

One answer to "which abilities does this die have":

```
card.PermanentAbilities   (always)
card.Abilities            (unless blanked - the line increment 2 guards)
die.GrantedAbilities      (always - Part 16's ruling)
```

with `ContinuousOf(card)` doing the same for the continuous half. Three
sites used to enumerate `card.Abilities`/`card.Continuous` themselves;
each would otherwise have had to remember, independently, that permanent
text is never blanked and granted abilities are never blanked. Three
chances to forget the same two rules.

`EventBus.Fire` and `ContinuousRegistry` now go through it. **`TurnEngine.
UseGlobal` deliberately does not**, and carries a comment saying why: a
Global's address is `(cardId, abilityIndex)` with no die involved, so
splicing permanent text into that list would shift every Global's index.
Blanking a Global is card-scoped anyway and belongs with the card-scoped
store in increment 2.

**A bug this removed on the way**: `EventBus.Fire` skipped cardless dice
outright (`if (listener.CardId is not { } cardId) continue;`). A Sidekick
has no printed abilities, but it can be *granted* one — which is exactly
Lantern Ring — and would have been silently unable to trigger it. The
guard is gone; `AbilitiesOf` returns nothing for a die with neither.

### `PermanentAbilities` / `PermanentContinuous`

The user's PermanentText proposal, as two nullable collections on
`CardDef`, usually empty. Immunity is structural rather than a flag: the
blanking filter only ever sees `Abilities`/`Continuous`, so there is
nothing for a filtering site to forget to check.

### `GrantAbility` + `DieInstance.GrantedAbilities`

Mirrors `GrantTag` exactly — same `Duration` enum, same
`GrantedDuringPlayerId` convention, same clearing when a die leaves
active play (rule 3.4.5.4). The difference is only what lands on the die,
and that blanking will never take it back off.

### Verification

171 v2 tests (761 across the solution). Five new, and the two expiry
paths were mutation-checked — deleting the Clean Up sweep and deleting
the leaves-play clear each fail exactly one.

The permanent tests are worth naming for what they actually pin: nothing
blanks yet, so what they prove is that the permanent lists are **read at
all**. A permanent ability that never registers would be silently inert,
and silence is the failure mode every one of the 34 immune clauses would
have had.

### Next increment

The two suppression stores (die-scoped, and card-scoped keyed
`(player, cardId)` with `TextIgnored`/`CantPurchase`/`CantField`), the
four derived queries over them, and the guard on `AbilitiesOf`'s middle
line. Then the vocabulary templates that write to those stores.

---

## Part 24 — Spike A, increment 2: blanking works (2026-09-01)

The stores, the four derived queries, the consultation sites, and the
two one-shot templates. What is left after this is the *continuous* half
(`AbilityBlank`, `Lockout`) and the chosen-card memory - increment 3.

### The two stores

| Store | Lives on | For |
|---|---|---|
| `DieInstance.Suppressions` | the die | die-scoped blanking - Web Shooters, Loki, Adam Warlock |
| `GameState.CardSuppressions` | the game, keyed `(player, card, kind)` | card-scoped - Scarlet Witch, both Shrieks, Prismatic Spray, Sinister; and the lockout flags |

Both hold **one-shot** suppression only, each with a `Duration`, swept by
the same rules as `GrantedTags`/`GrantedAbilities`. The continuous half
is recomputed on read, per the user's boolean question and the answer in
Part 20: a stored flag would have to be re-flipped on every field, KO,
spin and cost change.

### The four queries

```
AbilitiesActive(state, die)            // die-scoped ∪ its card's card-scoped
CardTextActive(state, player, cardId)
CanPurchase(state, player, cardId)
CanField(state, player, cardId)
```

Consulted at: `AbilitiesOf`'s middle line, `GetBaseTags`' keyword loop,
`TurnEngine.UseGlobal`, `TurnEngine.Purchase`, `TurnEngine.Field`,
`ContinuousRegistry.ActiveSourceDice`, and `EffectInterpreter.
ResolveQueued`.

That last one is the already-queued case: an ability blanked between
enqueue and resolution still fires and does nothing. It falls out of rule
3.2.5's per-ability snapshots, so it needed no mechanism - but it needed
writing down, and it carries the Part 16 exemption, because a *granted*
ability in the queue is not the blanked card's and still resolves.

### The declared model, as tests

Five tests state what blanking is, and all three ways of getting the
scope wrong are caught by mutation:

- blanking a die ALSO taking its granted abilities → fails
- blanking a die ALSO taking its affiliations → fails
- a die-scoped blank leaking to every copy of the card → fails

A blanked die loses its keywords, triggered abilities, Globals and auras.
It keeps its affiliations, name, energy type, face stats, granted tags,
granted abilities and permanent text. `Card_Scoped_Blanking_Reaches_
Every_Copy_Including_Unpurchased_Ones` is the one that proves the two
scopes both have to exist: it blanks a die still in the bag, which a
die-scoped blank cannot reach, and leaves the same card untouched for
the other player.

### `BlankCardText` has two modes, and the second is not a wide filter

`Target` resolves to dice and suppresses each one's card, which is what
"ignore the text on target character die's character *card*"
(Kryptonite) means. `AllOpposing` resolves nothing and suppresses every
card the opponent owns - Scarlet Witch, Shriek "Dark Empathy", Prismatic
Spray "Lesser Spell".

The second is a separate mode rather than a very permissive
`TargetFilter` because a filter only ever reaches cards with a die
already in play, and these cards exist precisely to cover the ones that
are not. Writing it as a filter would have looked right and silently
missed the point of the card.

### Not done yet

- **Continuous blanking** (`AbilityBlank`) - D'Ken, Magneto AOU139's
  first clause, Adam Warlock, Shriek "Sonic Beam" while active.
- **`Lockout`** - the store and `CanPurchase`/`CanField` are built and
  enforced, but nothing writes `CantPurchase`/`CantField` yet.
- **`RememberCard`** - the "choose an opposing card, replacing all
  previous choices" memory both families share.

---

## Part 25 — Spike A, increment 3: the continuous half. SPIKE COMPLETE (2026-09-01)

`AbilityBlank`, `Lockout` and `RememberCard`. With these, Spike A is
built.

### The recursion, which is real in the rules and not just the code

A continuous blank's own source die has to be checked for blanking before
it can blank anything — v1 answers "does a blanked die's continuous text
switch off" with yes, and Phase 8 task 3 asked for that answer
explicitly. But if that check consulted continuous blanks in turn, D'Ken
asking "am I blanked" would evaluate D'Ken.

**Two mutually-blanking dice are a genuine paradox in Dice Masters, not
an artifact of this implementation.** The engine resolves one level and
stops, via `QueryEngine.AbilitiesActiveBase` — blanking from the *stored*
suppressions only. So:

- a die blanked by a **one-shot** effect grants no continuous blank;
- a die blanked by another **continuous** blank still does.

That is the same break `GetBaseTags` needed, for the same reason, and it
is worth noting the shape recurred: the first time it was found as an
actual `StackOverflow` in a test run, and this time it was anticipated
because the earlier one had been written down.

### `RememberCard` is keyed on the SOURCE card

"Choose an opposing card, **replacing all previous choices**" — the
memory is keyed `(player, source card, name)`, like `GameState.Counters`.
Keying on the source card rather than the source die is what makes
"replacing" automatic: a second Blob fielded by the same player
overwrites the choice instead of stacking a second lockout, which is what
the text says happens. Keyed by die, two Blobs would lock two cards.

### `Lockout` reads it back, or names its card outright

Blob and Drax choose when fielded and read the memory; Magneto AOU139's
"Professor X can't be fielded" names the card in the template and needs
no choice step at all. Both go through the same `CanPurchase`/`CanField`
queries, which now fold the stored flags and the live registrations.

A locked-out card is **not** a blanked one — the die keeps its text, it
just cannot be bought or fielded again. Different flags on the same
store, and the test says so explicitly, because "suppression" covering
both invites conflating them.

### Verification

183 v2 tests (773 across the solution). Both new hazards mutation-checked:

- removing the recursion break (a blanked source still blanking) → fails
- a lockout hitting everyone instead of the source's opponent → fails

### Spike A: what is built, and what is still tailed

**Built**: the four derived queries; die-scoped and card-scoped
suppression, one-shot and continuous; `BlankText`, `BlankCardText`,
`AbilityBlank`, `Lockout`, `RememberCard`, `GrantAbility`;
`PermanentAbilities`/`PermanentContinuous`; the `AbilitiesOf` choke point;
the resolution-time re-check.

**Tailed, by decision rather than omission**:

- **Vulcan** (engagement scoping) — user's call at sign-off. `TargetFilter`
  has no notion of "engaged with the source die" and v1 does not express
  it through targeting either.
- **The ability-class family** — Angela IG058 ("ignore your opponents'
  'When fielded' abilities"), Ant-Man 10M2016, Dormammu DRS011. Under
  Part 21's declared model these are trigger suppression, a different
  question, and get no third mechanism.
- **Prismatic Spray "Greater Spell"** (BFF096) — "treated as if they had
  1A and 1D regardless of bonuses" is a stat override outranking
  modifiers, not blanking.

**Next**: migrating the cards that motivated all this — D'Ken, both
Shrieks, Scarlet Witch, Prismatic Spray "Lesser Spell", Blob, Drax,
Magneto AOU139, Mister Sinister — then resuming Phase 8 task 4's batches
with 116 DPS cards to go.

---

## Part 26 — Keyword triggers: mostly not triggers (2026-09-01)

Batch 3's closing note said keyword triggers were the biggest remaining
blocker in the DPS migration. **That was measured wrong** - it counted
v1 `TriggerType` enum names that v2 lacks, not shapes v2 cannot express.
Re-measured against what v2 can actually say, the 32 "blocked" cards
break down very differently.

### Already expressible - 8 cards, no work needed

| v1 trigger | cards | v2 |
|---|---|---|
| `WhenAnotherDieKOd` | 4 | `DieKOd` + `ExcludeSelf` |
| `WhenAnotherDieAttacks` | 1 | `DieAttacks` + `ExcludeSelf` |
| `WhenAnotherDieFielded` | 1 | `DieFielded` + `ExcludeSelf` |
| `StartOfOpponentsAttackStep` | 2 | `TurnStepEntered` + `Step` |

v1 spelled each as its own enum value; v2 composes them from an event
plus a filter it already had. These were never blocked - they were
counted by their v1 name.

### Built here as PREDICATES - 10 cards

Two `EventFilter` flags, no new `TriggerKind`:

- **`LevelIncreased`** — keyword **Awaken** (4 cards). "Every time this
  die spins up one or more levels, regardless of what caused the spin."
  A `DieFaceChanged` whose payload shows a higher character level than
  before. Cause is deliberately unchecked, per v1's own note; a change
  from an energy face does not count, because there is no level to have
  risen from.
- **`SharesAffiliationWithListener`** — keyword **Teamwatch** (6 cards).
  "When a character with Teamwatch is active and you field a DIFFERENT
  character die with the SAME affiliation, use their Teamwatch ability."
  The affiliation is whatever the LISTENER has, so it cannot be written
  as a fixed `Affiliations` TagQuery - hence a flag rather than a value.
  Pairs with `ExcludeSelf`, which is the other half of the same sentence.

That is five v1 enum values (`Energize`, `Awaken`, `Attune`,
`WhenInfiltrates`, `Teamwatch`, plus the four "another die" spellings)
collapsing to two booleans. The closed-vocabulary bet holding up.

### The real blocker is Energize, and it is not a trigger - 15 cards

**Energize fires on a DOUBLE-ENERGY face**, once the reroll window
closes. v2's migrated dice do not have one.

`MigrationDice` builds a character die as *one* energy face carrying a
single pip, plus one face per level - a documented approximation, flagged
in its own remarks as such, because v1's data model never stored face
layouts. The real die is **two double-energy faces, one single, and three
character faces**. So Energize has nothing to fire on, and no trigger
work fixes that.

**v1 now has the real layouts**: `src/DiceFight.Engine/DieFaces.cs` was
built during the match-table redesign and states the composition exactly
("One energy type: two doubles and a single, all of that type"; Crossover
doubles split across both types with a Generic single; four-energy cards
get a Wild single). Porting that convention into `MigrationDice` would
close Energize, remove the approximation, and make every migrated die
physically real.

**The cost is index churn, and it is not small.** Face index 0 is
currently always energy and 1..n are the levels; every v2 test that
places a die uses that. Under the real layout levels start at index 3.
Roughly 40 call sites across the v2 tests, all mechanical, plus
`MigrationDice`'s own contract.

**Recommendation**: do it, as its own change with no card migration mixed
in, so that if an index is fumbled the failure is obviously an index and
not a card. It is also the last thing standing between the migration and
the biggest single keyword group left.

---

## Part 27 — Face kind is declared, and the dice are real (2026-09-01)

The user asked whether die faces should have categories, each with a
default. Half yes, and the question found a live bug.

### The bug: face kind was inferred, and could not be

`Face` had no kind. `OnFaceKind` and `SpinToEnergy` both asked
`Character is null` and called the answer "energy face". A Basic Action
die's faces carry neither symbols nor character data, so **every action
face read as an energy face** — `OnFaceKind(EnergyFace)` returned true
for a die sitting on its action face.

Inference cannot be rescued by picking a better predicate, either:
`Symbols.Any()` fails the other way, because a CHARACTER face may
legitimately print energy symbols — the model's own comment said so.
Nothing in the data classifies a face; only the card does. So `Face.Kind`
is now declared, with three values the engine has real behaviour for.

### The default-per-category half: no, and here is why

The cases that looked like they needed a stored default turn out to be
rules:

- **Spin-down of a half-spent double** is rule 2.6.1.4 — it lands on the
  SINGLE energy face, chosen by pip count. And a Basic Action die has no
  single at all (2.6.1.5, its energy faces are all doubles), which is why
  v1's `SingleEnergyFace` returns null. A stored "default energy face"
  would have answered that confidently and wrongly.
- **`SpinToEnergy`** already names the pip count it wants; only its
  fallback was arbitrary, and that is now the FEWEST-pip energy face —
  2.6.1.4's own logic stated as a rule rather than left to face order.
- **Fielding level** was already settled: `FieldDie` fields at the level
  the die rolled.

So the tie-break is a rule in one place, not data on 3,884 dice. If a
card ever needs a genuine per-die override, the declared category makes
that additive.

**On the v3 argument, honestly**: a category *label* alone buys little
extensibility. A new face kind needs engine behaviour, and the engine can
do nothing with a name it does not understand. What the category buys is
correctness now, and a place to put the distinction instead of null
checks spread across three files. That is enough on its own.

### The dice are now the real six

Done in the same change, because both rewrite `MigrationDice` and touch
the same call sites — separately would have paid the churn twice.

`MigrationDice`'s stated approximation (one single-pip energy face plus
the levels) is gone. A character die is now **two doubles, one single,
then one face per level**, mirroring `DieFaces.cs` in v1: a Crossover's
doubles carry one pip of EACH type (rule 2.6.2.3) and its single is
symbol-less, and a Basic Action die is three generic-energy faces plus
three action faces (rules 1.3.10, 2.6.1.5).

**This is what makes Energize expressible** — it fires on a double-energy
face, and until now no migrated die had one.

Face order is energy first, so level N is at index N + 2. Test helpers
now take a LEVEL and do that arithmetic in one place each; ~30 call sites
went from naming an index to naming a level, which is what they meant.

### Gap found on the way: generic energy has no symbol

`GameConfig.EnergySymbols` declares the four types plus Wild. There is no
Generic, so a Basic Action die's "double generic" faces are symbol-less
and indistinguishable from "no energy" by pip count. Nothing depends on
that today — 2.6.1.5 means those dice never spin down to a single, and no
Basic Action card has Energize — but it is worth knowing before anything
tries to count generic energy. Recorded, not fixed.

---

## Part 28 — Basic Action dice, and two energy bugs (2026-09-01)

Part 27 recorded "generic energy has no symbol" as a gap that nothing
depended on. Looking properly, something did: **`SpendEnergy` sums
`face.Symbols`, so a Basic Action die on an energy face paid nothing at
all.** Every game has two Basic Action cards and six of their dice; they
are a real part of the energy economy, and they were contributing zero.

Fixing that turned up a second bug beside it.

### Bug 1 — generic energy was unrepresented

`GameConfig.EnergySymbols` declared the four types plus Wild. Rule 1.4.3
names a third: generic, "which can be spent on purchasing/fielding/
abilities but is not considered to be any type of energy". Rule 1.3.10
gives it to Basic Action dice; the Crossover glossary gives it to a
Crossover's single face.

`SymbolDef` gains `IsGeneric` beside `IsWild` - the two are opposites,
and both are properties OF a symbol rather than special-cased ids, so a
variant game can declare its own. Basic Action energy faces now carry
`Generic 2` (rule 2.6.1.5 - all three are doubles, which is why such a
die has no single to spin down to), a Crossover's single carries
`Generic 1`, and a four-type card's single carries `Wild 1`.

### Bug 2 — one wildcard satisfied every type requirement at once

`SpendEnergy` did `unmatched.Clear()` on seeing a wild. Rule 1.4.3 says a
wildcard is one energy that "may represent **any of** the four energy
types" - one energy, one type, not a skeleton key.

The effect was concrete: rule 2.6.2.3's own example (2) is a 3-cost
bolt-fist Crossover needing "2 of those energy types [to] be a bolt and a
fist". One Sidekick's wild plus two generic bought it outright.

Now each wild PIP covers one outstanding type, and wilds are applied
AFTER the printed symbols so a wild is never spent covering a type a real
symbol already covered - otherwise bolt + wild would fail a bolt-fist
card, which is the obvious way to get this wrong in the other direction.
A double wild covers two.

### Tests are the rulebook's worked examples

`EnergyPaymentTests` uses 2.6.2.3's own examples where it has them,
because both bugs are the kind a plausible implementation passes.
Mutation-checked: reverting either fix fails a test.

One honest note recorded in the code: the explicit "skip generic when
matching types" line is belt-and-braces. No card prints Generic among
its energy types, so generic would fall through the else and be removed
from a set it was never in. The line states rule 1.4.3 where the
decision is made rather than relying on that; it is not load-bearing,
and its comment says so.

**Energize is next**, and now has both things it needs: a double-energy
face to fire on, and dice whose energy actually pays.

## Part 29 — Energize: implemented, plus a Self/Bound gap it exposed (2026-09-01)

Part 28 left Energize unblocked; V2_TAIL_POLICY.md's own entry (written
the same day, after the F14-era plan text was corrected) already had the
right shape sketched: `TurnStepEntered(Main)` + a condition reading
`Face.SymbolCount`, NOT a `DieFaceChanged` filter (the decisive case - a
die already on double energy that nobody rerolls - has no face change to
filter on). Confirmed against the Comprehensive Rules text directly this
session: "whenever you roll this die on one of its double energy
faces... during the Roll and Reroll Step, only check at the end of the
Step" and "does not need to be active to trigger." The user supplied the
correction that sent this session looking: the previous "doesn't fire
when rolled, but at a stage boundary" framing was right about the
mechanism but risked being read as "not tied to the roll at all," which
the rule text contradicts.

**Signed off**: one vocabulary addition, `StatKind.SymbolCount`
(threaded through `GetStatValue`/`GetBaseStatValue`, reading
`Face.SymbolCount` on the checked die's current face) - reusing
`StatThreshold` exactly as the tail note itself recommended, no new
shape. Two engine-level fixes needed no sign-off (mechanism, not
vocabulary): `TurnEngine.FinishRoll` never fired `TurnStepEntered(Main)`
at all - a real gap, not a design question - and `EventBus.Fire`'s
candidate scan (Field/Attack Zone only) can't see a die sitting in the
Reserve Pool, which is exactly where a just-rolled die is by the time
`FinishRoll` runs. Both fixed, the second narrowly scoped to
`TurnStepEntered(Main)` specifically (not a general widening) so Colossus
"Piotr"-style CleanUp abilities keep their "while active" zone gate.

**The real find**: building the generic plumbing test
(`EnergizeTests.cs`) caught that `CountAtLeast(TargetFilter(Self: true,
Stat: SymbolCount>=2), 1)` always returned true regardless of the die's
actual face. `TargetResolver.Query`'s `Self`/`Bound` branches returned
their fixed id immediately, bypassing every other field on the filter -
correct for Zones/Kind/Ownership (meaningless once identity is already
fixed) but wrong for Tags/Affiliations/Stat, which are live questions
about that specific die's CURRENT state, not ways of finding it. Fixed by
having both branches run the same Tags/Affiliations/Stat checks the pool
path already used, returning `[]` on a failed check instead of echoing
the id back unconditionally. Checked against every existing `Self: true`
call site first (none combine it with Stat/Tags/Affiliations), so this
closes a real gap rather than changing behavior anything relied on.

**Migrated**: all 15 DPS cards printing Energize (Phoenix "Firepower",
Storm "Queen", Professor X "Uncanny Leadership" and "Dreamer", Cyclops
"Defending the Phoenix", Rogue "Strength Absorption", Psylocke
"Heiress", Jubilee "Rebellious Nature", Mystique "Taught by Magneto",
Wolverine "Hardened by Madripoor", Iceman "Mr Ice Guy", Angel "Wings
Over the World", Cable "I'll Do This All Day", Colossus "Skilled
Painter", Toad "Looking for Comradery"). Two clauses tailed, both
`IsImplemented: false` rather than fully vanilla since the rest of each
card works: Iceman's own Energize ("double target die's printed A") has
no live-doubling shape in the vocabulary (`ModifyStat`'s deltas are
plain `int`, and `SetAttack`'s `StatOf` would only echo the same value
back, not double it - a single-card gap, not worth a second live-value
spike); Professor X "Dreamer"'s WhenFielded clause is the payment-source
visibility gap the user already declined to build (V2_PLAN.md's F13
addendum, "2 Bishop, Forge, Professor X").

Verified: `dotnet build DiceFight.slnx` clean; v2 tests up from 214 to
233 (19 new: 5 generic Energize plumbing, 14 per-card); v1's full suite
re-run untouched (580/580).
