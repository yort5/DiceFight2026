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
~82% (V2_VOCABULARY.md Part 11) for a specific, known reason: the
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

### Investigated: why `Bound` cannot simply compose with the filter (2026-08-24)

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
