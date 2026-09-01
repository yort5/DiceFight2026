# V2 Tail Policy

Cards whose real ability text doesn't fit the closed v2 vocabulary
(V2_PLAN.md ground rule 2 - Appendix C format). Policy meanings:

- **Approximate** — expressed in templates with a stated difference
  (also noted in the card's own definition comment).
- **Vanilla** — no ability; RawText still shown in UI. Default while
  migrating.
- **Ask** — flag for the user; candidate for redesign under Direction
  C, or for a small vocabulary sign-off ask later.

Ask-policy entries below are **Vanilla** in `src/DiceFight.V2/Data/CardCatalog.cs`
(`IsImplemented: false`) pending a user decision on whether/how to
close the gap. Never guess a wrong approximation silently (house rule,
carried from v1).

## Curated team migration (V2_PLAN.md Phase 8 task 2, 2026-08-23)

Of the 20 curated-team cards (`CardCatalog.TeamA*`/`TeamB*`), 9 are
implemented (Apocalypse, HarleyQuinn, CaptainMarvel, Dazzler,
ShockingGrasp, FranklinsGalactus, GodEmperorDoom, Groot fit cleanly;
Casket of Ancient Winters is Approximate - see its row) and 11 are
tailed Ask below. This is a lower fit rate than the DPS set's own
~82% (V2_VOCABULARY_HISTORY.md Part 11) for a specific, known reason: the
curated rosters were deliberately built by v1's own author to exercise
one live example of each Attack-Step keyword the web client needs
(Call Out, Infiltrate, Tag Out, Range, Intimidate) - and Phase 7's own
combat implementation deliberately did NOT port any of those five
keywords (only Overcrush and Fast), so every one of their showcase
cards was always going to tail here. Not a representative sample of
the wider catalog's fit rate.

*(2026-08-24)*: Casket of Ancient Winters' original Ask entry (the
rule-3.2.5 live-resolution gap) is RESOLVED - the user signed off on
per-ability snapshot semantics (every TargetFilter candidate pool
inside one ability resolves against that ability's own
start-of-resolution zone/face snapshot; the snapshot dissolves when
the ability finishes, so later queued abilities see live state -
which is also the semantics a blanked card's already-queued trigger
will need once the ability-blanking spike lands). Implemented in
`EffectInterpreter`/`TargetResolver`; conditions (`TargetWasKOd`) and
`PerMatch` amounts deliberately stay live. The card's remaining
difference is only its Epic Basic Action mechanics, tracked below.

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| MSW019 | Beast | Regenerate keyword (reroll instead of KO) - not CombatFlag/CombatRule-shaped, not ported in Phase 7 | Ask |
| MSW020 | Black Panther | Energize's precise trigger (an energy face showing 2+ pips, during Roll & Reroll) - `EventFilter`/`Condition` have no symbol-count check; deferred exactly per Phase 5's own note ("wiring deferred to whichever card needs it first") | Ask |
| GOTG005 | Black Widow | Call Out keyword (designated-blocker restriction + cancellation rules) - not ported in Phase 7 | Ask |
| JLL002 | Ant-Man (Through The Cracks) | Amplify keyword - reacts to ANY of the controller's Action-die uses, not just its own (`TriggerKind.DieUsed`'s self-only shape doesn't cover "any action die"); Amplify itself also not ported | Ask |
| MSW002 | Cosmic Cube (epic) | `SwapLife` - life-total swap, explicitly named non-coverage (V2_PLAN.md Appendix A); also Epic Basic Action mechanics (once-per-turn limiter, returns to card) have no `CardType` distinction | Ask |
| MSW027 | Falcon | `Teamwatch` isn't one of the 10 frozen trigger kinds; its Global's `FieldSidekickForEachPlayer` per-player "field one if able" shape has no template equivalent | Ask |
| GOTG105 | Ricochet | Infiltrate keyword (+ its own `WhenInfiltrates` reactive) - not ported in Phase 7 | Ask |
| TAG003 | Big E | Tag Out keyword - not ported in Phase 7 | Ask |
| SKC090 | Starfire (Starbolts) | Range keyword - not ported in Phase 7 | Ask |
| CW014 | Scarlet Spider | Intimidate keyword; its own destination (`Zone.Intimidated` in v1) has no equivalent in v2's 10-zone list at all | Ask |
| MSW001 | Casket of Ancient Winters (epic) | Effect tree fully implemented (rule-3.2.5 per-ability snapshot, signed off 2026-08-24 - see the dated note above). Remaining difference: Epic Basic Action mechanics (rule 1.2.3 - once-per-turn limiter, die returns to its card instead of Out of Play) aren't modeled; `CardType` has no Epic distinction, so the die behaves as an ordinary Basic Action die | Approximate |
| GOTG008 | Cosmic Cube (Infinite Possibilities) | A "redraw a chosen subset of dice already drawn this turn" flow - explicitly named non-coverage (V2_PLAN.md Appendix A: "draw-and-choose flows") | Ask |

## DPS catalog batch 1 (V2_PLAN.md Phase 8 task 4, 2026-08-24)

14 of 15 implemented. The one below is tailed.

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS029 | Deathbird (Treacherous) | Deadly keyword - Phase 7 deliberately ported only Overcrush and Fast. Deadly is this card's entire text, so there is nothing else to express | Ask |

### RESOLVED: the timing-window model (Spike C, signed off + implemented 2026-08-24)

The user signed off on a **flat, ordered, extensible step list** and it
is now built (`V2_VOCABULARY.md` Part 13 for the design;
`Model/TurnStep.cs`, `GameConfig.Steps`, `EventFilter.Step`).
Colossus "Piotr" is un-tailed and implemented - its ability names
`StepIds.CleanUp` and fires there and nowhere else.

What this does NOT yet un-tail: the five combat keywords (Call Out,
Infiltrate, Tag Out, Range, Intimidate) are now *expressible* - the
step list can name their windows - but expressible is not built. Each
still needs its actual keyword behavior implemented, and their step
entries are added to `TurnStepDefs.Standard` when that happens, per
the same "declare it when it has a consumer" rule. They stay Ask.

**All three of Spike C's named fidelity gaps are fixed** (2026-08-24):
Main's end-of-step unfielded-dice sweep, the Reserve Pool clearing at
Clear and Draw rather than Clean Up, and the Attack Step's three
"resolve effects" windows plus the Fast/normal damage split.
`TurnStepDefs.Standard` now runs the whole TURN SUMMARY. The only step
ids still reserved-but-unused are the keyword windows (Range /
Infiltrate / Tag Out), which join the list when those keywords are
built.


