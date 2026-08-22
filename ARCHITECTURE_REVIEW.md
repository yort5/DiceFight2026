# Architecture Review — After the DPS Pass (2026-08-22)

A step-back review requested after completing the Dark Phoenix Saga
card-by-card pass. The prompt: the original design assumed most card
abilities would be expressible as reusable data over a small primitive
vocabulary, but in practice most cards seemed to require custom code.
Three questions, answered in order:

1. Keeping the game as-is, is there a better architecture for the rules
   engine, given what a full set taught us?
2. If we allow *simplifying* ability complexity into a templatable rules
   stack (this is a digital game — it doesn't have to be an exact copy),
   how much smaller and cleaner does the engine get, and does it stay
   extensible?
3. If we go further and treat this as a **new iteration of the game**
   (same bones — e.g. draw 6 dice instead of 4, split the 8 identical
   Sidekick dice into two 4-die sets with different face mixes, reskin
   icons/terminology), what does the architecture need to look like?

This is an analysis document — no code changes accompany it. Scope for
question 3 is architecture impact only, not a new-game rules design.

---

## Part 0 — What actually happened: the reuse audit

Numbers gathered from the current codebase (`main` @ `3e870dc`).

**Catalog**: 203 `CardDef`s in `SampleCards.cs` (4,364 lines), 145
distinct DPS card ids, 166 authored `AbilityDef`s, 25 keywords.

**The effect DSL** (`EffectNode.cs`, 804 lines): **49 node types**.
Usage across the whole catalog:

| Usage count | Node types | Share of vocabulary |
|---|---|---|
| Used ≥ 5 times | 16 (DealDamage 28, Conditional 28, Sequence 27, Ko 17, MoveDie 15, ModifyStat 14, PrepFromBag 11, Spin 10, PrepDie 9, DrawDice 9, …) | 33% of types, **~85% of all ability-instance usages** |
| Used 2–4 times | 10 | 20% |
| **Used exactly once** | **23** | **47% of types, ~8% of usages** |

**TargetSpec**: grown to **25 parameters**, of which ~19 are optional
filters (`MaxAttack`, `RequiredLevel`, `MinLevel`, `MaxDefense`,
`RequiresLoyaltyCounter`, `RequiredCardId`, `NameContains`,
`RequiresUnblockedAttacker`, `MatchesOwnTeamAffiliation`, …). The
source comments are honest about provenance: nearly every filter names
the single card that needed it.

**EffectCondition**: **17 members**, ~13 of them single-card
(`OwnOtherAttackingAffiliateCountAtLeast`,
`OwnTeamWideLoyaltyCounterCountAtLeast`, …).

**The continuous-effect side** (`CardDef`): **39 bespoke `Grants*`
flags** (`GrantsPrepsTwoOwnDiceWhenOpponentDrawsExtraDuringClearAndDraw`,
`GrantsDamageWhenOpposingHighDefenseDieIsKOdInCombat`, …), essentially
**one flag per card** — zero reuse. Each is consumed by hardcoded
checks scattered across the engine: 26 references in `DieStats.cs`, 22
in `TurnEngine.cs`, 8 in `CombatEngine.cs`.

**The interpreter** (`EffectInterpreter.cs`): 1,174 lines, 77 switch
arms.

**Trigger plumbing**: 13 `TriggerType`s plus three near-identical
per-trigger filter records (`KOdDieMatch`, `FieldedDieMatch`,
`AttackedDieMatch`) that exist because reactive triggers couldn't reuse
`TargetSpec`.

**Even "faithful" is already approximate.** The DSL comments record
deliberate simplifications made along the way: Cyclops DPS140's damage
split is auto-divided rather than player-assigned; Rogue DPS049's "you
may" collapses to "always"; Alfred Pennyworth, Robin's Energize, and
The Rock's Global are left unscripted because their mechanisms
(purchase-cost modifiers, N-way target-spec unions) were judged not
worth building. The pure-fidelity target was already being traded off
card by card — just implicitly.

### Diagnosis

The original bet was: *"a small, closed set of primitives that authored
card abilities are composed from… New card text is expressed by
combining these, not by adding new C# code paths"* (EffectNode.cs's own
header). The DPS pass falsified the "closed" half. Dice Masters card
text is effectively open-vocabulary natural language; a full set forced
the "closed" DSL to grow a new node, filter parameter, condition, or
CardDef flag for roughly **one card in three**. Adding a record type +
interpreter case + (often) a GameState field + a scattered enforcement
check *is* adding a C# code path — the data-driven goal was met in
letter, not in spirit. Each new set would repeat this at a similar
rate: the high-frequency vocabulary is saturated (the next set's
"DealDamage" cards are free), but the long tail is unbounded.

The second, independent problem: there are **two parallel ability
systems**. Triggered abilities go through the effect DSL; continuous/
static abilities ("while active, X") never got a representation at all
— each became a named boolean on CardDef plus if-statements at whatever
engine points it touches. This is the worst-scaling surface in the
codebase: per-card logic compiled into the engine core, with
enforcement smeared across three files.

What **worked** and should survive any redesign:

