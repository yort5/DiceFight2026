# DiceFight v2 Core — Implementation Plan

**Status: Phase 0 complete, pending a user decision before Phase 1
begins in earnest.** Update the checkboxes in the Phase Overview as
phases complete, and add a one-line note after any phase where reality
diverged from this plan.

**Phase 0 outcome (2026-08-22)**: validated against 20 cards, then
expanded to 60 at the user's request. 28/60 (47%) fit the vocabulary
as originally specified; an architect review (`V2_VOCABULARY.md` Part
4) amended the findings after verifying them against v1 code, and the
user **signed off on the full amended set the same day**: Findings
1-8 as amended (notably `DieFaceChanged` instead of a roll-only
event, and `Reroll` fold-in params), target bindings (9),
DamageModifier source scope (10), and the purchase-cost-floor
erratum. Projected fit ~45/60 (75%). `V2_VOCABULARY.md` Part 1 is now
the adopted spec; this plan's Appendix A and phase descriptions carry
matching amendment notes. Two structural gaps (ability-blanking,
live-value Amounts) are deferred as named Phase 8 design spikes, each
hit by 3-4 independent cards. **Phases 1-7 are unblocked.**

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

## Phase overview

| # | Phase | Deliverable | Status |
|---|---|---|---|
| 0 | Vocabulary validation on paper | `V2_VOCABULARY.md` + 20 cards re-expressed | [x] |
| 1 | Project scaffolding + data model | `DiceFight.V2` + `DiceFight.V2.Tests` projects; GameConfig/DieDef/CardDef records | [ ] |
| 2 | Game state, zones, turn machine | Config-driven state + turn steps, no abilities | [ ] |
| 3 | Query pipeline | Stat/cost/legality queries with modifier interception | [ ] |
| 4 | Event bus + triggered abilities | Events, subscriptions, FIFO ability queue | [ ] |
| 5 | Effect template interpreter | All Appendix A effect templates working | [ ] |
| 6 | Continuous templates | All Appendix A continuous templates working | [ ] |
| 7 | Combat | Attack/block/damage using queries + events | [ ] |
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
   list — empty for now), life totals, turn/step tracker. Zones are
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
bug class). Energize/Awaken are EventFilters over DieFaceChanged
(double-energy NewFace during Roll & Reroll; character-level increase
with any Cause, respectively), not distinct trigger kinds.

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
   DealDamage, KO, MoveDie, DrawToZone, Reroll (with its adopted
   NonCharacterMoveTo/DamagePerMoved params), Spin, SpinToEnergy,
   ModifyStat (deltas + adopted SetAttack/SetDefense modes), GrantTag,
   FieldDie, PurchaseModifier, CombatFlag, Sequence, MayPay,
   Conditional, DrawAndChooseOne (adopted 17th template). Amount
   resolution (Fixed vs PerMatch) is one shared helper. The
   interpreter's execution context carries the adopted binding table
   (BindAs/Bound, reserved name "event") — build it into TargetFilter
   resolution from the start, and define TargetWasKOd/OnFaceKind
   evaluation against bindings, not ad-hoc shared state.
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
     DPS083, Vulcan DPS095): likely shape is the reserved 8th query
     `AbilitiesActive(die)` consulted by the trigger registry and
     activation paths, with a continuous + one-shot template pair on
     top; the spike must also decide whether a blanked die's own
     continuous templates switch off (v1's answer: yes, via the
     GetCard choke point — match it). Write the design up in
     `V2_VOCABULARY.md`, get user sign-off, then implement.
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