## Spike B findings (2026-08-24)

Cards evaluated while implementing live-value Amounts. Rogue "Mrs. X"
(DPS049) closed and is implemented; these did not.

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS001 | Archnemesis | Global half now **implemented** (card-scoped Globals + Spike B's live `SetDefense`). Its WhenUsed half still needs a bind-only step: both dice must be bound before either takes damage, but a `TargetFilter` only binds as a side effect of the node using it. A no-op `ModifyStat(AtkDelta: 0)` works as a bind step but is an obscure idiom to propagate through card data; proposed instead is a `Bind(TargetFilter)` template | Ask |
| DPS107 | Dark Phoenix (Destructive Force) | `EventValue` now supplies "that much damage", but "when an **opposing** character die damages Dark Phoenix" needs damage-SOURCE visibility, which no event payload carries. Same family as the payment-source gap the user already designated alter-or-skip | Ask |

### RESOLVED: Globals are card-scoped, not die-scoped (fixed 2026-08-24)

Rule 2.6.5.2 - a Global ability is usable by **card ownership alone**,
without any die of that card being active - and the TURN SUMMARY states
plainly that **both** players can use Global Abilities (the inactive
player after priority passes). v1 implements this correctly:
`UseGlobalAbility(state, queue, cardId, playerId, energy)`.

v2's `TurnEngine.UseGlobal(state, queue, dieId, abilityIndex, energy)`
instead requires an active fielded die controlled by the **active**
player. Consequences: a Global printed on a Basic Action card can never
be used (no such die is ever fielded), and the inactive player can never
use any Global.

Fixed. `UseGlobal(state, queue, cardId, playerId, abilityIndex, energy)`
is now card-scoped and player-parameterised, with rule 1.5.8.5's
inactive-player rule (spent energy to the Used Pile, not Out of Play)
implemented alongside it. Archnemesis's Global is migrated and works
with no die of the card anywhere in play; a second test covers the
inactive player using a Global.

`MayPay` gained a fallback stand-in for its yes/no choice token (the
answering player's id) since a card-scoped Global has no "self" die to
use as one.

## DPS catalog batch 2 (V2_PLAN.md Phase 8 task 4, 2026-08-24)

11 cards. 7 fully implemented (Making the Team un-tailed same day - see its row) (Mutation, Gambit "Unless I Got Someone
to Play With", Psylocke "Advanced Telekinetic Combatant", Jean Grey
"Peaceful Coexistence", Deadpool "Collect THIS!", Angel "Xavier's
Dream"), 2 partial (Magneto "Visionary", Blob "Immovable" - their
continuous halves work), 3 vanilla.

Batch 2 was chosen to exercise vocabulary nothing had touched: Spin
(both modes), Reroll's Finding-8 params, GrantCounter, CostModifier,
TargetingProtection, CombatRule. All six worked on first authoring.

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS007 | Making the Team | **Implemented** (2026-08-24). The `FieldDie` default was the problem, not the vocabulary - corrected per user ruling, see below. Remaining difference: "a **character** die from your Used Pile" is approximated as `Kind: AnyDie` + `NoneOf: ["sidekick"]`, because a dormant die has no face to read and `TargetFilter.Kind` cannot express "character-type CARD" (`CharacterDie` reads the current face; `ActionDie` reads CardType, with no negation). A Basic Action die in the Used Pile would therefore be offered as a choice, then always fail the character-face check and be Prepped | Approximate |
| DPS086 | Phoenix (Psionic Maelstrom) | **No tag-check condition** - see the investigation below. `BindAs` closed half of Part 3 #24 (the second clause can reference the die the first damaged), but "if that character die is a **Villains** character die" is a tag test on a bound die, and none of the 7 frozen conditions test tags | Ask |
| DPS063 | Colossus (Organic Steel) | Confirms Part 2 #14 on a second card: `DamageModifier` is a CONTINUOUS template, and this is a one-shot, once-per-turn, optional redirect with a burst-face alternative. None of one-shot-ness, the frequency limit, the choice, or the burst branch is expressible on a continuous grant | Ask |
| DPS081 | Magneto (Visionary) | CombatRule + Global **implemented**; `Teamwatch` is not one of the 10 frozen trigger kinds, so that clause is dropped (v1 made the same call on the same card) | Ask |
| DPS101 | Blob (Immovable) | CombatRule **implemented**; "when Blob KO's an opponent's Sidekick, return it to their bag" needs KO-SOURCE attribution, which `DieKOd`'s payload does not carry - same family as the damage-source gap (DPS107) and the payment-source group | Ask |

### Keyword behavior still unbuilt (affects implemented cards)

Jean Grey "Peaceful Coexistence" is fully implemented as a card: her
ability is exactly "put a Loyalty Counter on this card", and it does.
But **Loyalty's own rule - a die gets +1A/+1D per counter - is engine
behavior that does not exist yet**, so the counters accumulate without
effect. Same category as Deadly and Regenerate: the card is right, the
keyword is not built. Notably this is *not* expressible as a
`StatAura` either - its `AtkDelta` would need to be "the value of a
named counter", and `PerMatch` counts matching DICE, not a counter's
magnitude.

### RESOLVED: `FieldDie` fields at the level the die rolled (2026-08-24)

User ruling, and it reframed the problem correctly: a die being fielded
**always has a level**, because a die showing a character face has one.
So the level is never in question - `FieldDie.Level` is an OVERRIDE,
not the source of truth.

`FieldDie.Level` is now `int?`, defaulting to null ("field it as it
stands"). It is named only when a card overrides the rolled level
(Jubilee "Rebellious Nature" - "field this die for free at level 2").

Implementing it surfaced a third case neither of us had named: a die
showing a rolled **energy** face is not dormant, so it has a
`CurrentFaceIndex` and `MoveToZone` would have left it there - fielding
it on an energy face. All three cases now resolve explicitly, and all
three end on a character face: rolled character face with no override
keeps it; a named level takes that level's face; dormant or
energy-faced takes the lowest character face.

The old default (`int Level = 1`) silently snapped a die that rolled
its level-3 face down to level 1 on being fielded. Regression test
covers exactly that.

### RESOLVED (narrowly): `Self`/`Bound` now honor Tags/Affiliations/Stat (2026-09-01)

Energize's design (see this file's own entry below, now resolved) needed
exactly the composition this section originally rejected -
`CountAtLeast(TargetFilter(Self: true, Stat: SymbolCount>=2), 1)` to ask
"is the die I already am showing a double-energy face" - and the
build-out proved the rejection below was about the WRONG half of the
split it itself identifies two paragraphs down. `TargetResolver.Query`
implemented exactly that split, not the composing-with-everything version
that broke Making the Team: `Self`/`Bound` still skip Kind/Zones/
Ownership unconditionally (Making the Team's own Else branch, still
green), but now DO check Tags/Affiliations/Stat when the author set them,
returning no match instead of echoing the id back unconditionally when
they fail. Covered by the same sign-off as Energize's design (the
approved tree only works if this composes) rather than a fresh ask - no
card yet needs `Bound` + `Tags` specifically, but nothing new is required
if one does; the recommended `HasTag` condition below is superseded, not
needed.

### Investigated: why `Bound` cannot simply compose with the filter (2026-08-24, superseded above)

The batch-2 write-up floated making a `Bound` filter fall through to
the rest of the filter chain, so `CountAtLeast(TargetFilter{Bound:"t",
Tags: AnyOf["Villains"]}, 1)` could stand in for a tag condition with
no vocabulary addition. **Tried it; it breaks real cards.** Verified,
not reasoned: composing `Bound` with the full chain fails both Making
the Team tests.

The reason is `TargetFilter.Kind`, which defaults to `CharacterDie`. A
bound reference is frequently to a die that is *not* on a character
face - Making the Team's own Else branch is
`MoveDie(TargetFilter(Bound: "rolled"), PrepArea)` for a die that just
rolled an ENERGY face. Under a composing `Bound` that filter matches
nothing and the die silently stays in the Used Pile.

So `Bound` skipping the SELECTION SCOPE fields (Kind / Zones /
Ownership) is load-bearing, not an implementation shortcut - the
frozen spec's own wording ("skip resolution") is right. Only the
IDENTITY PREDICATES (Tags / Stat) are arguably skippable-by-accident,
and splitting the filter into "scope fields that Bound ignores" versus
"predicate fields it honours" is a subtle rule to carry.

**Recommendation: add an 8th condition instead** -
`HasTag(CheckBinding, TagQuery)`. It is consistent with how conditions
already address a bound die: three of the seven (`TargetWasKOd`,
`OnBurstFace`, `OnFaceKind`) already take a `CheckBinding` and inspect
that die's state, and a tag test is exactly that shape. Its absence
reads more like an oversight in the frozen set than a decision. It also
reads directly as card data - `Conditional(HasTag("t",
AnyOf["Villains"]), ...)` - rather than as a count-the-bound-die idiom,
and it generalises to every other "if that die is an [affiliation] die"
card in the catalog rather than closing one.

Needs sign-off (ground rule 2). Not implemented.

## DPS catalog batch 3 (V2_PLAN.md Phase 8 task 4, 2026-09-01)

10 migrated, 3 partials, 1 tailed outright. Led by the two cards Spike A
was built for - migrating them is the check that the vocabulary signed
off in Parts 19-21 actually expresses them, and it does.

**D'Ken "Shi'ar Civil War" (DPS141)** — `AbilityBlank` with a
`PurchaseCost Max 3` threshold, plus a fielding `CostModifier` of -99 for
the "free to field" half (`GetFieldingCost` already floors at 0, so no
set-to-zero mode was invented for one card). Fully migrated.

**Mister Sinister "Mutant Supremacist" (DPS083)** — fully migrated, and
worth noting because Part 19 listed its side-wide half under *"what this
spike does NOT close"*. `BlankCardText`'s `AllOpposing` mode closes it.
The card uses BOTH scopes: its when-fielded half is card-scoped (copies
not in play, Globals included) and its Global is die-scoped ("target
attacking character die"). That is the clearest single piece of evidence
in the catalog that the two scopes are genuinely different things, and
it is a card, not an argument.

### PARTIAL: clauses migrated minus one

- **Sabretooth "Am I Interrupting?" (DPS051)** — the "target Wolverine
  character die" half is migrated (a card name is a tag). The other half,
  *"or any character die with a 'While Wolverine is active' ability"*,
  addresses another card's ability TEXT. No filter in the frozen
  vocabulary can see that, and it is not clear one should: it is a
  predicate over rules text, not over game state.
- **Beast "Combat Ready" (DPS098)** — the attack trigger is migrated. The
  surcharge (*"the first Beast die you purchase each game costs 1
  extra"*) is not: no `Duration` or `Condition` expresses "the first one
  this GAME". Everything in the vocabulary is turn-scoped or permanent.
  A once-per-game counter would do it (`GameState.Counters` is already
  the right shape) but that is a vocabulary addition, so it waits.
- **Rogue "Surveillance Immunity" (DPS089)** — the action-die removal is
  migrated. *"Fielding Rogue doesn't trigger opposing effects"* is
  trigger suppression, which Part 25 tailed as its own family. v1 did not
  model it either.

### TAILED: not attempted

- **Supreme Intelligence "Psionic Collective" (DPS093)** — Intimidate's
  destination is v1's `Zone.Intimidated`, which has no equivalent in
  v2's 10-zone list. Already recorded at `CardCatalog.cs:209` for the
  curated Beast; the same gap, a second card.

### Selection note, worth keeping for the next batch

Batch 3's candidates were picked by filtering the 116 remaining DPS cards
for those whose v1 TRIGGERS and EFFECTS both already have v2 equivalents.
That is 18 cards. Filtering on effects alone gives 42 - the gap is almost
entirely **keyword triggers**: `Energize` (15 remaining cards), `Awaken`,
`Teamwatch`. v2 has no `TriggerKind` for any of them.

**That is the single biggest blocker left in the DPS migration**, bigger
than anything Spike A closed, and it is a known one (see "Keyword
behavior still unbuilt" above). Roughly a quarter of what remains is
waiting on it. Worth costing before batch 4, since the alternative is
picking around it for several more batches.

### RESOLVED: Action vs Basic Action dice (2026-09-01)

Recorded as a gap and then closed the same day, once the user supplied
the taxonomy - which was not three sibling types but a NESTING:

> "Action Die is the broader category - dice that don't have fielding
> costs, attack or defense. Basic Action is a subset of that, the Action
> Dice available to both players. Epic Basic Action is a subset of Basic
> Action, with restrictions on when they can be purchased."

So "action die" in card text (Attune) is satisfied by any of them, while
"Basic Action die" (Boom Boom) is not. `CardTypes.IsActionDie()` and
`IsCommunity()` state the two levels; three of the four places that
compared against `CardType.BasicAction` meant the broad category and were
wrong:

| Site | Was | Now |
|---|---|---|
| `TurnEngine.UseAction` | Basic Actions only | any action die |
| `TurnEngine.CleanUp`'s unused sweep | Basic Actions only | any action die |
| `TargetResolver`, `TargetKind.ActionDie` | Basic Actions only | any action die |
| `TurnEngine.Purchase`'s community check | Basic Actions only | unchanged - correct |

`TargetKind.BasicActionDie` is the narrow filter for text that really
does mean the shared subset.

**Faces differ too.** A Basic Action die's three energy faces are generic
doubles (rules 1.3.10, 2.6.1.5); a non-basic Action die carries the
CARD'S OWN printed energy, exactly as a character die does.
`MigrationDice.BasicAction` and `MigrationDice.Action` are now separate,
the latter reusing the same `EnergyFaces` a character uses.

Tested with a synthetic Cosmic Treadmill "Antique Shop Discovery"
(GAF009), the user's own reference card - a fist/mask Crossover Action
card, so its die shows a generic single, two fist/mask doubles and three
action faces. Both directions mutation-checked: narrowing `IsActionDie`
back to Basic-only fails four tests, widening `IsCommunity` fails three.

**Epic Basic Action is deliberately not modelled** (user's call). When it
is, it is a subset of Basic Action and must therefore answer true to BOTH
`IsActionDie` and `IsCommunity`.

### RESOLVED: Energize is a step boundary, not a face change (2026-09-01)

15 remaining DPS cards need Energize, and it is now unblocked — the
migrated dice have real double-energy faces to fire on, and their energy
actually pays. Before anyone builds it, one correction, because
`V2_PLAN.md` asserted the wrong shape for a week and it is an easy
mistake to repeat.

v1's own definition (`Enums.cs`, `TriggerType.Energize`):

> "Fires once, **after Roll and Reroll completes**, for any
> Energize-keyword die that **ended up** on a double-energy face. Not
> checked against the initial roll — a die rerolled off double energy
> never triggers it, but a die left alone on a double-energy face does,
> once the reroll window closes."

**So it cannot be an `EventFilter` over `DieFaceChanged`**, which is what
the plan claimed. Three cases show why:

| Case | DieFaceChanged filter | Correct |
|---|---|---|
| Rolls double energy, then rerolled away | fires (on the roll) | must NOT fire |
| Rolls double energy, left alone | fires (on the roll) | fires — but at window close, not at the roll |
| **Already on double energy, never rerolled** | **never fires — no face change happened** | **must fire** |

The third is decisive: `DieFaceChanged` needs a change, and this die has
none. Energize is a **check over current state at a step boundary**, the
same shape as "at the end of your turn" cards.

**Likely shape**, not yet signed off:

- `TriggerKind.TurnStepEntered` with `Step` set to whatever follows Roll
  and Reroll (`StepIds.Main`), so it fires once the window has closed.
  The listener is the die itself, which `EventBus.Fire` already scans
  per-die.
- Plus a condition "this die is on an energy face showing 2+ pips".
  Nothing expresses that today: `OnFaceKind` distinguishes energy from
  character but not single from double. `Face.SymbolCount` exists and is
  what the condition would read. This is the one real vocabulary
  addition — probably a `MinSymbolCount` on `OnFaceKind`, or a new
  `StatKind` so the existing `StatThreshold` machinery covers it. The
  second is likely better: it reuses a shape rather than adding one, and
  `StatThreshold` already appears on both `TargetFilter` and
  `EventFilter`.
- Check the zone question flagged at `V2_PLAN.md`'s Phase 5 notes: v1's
  `CheckEnergize` has no zone gate, so a die in the Prep Area mid-roll
  can trigger. Decide deliberately rather than inheriting it.

The MSW020 Black Panther row above is the same gap, filed earlier from
the other direction ("`EventFilter`/`Condition` have no symbol-count
check"). Both close together.

**Built 2026-09-01, user-signed-off** (full account: `V2_VOCABULARY_HISTORY.md`
Part 29). Confirmed against the Comprehensive Rules text directly (not
just v1's own comment): "only check at the end of the Step," "does not
need to be active to trigger." The "likely shape" above landed almost
exactly as sketched, with the `StatKind` route chosen (`StatKind.
SymbolCount`, not a `MinSymbolCount` param on `OnFaceKind`). The zone
question resolved to v1's own precedent: `EventBus.Fire` now adds
`Zone.ReservePool` as extra listener candidates specifically for
`TurnStepEntered(Main)` (where `TurnEngine.FinishRoll` - previously
silent - now fires it), matching `CheckEnergize`'s own `Zone.ReservePool`
scan and keeping every other `TurnStepEntered` step (CleanUp, etc.)
Field/Attack-Zone-only. All 15 DPS cards printing Energize are migrated;
see `DpsCards.cs`'s own Batch 4 for two single-card tail items this
uncovered (Iceman "Mr Ice Guy"'s live-doubling gap; Professor X
"Dreamer"'s WhenFielded clause, which is the payment-source visibility
group `V2_PLAN.md`'s Appendix A addendum already named - "2 Bishop,
Forge, Professor X" - one of that four is Professor X "Dreamer").

## DPS catalog batch 4 (V2_PLAN.md Phase 8 task 4, 2026-09-01)

All 15 DPS cards printing the Energize keyword, unlocked by the entry
above. 13 fully migrated, 2 partial (both `IsImplemented: false`).

### PARTIAL: clauses migrated minus one

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS114 | Iceman (Mr Ice Guy) | The Energize clause ("double target character die's printed A until end of turn") needs a delta equal to a LIVE bound die's own stat - `ModifyStat`'s `AtkDelta`/`DefDelta` are plain `int`, and `SetAttack`'s `StatOf` would only echo the same value back (a no-op), not double it. The continuous half ("your Sidekick dice get +1A while active") is migrated | Ask |
| DPS047 | Professor X (Dreamer) | The WhenFielded clause ("if you spend an X-Men die to field Professor X, Prep a die from your bag") needs payment-source visibility - same family as the group `V2_PLAN.md`'s Appendix A addendum already designated alter-or-skip (Bishop x2, Forge, this card). The Energize clause is migrated (same shape as Professor X "Uncanny Leadership"'s identical text) | Ask |

## DPS catalog batch 5 (V2_PLAN.md Phase 8 task 4, 2026-09-01)

12 cards: 4 full, 4 partial, 4 tailed outright. First batch to hit the
affiliation-first-class split's own blind spot - see the new gap below,
found via two different cards independently.

### NEW GAP: nothing can GRANT an affiliation any more

`GetAffiliations` reads only `CardDef.Affiliations`, with no fold-in of
anything granted - unlike `GetTags`, which folds in `DieInstance.
GrantedTags` and every active `TagAura`. Before the affiliation-first-
class split (Parts 17-18, 22) this worked by accident, because
affiliations were just tags and `GrantTag` covered both. Two cards this
batch print "gains [Affiliation]" independently (Radicalization's Global,
Emma Frost "Influential"'s Sidekick clause) - not a single-card miss.
The fix shape is presumably a `TagAura`-equivalent for affiliations (or
widening `TagAura`/`GrantTag` to also touch affiliations, which is a
question the tag/affiliation split was explicitly built to prevent) - a
vocabulary question, needs sign-off, not attempted here. Both clauses
below are tailed against it; watch for a third card before proposing a
shape.

### NEW GAP (investigated, not migrated this batch): Continuous Basic Action mechanics

Lab Test (DPS005), Organic Steel "Prevent Damage" (DPS010), and Dampening
Collar (DPS002) are all "Continuous" Basic Actions (rule 1.2.3/2.6.4.2) -
an Action die that sits in the Field Zone granting an ongoing effect
rather than resolving once and leaving. `V2_PLAN.md`'s Phase 8 batch-1
note already flagged `CardType` has no Epic/Continuous distinction yet;
these three are exactly the cards that would exercise it. Also missing:
`TriggerType.ContinuousResolve` (the "send this die to your Used Pile to
[effect]" trigger, not one of the 11 frozen `TriggerKind`s) and a
one-shot `PreventDamage` template (Organic Steel's own clause - no
existing template reduces/prevents a SPECIFIC upcoming damage instance,
as opposed to `DamageModifier`'s continuous reduction). Not attempted -
recorded so a future session doesn't re-derive it from scratch.

### PARTIAL: clauses migrated minus one

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS016 | Tight Ranks | The WhenUsed clause ("at least 3 active dice that share A Team Affiliation") is existential over affiliations - CountAtLeast can only ask "at least N of a NAMED affiliation," not "some affiliation repeats 3+ times." The Global (Counter-threshold ModifyStat) is migrated | Ask |
| DPS012 | Radicalization | The Global ("target character die gains X-Men or Brotherhood of Mutants") is this batch's affiliation-grant gap. The DealDamage + double-burst Ko half is migrated | Ask |
| DPS062 | Cable (Bosom Buddies) | The purchase-discount half ("your Deadpool costs 1 less") needs a continuous discount scoped to ONE NAMED CARD - `CostModifier.Whose` is player-scoped for `Purchase`/`GlobalEnergy` (Jean Grey "Xavier's Dream" precedent), with no card-identity field the way the one-shot `PurchaseModifier.CardKind` has. The +2A `StatAura` (card-name tag) is migrated | Ask |
| DPS030 | Emma Frost (Influential) | "...and gain the Hellfire Club affiliation" is this batch's affiliation-grant gap. The +1A/+1D Sidekick `StatAura` is migrated | Ask |

### TAILED: not attempted

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS019 | Bishop (Tortured Timeline) | "Opposing effects can't cause Bishop to be rerolled or spun" protects against specific EFFECT TYPES regardless of whether they target at all - a different axis than `TargetingProtection`'s targeting-by-source-type block. No shape in the frozen vocabulary reaches it | Ask |
| DPS059 | Bishop (I'm Back) | Payment-source visibility - one of the "2 Bishop" cards `V2_PLAN.md`'s Appendix A addendum already named | Ask |
| DPS071 | Forge (Support Technician) | The purchase surcharge is qualified by the PURCHASED CARD's own cost ("2 or less") - the same card-identity gap Cable "Bosom Buddies" hits, via a threshold instead of a name. Dropping the threshold would silently surcharge every purchase, a real behavior change, not a stated approximation (house rule: never guess wrong silently) | Ask |
| DPS004 | Greetings from Krakoa | "Each of your dice that spins up gets +2A" is conditioned on whether THAT SPECIFIC die's own spin actually moved it - a per-die outcome-conditioned bonus no template composition reaches (`Spin` has no "and a bonus for whichever ones actually changed" companion) | Ask |

## DPS catalog batch 6 (V2_PLAN.md Phase 8 task 4, 2026-09-01)

14 cards: 8 full, 6 tailed outright (no partials this batch - the two
new gaps below each killed a whole card, not one clause of it).

### NEW GAP: TargetFilter can't exclude the source die from its own pool

`EventFilter` has `ExcludeSelf` for reactive triggers; `TargetFilter` has
no equivalent for "match this shape, but never the die granting the
effect/aura." Two cards hit it independently: Cable "High Stakes"
("double the printed A of all your OTHER character dice," a one-shot
triggered effect where Cable himself, now in the Attack Zone, shares a
zone with the "other" dice being buffed) and Angel "Jean Grey's School"
("other character dice with Founder get +1A," a continuous aura where
Angel is herself Founder and herself active at the same moment). Where
the source's own zone at query time already excludes it - an attacker
scoped to "Field Zone" while it's sitting in the Attack Zone (Cyclops
"Utopia Realized"/"Xavier's Dream", this batch's own full migrations) -
this doesn't bite; it's specifically the same-zone-same-moment case that
has no expression. Likely shape, not proposed for sign-off yet: a plain
`ExcludeSelf: bool` on `TargetFilter`, mirroring `EventFilter`'s own
field, resolved the same way `Self`/`Bound` already are (against the
`"self"` binding) - watch for a third card before asking.

### NEW GAP: a card can't grant a continuous benefit to ITSELF for something that happens before it's active

Every continuous template requires its own source die to already be in
the Field or Attack Zone (`ContinuousRegistry.ActiveSourceDice`) before
it grants anything to a target. That's fine for D'Ken/Mystique-style
"while I'm active, OTHER dice get X" grants, and fine for Jean Grey
"Marvel Girl"'s own Global-surcharge clause (only cares whether SHE is
active). It breaks for her OTHER clause, "Jean Grey is free to field [while
a different X-Men die is active]" - being free to field is a property she
needs BEFORE she's fielded, i.e. before she can ever be an
`ActiveSourceDice` candidate. Genuinely circular with the current
architecture, not a targeting-shape problem - no `TargetFilter` change
fixes it. Filed as a real gap; no shape proposed.

### FULL: no new gap

**Cyclops "Utopia Realized" (DPS105)** and **"Xavier's Dream" (DPS140)** -
"while you have 2+ [Sidekick-gated for the latter] character dice in the
Field Zone" reads correctly because Cyclops himself is in the Attack
Zone by the time either check runs (he's the one attacking). "Xavier's
Dream" is also `Distribute`'s first real second user after its own
motivating example (`V2_PLAN.md`'s F5 addendum) - "divided how you
choose among any number of target character dice" is `Count: 0` (the
full pool, no separate choice) plus the point-by-point `Distribute`
assignment.

**Jean Grey "Xavier's Dream" (DPS075)** - the `CostModifier(GlobalEnergy,
Player-scoped Whose)` shape `ContinuousRegistryTests.cs` already had as a
generic worked example (`V2_VOCABULARY_HISTORY.md` Part 2), migrated as
a real card for the first time.

### RESOLVED (found while building EmmaFrostFinesse): `MoveToZone` wiped a Reserve-Pool-bound die's face

Emma Frost "Finesse"'s own `Reroll(NonCharacterMoveTo: Zone.ReservePool)`
exposed that `EffectInterpreter.MoveToZone`'s "leaving Field/Attack Zone"
reset wiped `CurrentFaceIndex` to null for ANY destination, including the
Reserve Pool - which `DrawToZone`'s own convention says should keep its
face ("landing in Reserve Pool means rolled"). A die moved there this
way ended up dormant and functionally useless as energy. Fixed
generally (`ShowsFace` now includes `ReservePool` alongside Field/Attack
Zone in the face-preservation check); `EffectInterpreterTests.cs` has the
regression test. No sign-off needed - an engine bug, not a vocabulary
gap.

## DPS catalog batch 7 (V2_PLAN.md Phase 8 task 4, 2026-09-01)

17 cards: 7 full, 3 partial, 7 tailed. Kitty Pryde "Right of Passage" and
Toad "Secondary Mutation" are the catalog's first two real Awaken cards -
built on the same day's `RequireSelf` fix (without it, an unrelated die's
own Awaken would have cross-fired on either one).

### RESOLVED (found building Gambit "Ace in the Hole"): character dice had no way to print a burst face at all

`MigrationDice.Character`'s only overloads built every character face
with `Burst: 0` - fine for every card so far, since none of them checked
their OWN current face's burst level (only Action/Basic Action dice, via
the separate `bursts:` param on those helpers, ever got one). Gambit's
own "you may draw and roll a die. *Instead* [on a single-burst face],
draw 2..." needed exactly that, and the condition could never be true
under the old helper - the card would have silently always taken the
ordinary branch. Fixed with a new burst-carrying `Character` overload
(`bursts[i]` pairs with `levels[i]` by index, default 0); the two plain
overloads now just delegate to it with an empty list. Not a vocabulary
gap - `Condition.OnBurstFace` already existed and works fine once the
die itself can show what it's asking about.

### NEW GAP: "on your TEAM" reads the roster, not the board

Three cards hit this independently: Wolverine "Pure of Heart" ("if you
have no Villains character dice on your team"), Mystique "Relentless"'s
Global ("shares a Team Affiliation with a character card on your
team"), and Dark Phoenix "Malevolent"'s purchase discount ("if your
opponent has an X-Men character on their team"). v2 has no team-roster
concept at all yet - `Player.TeamCardIds` exists but nothing queries it
for card text; the roster is a Phase 9 (API/web) concern architecturally,
not something `GameState` tracks as game-relevant data today. All three
tailed; no shape proposed (this is a bigger question than a TargetFilter
field - it needs the roster to exist as queryable state first).

### PARTIAL / TAILED table

| CardId | Name | What's missing | Policy |
|---|---|---|---|
| DPS043 | Mister Sinister (Geneticist) | "When Mister Sinister KOs an opposing character" needs KO-SOURCE attribution - `DieKOd`'s payload has none, same family as Blob "Immovable" (batch 3). The Sidekick-KO and Global (Deadly grant) clauses are migrated | Ask |
| DPS033 | Gladiator (Psi Resistance) | Intimidate has no Zone (still not built, `V2_PLAN.md` Phase 2's own deferral). The Global also has no home: it's a ONE-SHOT activation that grants a TEMPORARY but still fundamentally continuous effect ("can't be targeted... until end of turn") - no one-shot template grants continuous-shaped protection; `GrantAbility` grants a triggered ability, not immunity. Both clauses tailed | Ask |
| DPS045 | Mystique (Relentless) | Global is the team-roster gap above. The Continuous "+2A while Wolverine active" is migrated | Ask |
| DPS027 | Dark Phoenix (Malevolent) | Purchase discount is the team-roster gap above. WhenFielded (Ko + Bound-affiliation-gated bonus damage, made real by today's `TargetResolver.Self`/`Bound` fix) and Global (self-Ko + `PurchaseModifier`) are migrated | Ask |
| DPS028 | Deadpool (#1 Draft Pick) | "If this game is in the draft format" - no game-format concept exists | Ask |
| DPS056 | Wolverine (Pure of Heart) | Team-roster gap, AND (independently) batch 6's "grant myself a benefit before I'm active" gap - either alone would tail it | Ask |
| DPS031 | Forge (More Than Firepower) | Payment-source visibility - the "Forge" in the F13 group (`V2_PLAN.md`'s Appendix A addendum: "2 Bishop, Forge, Professor X") | Ask |
| DPS053 | Supreme Intelligence | "A card with Kree IN ITS NAME" is a substring match; Tags carry the exact printed name only | Ask |
| DPS038 | Lilandra (Politician) | "If you have purchased a CHARACTER die this turn" - `TurnFact.PurchasedThisTurn` has no character-only variant; approximating would also fire off a Basic Action purchase | Ask |
| DPS044 | Moira (It's Not a Dream) | Continuous Action die mechanic, still not modeled | Ask |

## DPS catalog batch 8 (V2_PLAN.md Phase 8 task 4, 2026-09-01)

16 cards: 5 full, 3 partial, 8 tailed. The notable finding this batch
isn't a missing predicate - it's THREE cases of a vocabulary shape that
was signed off, sometimes even NAMED for the exact card that needed it,
but never actually wired to a real action anywhere in the engine. Each
was caught by building the real card, not by inspection:

- **`CombatRuleKind.CantFieldMore`** (Gambit "I Like Solitaire") - declared
  in the closed vocabulary, no consumer anywhere; `TurnEngine.Field`
  never checks it.
- **`EventFilter.Stat` on `DieKOd`** (Deathbird "Usurper" - the
  vocabulary's OWN canonical worked example for this exact shape,
  `Common.cs`'s own remarks name the card) - `KoDie` moves the die to
  the Prep Area BEFORE firing the event, so the stat check reads a
  dormant die's reset value (0), never the KO'd die's real one.
  Reordering isn't free either: `TargetWasKOd` and other reactive logic
  depend on the KO'd die already sitting in the Prep Area by the time
  abilities resolve.
- **`CostKind.ActionDieUse`** (Lilandra "Freedom Fighter" - `Finding 14`'s
  own named motivating example) - registered into `GameState.
  ActionDieUseCostModifiers` by `ContinuousRegistry`, but nothing reads
  that list, and `TurnEngine.UseAction` has no cost-charging step at
  all (using an Action die is free today - there is no base cost to
  surcharge in the first place).

None of these are vocabulary gaps in the usual sense - the SHAPE exists
and was signed off. They're implementation debt: a spec-level decision
that was never carried through to a real code path. All three tailed
rather than shipping a CostModifier/Condition/rule nothing actually
reads. Worth a deliberate pass before the DPS sweep finishes, since
there may be more of these hiding in already-migrated "full" cards that
happened not to get exercised by a real test.

### FULL

Magik "Better than Belasco" (Awaken - roll a die from the bag), Moira
"If It's Real" (three independent clauses, all fit), Sabretooth "Do I
Smell... Weakness?" (`PerMatch` stat-threshold aura), Jubilee "Things
Never Change", Iceman "Frozen Fists of Fury" (all three the same
`CountAtLeast(Tags: [NamedCard])` "while X is active" shape, now a
five-card-deep precedent).

### PARTIAL / TAILED table

| CardId | Name | What's missing | Policy |
|---|---|---|---|
| DPS077 | Kitty Pryde (Headmistress) | "Can't be targeted by your opponent" is unqualified - broader than `TargetingProtection`'s own `From: Global\|Action\|Both` (the ONLY targeting axis the engine checks at all; ordinary triggered-ability targeting has no protection hook). The +1A `StatAura` is migrated | Ask |
| DPS073 | Gladiator (The Empire Must Stand) | The Global is the same "one-shot activation granting temporary continuous-shaped protection" gap Gladiator's own "Psi Resistance" printing hit. The Loyalty-Counter clause (a plain card-name Tag match) is migrated | Ask |
| DPS061 | Blob (MGH Dependent) | Intimidate still has no Zone. The life-loss clause is migrated | Ask |
| DPS064 | Corsair (Leading the Starjammers) | "If Corsair's A or D is increased by an effect" needs to react to "my own stat was just modified BY THIS MUCH" - no event or payload reports a stat modification happening at all | Ask |
| DPS095 | Vulcan (Power Suppression) | "Ignore the abilities of dice blocking or blocked by Vulcan" needs a combat-ENGAGEMENT-scoped target: TargetFilter has no "currently engaged with die X" field | Ask |
| DPS066 | D'Ken (Obsessed) | v1's own call (`isImplemented: false`) - "use an action die from either player's Used Pile" needs a mechanic v1 itself never built | Ask |
| DPS060 | Blink (Exiles Team Leader) | "Each of your X-Men dice in the Field Zone" while Blink herself is attacking (same zone, same moment) - the TargetFilter self-exclusion gap (batch 6) | Ask |
| DPS072 | Gambit (I Like Solitaire) | `CombatRuleKind.CantFieldMore` gap above - the Reroll half fits, but shipping it alone would let a player exploit the missing enforcement | Ask |
| DPS093 | Supreme Intelligence (Psionic Collective) | Intimidate still has no Zone. Keywords (`Intimidate`, `Overcrush`) are still recorded - Overcrush is engine-native and works regardless | Ask |
| DPS069 | Deathbird (Usurper) | `DieKOd`/`EventFilter.Stat` timing gap above | Ask |
| DPS078 | Lilandra (Freedom Fighter) | `CostKind.ActionDieUse` gap above | Ask |

## DPS catalog batch 9 (V2_PLAN.md Phase 8 task 4, 2026-09-01) - final batch, closes 145/145

32 cards: 14 full, 5 partial, 13 tailed. No new missing predicates this
batch - every tail entry below matches a gap already on file, except
two genuinely new ones:

- **No token mechanic.** Master Mold "Endless Sentinels" would place a
  Sentinel die into the Field Zone from neither player's own bag/deck -
  no `PlaceToken`-equivalent template exists anywhere in the vocabulary.
- **No KO-count-this-turn number.** Corsair "Back from Outer Space"
  needs "if 4 or more of your character dice were KO'd this turn" -
  `Condition.NoKOsThisTurn` only expresses the boolean "zero," nothing
  reads a running count.

Two real card-definition bugs were also found and fixed while writing
this batch's tests (not vocabulary gaps - existing templates misused):
Mister Sinister "Dark Experimentation" targeted Used Pile Sidekicks
with `TargetKind.CharacterDie`, which a dormant die never matches
(fixed to `AnyDie`, per the Professor X "Uncanny Leadership"
precedent); Mystique "Freedom Force"'s `DamageModifier` targeted
`Self: true` only, when the printed text (and v1's own field name,
`grantsOwnDamageReductionFromOpponentAbilities`) protects her whole
side (fixed to `TargetFilter(Kind: CharacterDie, Ownership: Own)`).

### FULL

Deathbird "War of Kings", Ronan the Accuser "No Exceptions", Sabretooth
"You Ready to Party?", Moira "Strength of Foresight", Rogue "Unity
Squad", Mystique "Freedom Force" (bug fixed above), Mister Sinister
"Dark Experimentation" (bug fixed above - also the batch's first card
to need two independent, non-Bound Sidekick targets from one Global,
which v2's live-resolving `TargetFilter`s handle for free, unlike v1's
own same-instance-caching workaround), Wolverine "Trainer" (second user
of Toad "Secondary Mutation"'s Spin-based Teamwatch precedent, this
time watching for ANOTHER die's spin-up), Mystique "She Walks Among
Us", Magneto "Master of Magnetism", Kitty Pryde "Experienced Leader",
Toad "Journey Into Misery", Vulcan "Aggression", Beast "Xavier's
Dream".

### PARTIAL / TAILED table

| CardId | Name | What's missing | Policy |
|---|---|---|---|
| DPS106 | D'Ken (M'Kraan Crystal) | WhenAttacks half fits (same shape as D'Ken "Emperor"). "You take no more than 7 damage this turn" is a damage-to-a-PLAYER ceiling - no existing mechanism is player-scoped rather than die-scoped; v1 never built it either | Ask |
| DPS107 | Dark Phoenix (Destructive Force) | Global fits (same shape as Dark Phoenix "Malevolent"). Retaliation clause needs damage-SOURCE visibility on `DamageDealtPayload` - the long-standing pre-existing gap already on file for this exact card | Ask |
| DPS148 | Mister Sinister (Biologist) | Overcrush-grant Global fits. "Prevent non-combat damage to your OTHER character dice" is the TargetFilter self-exclusion gap (batch 6) - he shares a zone with the dice he'd protect | Ask |
| DPS145 | Lilandra (Majestrix) | Energy life-surcharge half fits (`CostModifier`'s `Currency: Life`). Action-Die-use surcharge half is batch 8's `CostKind.ActionDieUse` gap - registered, read by nothing | Ask |
| DPS152 | Wolverine (Tough for the Kids) | Global (Prep a die, once per turn) fits. Regenerate isn't built; the reroll/spin-protection axis is the same gap as Bishop "Tortured Timeline" - protects effect TYPES, not targeting-by-source | Ask |
| DPS002 | Dampening Collar | Continuous Basic Action mechanic (rule 1.2.3/2.6.4.2), still not modeled. Also independently needs an opponent-payable removal clause | Ask |
| DPS005 | Lab Test | Continuous Basic Action mechanic, still not modeled | Ask |
| DPS006 | Living the Dream | Continuous Basic Action mechanic, AND independently the team-roster gap (batch 7) for its team-wide Loyalty-Counter aggregation | Ask |
| DPS010 | Organic Steel | Continuous Basic Action mechanic, still not modeled | Ask |
| DPS015 | The Front Line | "Unblocked attacking character dice" needs a live combat-state predicate TargetFilter has no field for. The Global's opponent-payable escape from a debuff is the opposite shape from `MayPay` (controller pays, not opponent) - no template reaches it | Ask |
| DPS097 | Angel (Air Support) | "When an opponent targets one of your dice" needs `DieTargeted`, deliberately deferred at the Phase 0 gate review (`V2_PLAN.md` Appendix A) | Ask |
| DPS099 | Bishop (Time Traveller) | Payment-source visibility - a new example in the F13 group (2 Bishop, Forge, Professor X) | Ask |
| DPS100 | Blink (Warp Portals) | v1's own call (`isImplemented: false`) - "cancel a Global Ability" needs a real interrupt/cancellation primitive nothing here has | Ask |
| DPS116 | Jubilee (Fireworks) | Payment-source visibility again - "when you spend energy from an X-Men die" | Ask |
| DPS113 | Gladiator (Majestor Kallark) | Same one-shot-activation-granting-temporary-continuous-protection gap as Gladiator's "Psi Resistance"/"The Empire Must Stand" printings | Ask |
| DPS118 | Lilandra (Grand Admiral of the Guard) | Needs a reroll-after-damage-resolves hook conditioned on being an unblocked attacker - same class of gap as The Front Line's unblocked-attacker predicate, from the combat-engine side | Ask |
| DPS147 | Master Mold (Endless Sentinels) | Token gap above - no `PlaceToken`-equivalent template | Ask |
| DPS139 | Corsair (Back from Outer Space) | KO-count-this-turn gap above - `NoKOsThisTurn` is boolean only | Ask |

**Phase 8 Task 4 is now COMPLETE: 145/145 DPS cards migrated.**

## Post-Task-4 audit: "declared but unwired" vocabulary shapes (2026-09-01)

Following batch 8's discovery of three signed-off vocabulary shapes with
no real consumer (`CombatRuleKind.CantFieldMore`, `EventFilter.Stat` on
`DieKOd`, `CostKind.ActionDieUse`), the user asked for a deliberate
sweep of the rest of the vocabulary for the same pattern, rather than
waiting for the next card that happens to exercise one.

Method: for every declared enum value / field in `V2_VOCABULARY.md`,
grep for a real READ in engine code (`TurnEngine.cs`,
`EffectInterpreter.cs`, `CombatEngine.cs`, `ContinuousRegistry.cs`,
`EventBus.cs`, `QueryEngine.cs`, `TargetResolver.cs`,
`AmountResolver.cs`, `ConditionEvaluator.cs`) - not just a SET
(card-data or model declaration). Checked and confirmed WIRED: all 11
`EventFilter` fields, all 6 `TargetKind` values, all 6
`CombatFlagKind` values, all 5 `DamageModifierMode` values + all 3
`DamageSource` values, 3 of 4 `CombatRuleKind` values, 3 of 4
`CostKind` values, all 7 `StatKind` values, all 7 Condition kinds
(incl. all 3 `TurnFactKind` values), all 3 `SuppressionKind` values,
all 3 `ProtectionFrom` values (deliberately scoped to
`Global`/`DieUsed` triggers only, rule 3.8), all 4 Amount kinds, both
Duration expiry paths (`EndOfTurn`, `UntilYourNextTurn`'s
one-more-Clean-Up survival), and all 21 effect templates (each has a
real `EffectInterpreter` dispatch case) including confirming
`GrantCounter`'s counters are actually read back by `StatKind.Counter`
and `RememberCard`'s memory is actually read back by `Lockout`.

### NEW FINDING: `CombatRuleKind.CantSpinUp` — declared, validated as a fit, never wired

`ContinuousDef.cs:34` declares it; `V2_VOCABULARY_HISTORY.md` Part 11
(finding #47) validated it against Dampening Collar (DPS002) during
Phase 0 as a clean **Fit** - `CombatRule(CantSpinUp, Opposing)` "matches
the table directly." But `CombatEngine.cs`'s `ExecuteSpin`/
`ExecuteSpinToEnergy` (in `EffectInterpreter.cs`) never check
`state.CombatRules` for it at all - unlike `MinBlockers`/`BlocksN`,
which both have a real `Validate*` consumer in `CombatEngine.cs` and a
real card exercising each (Magneto "Visionary" DPS081, Blob "Immovable"
DPS family). And no card in the catalog ever actually sets it: when
Dampening Collar (DPS002) was built for real in batch 9, the whole card
tailed for an unrelated, orthogonal reason - the Continuous Basic
Action mechanic (rule 1.2.3/2.6.4.2) isn't modeled at all - so the
`CantSpinUp` clause was never even reached. Same shape as
`CantFieldMore`: a real consumer needs to be added to
`ExecuteSpin`/`ExecuteSpinToEnergy` (reject/no-op a spin targeting a
die a `CantSpinUp` rule covers) before any future card can safely rely
on it. Filed here rather than fixed - no card currently needs it fixed
today, same call as `CantFieldMore`.

No other new gaps found. `DiceFromBag`/`DiceFromPrep` (the two `Zone`
values with zero consumers) were re-confirmed as already-known,
already-documented (`TurnEngine.cs`'s own comment, `V2_PLAN.md`'s Rip
Hunter note) rather than a new finding.
