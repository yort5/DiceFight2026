# DiceFight v2 Core — Implementation Plan

**Vocabulary**: `V2_VOCABULARY.md` states what the vocabulary IS (now
including the Energize shape), derived from the code. `V2_VOCABULARY_HISTORY.md`
keeps how it got there (29 parts, 2026-08-22 to 2026-09-01). Cite the
history for reasoning; code against the spec.

**Status (refreshed 2026-09-01): Phases 0-7 complete; vocabulary FROZEN
at the 2026-08-22 gate review (`V2_VOCABULARY.md` Part 11), amended once
since under the same sign-off discipline (`StatKind.SymbolCount`, Part
29 - Energize); Phase 8 (Dice Masters as a game definition / card
migration) IN PROGRESS.**

- **Task 1-2** (config + curated teams) - done 2026-08-23.
- **Task 3** - became THREE spikes, not two, and **all three are now
  signed off AND implemented**: Spike B (live-value Amounts) and Spike C
  (the timing model) on 2026-08-24 (`V2_VOCABULARY.md` Parts 13-14);
  Spike A (ability-blanking + named-card lockout) across three
  increments on 2026-09-01 (`V2_VOCABULARY_HISTORY.md` Parts 23-25) -
  the `AbilitiesOf`/`ContinuousOf` choke point, `AbilityBlank`, and
  `Lockout` are all live. Affiliation-as-first-class (Parts 17-18, 22)
  landed the same day.