- The FIFO **ability queue** with interrupt semantics (maps 1:1 to rule 3.2).
- The **zone/turn-step state machine**, including the DiceFromBag/DiceFromPrep staging-zone split.
- **LegalTargets** as a single query layer — the *concept* of a declarative TargetSpec is right even though the record bloated.
- **PendingChoice** for mid-resolution interactive decisions.
- The testing culture (547 tests; rulebook worked examples; catalog-wide invariant scans like the keyword/AbilityDef mismatch test).
- The API/client split and the data pipeline (bulk catalog import separate from ability authoring).

---

## Part 1 — Direction A: faithful sim, restructured

If exact physical-game fidelity remains the target, the honest lesson
from every mature digital TCG engine (MTG's Forge and XMage,
Hearthstone simulators) applies: **nobody has a closed pure-data
representation of full card text.** They all converge on the same
hybrid:

1. **A unified event + query pipeline as the engine spine.**
   - Every state read that card text can modify goes through a query:
     `GetAttack(die)`, `GetPurchaseCost(card, player)`,
     `CanBlock(die, attacker)`, `GetLegalTargets(spec)`. Registered
     *modifiers* (from cards, keywords, applied effects) intercept and
     transform the answer.
   - Every state change emits an event: `DieFielded`, `DieKOd`,
     `DamageDealt`, `PurchaseMade`, `DiceDrawn`. Triggered abilities
     are event subscriptions with declarative filters.
   - This **collapses both parallel systems into one**: the 39
     `Grants*` flags become registered modifiers/subscriptions declared
     on the card, not engine edits; the three `*DieMatch` filter
     records become one event-filter shape; scattered enforcement
     points become "the query asks its interceptors."
2. **A small template vocabulary for the head of the distribution.**
   The 16 nodes used ≥5 times, kept as data — they earn it.
3. **Cards-as-scripts for the tail.** A card in the last 47% is a
   small class implementing against the event/query API — honest,
   locally-contained C#, instead of a fake-generic DSL node named
   `SwapFieldAndUsedPileDice` that one card will ever use. Same amount
   of code per weird card as today, but it stops polluting the shared
   vocabulary, the interpreter switch, TargetSpec, and GameState.

**What this buys**: new-set cost drops to (a) free for template-shaped
cards, (b) one self-contained script file for exotic cards, (c) engine
changes only for genuinely new *mechanisms* (capturing, priority
windows). The interpreter switch, TargetSpec, EffectCondition, and
CardDef stop growing. **What it costs**: the event/query pipeline is a
real rewrite of the engine's midsection (DieStats' stat math,
TurnEngine's purchase/field paths, CombatEngine's block legality —
everywhere a `Grants*` check lives today), touching most of the 547
tests' fixtures even where behavior is unchanged. Estimate: the
largest of the three directions by a wide margin, delivered
incrementally (queries first, then events, then migrate flags
one-by-one — each flag migration is small and test-protected).

---

## Part 2 — Direction B: simplified, templatable rules stack

The reuse audit is direct evidence for this direction: **~85% of
authored ability instances already fit 16 primitives.** If the design
stance flips from "implement what the card says" to "cards say what
the template vocabulary can express," the engine contracts sharply:

- **Effect vocabulary**: fix a closed set of ~15–20 parameterized
  templates (deal damage / KO / move / spin / stat-modify / grant
  keyword / prep / draw / life change / purchase discount, plus
  `Sequence` and a small `Conditional` set). Delete the 23 single-use
  nodes and the exotic conditions.
- **TargetSpec**: shrinks to ~8 fields (ownership, zone, die-kind,
  count, and a *bounded* filter set chosen deliberately — e.g. level
  and one stat threshold — rather than accreted).