- **Task 4** (DPS catalog batches) - IN PROGRESS. Five batches landed:
  batches 1-2 on 2026-08-24, batch 3 (10 migrated, 3 partial, 1 tailed -
  D'Ken/Mister Sinister, the two cards Spike A was built for), batch 4
  (all 15 Energize cards, 13 full + 2 partial), and batch 5 (12 cards: 4
  full, 4 partial, 4 tailed - see `V2_TAIL_POLICY.md` for a new gap this
  one surfaced, affiliation-granting) all on 2026-09-01.
  **66 of v1's 145 curated DPS cards migrated, 79 to go.**
  (A 67th card, Domino "Not Really A Party Girl" (XFO010), was also
  migrated the same day, but sits outside this count entirely - `BonusCards.cs`,
  a one-off from the bulk catalog, not v1's curated 145.)
- **Task 5** (catalog-wide invariant tests) - not started.

The previous version of this header (refreshed 2026-08-31) said Spike A
was signed off but not implemented, and put the DPS count at 29/145; both
were stale within the day. Update the checkboxes in the Phase Overview as
phases complete, and add a one-line note after any phase where reality
diverged from this plan.

**Phase 0 outcome (2026-08-22, full arc)**: validated against 20
cards, expanded to 60, then against the community "Orange Ban" list,
then a scripted audit of the entire 145-card DPS set. The adopted
amendment chain (F1-F14, `V2_VOCABULARY.md` Parts 4-11 — all
user-signed-off) lands the frozen spec at: 11-field TargetFilter with
bindings and AnsweredBy, 18 effect templates, 7 conditions, 6
continuous templates with ActiveWhen/Source/Amplify, 10 events incl.
`DieFaceChanged`, per-card counters, and ground rules 1-8 (notably:
"you may" is always a real choice). **~119/145 (82%) of the DPS set
fits the frozen vocabulary cleanly**; the remainder is fully
accounted for: two named design spikes (ability-blanking + named-card
lockout; live-value Amounts), 5 architecturally-alien cards
(unchanged from v1), the payment-source group (user-designated
alter-or-skip), and single-card tail items. `V2_VOCABULARY.md` Part 1
is the single authoritative spec; implementing sessions code against
it, file misfits to `V2_TAIL_POLICY.md`, and never amend vocabulary
without sign-off.

## Context

`ARCHITECTURE_REVIEW.md` (read it first) audited the v1 engine after
implementing the full DPS set and found the open-vocabulary effect DSL
scaled badly: 47% of effect nodes are single-card, and continuous
effects became 39 one-per-card `Grants*` flags hardcoded into the
engine. The chosen direction (user decision, 2026-08-22) is **Option
B: a v2 core with a closed, simplified template vocabulary**, built
**with an eye toward Option C** (a future new game iteration) — so
dice, energy types, and rules constants are data from day one, never
hardcoded.

This plan is written to be executed by follow-up sessions
incrementally. Every architectural decision is already made here.
Implementing sessions should not redesign — if something in this plan
turns out to be impossible or clearly wrong, STOP and ask the user
rather than improvising a different architecture.

## Ground rules for every implementing session

1. **Never modify v1.** `src/DiceFight.Engine`, `tests/DiceFight.Engine.Tests`,
   `src/DiceFight.Api`, and `web/` are read-only reference material
   until Phase 9 (integration). v1 stays deployed and working.
2. **The vocabulary is closed.** If a card ability cannot be expressed
   with Appendix A's templates: do NOT add a template, a parameter, or
   an enum member. Add the card to the "Tail list" in
   `V2_TAIL_POLICY.md` with one line saying what it needs, leave it
   vanilla, and move on. Vocabulary changes require the user's
   explicit sign-off, recorded by editing Appendix A.
3. **Build + test before every commit**: `dotnet build` and
   `dotnet test` (all projects) must pass. `export PATH="$HOME/.dotnet:$PATH"`
   first (see the dev-environment notes in DESIGN_LOG/memory).
4. **Commit per completed task or small batch, push when a phase
   completes.** Direct commits to `main` (established project
   workflow). Pushing triggers a Cloud Run redeploy; v2 projects are
   not referenced by the Dockerfile until Phase 9, so this is safe.
5. **Log as you go**: append a short status update to `DESIGN_LOG.md`
   per work session (what/why/what broke), and tick this plan's
   checkboxes. This is how context survives across sessions.
6. **Tests exercise the real path.** Trigger firing must be tested
   through the event bus from a real game action, not by invoking an
   ability handler directly (v1 learned this the hard way — see the
   Awaken/Energize keyword-gate bug in DESIGN_LOG).
7. **When porting a rule, cite it.** v1 code comments cite rulebook
   sections (e.g. "rule 2.6.2.3"); keep doing that in v2 so the
   faithful-vs-simplified diffs stay auditable.
8. **"You may [X]" is always a real choice, cost or no cost.** Model
   with `MayPay` (a no-op `Cost` is fine) — never collapse to "always
   does X" on the reasoning that declining seems pointless. Corrected
   2026-08-22 (`V2_VOCABULARY.md` Part 7): declining can matter even
   with no attached cost — you may not want the effect, or accepting
   it may hand the opponent a trigger for one of their own reactive
   abilities. v1 got this wrong on exactly 2 cards (Rogue "Mrs. X",
   Moira "It's Not a Dream"); don't repeat it in v2.

## Phase overview

| # | Phase | Deliverable | Status |
|---|---|---|---|
| 0 | Vocabulary validation on paper | `V2_VOCABULARY.md` + 20 cards re-expressed | [x] |
| 1 | Project scaffolding + data model | `DiceFight.V2` + `DiceFight.V2.Tests` projects; GameConfig/DieDef/CardDef records | [x] |
| 2 | Game state, zones, turn machine | Config-driven state + turn steps, no abilities | [x] |
| 3 | Query pipeline | Stat/cost/legality queries with modifier interception | [x] |
| 4 | Event bus + triggered abilities | Events, subscriptions, FIFO ability queue | [x] |
| 5 | Effect template interpreter | All Appendix A effect templates working | [x] |
| 6 | Continuous templates | All Appendix A continuous templates working | [x] |
| 7 | Combat | Attack/block/damage using queries + events | [x] |
| 8 | Dice Masters as a game definition | Current game expressed as data; card migration pass | [ ] |
| 9 | API + web integration | v2 playable in the web client behind a switch | [ ] |

Phases must be done in order. 5 and 6 may interleave once 4 is done.

---

## Phase 0 — Vocabulary validation on paper

**Goal**: prove Appendix A's vocabulary covers real cards *before*
writing engine code, and produce the reference doc implementing
sessions will code against.

**Tasks**:
1. Copy Appendix A into a new `V2_VOCABULARY.md` (repo root) as the
   living spec. This plan's appendix is the seed; the spec file is
   authoritative from then on.
2. Re-express **20 cards on paper** in the spec file, as pseudo-data
   (the eventual C# record syntax, roughly). Choose: 10 cards that v1
   scripted with common nodes (pick from DealDamage/Ko/Spin/ModifyStat
   users in `SampleCards.cs`), 5 cards that v1 gave single-use nodes
   (e.g. Archnemesis DPS001, Colossus DPS103, Mutation DPS009), and 5
   cards v1 implemented as `Grants*` flags (search `CardDef.cs` for
   the flag names, then find the card in `SampleCards.cs`).
3. For each: either it fits (write the expression) or it doesn't
   (write one line on the nearest approximation and what's lost).
   Target: ≥15 of 20 fit cleanly. If fewer than 12 fit, STOP — the
   vocabulary needs a revision pass with the user before Phase 1.
4. Record per-card verdicts in a table at the bottom of
   `V2_VOCABULARY.md`. Commit.

**Acceptance**: `V2_VOCABULARY.md` exists with 20 worked examples and
verdicts; user has been shown the misfit list.

## Phase 1 — Project scaffolding + data model

**Goal**: compilable data model for games-as-data. No behavior.

**Tasks**:
1. `dotnet new classlib` → `src/DiceFight.V2` (namespace
   `DiceFight.V2`), `dotnet new xunit` → `tests/DiceFight.V2.Tests`;
   add both to `DiceFight.slnx`. Target the same TFM as v1 (net10.0).
2. Create the records from Appendix B, one file per concept, under
   `src/DiceFight.V2/Model/`. All records must round-trip through
   `System.Text.Json` (write one test proving GameConfig serializes
   and deserializes to an equal value — this keeps the door open for
   JSON card data and a card editor later).
3. Validation helpers: `GameConfig.Validate()` returning a list of
   error strings (die faces reference declared symbols, card die
   definitions have ≥1 face, template parameters reference declared
   keywords/tags, etc.). Test with 3–4 deliberately-broken configs.
   *(Amended per sign-off)*: also warn when a config's energy symbol
   ids collide with its affiliation/keyword strings — symbol ids join
   the tag namespace under adopted Finding 4.

**Acceptance**: solution builds; serialization + validation tests
pass; zero references from v2 projects to `DiceFight.Engine`.

## Phase 2 — Game state, zones, turn machine

**Goal**: a playable-without-abilities skeleton: setup, draw, roll,
purchase, field, pass turn — all constants from `RulesConfig`.

**Tasks**:
1. `GameState`: two players, per-die `DieInstance` (id, owner,
   controller, current zone, current face index, applied modifiers
   list — empty for now), life totals, turn/step tracker, and the
   **card counter store** (per-`(player, cardId, counterName)` int —
   adopted Finding 13; the only card-scoped-not-die-scoped state in
   the model, don't attach it to DieInstance). Zones are
   the same nine as v1 (Bag, PrepArea, ReservePool, FieldZone,
   AttackZone, UsedPile, OutOfPlay, plus the DiceFromBag/DiceFromPrep
   staging zones — port v1's staging-zone rationale comment).
2. Setup from `GameConfig` + two team lists: build each player's bag
   from the config's `BasicDicePool` (this is where "8 identical
   Sidekicks" vs "two 4-die sets" becomes pure data) + purchased-dice
   rules. Starting life, draw count, team caps all from RulesConfig.
3. Turn steps as v1 has them (ClearAndDraw → Roll → FinishRoll →
   Main → Attack sub-steps → CleanUp), each a method on a
   `TurnEngine` equivalent. Port the *structure* from v1's
   `TurnEngine.cs`, not the code — v1's version is 1,769 lines mostly
   because of ability hooks; v2's should be a few hundred here.
4. Main-step actions: Purchase (energy matching against face symbols
   — symbol ids, not an enum; "wild" is a symbol property), Field
   (fielding cost from face data), UseGlobal/UseAction stubs that
   throw NotImplemented.
5. Injectable `IDiceRoller` (port v1's `PlaceholderDiceRoller`
   pattern; deterministic seeded roller for tests).

**Acceptance**: a scripted full turn-cycle test passes (setup → draw →
roll → purchase → field → attack step skipped → cleanup → next turn)
using a minimal test GameConfig defined in test code, NOT the real
Dice Masters config (that's Phase 8 — keeping the test config tiny
proves nothing is hardcoded).

## Phase 3 — Query pipeline

**Goal**: every value card text can modify is read through an
interceptable query. This is the spine that replaces v1's 39 `Grants*`
flags.

**Design** (fixed — do not extend the query list without user
sign-off, mirroring the closed-vocabulary rule):

```
Queries (7):
  GetAttack(die), GetDefense(die)          — base from current face + modifier sum
  GetPurchaseCost(card, buyer)             — base + modifier sum, floor 1 (erratum
                                             2026-08-22: the game's own "to a minimum
                                             of 1" text; was wrongly "floor 0")
  GetFieldingCost(die)                     — same shape, floor 0 (printed-0 faces and
                                             free-fielding are real)
  GetKeywords(die)                         — printed + granted set
  CanBeTargeted(die, byWhom, triggerKind)  — bool, AND of interceptor verdicts
  GetGlobalEnergyCost(card, payer)         — base + modifier sum (covers surcharges)

An 8th query, AbilitiesActive(die), is RESERVED for the Phase 8
ability-blanking design spike — do not implement it early, but do not
take its name either.
```

**Tasks**:
1. `QueryEngine`: each query walks (a) the die/card's own base data,
   (b) per-die applied modifiers (from one-shot effects, with
   duration), (c) registered *continuous modifiers* (from Phase 6 —
   for now, an empty registry with the right interface).
   `IStatModifier { AppliesTo(state, die): bool; Delta: int }` — keep
   interfaces this dumb; no ordering/layers system (Dice Masters
   doesn't need MTG's layer system; document that decision).
2. Applied-modifier storage on DieInstance with `Duration`
   (EndOfTurn | Permanent) and CleanUp expiry (port v1's
   AppliedModifiers-cleared-at-CleanUp fix — it was a real bug once).
3. Route Phase 2's purchase/field code through the queries.

**Acceptance**: tests prove (a) modifier changes an attack value and
expires at cleanup, (b) a purchase-cost modifier changes what Purchase
charges, (c) an empty registry reproduces Phase 2 behavior unchanged.

## Phase 4 — Event bus + triggered abilities

**Goal**: the trigger system, replacing v1's TriggerType enum +
three `*DieMatch` filter records with one event + one filter shape.

**Design** (fixed):

```
Events (10): DieFielded, DieKOd, DieDamaged, DieAttacks, DieBlocks,
             DiceDrawn, PurchaseMade, TurnStepEntered, DieUsed,
             DieFaceChanged   (amended per sign-off — Finding 1)
Each event carries: the acting die id (if any), its controller,
step context, and event-specific values (DieDamaged carries the damage
amount; DieFaceChanged carries {PriorFace, NewFace, Cause: Roll |
Reroll | Spin | Effect} and MUST be emitted from every face-mutation
site — roll, reroll, ability spin, energy-face spin — v1's CheckAwaken
funnel is the precedent; a skipped site is the silently-never-fires
bug class). Awaken is an EventFilter over DieFaceChanged (a
character-level increase, any Cause) - built 2026-09-01 as
`EventFilter.LevelIncreased`.

**Energize is NOT**, despite this plan having said so until 2026-09-01;
see V2_TAIL_POLICY.md's "Energize is a step boundary" note for why a
DieFaceChanged filter cannot express it. Neither is a distinct trigger
kind either way.

A card trigger = (EventKind, EventFilter, Ability).
EventFilter = { Ownership (relative to listener), TagFilter?,
                ExcludeSelf?, MinPurchaseCost? }
  — TagFilter matches the event die's tags (Appendix B: affiliations,
    keywords, names, and "sidekick" are all just tags).
SelfOnly triggers ("when THIS die is fielded") = EventFilter with
  the listener's own die required — same shape, no special case.
```

**Tasks**:
1. Event emission from the Phase 2/3 code paths (fielding emits
   DieFielded, etc.). Keep emission points few and choke-pointed.
2. Port v1's **FIFO ability queue** semantics (`AbilityQueue` — this
   part of v1 is good; port its ordering rules: active player's
   triggers first, then inactive, resolve FIFO).
3. Subscription registry built from cards in play/on teams; a fired
   event enqueues matching abilities; the turn engine drains the
   queue at the same points v1 does.
4. Global abilities: paid activation (energy cost via symbols),
   once-per-turn limiter as a template parameter (port v1's
   GlobalsUsedThisTurn approach).

**Acceptance**: a test card with "when another tagged die is fielded,
[stub effect]" fires through a REAL fielding action (ground rule 6);
ordering test with three simultaneous triggers matches v1's queue
ordering; a self-only trigger doesn't fire for other dice.

## Phase 5 — Effect template interpreter

**Goal**: implement all effect templates from `V2_VOCABULARY.md`
(seeded from Appendix A). Est. the interpreter lands ~300–400 lines.

**Tasks**:
1. `TargetFilter` resolution as a single query function (port v1
   `LegalTargets.Query`'s good ideas; the filter shape is Appendix
   A's 8 fields, closed).
2. Pending-choice flow: port v1's `PendingChoice` pattern (game
   pauses, exposes candidates + count, an answer API resumes). All
   player decisions route through this one mechanism — including
   yes/no decisions (v1's MayPayLife stand-in-token trick is fine;
   keep it and its comment).
3. Implement templates in this order (dependency-ish): LifeChange,
   DealDamage (deltas + adopted `Distribute` flag — resolved as N
   repeated PendingChoice picks, not a new mechanism), KO, MoveDie,
   DrawToZone, Reroll (with its adopted NonCharacterMoveTo/
   DamagePerMoved params), Spin (deltas + adopted `SetLevel` absolute
   mode, mutually exclusive with LevelDelta), SpinToEnergy, ModifyStat
   (deltas + adopted SetAttack/SetDefense modes), GrantTag, FieldDie,
   PurchaseModifier, CombatFlag, Sequence, MayPay, Conditional,
   DrawAndChooseOne, GrantCounter (adopted 17th and 18th templates).
   Amount resolution (Fixed vs PerMatch, incl. the adopted Distinct/
   Unit params) is one shared helper. The interpreter's execution
   context carries the adopted binding table (BindAs/Bound, reserved
   name "event") — build it into TargetFilter resolution from the
   start, and define TargetWasKOd/OnFaceKind evaluation against
   bindings, not ad-hoc shared state. Choice resolution honors
   `AnsweredBy` (TargetFilter and MayPay) by routing the PendingChoice
   to the named player.
4. Per-template tests: at least one happy path + one "no legal
   target → rule 3.1.10 skip" case each. Conditions: one test per
   ConditionKind.

**Acceptance**: every template + condition has passing tests; the
Phase 0 paper expressions for the 10 "common node" cards now run as
real in-code card definitions in tests.

## Phase 6 — Continuous templates

**Goal**: implement the ~6 continuous templates as registered
query-modifiers/interceptors — the replacement for all 39 v1 flags.

**Tasks**: implement per Appendix A as amended: StatAura, CostModifier,
TagAura, CombatRule, DamageModifier (with its adopted `Source:
Ability | Combat | Any` scope), TargetingProtection — all six carrying
the adopted `ActiveWhen: ConditionKind?` gate (evaluated live at query
time; conditions are pure state reads, so this is safe). Each is a
factory that registers against Phase 3 queries / Phase 4 events;
"while active" scoping (die must be in Field/Attack zone) is one
shared predicate, with `ActiveWhen` AND-ed on top when present.

**Acceptance**: tests per template, including: aura appears/disappears
as the source die enters/leaves the field; two auras stack additively;
the Phase 0 paper expressions for the 5 ex-`Grants*` cards run in
tests.

## Phase 7 — Combat

**Goal**: attack/block/damage on the v2 spine.

**Tasks**: port v1 `CombatEngine`'s rules content (declare attackers →
blockers → action/global window → assign damage → KO resolution;
"once blocked, always blocked"; unblocked-damage-to-player) but route
every stat read through queries, every KO/damage through events, and
every restriction through CombatFlags (from effects) + CombatRules
(continuous). Overcrush/Fast-style keyword behavior keys off
`GetKeywords` — keyword behavior is engine code keyed to keyword ids
declared in GameConfig (document: keyword *behaviors* are part of the
engine's closed vocabulary too; a game config can only use declared
keywords).

**Acceptance**: port v1's combat test scenarios (the rulebook worked
examples in v1's test suite are the oracle — find them in
`tests/DiceFight.Engine.Tests` and replicate outcomes), plus
KO-triggers-fire-through-combat and aura-affects-combat-stats tests.

## Phase 8 — Dice Masters as a game definition

**Goal**: the current game, expressed as one `GameConfig` + card data.

**Tasks**:
1. `DiceFightClassicConfig`: 4 energy symbols + wild, the standard
   Sidekick die (its real 6 faces), draw 4, life 20, team caps, the
   keyword list. This file is the proof of direction-C readiness — a
   variant config (draw 6, split Sidekick pools) should be
   constructible in a test with zero engine changes. Write that test.
2. Migrate the two curated v1 teams' ~26 cards first; verify against
   v1's test expectations for those cards.
3. **Design spikes (adopted at sign-off — do these BEFORE the batch
   migration reaches the cards that need them):**
   - *Ability-blanking spike* (needed by D'Ken DPS141, Mister Sinister
     DPS083, Vulcan DPS095, Shriek SMC016, and — added at the Part 11
     freeze — the named-card LOCKOUT family: Blob XFC087, Drax IG107,
     Magneto AOU139's "can't purchase/field that card" texts, since
     both families share the same per-die "choose an opposing card
     when fielded and remember the choice" memory): likely shape is
     the reserved 8th query `AbilitiesActive(die)` consulted by the
     trigger registry and activation paths, with a continuous +
     one-shot template pair on top, plus a lockout continuous template
     over the same chosen-card mechanism; the spike must also decide
     whether a blanked die's own continuous templates switch off
     (v1's answer: yes, via the GetCard choke point — match it). Write
     the design up in `V2_VOCABULARY.md`, get user sign-off, then
     implement.
   - *Live-value Amounts spike* (needed by Archnemesis DPS001, Cosmic
     Cube MSW002, Rogue DPS049, Dark Phoenix DPS107): extend `Amount`
     with binding-referencing sources (`StatOf(binding, Attack|
     Defense)`, `EventValue`), with values captured at bind time so
     Archnemesis's rule-3.1.7 both-before-either simultaneity falls
     out naturally. Same write-up → sign-off → implement flow.
4. Then the DPS catalog, in batches of ~10–15 cards per session,
   using v1 `SampleCards.cs` as the source of truth for stats/text
   and `V2_VOCABULARY.md` expressions where Phase 0 already worked
   them out. Every card that doesn't fit goes to `V2_TAIL_POLICY.md`
   (Appendix C format) — no vocabulary additions (ground rule 2).
5. Port v1's catalog-wide invariant tests (keyword/ability mismatch
   scan, etc.).

**Acceptance**: curated teams fully migrated with passing behavior
tests; DPS migration ≥80% of v1's implemented cards; tail list
complete for the rest; variant-config test passes.

## Phase 9 — API + web integration

**Goal**: v2 playable end-to-end in the existing web client.

**Tasks**:
1. Reuse `DiceFight.Api`'s controller shape: either (a) a parallel
   `/api/v2/games` controller set, or (b) an engine-interface
   abstraction — choose (a), it's dumber and keeps v1 untouched.
   DTOs largely mirror v1's.
2. Web client: a "v2 engine" toggle on game creation (query param or
   UI switch); the board UI already speaks in zones/dice/actions and
   should need modest changes. Budget real time for choice-flow
   differences.
3. Verify in headless Chromium per house rules (see
   dev-environment memory for the Playwright recipe) before calling
   it done. Full turn played on v2 through the UI.
4. Only now does the Dockerfile build include v2. Deploy, verify
   live, then decide with the user when v2 becomes the default.

**Acceptance**: a complete v2 game playable in the deployed web app;
v1 games still work.

---

## Appendix A — The v2 vocabulary (seed for V2_VOCABULARY.md)

Closed sets. Changing ANY of these requires user sign-off.

> **AMENDED 2026-08-22 (user-signed-off; recorded here per ground rule
> 2, full text in `V2_VOCABULARY.md` Part 1, which is authoritative):**
> TargetFilter gains `BindAs`/`Bound` binding fields (reserved binding
> "event" = the triggering event's subject) and `FieldingCost` as a
> 5th Stat kind; the tag set additionally includes each die's printed
> energy symbol id; effect templates number 17 (+`DrawAndChooseOne
> (Count, PlayerTarget, ChosenToZone, RestToZone)`), with `ModifyStat`
> gaining absolute `SetAttack`/`SetDefense` modes and `Reroll` gaining
> `NonCharacterMoveTo`/`DamagePerMoved`; conditions number 7
> (+`OnFaceKind`); all six continuous templates gain `ActiveWhen:
> ConditionKind?` and `DamageModifier` gains `Source: Ability | Combat
> | Any`; trigger events number 10 (+`DieFaceChanged {PriorFace,
> NewFace, Cause: Roll|Reroll|Spin|Effect}`, from which Energize/
> Awaken are expressed as event filters). Deferred by the same
> decision: ability-blanking and live-value Amounts (Phase 8 design
> spikes), `DieTargeted` (deferred, rejection-leaning), and the seven
> tail items listed in `V2_VOCABULARY.md`.
>
> **ADDENDUM, same day (user-signed-off, `V2_VOCABULARY.md` Part 5):**
> `DealDamage` gains `Distribute: bool` (resolves Amount as repeated
> 1-point choices instead of one lump sum — no new choice mechanism,
> reuses the Phase 5 `PendingChoice` pipeline N times); `Spin` gains
> `SetLevel: int?`, mutually exclusive with `LevelDelta` (absolute
> level set, mirroring `ModifyStat`'s `SetAttack`/`SetDefense`, ported
> from v1's `SpinToCharacterLevel`). Both closed cards a human review
> of the vocabulary caught — Cyclops's "divided how you choose" and
> Mutation's level-1 landing — that the card-by-card pass had
> respectively under-argued and over-claimed as solved.
>
> **ADDENDUM 2026-08-22, from the full 145-card DPS audit
> (`V2_VOCABULARY.md` Parts 9-10) — Finding 13, adopted:** a per-
> `(player, cardId, counterName)` count on `GameState` (Loyalty
> Counters belong to a *card*, not a die, unlike everything else in
> the model); an 18th effect template, `GrantCounter(TargetFilter,
> CounterName, Amount)`; `TargetFilter.Stat` gains a `Counter(name)`
> kind alongside the fixed stat kinds, read via the existing
> `CountAtLeast`/target-filtering machinery — no parallel query system
> needed. Real, 6+-card-confirmed gap the earlier sampling rounds
> missed (round 1 marked a Loyalty-using card "fit" without
> questioning the node it used). **Not adopted, user's explicit
> call**: the "payment-source visibility" gap (4 cards — 2 Bishop,
> Forge, Professor X) is deliberately being presented to players as an
> alter-or-skip candidate rather than built, despite looking
> technically buildable (event-payload richness, the same shape as
> `DieDamaged`'s damage amount) — see Part 10's own note on this being
> a product call, not a technical one.
>
> **FINAL ADDENDUM 2026-08-22 — the F14 batch, adopted at the Part 11
> gate review, and the spec FROZEN:** `CombatFlag.Unblockable`;
> `PerMatch` gains `Distinct: bool` and `Unit: Dice | EnergySymbols`;
> `Duration` gains `UntilYourNextTurn`; `CostModifier` gains kind
> `ActionDieUse` and `Currency: Energy | Life`; `AnsweredBy: Own |
> Opposing` on `TargetFilter` and `MayPay`; `EventFilter` gains a
> `Stat` threshold; `DamageModifier` gains `Amplify(n)` / `Double`
> modes with the fixed ordering rule "multipliers before flat
> reductions." Named-card lockout deferred INTO the ability-blanking
> spike (shared chosen-card-memory mechanism); player-damage trigger
> and two event-payload singletons tailed. The vocabulary is frozen
> from this point: further changes route only through the two spikes
> or `V2_TAIL_POLICY.md`, each with user sign-off. `V2_VOCABULARY.md`
> Part 1 is the single authoritative statement of the frozen spec —
> the seed sections below are of historical interest only.

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

Tags unify v1's affiliations, keywords, card names, and
Sidekick-ness: a die's tag set = its card's affiliations + keywords +
its card name + "sidekick" if applicable + granted tags. This one
change replaces ~9 v1 TargetSpec parameters
(RequiredAffiliations, NameContains, SidekicksOnly, ExcludeSidekicks,
RequiredCardId, RequiredKeyword, MatchesOwnTeamAffiliation…). The
deliberate loss: compound queries beyond AnyOf/NoneOf. Accept it.

### Amounts

```
Amount = Fixed(n) | PerMatch(TargetFilter, multiplier)
```

PerMatch counts live matches at resolution (replaces v1's
DealDamagePerActiveAffiliate, DealDamagePerMatchingDie, and the
count-scaled stat grants).

### Effect templates (16)

| Template | Parameters | Replaces (v1) |
|---|---|---|
| DealDamage | Amount, TargetFilter | DealDamage + 3 scaling variants |
| KO | TargetFilter | Ko, Sacrifice (param: `TriggersKOAbilities: bool`) |
| MoveDie | TargetFilter, ToZone | MoveDie, PrepDie, RedrawFromBag(approx) |
| DrawToZone | Count, FromZone(Bag), ToZone | DrawDice, PrepFromBag, Corrupt(approx) |
| FieldDie | TargetFilter, Level | FieldDie, RollAndFieldOrPrep(approx) |
| Reroll | TargetFilter | Reroll, RerollAndMove*(approx via Conditional) |
| Spin | TargetFilter, LevelDelta | Spin |
| SpinToEnergy | TargetFilter | SpinToEnergyFace, SpinToCharacterLevel(inverse, param) |
| ModifyStat | TargetFilter, AtkDelta, DefDelta, Duration | ModifyStat, SetStat(approx), swap/double variants(approx) |
| GrantTag | TargetFilter, tags, Duration | GrantKeyword, GrantAffiliation |
| LifeChange | Amount, Whose | GainLife, LoseLife, MayPayLife's cost half |
| PurchaseModifier | Delta, CardKind?, GoesToZone? | GrantNextPurchaseDiscount, GrantNextPurchaseGoesToBag |
| CombatFlag | TargetFilter, MustBlock\|CantBlock\|MustAttack\|CantAttack\|OnlyBlocker | ForceBlock, CantBlock, ForceAttack, SetCallOutTarget |
| Sequence | Effect[] | Sequence |
| MayPay | Cost(Effect), Then(Effect) | MayPayLife generalized; always PendingChoice |
| Conditional | ConditionKind, params, Then, Else? | Conditional |

### Conditions (6 kinds — replaces v1's 17)

```
CountAtLeast(TargetFilter, n)      // replaces ~9 of v1's counting conditions
TargetWasKOd
OnBurstFace(single|double)
LifeComparison(Own < Opponent)     // extend comparators only w/ sign-off
NoKOsThisTurn(scope: any|own)
TurnFact(PurchasedThisTurn | FieldedNoOtherCharacterThisTurn | PrepAreaEmpty)
```

### Triggers

Event kinds + EventFilter as specified in Phase 4. Trigger-level
extras: `OncePerTurn: bool`, `EnergyCost` (Globals/paid abilities
only, in symbol ids).

### Continuous templates (6)

| Template | Parameters | Replaces (v1 flags, examples) |
|---|---|---|
| StatAura | TargetFilter, AtkDelta, DefDelta (deltas may be PerMatch) | Team bonuses, named-card buffs, opponent debuffs, per-match bonuses |
| CostModifier | Purchase\|Fielding\|GlobalEnergy, TargetFilter(whose), Delta | Fielding discounts, opponent surcharges, conditional purchase discounts |
| TagAura | TargetFilter, tags | Affiliation/keyword grants to Sidekicks etc. |
| CombatRule | TargetFilter, BlocksN\|MinBlockers\|CantSpinUp\|CantFieldMore | Multi-block, minimum-blockers, spin-prevention |
| DamageModifier | TargetFilter, Reduce(n)\|RedirectToSelf\|PreventNonCombat | Damage reduction/redirect/prevention flags |
| TargetingProtection | TargetFilter, from: Global\|Action\|Both | Targeting-immunity flags |

Known non-coverage (goes to tail policy, not templates): whole-side
text blanking, life-total swap, purchase-goes-to-bag beyond the
PurchaseModifier param, draw-and-choose flows, cross-die swaps,
attack-stat mirroring. ~10–15% of DPS by v1's own usage histogram.

## Appendix B — Data model sketch (Phase 1)

```
GameConfig {
  Id, Name
  EnergySymbols: SymbolDef[]        // { Id, IsWild }  — no enum anywhere
  Keywords: KeywordDef[]            // { Id, Timing hints } — engine knows behaviors by Id
  Rules: RulesConfig
  BasicDicePool: (DieDefinition, count)[]   // the Sidekick pool, as data
  BasicActionSlots: int
}
RulesConfig {
  StartingLife, DrawCount, MaxTeamCards, MaxTeamDice,
  BasicActionCount, FieldZoneCap?  // extend only with user sign-off
}
DieDefinition { Id, Faces: Face[] }             // any face count
Face {
  Symbols: (symbolId, count)[]     // energy pips
  Character: { Level, FieldingCost, Attack, Defense }?   // null = energy face
  Burst: 0|1|2
}
CardDef {
  Id, Name, Subtitle, Set, CardType, PurchaseCost, EnergyType(symbolId)?
  Die: DieDefinition
  DieLimit, Affiliations: string[], Keywords: string[]   // → tags
  RawText                          // always kept, for UI + audit
  Abilities: TriggeredAbility[]    // (Trigger, EventFilter?, Effect tree)
  Continuous: ContinuousDef[]      // continuous templates
  IsImplemented                    // same catalog convention as v1
}
```

## Appendix C — Tail policy file format (`V2_TAIL_POLICY.md`)

One table: `| CardId | Name | What it needs | Policy |` where Policy ∈
**Approximate** (expressed in templates with a stated difference —
also note the difference in the card's own definition comment),
**Vanilla** (no ability; RawText still shown in UI), or **Ask**
(flag for the user — candidate for redesign under direction C).
Default while migrating: Vanilla + one-line entry. Never guess a
wrong approximation silently (house rule from v1).

**Phase 1 note (2026-08-22)**: the full effect vocabulary (TargetFilter,
Amount, Condition, the 18 EffectNode templates, the 6 ContinuousDef
templates, Events/TriggeredAbility) was built as pure data records in
this phase too, not deferred to Phase 5 - Appendix B's CardDef needed
real types for its Abilities/Continuous fields to compile and round-trip
meaningfully, and none of it has behavior yet (matches "compilable data
model, no behavior"). Phase 5 adds the interpreter that walks these
trees; the shapes themselves are locked in now. One deliberate scope
line: CardDef's own JSON round-trip (its Abilities/Continuous fields are
polymorphic - EffectNode/ContinuousDef subtypes - and need
`JsonDerivedType` configuration System.Text.Json doesn't provide for
free) is deferred to whenever CardDef JSON loading is actually needed
(Phase 8's card catalog), not built speculatively now. The task's own
round-trip test is scoped to `GameConfig` only, which doesn't touch
polymorphic types, so this doesn't block Phase 1's acceptance bar.

**Phase 2 note (2026-08-22)**: found and corrected a real plan erratum
while implementing - "the same nine [zones] as v1" undercounted v1's real
Zone enum by one. v1 also has `Unpurchased`, where EVERY card's dice
(Character and Basic Action alike) sit until bought - not keyword-gated
the way v1's own `Intimidated` zone is, so it's load-bearing for Purchase
itself, unlike Intimidated (correctly left out, deferred to Phase 7).
Corrected in place (`Zone` is now 10 values) rather than treated as a
sign-off question - same category as the purchase-cost-floor erratum:
the plan's own stated intent was "same as v1," so this is a faithful-port
fix, not a new design decision. Documented in `Zone.cs`'s own comment.

DieInstance ended up slimmer than v1's (no Status/Level/EnergyKind/
EnergyAmount/BurstStars) because v2 dice carry real per-die face data
from Phase 1 - CurrentFaceIndex plus a DieDefinition lookup replaces all
of those as derived facts, which v1 couldn't do (no real face data
existed - see PlaceholderDiceRoller's own remarks). Also added a
DieInstance.PoolDieId (alongside CardId) rather than reusing v1's
"CardId null = Sidekick" shape outright, since Direction C wants more
than one interchangeable pool-die type expressible, not just one
implicit "Sidekick."

**Phase 3 note (2026-08-22)**: task 2's literal text says "Duration
(EndOfTurn | Permanent)" - written before Finding 14 added
`UntilYourNextTurn` to the frozen `Duration` enum (Phase 1 already built
the correct 3-value type, per Part 1). Implemented CleanUp expiry for all
three: EndOfTurn always clears; Permanent never clears on its own;
UntilYourNextTurn needed one new small field (`AppliedModifier.
GrantedDuringPlayerId`) and a derived rule - it survives the Clean Up
ending the granter's OWN turn (needs to last through the opponent's
whole turn), and expires at the Clean Up that hands control back to the
granter (exactly "gone by the start of your next turn"). Not a new
erratum - just using the already-current frozen type instead of the
stale echo in this task's own older wording.

Query design notes: `IStatModifier`'s single shape from the plan's own
text didn't quite fit both die-scoped queries (Attack/Defense/
FieldingCost) and card+payer-scoped ones (PurchaseCost/GlobalEnergyCost)
at once, so it split into two interfaces (`IDieStatModifier`,
`ICardCostModifier`) - same "dumb, flat delta, no layers" spirit, just
honest about the two different things being checked. `GetKeywords` is
scoped to printed keywords only for now (no per-die "granted tags"
storage exists yet - that's Phase 5's `GrantTag` interpreter to add,
same as-needed pattern every other phase here has followed).

**Phase 4 note (2026-08-22)**: a real design gap found via the DieFaceChanged
test, worth flagging clearly since it's more than a test-fixture fix.
`EventBus.Fire`'s first draft only scanned Field/Attack Zone dice as
listener candidates ("active dice," matching every OTHER-die reactive
trigger in the codebase) - but self-only abilities (null `Filter`, the
common "when [this card] does X" pattern) need to fire from the exact die
the event is about regardless of that die's zone AT THE MOMENT the event
fires: Energize/Awaken react to a die still in the Prep Area mid-roll
(v1's CheckEnergize/CheckAwaken have no zone gate at all, for exactly
this reason), and a future "when I am KO'd" ability's own die will
already have left the Field/Attack Zone by the time DieKOd fires. Fixed
by always adding the event's own `SubjectDie` as an extra listener
candidate alongside the normal active-dice scan, deduplicated. General,
not specific to DieFaceChanged - would have silently broken every
self-only reaction to an event whose subject die isn't currently active,
which is a lot of them (KO'd/damaged reactions especially).

`UseGlobal` (task 4) is fully implemented, not stubbed: validates the
source die is active and owned, looks up the ability by index (a card
could in principle print more than one Global), enforces `OncePerTurn`
via the new `GameState.GlobalsUsedThisTurn` set (reset in CleanUp,
same turn-scoped lifetime as v1's `GlobalsUsedThisTurn`), spends energy
through the same `SpendEnergy` helper Purchase/Field use, and enqueues
the ability - it just can't be DRAINED yet (Phase 5). `UseAction` stays
stubbed per Phase 2's own note (needs the interpreter too, and Action-die
mechanics haven't been touched at all yet).

Event emission was wired at every real action that currently exists:
Field (DieFielded), Purchase (PurchaseMade), ClearAndDraw (DiceDrawn),
Roll (DieFaceChanged, per Part 1's "every face-mutation site" mandate -
the only such site that exists so far), EnterAttackStep/SkipAttackStep/
CleanUp (TurnStepEntered). DieKOd/DieDamaged/DieAttacks/DieBlocks/DieUsed
have no emission site yet because no KO/damage/combat/Action-die
mechanic exists anywhere in the codebase to emit them from - wiring them
now would mean firing an event for something that doesn't actually
happen, which is worse than leaving them unwired with a clear comment
saying where they'll go (Phase 5 KO effects, Phase 7 combat).

**Phase 5 note (2026-08-23)**: all 18 effect templates and all 7
conditions are implemented and tested (70/70 v2 tests passing, up from
23). Written continuation-passing style (`Action onComplete` threaded
through every private Execute* helper) rather than a flat switch, so
that Sequence/Conditional/MayPay/DrawAndChooseOne/Distribute all share
ONE pause mechanism (`PendingChoice`, ported from v1) instead of each
needing bespoke pause/resume bookkeeping - a real design commitment
beyond v1's own targeting seam (`EffectContext.ResolveTargets`, a
caller-supplied function that never actually paused for a real player
decision). See `EffectInterpreter.cs`'s own class remarks for the full
reasoning and the one documented simplification taken against the
Phase 5 budget: TargetFilters resolve LIVE at the point their own node
executes rather than being pre-resolved-and-cached against a single
pre-execution snapshot the way v1's rule-3.2.5 handling does (the
"Casket of Ancient Winters" case) - no currently-authored card needs
that precision; flagged as a revisit-if-Phase-8-needs-it gap, not a
silent one.

Real gaps found and resolved while building, each documented at its
own site rather than here:
- `TargetKind.CharacterDie` reads the die's CURRENT face (matches
  in-play/combat semantics), so it can never match a dormant die
  sitting in the Used Pile/Bag - confirmed this is the vocabulary's own
  intent (Part 2's Rally example explicitly uses `Kind: AnyDie` for
  reaching into dormant zones), not a bug; documented at
  `TargetResolver` and exercised by the MoveDie/FieldDie tests.
- `Ko`'s destination is always the Prep Area, unrolled (rule 1.5.3.2) -
  confirmed by grepping v1's own `ForceKO`, and is also the signal
  `TargetWasKOd` reads (Zone == PrepArea && no current face). v1's
  separate Sacrifice-shape OutOfPlay/UsedPile nuance (Appendix 1) is
  NOT preserved - Ko's own data shape has no destination-zone param to
  carry it, a deliberate simplification, not an oversight.
- A dormant die entering the Field/Attack Zone (MoveDie/FieldDie/
  DrawAndChooseOne) needs SOME current face; defaults to the die's own
  first character face, with `Spin(SetLevel:n)` as the documented
  follow-up for a specific level (Finding 12's Mutation writeup already
  established this exact pattern - MoveDie doesn't need its own
  level-set param).
- Added three small turn-scoped trackers to `GameState`
  (PurchasedThisTurn/FieldedCharacterThisTurn/CharacterDiceKOdThisTurn)
  for `TurnFact`/`NoKOsThisTurn` - `FieldedCharacterThisTurn` is a
  known simplification (doesn't exclude the ability's own just-fielded
  die from its own "no OTHER character" check), acceptable since
  Phase 5's own acceptance bar only asks for one test per Condition
  KIND, not per enum value; revisit when a real migrated card needs
  the precise reading.
- `PurchaseModifier` needed real TurnEngine.Purchase plumbing (a
  one-shot, per-controller `PendingPurchaseModifier` list, consumed by
  the next matching purchase or discarded at CleanUp) since it grants a
  FUTURE action a discount/zone override rather than mutating anything
  in the moment - not a continuous registry (Phase 6's concern), a
  one-shot queue of exactly one thing to consume.
- `AbilityQueue`/`EventBus` gained the "event" binding plumbing Finding
  9's reactive-trigger design always implied but Phase 4 hadn't
  actually carried yet: `QueuedAbility.EventSubjectDieId`, populated by
  `EventBus.Fire` from the firing `GameEvent.SubjectDie`, seeded into
  `EffectContext.Bindings["event"]` before an ability's own tree runs -
  without it, `TargetWasKOd`/`Bound:"event"`-style reactive effects
  would have nothing to actually reference.

Verified: `dotnet build DiceFight.slnx` clean (0 warnings/errors, all
5 projects); v2 tests 70/70 passing (47 new); v1's full suite re-run
untouched, still 547/547. Phase 5 checkbox ticked; plan status header
updated - Phase 6 (continuous templates) is next.

**Phase 6 note (2026-08-23)**: all 6 continuous templates implemented
as `ContinuousRegistry`, compiled once per game (`GameSetup.NewGame`)
from every `CardDef.Continuous` entry into the Phase 3/5 registries.
Each modifier object re-scans its OWN card's currently-active
(Field/Attack Zone) dice, across both players independently, every
time it's queried - "aura appears/disappears as the source die enters/
leaves the field" and "two auras (including two copies of the same
card) stack additively" both fall out of that live re-scan for free,
no Field/CleanUp hook needed to add or remove anything. Every
template resolves its own `Target`/`Whose` filter relative to EACH
qualifying active source die's OWN controller, mirroring how a
triggered ability only listens while its own source die is active
(the Part 2 Magneto precedent).

`IDieStatModifier`/`ICardCostModifier`'s `Delta` became a state-aware
`GetDelta(state, ...)` method (was a plain property) - a continuous
`StatAura`'s `AtkDelta`/`DefDelta` can be `PerMatch` (a live count),
which a parameterless property can't compute. `AmountResolver` was
extracted from `EffectInterpreter`'s private Fixed/PerMatch logic so
both it and `ContinuousRegistry` share one implementation.

**A real StackOverflow was found and fixed while writing this phase's
own tests**, not anticipated in the design: a `TagAura`/`StatAura`/
`CostModifier` whose own `Target`/`Whose` filter checks a Tag or Stat
that its OWN registry contributes to (e.g. Darkseid's Target filters
on the "sidekick" tag, and `QueryEngine.GetTags` now folds in ALL
registered `TagAuras` to answer that) recurses into evaluating itself
to answer "am I even active." Fixed generally, not just for the one
case that crashed: added `GetBaseTags`/`GetBaseAttack`/`GetBaseDefense`/
`GetBaseFieldingCost`/`GetBasePurchaseCost`/`GetBaseStatValue` to
`QueryEngine` (printed + one-shot data only, no continuous fold-in),
and threaded an `includeContinuous` flag through `TargetResolver.Query`,
`ConditionEvaluator.Evaluate`, and `AmountResolver.Resolve` so
`ContinuousRegistry`'s own eligibility checks (Target/Whose/ActiveWhen)
always resolve against Base state - every other caller (ordinary
ability targeting) is unaffected and still sees the fully continuous-
inclusive values. This is the load-bearing reason a continuous
template's own eligibility can never depend on another continuous
grant (including itself); documented at `QueryEngine.GetBaseTags`'s own
remarks as the canonical explanation.

`DamageModifier` got a real consumer immediately (unlike CombatRule/
ActionDieUse's CostModifier, which still have none - Combat/Action-die
mechanics are unbuilt): `EffectInterpreter.ApplyDamage` now walks
`GameState.DamageInterceptors` - `PreventNonCombat` blocks the instance
outright, multipliers (`Amplify`/`Double`) apply before flat `Reduce`
(the fixed ordering rule, Part 1/11), and `RedirectToSelf` changes who
actually takes the (already-modified) hit and who `DieDamaged`/KO fire
against. Only `DamageSource.Ability` is reachable before Phase 7 builds
a Combat damage source.

`CostModifier`'s single `Whose: TargetFilter` field resolves to
different id spaces depending on `Kind`: Purchase/GlobalEnergy expect
`Whose` to name a PLAYER (`Kind:Player`, checked against the payer id -
Jean Grey "Xavier's Dream"), Fielding/ActionDieUse expect it to name a
DIE (`Kind:CharacterDie`, checked against the die id - Deadpool
"Collect THIS!") - both real Part 2 paper examples, both now passing as
actual tests.

Tests (`ContinuousRegistryTests.cs`) cover all 6 templates, the
appear/disappear and additive-stacking acceptance criteria, the
multiplier-before-reduction damage ordering, and all 5 of Part 2
Bucket C's ex-`Grants*` paper examples (Captain Marvel, Darkseid,
Deadpool, Jean Grey, Moira's continuous half) running as real card
definitions.

Verified: `dotnet build DiceFight.slnx` clean (0 warnings/errors, all
5 projects); v2 tests 83/83 passing (13 new); v1's full suite re-run
untouched, still 547/547. Phase 6 checkbox ticked; plan status header
updated - Phase 7 (combat) is next.

**Phase 7 note (2026-08-23)**: core combat loop implemented
(`src/DiceFight.V2/CombatEngine.cs` + `CombatAssignment.cs` +
`Model/AttackSubStep.cs`) - declare attackers -> declare blockers ->
action/global window -> assign damage -> KO resolution, "once blocked
always blocked," unblocked-damage-to-player, and both named keyword
behaviors (Overcrush, Fast) keyed off `QueryEngine.GetKeywords`. Every
stat read goes through QueryEngine (continuous auras affect combat
automatically - a test proves it), every KO/damage goes through
EffectInterpreter's own choke points, and every restriction goes
through CombatFlags (MustAttack/CantAttack/MustBlock/CantBlock/
Unblockable/OnlyBlocker, Phase 5) + CombatRules (BlocksN/MinBlockers,
Phase 6) - both of which get their first real consumer here, closing
the "no consumer yet" note left on them.

Deliberately NOT ported from v1's CombatEngine: Range, Infiltrate, Tag
Out, Energy Drain, Deadly, Call Out, Obscure, Regenerate, Retaliation,
and every card-specific Grants* combat hook (Blob's Sidekick-return,
Deathbird's damage-on-high-defense-KO, Lilandra's reroll-to-Prep-Area,
etc.) - none of those are CombatFlag/CombatRule-shaped in the closed
vocabulary, and the plan's own task list never named them. They go to
`V2_TAIL_POLICY.md` if/when Phase 8's card migration needs them.

**A real sequencing bug was found and fixed while porting the
rulebook's own Fast worked example**, not anticipated in the design:
`EffectInterpreter.ApplyDamage` (Phase 5) resolves KO immediately after
marking damage, which is correct for ability damage (rule 3.2.2 -
resolves one instance at a time) but wrong for combat, where rule
2.7.6.1 requires damage to be simultaneous within a wave - an
immediate-KO call would let one side's lethal hit stop the other side's
own damage from ever landing in the same wave (both non-Fast dice
should die TOGETHER; a Fast attacker's damage should still land on a
Fast blocker before either side's KO is decided). Fixed by splitting
`ApplyDamage` into `MarkDamage` (interception + `DieDamaged`, no KO) and
`TryResolveKO` (the threshold check + `KoDie`), both now public;
`ApplyDamage` chains them for ability callers, `CombatEngine` calls
`MarkDamage` for every hit in a wave first and only then runs
`TryResolveKO` over everyone still in the Attack Zone, exactly
mirroring v1's own two-pass `DieStats.ApplyDamage`/`TryResolveKO` split
(this project had already ported that shape once before and should
have recognized it sooner).

Also fixed while wiring this: `TurnEngine.CleanUp` never reset
`Damage` on Field Zone survivors (rule 2.8.1 - damage clears at Clean
Up for characters that weren't KO'd) or on dice swept from Reserve
Pool/Out of Play to the Used Pile (leaving active play, same rule
3.4.5.4 reasoning `EffectInterpreter.MoveToZone` already uses) - both
gaps were latent since Phase 2/4 (Damage didn't exist until Phase 5),
now closed.

Deliberate deviation from v1: `AssignCombatDamage` does NOT advance
`CurrentStep` to CleanUp itself (v1 does) - the caller calls
`TurnEngine.CleanUp` explicitly afterward, same as the skip-combat
path already does, keeping `CleanUp`'s own `RequireStep(Attack)`
contract identical regardless of whether combat happened.

Tests (`CombatEngineTests.cs`) port v1's own acceptance scenarios
(unblocked attacker, blocked survivor, incomplete-split rejection,
"once blocked always blocked" wasting damage without Overcrush,
Overcrush's three shapes, and all four of the rulebook's own Fast
worked-example variants verbatim), plus a KO-fires-DieKOd-through-the-
real-event-bus test, a StatAura-affects-combat-attack test, and
CombatRule/CombatFlag enforcement tests.

Verified: `dotnet build DiceFight.slnx` clean (0 warnings/errors, all
5 projects); v2 tests 102/102 passing (19 new); v1's full suite re-run
untouched, still 547/547. Phase 7 checkbox ticked; plan status header
updated - Phase 8 (Dice Masters as a game definition / card migration)
is next.

**Phase 8 progress note (2026-08-23, tasks 1-2)**: `DiceFightClassicConfig`
(`src/DiceFight.V2/Data/DiceFightClassicConfig.cs`) built from v1's own
real constants (Fist/Bolt/Mask/Shield/Wild, the Sidekick die's real 6
faces per DESIGN_LOG's "corrected Sidekick die faces" entry, draw 4,
life 20, the 8+2 team shape) plus the Direction-C variant-config test
the task asks for.

Migrated the two curated v1 teams (20 cards: `src/DiceFight.V2/Data/CardCatalog.cs`,
ported from `SampleCards.cs`'s `TeamA/TeamBCharacterIds`/`BasicActionIds`)
verbatim on name/subtitle/text/stats/keywords, including v1's OWN
placeholder stats where v1 itself never sourced real ones - this task
doesn't upgrade v1's data quality, only ports it. Real per-die face
LAYOUT isn't in v1's data model at all (v1 synthesizes faces at roll
time; v2 needs them stored) - documented, single convention adopted:
one energy face (1 pip, printed symbol) + one character face per v1
level, flagged once in `CardCatalog.cs` rather than re-flagged per card.

8/20 fit the frozen vocabulary cleanly and are fully implemented+tested
(Apocalypse, HarleyQuinn, CaptainMarvel, Dazzler, ShockingGrasp,
FranklinsGalactus, GodEmperorDoom, Groot); 12/20 are tailed to
`V2_TAIL_POLICY.md` (all Ask). This is a lower fit rate than the DPS
set's own ~82% for a known, non-alarming reason: the curated rosters
were deliberately built to showcase one live example each of Call Out/
Infiltrate/Tag Out/Range/Intimidate - and Phase 7 deliberately didn't
port any of those five keywords, so every showcase card for them was
always going to tail.

`TurnEngine.UseAction` (a stub since Phase 2) had to be implemented for
real this task - the first Basic Action cards migrated (Shocking Grasp)
needed an actual way to fire `TriggerKind.DieUsed` at all. Minimal,
faithful port of rule 2.6.4.1's default (Out of Play after use); Epic/
Continuous Basic Action mechanics (rule 1.2.3/2.6.4.2) are NOT modeled -
`CardType` has no Epic/Continuous distinction yet, not exercised by
anything that actually needs it (Cosmic Cube, the one curated Epic
card, is already tailed for its own `SwapLife` gap).

**A real EffectInterpreter gap, already documented as a Phase 5
simplification, was confirmed by an actual migrated card** - not a new
finding, but its first real consequence: Casket of Ancient Winters'
own effect tree resolves each `TargetFilter` LIVE, so its Ko clause's
own KO'd dice (landing in the Prep Area, rule 1.5.3.2) dilute the
later Prep-Area-targeting `MoveDie` clause's candidate pool from 3 to
6, raising an unintended `PendingChoice` instead of auto-resolving.
Left vanilla (tailed) rather than silently producing the wrong
behavior; the real fix (pre-execution-snapshot resolution, rule 3.2.5)
is unchanged in scope from Phase 5's own note.

Verified: `dotnet build DiceFight.slnx` clean (0 warnings/errors, all
5 projects); v2 tests 115/115 passing (13 new: 4 config, 9 catalog);
v1's full suite re-run untouched, still 547/547. Tasks 1-2 checked off
above; task 3 (design spikes) needs the user's sign-off before any
implementation - see `V2_TAIL_POLICY.md`'s own entries for the
concrete gaps a spike (or a small additional sign-off ask) could close.

**Rule 3.2.5 note (2026-08-24, user-signed-off)**: the Phase 5
resolve-live simplification is replaced by a PER-ABILITY snapshot -
each queued ability's TargetFilter candidate pools resolve against a
zone/face snapshot taken when ITS OWN resolution begins, and the
snapshot dissolves when that ability finishes (later queue entries see
live state - which is also what a blanked card's already-queued
trigger needs once the ability-blanking spike lands: it still fires,
against live state where its text is blank). Conditions and PerMatch
amounts deliberately stay live. Casket of Ancient Winters is
un-tailed and fully implemented; see DESIGN_LOG's same-day entry for
the implementation shape and the real-firing-path bug the new test
caught (ResolveQueued bypassing the snapshot-capturing entry point).

**Phase 8 progress note (2026-09-01, task 4 batch 4 - the Energize
unlock)**: this session opened by correcting a live misconception - the
prior session's "doesn't fire when rolled, but at a stage boundary" note
in `V2_TAIL_POLICY.md` was directionally right but risked reading as
"unrelated to the roll," which the Comprehensive Rules text ("only check
at the end of the [Roll and Reroll] Step") contradicts. Confirmed
against the rulebook directly, then signed off with the user before
touching code (ground rule 2): one vocabulary addition,
`StatKind.SymbolCount` (`V2_VOCABULARY_HISTORY.md` Part 29 has the full
account).

Three engine fixes came out of building it, none requiring sign-off
(mechanism, not vocabulary): `TurnEngine.FinishRoll` never fired
`TurnStepEntered(Main)` at all (now does, and takes an `AbilityQueue`
parameter it previously didn't need); `EventBus.Fire`'s candidate scan
couldn't see a Reserve Pool die for that one step (now can, scoped
narrowly to `TurnStepEntered(Main)` so other steps' "while active"
semantics are untouched); and `TargetResolver.Query`'s `Self`/`Bound`
branches ignored a filter's own Tags/Affiliations/Stat fields
unconditionally, which made the signed-off Energize condition
(`CountAtLeast(Self, Stat: SymbolCount>=2)`) always true regardless of
face - caught by the new plumbing tests, not by inspection. Fixed
narrowly (Kind/Zones/Ownership still skip; Tags/Affiliations/Stat now
apply) - `V2_TAIL_POLICY.md`'s own "Investigated: why Bound cannot
compose" note from 2026-08-24 had flagged this exact split as the
correct one and left it undone pending a cleaner alternative; today's
fix is that composition, verified against every existing `Self`/`Bound`
call site first.

All 15 DPS cards printing Energize are migrated (13 full, 2 partial -
Iceman "Mr Ice Guy"'s live-doubling gap and Professor X "Dreamer"'s
payment-source clause, both new one-line tail entries, not new spikes).
Verified: `dotnet build DiceFight.slnx` clean; v2 tests 233/233 (19 new:
5 generic Energize plumbing in `EnergizeTests.cs`, 14 per-card in
`DpsCardsTests.cs`); v1's full suite re-run untouched, 580/580. Status
header above updated; batches 1-4 total 54/145 DPS cards.

**Phase 8 progress note (2026-09-01, task 4 batch 5)**: also same-day,
after a user-requested one-off (Domino "Not Really A Party Girl", XFO010
- `BonusCards.cs`, new file, deliberately kept out of the DPS count since
she was never in v1's curated set). 12 more DPS cards: 4 full
(`MutantResearchProgram`, `CorsairRecruitingACrew`, `EmmaFrostManipulative`,
`BeastFirstClass`), 4 partial, 4 tailed outright (`V2_TAIL_POLICY.md` has
the per-card table).

Two real findings, both documented at their own site rather than here:
`DrawToZone`'s destination zone decides "rolled" vs. not (Part 1's own
wording) - a card literally printing "draw AND ROLL" needs `Zone.
ReservePool`, not the `Zone.PrepArea` this session initially reached for
by pattern-matching the more common "Prep a die from your bag" phrasing;
caught before commit by checking Groot's (MSW031) own "roll 2 dice from
your bag" precedent, not by a failing test. And a real, batch-spanning
vocabulary gap: `GetAffiliations` has no grant fold-in at all (unlike
`GetTags`), so nothing can express "gains [Affiliation]" any more since
the affiliation-first-class split - found independently on two different
cards (Radicalization, Emma Frost "Influential"), which is what makes it
a real gap rather than a one-off. Not fixed here (needs sign-off); tailed
and flagged clearly so a third card doesn't get silently mis-approximated.

One card-selection bug worth a general note: a DealDamage test built
against a target whose Defense exactly equalled the damage dealt (3 into
3D) KO'd the target instead of just marking damage - `TryResolveKO`
treats damage >= defense as lethal, and a KO'd die's own `Damage` resets
to 0 on leaving the field (rule 3.4.5.4's zone-transition reset), which
made the test's own damage assertion read as "0, unapplied" instead of
"3, then KO'd." Not an engine bug - a reminder for future test-writing:
pick damage-assertion targets with headroom above the damage dealt,
unless the KO is the point.

Verified: `dotnet build DiceFight.slnx` clean; v2 tests 243/243 (10 new:
2 Domino, 8 batch 5); v1's full suite re-run untouched, 580/580. Status
header above updated; batches 1-5 total 66/145 DPS cards (67 incl.
Domino, tracked separately).