- **Continuous effects**: the template stance is what makes these
  tractable — a closed set of ~6–8 continuous templates ("while
  active: stat aura," "cost modifier," "combat restriction," "damage
  interception") replaces all 39 flags. This still wants the Part 1
  query pipeline underneath, but a much smaller one, because the set
  of things a modifier can intercept is fixed by the template list
  instead of open-ended.
- **Interpreter**: est. ~300–400 lines instead of 1,174; no per-card
  engine edits ever.

**What existing DPS cards would lose**: the 23 single-use-node cards
either get re-expressed approximately in templates (most are close —
e.g. `MutualDamageEqualToOwnAttack` ≈ two `DealDamage`s with a
computed amount) or get *redesigned* to a template-shaped ability.
That's a per-card editorial decision, and the project already makes
those decisions (see "even faithful is already approximate" above) —
this direction just makes the policy explicit instead of implicit.

**What it buys beyond size**: a closed vocabulary is machine-legible.
Balance tooling, procedural card generation, a heuristic AI opponent,
and card-editor UI all become feasible because every ability is one of
N known shapes with numeric parameters. It also fixes the authoring
bottleneck: templated cards could be authored as JSON/data by a
non-programmer (or bulk-suggested from rawText by pattern matching —
the design doc's own "data pipeline" section anticipated exactly this).

**Extensibility**: adding a template later is cheap and safe (one node,
one interpreter case, no per-card engine edits). The discipline is
saying *no* to per-card requests — which is exactly the discipline the
physical-fidelity goal made impossible.

---

## Part 3 — Direction C: a new iteration of the game

Question asked: architecture impact only. The inventory of what's
currently hardcoded that the examples given (6-die draw, heterogeneous
Sidekick dice, reskinned icons/terminology) would collide with:

| Hardcoded today | Where | New-game requirement |
|---|---|---|
| `EnergyType` enum: exactly Fist/Bolt/Mask/Shield (+Wild as an `EnergyKind`) | `Enums.cs`, threaded through purchase matching, Globals, TargetSpec | Energy as an **open symbol set** defined in game-config data; "wild" as a symbol property, not a special case |
| Sidekick as a special kind | ~78 references across TurnEngine (17), DieStats (48), CombatEngine (12), TeamSetup | "Sidekick" becomes a **tag on a die definition**, with the basic-die pool defined per game config (8 identical dice → two 4-die sets is then pure data) |
| Die faces: `CharacterFace(FieldingCost, Attack, Defense, BurstStars)`, six-face/level-1–3 assumptions | `CardDef.cs`, `DieStats.GetFace`, PlaceholderDiceRoller's energy-face formula | **Dice as pure data**: a `DieDefinition` is a list of faces; a face is a bag of symbols (energy pips, stats, level, burst, wild) — the roller and stat lookups read the data, never assume a layout |
| Turn constants: draw 4, 8 Sidekicks + 1–2 basic actions, 20-dice team cap, starting life | TurnEngine, TeamSetup, RandomTeamBuilder | A **RulesConfig record** (draw count, pool composition, caps, life) injected into the engine — the current game is just one config |
| Terminology/icons (fist/mask names, "Sidekick", "Prep Area") | Mostly frontend + card data strings | A **theme/skin layer**: engine speaks neutral ids; display names and icons come from a theme file. Cheap; almost entirely a web-client concern |

None of these are individually hard; what's hard is that they're
assumptions *woven through* the same files the `Grants*` flags live in.
Which is the key observation:

**Direction C requires Direction B, and mostly subsumes it.** In a new
iteration you author the card pool yourself, so the ability vocabulary
is closed *by construction* — no physical card exists to demand a 24th
single-use node. The templatable rules stack isn't a compromise there;
it's simply the game's design language, and the rules config + data-
driven dice are the other half of the same "engine reads game
definitions as data" posture. The faithful-sim mode then becomes: one
particular (large, slightly awkward) game definition that covers ~85%
of Dice Masters cleanly and approximates or omits the tail.

---

## Comparison and recommendation

| | A: Faithful, restructured | B: Simplified templates | C: New iteration |
|---|---|---|---|
| Engine size trend | Stops growing per-card | Shrinks sharply | Shrinks + generalizes |
| Rewrite scope | Largest (event/query spine under existing behavior, 547-test migration) | Medium (smaller spine + card re-expression pass) | Medium+ (B's scope + dice/config data model; but green-field rules, fewer edge cases to honor) |
| New-set / new-card cost | Low for templates, one script per exotic card | Near zero (data only) | Near zero, and you control the pool |
| Fidelity | Full (finally including the currently-skipped cards) | ~85% clean, tail approximated — policy already implicitly in effect | N/A — new game |
| Unlocks | Complete DPS/future sets as-printed | Balance tooling, card editor, AI opponent, non-programmer authoring | All of B, plus original-game upside and no IP shadow over a public deployment |

**Recommendation.** The three directions share one load-bearing
decision — *is the vocabulary open (A) or closed (B/C)?* — and the
audit says the open-vocabulary bet is what made the DPS pass
expensive. Unless as-printed completeness is itself the goal, the
closed-vocabulary engine core is the better foundation:

1. **Build the v2 core as B**: unified query/event spine sized to a
   fixed template list; dice, energy, and rules constants as data
   (C's table above — cheap to include from day one, even before any
   new-game design exists); the current game re-expressed as a game
   definition.
2. **Keep v1 running untouched meanwhile** — it works, it's deployed,
   and its 547 tests plus `SampleCards.cs` become the migration oracle
   (each re-expressed card can be diffed against v1 behavior).
3. **Defer the A-vs-C fork** until the core exists: if the pull turns
   out to be physical fidelity, bolt A's cards-as-scripts escape hatch
   onto the v2 spine (it's designed to accept one); if it's the new
   game, the core is already shaped for it and the remaining work is
   game design, not architecture.

This sequencing spends the rewrite budget once, on the part all three
futures agree on.

### If/when picked up, the first concrete steps would be

(Not a migration plan — direction markers only.)

- Write the template vocabulary as a spec first (the ~16 survivors +
  the ~6–8 continuous templates), and re-express a sample of 20
  diverse DPS cards on paper to validate coverage before any code.
- Prototype the query pipeline on a single stat (EffectiveAttack) and
  a single continuous template (stat aura) to size the pattern.
- Decide the tail policy explicitly: approximate, redesign, or drop —
  per card, recorded in the catalog the same way `IsImplemented`
  already is.
