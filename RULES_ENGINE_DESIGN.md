# Dice Masters Rules Engine — Design Notes (v0)

## Start here (current state, 2026-07-27)

**What exists**: `DiceFight.Engine` (rules engine - all four Main Step
actions, combat, the ability queue, the legal-target system),
`DiceFight.Api` (ASP.NET Core wrapper, one controller action per engine
action, in-memory game store, no persistence), `web/` (React + Vite - a
functional "dev console" board: click dice, contextual action tray, real
zone layout, a "How to Play" dialog explaining the controls - not a
polished game UI yet). 50 xUnit tests, all passing. `Zone` now splits the
Prep Area into a persistent zone (targeted by KO/Prep effects) plus two
transient staging zones used only within a single Clear & Draw → Roll &
Reroll cycle (`DiceFromBag`, `DiceFromPrep`) - see the two most recent
status updates for why (a Pepper Potts-shaped rules interaction the old
single-zone model couldn't express) and for the follow-up split of
`TurnEngine.RollAndReroll` into `Roll` + `FinishRoll` so a player sees the
roll before deciding what to reroll, instead of committing blind.
`Data/SampleCards.cs` declares 26 real cards; two fixed 10-card teams
(8 characters + 2 Basic Actions each, per real team construction rules -
the other 6 cards stay in the catalog, unused by either team) with real
names/subtitles/ability text pulled from
Teambuilder's data; only 7 have a scripted `AbilityDef` (see the
"Scripting policy" note near the top of that file) - the rest are
intentionally vanilla rather than simulating a partial/wrong subset of a
more complex card. Numeric stats (cost/energy/attack/defense) are mostly
still placeholder values (see next paragraph) - Big Barda, Harley Quinn,
Robin, and Starfire are the exception, with real cost/energy/per-level
stats pulled from the user's reference spreadsheet (see the bottom-most
status update).

**Deployed**: live on GCP Cloud Run with continuous deployment already
configured - every push to `main` on GitHub auto-builds the repo-root
`Dockerfile` (a combined container: the React build is copied into the
API's `wwwroot`, one process serves both `/api/*` and the app) and
redeploys automatically. Publicly accessible with no auth - acceptable for
now, explicitly a hobby project rather than something sensitive. The GCP
console/IAM setup details aren't recorded here since it's done and
working; nothing about continuing game development needs to touch it
unless the deploy itself breaks.

**Blocked on user input, not a technical task**: real per-level
attack/defense numbers. None of the six cloned DiceCoalition repos contain
them (confirmed by inspection - every community tool represents combat
stats via card-face images, never structured data), so every sample card
currently runs on placeholder stats. Getting real numbers means either a
manual data-entry effort against real physical/reference cards, or finding
a different data source - worth asking the user before investing time
here rather than assuming which approach they'd want.

**Actionable next steps, roughly high to low value**:
1. Keyword *behavior* (Overcrush, Regenerate, etc.) - currently just
   tagged as data on `CardDef.Keywords`, not simulated by `CombatEngine`.
2. Global ability UX - done as a standing sidebar (`GlobalAbilitiesPanel`)
   with its own energy-then-targets flow; every Global on both rosters
   (Distraction, Falcon, Invisible Woman, Starfire) is now scripted -
   still rough - no "does this need a target" hint, no affordability cue,
   no filtering which dice are valid energy.
3. Real per-attacker blocker assignment in the web client - only "no
   blockers" is wired up today.
4. Legal-target exclusions for captured dice / per-die "cannot be
   targeted" abilities - blocked on Capturing (rule 3.8) not being built.
5. A real `IDiceRoller` with actual face-table data, replacing the
   rough-guess placeholder roller (`DiceFight.Api/PlaceholderDiceRoller.cs`).
6. Team-construction legality (die-limit-sum-to-20, unique-card-name
   checks, rule 2.1.1/2.1.3) - currently unenforced; every card gets its
   full die limit regardless of team size (see `TeamSetup.cs`'s remarks).
7. Auth/login in front of the API - currently wide open, matches the
   deployment note above.

The chronological "Status update" sections below explain the reasoning
and bugs found behind each already-built piece - worth reading before
touching that area, since several of them (the legal-target system
especially) encode a real correctness fix, not just a design choice.

---

Scope: server-side rules engine only, no UI. Goal of this pass: settle on a
data model and architecture that can scale to the full card pool (thousands
of cards, each with bespoke ability text) without rewriting the core loop
every time a new keyword or card template shows up.

## Source material reviewed
- `Dice Masters Comprehensive Rules (4.11.2023).pdf` — full turn structure,
  zone rules, ability timing/queue model, keyword index (partial — index is
  long, sampled through "Vengeance").
- `Teambuilder/cards.php` — ~thousands of cards encoded as pipe-delimited
  strings per set array (e.g. `msw`, `skc`, `dps`...). Format observed:
  `"<rarity><cost><energy/hex><attack>Name|Subtitle|ability line 1|ability line 2..."`
  for characters, and `"<rarity><cost>0<max>Name|type|effect lines"` for
  Basic Actions. Icon dictionaries (`iconname`/`iconid`) map affiliation and
  energy shorthand codes to display names. This is a display-oriented format,
  not something to build the engine's data model around — treat it as a raw
  import source, not the schema.

## Why this is a hard extensibility problem
Two very different kinds of "ability" exist in the rules:
1. **Keyword abilities** (Appendix 1) — a bounded set (~50-60) of reusable,
   well-specified mechanics: Overcrush, Regenerate, Range X, Fabricate X-Y,
   Deadly, etc. Finite, engine can implement these once as first-class
   plugins.
2. **Bespoke card text** — free-form English on each card's text box
   ("When fielded, deal 3 damage to target character die and reroll target
   character die."). Thousands of unique combinations. Cannot be hand-coded
   one `if` branch at a time without an unmaintainable core loop.

The design below treats (1) as engine-native systems and (2) as **data**
interpreted by a small effect runtime, so adding new cards is authoring data,
not touching engine code.

## Core entities

```
Player { id, life, bag[], usedPile[], outOfPlay[] }

Zone = Bag | PrepArea | ReservePool | FieldZone | AttackZone | UsedPile | OutOfPlay

CardDef {
  id, name, subtitle,
  type: Character | Action | BasicAction | EpicBasicAction,
  purchaseCost, energyTypes: EnergyType[],   // fist/bolt/mask/shield/none
  affiliations: string[], alignment?: Good|Neutral|Evil,
  dieLimit, useCount (Basic Actions only, always 3),
  levels: [{ fieldingCost, attack, defense, burst?: 1|2 }],  // characters
  rawText: string,              // verbatim card text, kept for display/audit
  keywords: [{ name: KeywordId, params?: number[] }],
  abilities: AbilityDef[]       // structured, hand-authored (see below)
}

DieInstance {
  id, cardId, ownerId, controllerId,
  zone, face,                   // face encodes level (char) or energy/action
  damage, level,
  appliedModifiers: Modifier[], // until-end-of-turn, lost if die leaves Field
  attachedGear: DieInstance[]   // Equip keyword
}
```

`CardDef` intentionally separates `rawText` (always kept, verbatim, for
tooltips/audit/debugging) from `abilities` (the structured, engine-executable
representation). Cards without an authored `abilities` entry still exist,
display correctly, and can be purchased/fielded — they just no-op on their
text box until implemented. This lets card data import (from something like
the Teambuilder dataset) happen immediately, decoupled from ability
authoring, which can proceed incrementally card-by-card or template-by-
template.

## Ability representation (the extensibility core)

An `AbilityDef` is not code, it's a small tree of primitives:

```
AbilityDef {
  trigger: WhenFielded | WhenAttacks | WhenBlocked | WhileActive |
           WhenDamaged | WhenKOd | Global | Burst1 | Burst2 | ...
  cost?: EffectNode[]      // e.g. KO a die, spin down, lose life
  targeting?: TargetSpec   // "target opposing character die", "each of your..."
  effects: EffectNode[]    // ordered list, matches rule 3.1.7 sequential resolution
}

EffectNode =
  DealDamage(amount, target) | KO(target) | Move(die, fromZone, toZone) |
  ModifyStat(target, {attack?, defense?}, duration) | Reroll(target) |
  Spin(target, delta) | Draw(count) | Prep(source) | Field(target, free?) |
  GainLife(amount) | LoseLife(amount) | ApplyKeyword(target, keyword, duration) |
  Conditional(check, then, else) | Choice(options)
```

This is effectively a tiny interpreter (~20-30 primitive ops covers the vast
majority of observed card text patterns from the rulebook's own examples).
New card text = compose primitives into a tree; the engine never needs a
code change for a new card. Keyword abilities (Overcrush, Range, Fabricate,
etc.) are implemented as **engine plugins** that hook the same trigger points
rather than being expressed as effect trees themselves — they're too
structural (e.g. Range's simultaneous dual-resolution, Overcrush's damage
math) to be worth expressing as generic primitives.

## Turn engine

Mirrors rule Section 2 directly — this part of the rulebook is essentially
already a state machine spec:

```
Steps: ClearAndDraw → RollAndReroll → Main → Attack → CleanUp
Attack sub-steps: DeclareAttackers → DeclareBlockers →
                  ActionAndGlobalWindow → AssignCombatDamage →
                  WhenDamagedAbilities → ResolveDamageAndWhenKOd
```

Each step is a function over `GameState` that (a) performs the mandatory
zone moves the rules specify, (b) opens the appropriate trigger window, and
(c) drains the ability queue before advancing. Steps are one-way (2.2.4,
2.7.0.3) — no backtracking, matches a simple linear state machine, no need
for undo/rollback support beyond the queue itself.

## Ability queue (Section 3.2)

The rulebook literally specifies a FIFO queue with interrupt semantics — this
maps directly to an implementation:

- `queue: QueuedAbility[]`
- Abilities append to the end when triggered (order-of-entry = active player
  first, in their chosen order, then inactive player).
- Only Prevent/Redirect-type effects may interrupt (jump ahead of) the queue;
  everything else resolves strictly FIFO.
- Persistent abilities (3.4.4) register a standing listener instead of
  resolving once; those listeners get checked against the *game state at
  queue-entry time*, not resolution time (rule 3.2.5) — so each queued
  ability needs to snapshot the relevant game-state facts it depends on at
  entry, not re-query live state at resolution.
- Clean Up step resolves in two explicit passes: all Applied abilities
  (active player's then inactive player's), then all Persistent abilities,
  same ordering (rule 3.2.10) — worth encoding as an explicit two-pass drain
  rather than relying on natural queue order, since Applied and Persistent
  abilities can be interleaved when they're queued.

## Data pipeline (import vs. authoring)

1. **Extraction**: parse `Teambuilder/cards.php`'s pipe-delimited arrays into
   canonical `CardDef` JSON (name, subtitle, cost, energy/affiliation via the
   `iconname`/`iconid` maps, levels, rarity, dieLimit, rawText). Mechanical,
   no rules knowledge needed — gets the full card pool into the new schema
   with `abilities: []` (unimplemented) on every card.
2. **Authoring**: separately, hand-write `abilities` trees for cards,
   prioritized by: (a) cards with only keyword abilities (near-free, keyword
   plugin already exists), (b) common templates (e.g. "when fielded, deal N
   damage to target character die" recurs constantly — worth a small
   template-matching helper to bulk-generate these), (c) everything else,
   long tail, one at a time.
3. Keep extraction and authoring as separate passes/files so re-running the
   extractor (e.g. a new set gets added to Teambuilder) never clobbers
   hand-authored ability trees.

## Testing strategy
The rulebook itself contains ~25 fully worked numeric examples (the Queue
example in 3.2.2, the Range example, the Overcrush example, etc.) — these
should become the first engine test suite, since they're pre-validated
against the real rules and cover the trickiest timing/interrupt edge cases.

## Open questions for next session
- Effect-tree primitive list is a first pass — will need extending once
  authoring starts hitting patterns it can't express (expect this early,
  keyword-adjacent stuff like Heroic pairing or Fusion will need their own
  plugins similar to keywords rather than generic primitives).
- Not yet designed: legal-target computation (rule 3.3) as a reusable
  query layer — every `TargetSpec` needs to filter by zone/attribute/"legal
  target" rules (3.1.9's Legal Target definition) — this wants to be a
  shared predicate system, not duplicated per ability.
- Not yet designed: how `Controlling`/`Copying`/`Swapping` (3.9-3.11) attach
  to the DieInstance model — these mutate "which CardDef does this die
  reference" at runtime and need their own indirection (e.g. `virtualCardId`
  override) rather than being bolted onto `appliedModifiers`.

## Status update — first real cards wired in

Built: `EffectContext`/`EffectInterpreter` (executes the DSL against
`GameState`, with target resolution delegated to a caller-supplied callback
— the "legal target query layer" above still doesn't exist, this is just
the seam for it), `TurnEngine.Field` (Main Step fielding, pays cost from any
Reserve Pool energy per rule 2.6.3.2, enqueues `WhenFielded`), and
`CombatEngine` now enqueues `WhenAttacks`/`WhenKOd` too. `DieStats.TryResolveKO`
is shared between combat's simultaneous batch KO and the interpreter's
"ability damage KOs immediately" (abilities resolve one at a time, not in a
batch — rule 3.2.2).

`src/DiceFight.Engine/Data/SampleCards.cs` has 20 characters + 6 Basic
Actions (real names/subtitles/ability text from Teambuilder's `cards.php`,
`msw` set), split into two 10-character/3-Basic-Action teams. **Important
caveat discovered while building this**: none of the six cloned
DiceCoalition repos contain real per-level attack/defense numbers — every
community tool (including Teambuilder) represents combat stats only via
card-face images. So every sample card's `PurchaseCost`/`EnergyTypes`/
`Levels` are placeholder values, clearly marked as such in code comments;
only `Name`/`Subtitle`/`RawText`/`DieLimit` are real (die limit was
reverse-engineered from Teambuilder's card-line prefix and cross-checked
against every Basic Action card sharing the same value, matching rule
1.2.11's fixed "Use 3"). A real stats source is still needed before numbers
mean anything competitively.

Only 7 of the 26 cards got a scripted `AbilityDef` (Dazzler, God Emperor
Doom, Groot, Cosmic Cube, Shocking Grasp, Casket of Ancient Winters,
and Distraction's Global half only - its non-Global clause needs
multi-die opponent choice and a persistent "can't block" flag the engine
doesn't model yet) — the rest are intentionally left vanilla (empty
`Abilities`, real `RawText` + `Keywords` retained) rather than simulating
a partial/incorrect subset of a more complex card's text. The scripting
bar was: the *entire* ability text maps onto existing primitives, nothing
dropped.

Still not built: any keyword *behavior* (Overcrush/Regenerate are tagged
as data on cards but not simulated by CombatEngine yet), and the real
IDiceRoller face-table data behind Purchase/Field/UseActionDie (this
section's tests seed dice directly onto the face they need rather than
rolling for it).

## Status update — Main Step mechanics finished

All four Main Step game actions (rule 2.6.0.1) now exist: `TurnEngine.
Purchase` (rule 2.6.2 - unlike Field, requires at least one spent energy
die per distinct required `EnergyType`; `DieInstance.EnergyKind`
Wild/Specific/Generic, derived in `ApplyRoll` from the die's type, decides
what satisfies that), `Field` (already existed), `UseActionDie` (rule
2.6.4 - enqueues `WhenUsed`, and special-cases Epic Basic Actions
returning to their card instead of Out of Play plus the once-per-turn
limit, rule 1.2.3), and `UseGlobalAbility` (rule 2.6.5 - `AbilityDef` now
carries an optional `EnergyCost`; either player can pay, with the
Active/Inactive energy-destination split from rule 2.6.1.1/2.6.1.2).

`TeamSetup.SetupTeamDice` (called from `GameState.NewGame`) instantiates
each player's team-card dice into a new `Zone.Unpurchased`, so
`SampleCards`'s teams are now real, purchasable rosters rather than
hand-built `DieInstance`s in test code. One deliberate scope cut: rule
2.1.3's 20-dice team cap is a team-*construction* legality rule (is this
TeamCardIds list even a legal team to bring), not something die
instantiation should silently enforce - our 10-character sample teams
already exceed it (by design, per an earlier explicit ask for 20
characters total rather than the standard 8-card format), and an early
version of `TeamSetup` that tried to truncate at 20 silently produced
cards with zero dice. Team legality validation is unbuilt; every card
gets its full Die Limit regardless of team size for now.

On "priority passing" (rule 2.6.6): this turned out not to need its own
mechanical construct. The engine doesn't model an interactive turn loop -
it's a library of validated game actions (`Purchase`, `Field`,
`UseActionDie`, `UseGlobalAbility`, `DeclareAttackers`, ...) that a caller
sequences, with each action's legal window (`CurrentStep`/`AttackSubStep`)
enforced at the point of the call. Both players can already call
`UseGlobalAbility` in the right window in whatever order the caller
chooses; formalizing whose "turn" it is to act next is a caller-side (or
future network-protocol) concern, not something the engine itself needs
to arbitrate.

37 tests total (up from 26): new coverage includes `PurchaseTests`
(energy-type matching success/failure, Wild-vs-Generic, opponent's-card
rejection, Basic Action community purchase preserving owner vs.
controller, Epic Basic Action's cost-4+ gate) and expanded
`TwoTeamsDemoTests` (Purchase → Field → Attack end to end, Shocking
Grasp's action-die use, Cosmic Cube's Epic-specific return-to-card and
once-per-turn behavior, Distraction's Global ability paid by the
Inactive player).

## Status update — a first web client, then the legal-target system

Added `DiceFight.Api` (ASP.NET Core, in-memory `GameStore`, one controller
action per engine action) and `web/` (Vite + React + TypeScript). No engine
changes for that pass - the API is a thin DTO-mapping layer, verified
end-to-end with a locally-run headless Chromium (Playwright, with a
`LD_LIBRARY_PATH` workaround for missing shared libs - `apt-get download`
+ `dpkg-deb -x`, no root needed) clicking through Purchase/Field/Attack in
an actual rendered browser. The web board was then redesigned around the
playmat's zone layout and a Primary/Secondary die-selection model instead
of a flat "1st selected/rest" scheme.

Then: `TargetSpec` (Effects/EffectNode.cs) went from an opaque
`Description` string to a real structured filter - `TargetOwnership`
(Any/Own/Opposing), `CharacterDiceOnly`, `EligibleZones` (defaulting to
Field + Attack Zone per rule 3.3.4/3.3.5), `RequiredEnergyType`, and
`Count` - plus a `TargetSpec.Self` marker for self-reference (Shocking
Grasp's "Prep this die"). `LegalTargets.Query` computes the actual
candidate set from `GameState` (still a first pass: no captured-die or
per-die "cannot be targeted" exclusions, since neither Capturing nor
per-die targeting restrictions exist yet). `EffectInterpreter` now
validates every caller-chosen target against that computed legal set
instead of trusting whatever id a caller supplies, and enforces rule
3.3.11's "up to Count, or all of them if fewer exist" requirement.

The interesting bug this surfaced: resolving each `TargetSpec` live,
clause-by-clause, breaks rule 3.2.5 the moment an ability's own earlier
clause mutates the board state its later clause's legality depends on.
Casket of Ancient Winters makes this concrete - its own first clause KOs
3 dice (which land in the Prep Area, rule 1.5.3.2) before its third clause
asks for "3 dice from their Prep Area," so live resolution let clause 1's
own KOs inflate clause 3's candidate pool. Fixed by resolving every
`TargetSpec` in an ability's tree once, upfront, against the
pre-execution state (`EffectInterpreter.Execute` now walks the tree via
`CollectTargetSpecs` before running any clause) - which is also what
correctly handles a spec referenced twice within one ability (Shocking
Grasp's damage clause and its own "if that character is KO'd" check;
God Emperor Doom's damage-then-reroll of the same die) without a
re-derived legal set disagreeing with itself mid-ability.

47 tests total (up from 37): new `LegalTargetsTests` (ownership/zone/
character-only/energy-type filters, illegal-target rejection, the
count-enforcement rule, `Self` bypassing filtering entirely), plus fixes
to existing tests that had been implicitly relying on there being no
target validation at all (e.g. targeting Sidekicks with abilities that
require a specific energy type, which Sidekicks structurally can't have).

Still not built: legal-target exclusions for captured dice and per-die
"cannot be targeted" abilities (both because Capturing and per-die
restrictions aren't modeled yet), and Global ability UX in the web client
(needs two distinct kinds of secondary selection - energy, then targets -
that don't fit the current single-secondary-selection Action Tray).

## Status update — How to Play dialog

Added a "How to Play" button/modal (`web/src/HowToPlay.tsx`) to the web
client - no engine or API changes. Deliberately scoped as operating
instructions for this specific UI (the Primary/Secondary selection model,
what each turn-control button does and does not do yet, the zone layout),
not a Dice Masters rules primer - assumes the reader already knows the
physical game. Also calls out the known UI gaps (no real blocker
assignment, no Global ability targeting) inline so a new player doesn't
mistake an unimplemented feature for a bug. Verified in both light and
dark rendering via the headless-Chromium/Playwright setup (screenshots,
not just a type-check). `dotnet build` + `dotnet test` (47/47) and
`npm run build` both still pass - this pass touched only the web client.

## Status update — split Prep Area into a persistent zone plus two transient staging zones

The How to Play dialog write-up above surfaced a real engine bug, not
just a wording issue: `Zone.PrepArea` was doing double duty as both "this
turn's fresh draw, about to be rolled by Roll & Reroll" and "wherever a
KO or a Prep effect (`EffectNode.PrepDie`, used by Shocking Grasp) parks a
die," with no way to tell those apart. That's invisible for KO'd dice
(they're always Prepped by something that already happened *before* this
turn's Clear and Draw runs, so getting swept into this turn's roll is
correct), but it breaks a card like Pepper Potts: "draw an extra die at
the beginning of your Clear and Draw Step... If it is a non-Sidekick die,
Prep it." That extra die is Prepped *during* the same Clear and Draw step
its own turn's draw happens in - per the ruling, it should sit out this
Roll & Reroll and only get rolled next turn, but a single shared PrepArea
zone can't express "prepped a moment too late to make this turn's roll."

Fix: `Zone` gained two transient staging zones, `DiceFromBag` and
`DiceFromPrep` (`Model/Enums.cs`). `TurnEngine.ClearAndDraw` now sweeps
whatever's sitting in `PrepArea` into `DiceFromPrep` *before* drawing, then
draws straight into `DiceFromBag` (previously both landed in `PrepArea`
directly). `TurnEngine.RollAndReroll` now reads `DiceFromBag ∪
DiceFromPrep` as its working set instead of `PrepArea` itself. Net effect:
`PrepArea` is empty immediately after `ClearAndDraw` returns, and only
ever holds a die that landed there *after* that step's sweep already
ran - exactly the dice that should wait for next turn. KO and `PrepDie`
themselves are unchanged (still target `PrepArea` directly) since nothing
about how KO'd dice land there was wrong.

No turn-step-start trigger system exists yet (`TriggerType` has no
"beginning of Clear and Draw" timing - see `AbilityQueue`/`TurnEngine`
remarks), so Pepper Potts itself still can't be scripted; this pass only
fixes the zone model it depends on, verified with two new
`TurnEngineTests` cases that plant a die directly in `PrepArea` mid-test
to simulate the "Prepped after this step's sweep" and "left over from
before this step's sweep" cases without needing the trigger system to
exist. Also updated the How to Play dialog's Clear & Draw / Roll & Reroll
copy to describe the corrected model, and added both new zones to the web
client's zone list/display names (`web/src/types.ts`,
`web/src/PlayerBoard.tsx`) so they're visible in the board UI between a
Clear & Draw click and the Roll & Reroll click that follows it - confirmed
visually via the headless-Chromium setup (a full turn's Clear & Draw
followed by Roll & Reroll, screenshotted at each step). 49 tests total (up
from 47), all passing; `npm run build` still passes.

## Status update — split Roll & Reroll into Roll + FinishRoll, and other web-client UX fixes

User feedback after trying the new zones surfaced a second, related
correctness gap: rule 2.4.3 lets a player reroll any/some/none of their
dice, but only *after* seeing the roll (rule 2.4.1/2.4.2 happen first).
The old `TurnEngine.RollAndReroll(state, roller, chooseRerolls)` rolled and
rerolled in one atomic call, and the web client's only caller
(`GamesController.RollAndReroll`) had the client submit its reroll-id list
in the *same* HTTP request that triggered the roll - meaning the player
was committing to a reroll decision blind, before the dice had actually
been rolled. Real physical play doesn't work that way: you roll, look at
the faces, then decide.

Fix: `TurnEngine.RollAndReroll` is now two calls. `Roll(state, roller)`
rolls every die in `DiceFromBag`/`DiceFromPrep` and leaves them there
(now showing real faces, not yet in the Reserve Pool). `FinishRoll(state,
roller, rerollDieIds)` rerolls just the requested ids, then moves
everyone in the step to the Reserve Pool (rule 2.4.4). The API grew a
matching `/roll` and `/finish-roll` in place of the old `/roll-and-reroll`.
Added `TurnEngineTests` covering the two-phase split directly (`Roll`
alone must not touch the Reserve Pool; `FinishRoll` must reroll only the
requested ids, verified with a `SequentialRoller` fake that gives each
`Roll()` call a distinguishable, increasing Level so "rerolled" vs. "kept
as rolled" is observable). 50 tests total.

Web client changes to go with it: the "Roll & Reroll (selected = reroll)"
turn-control button (which required pre-selecting dice, playing right
into the same blind-decision bug) is gone. In its place: a "Roll" button
that appears only while the active player has unrolled dice in
`DiceFromBag`/`DiceFromPrep` and always rolls all of them - no selection
needed, since a player never rolls only *some* of this turn's dice.
"Advance Step" is disabled until rolled, and once rolled, clicking it
calls `finishRoll` with an empty reroll list before advancing - so a
player who wants to keep everything just clicks Advance Step once, rather
than needing a separate "keep all" button. To actually reroll, select the
dice first (the ordinary click-to-select flow, unchanged) and a new
contextual "Reroll Selected" action appears in the Action Tray whenever
the primary selection is a rolled (non-`Unrolled`) die in one of those two
zones.

Also fixed, from the same feedback: the Action Tray's secondary
selections were rendered as one joined text string ("+ Sidekick,
Sidekick") instead of individual chips, which read as a single
"+Sidekick" label piling up rather than one chip per die - each secondary
selection now gets its own `.secondary-chip` element, matching how the
primary chip already looked. Also renamed the `DiceFromBag`/`DiceFromPrep`
zone display labels from "... (unrolled)" to "Drawn This Turn"/"Carried
From Prep" - the old labels became actively wrong the moment `Roll`
executes, since the same zone then holds rolled dice awaiting the reroll
decision, not unrolled ones.

Verified the whole Roll → select-and-reroll → Reserve Pool flow, and the
"just click Advance Step to keep everything" shortcut, end-to-end via the
headless-Chromium setup (screenshots at each step, both Team A's `Roll`
button and disabled `Advance Step` before rolling, then the individual
selection chips and successful reroll). `dotnet build`/`test` (50/50) and
`npm run build` all pass.

## Status update — rolled dice land straight in the Reserve Pool; Advance Step moved out of Step actions

Two follow-up fixes from user feedback on the Roll/Reroll split above.

**Rolled dice now land directly in the Reserve Pool, not a holding zone.**
The previous pass had `Roll` roll dice in place (`DiceFromBag`/
`DiceFromPrep`) and only `FinishRoll` moved them to the Reserve Pool -
modeled on "the reroll decision needs to see the roll first," but that
detail (seeing results before deciding) doesn't require a separate
holding zone once you notice rule 2.4.3 is phrased as "may reroll any
dice in your Reserve Pool," i.e. the real rulebook already has rolled
dice land in the Reserve Pool and lets you reroll them *there*. Simpler
and more correct: `Roll(state, roller)` now rolls every
`DiceFromBag`/`DiceFromPrep` die and moves it straight to `ReservePool`
in the same call. `FinishRoll` is gone; `Reroll(state, roller,
rerollDieIds)` replaces it and just rerolls the requested ids in place
(no zone change) - it's now genuinely optional, since Roll alone already
leaves the turn in a valid state. This also simplified the web client:
Advance Step no longer needs to silently call a "finish with no rerolls"
before advancing (there's nothing left to finish), so that composed
handler in `App.tsx` is gone too.

**Advance Step moved out of the Step actions row into the status bar.**
User's observation: once Roll/Reroll/Advance Step got teased apart,
Advance Step is the one control a player uses every step of every turn,
while Clear & Draw / Roll / Enter or Skip Attack Step / Declare Blockers
/ Assign Combat Damage / Clean Up are really per-step admin actions -
each is only legal (and only does something) during its own step, closer
to a debug console's step-firing buttons than a normal play flow. Moved
the (now-styled, blue, disabled-while-unrolled) Advance Step button to
the status bar, to the left of the Step/Active readout; renamed the
remaining row's label from "Turn controls" to "Step actions" to match.
How to Play updated to match (a new "Advance Step" section ahead of "Step
actions").

Also answered (not a bug): why only Sidekick faces (`L1`/`Wild`) have
shown up in every demo so far - `PlaceholderDiceRoller` is a rough
probability model, not a real fixed-face table (still tracked as a
follow-up - see "Actionable next steps" above), and Sidekick dice are
correctly limited to exactly those two outcomes by rule 1.6.8 (always
Level 1). A non-Sidekick character die, once one is purchased/fielded and
rolled, will already show varying levels 1..N per the card's level count
under the current placeholder logic - it just hasn't come up yet because
every demo so far only ever rolled starting Sidekicks.

Verified the whole new flow end-to-end via headless Chromium: Roll lands
dice directly in the Reserve Pool (no more transient "Drawn This Turn"
sighting for rolled faces), reroll works in place, and Advance Step's new
position/disabled-state behaves correctly through a full
ClearAndDraw → Roll → Reroll → Advance → Main sequence. `dotnet
build`/`test` (50/50, tests updated for the `Reroll` rename and the
straight-to-Reserve-Pool behavior) and `npm run build` both pass.

## Status update — corrected Sidekick die faces: 5 energy faces, not all Wild

User correction: a Sidekick die has six faces total - one Level 1
character face, and *five* distinct energy faces (Wild, Fist, Bolt, Mask,
Shield), not five copies of a single "Energy → always Wild" face. The
engine had this wrong: `TurnEngine.ApplyRoll` hardcoded `die.CardId is
null` (a Sidekick) to always produce `EnergyKind.Wild` on any Energy-
status roll, regardless of what was actually rolled - collapsing five
physically distinct faces into one.

Fix required moving where "what kind of energy did this face provide"
gets decided. It used to be inferred downstream in `ApplyRoll` from the
die's card/type; now it's part of the roll result itself; `RolledFace`
(`TurnEngine.cs`) gained `EnergyKind`/`EnergyType?` fields, and
`ApplyRoll` just copies them from whatever `IDiceRoller.Roll` returns
instead of re-deriving them. `PlaceholderDiceRoller` now rolls a Sidekick
as a uniform 1-in-6 across `SidekickCharacter` + the four specific types +
Wild, instead of a fixed 1-in-3 chance of an Energy face that was always
Wild. Non-Sidekick character/Basic Action dice are unaffected - they
still produce their card's own fixed type / Generic energy respectively,
just expressed through the same `RolledFace` fields now instead of
`ApplyRoll`'s own if/else.

No web client changes needed: `ActionTray`/`dieHelpers.dieStatusText`
already rendered whatever `EnergyKind`/`ProvidedEnergyType` a die actually
had rather than assuming Wild, so Fist/Bolt/Mask/Shield faces just started
showing up once the engine could produce them. Verified two ways: a
60,000-roll standalone sanity check against `PlaceholderDiceRoller`
directly (all six Sidekick faces landed within ~16.5-16.9%, consistent
with a fair d6), and visually in the browser - the very first `New
Game` → `Clear & Draw` → `Roll` in a fresh headless-Chromium session
already showed a Sidekick Reserve Pool with `Shield`, `Bolt`, and the
character face side by side. Added `TurnEngineTests.
Roll_TrustsTheRollersEnergyKindAndType_ForEnergyFaces` to lock in that
`ApplyRoll` trusts the roller's `EnergyKind`/`ProvidedEnergyType` rather
than re-deriving them (`PlaceholderDiceRoller` itself still has no
dedicated test - it lives in `DiceFight.Api`, which the test project
doesn't reference, and remains explicitly a rough placeholder pending
real face-table data). 51 tests total; `dotnet build`/`test` and `npm
run build` all still pass.

## Status update — show purchase cost/energy on Unpurchased dice; 4 sample cards now use real stats

Two connected fixes. First, a real gap: the Unpurchased roster showed a
card's name but never its purchase cost or required energy type(s) -
`web/src/dieHelpers.ts`'s `dieStatusText` returned `""` for any die with
`status: "Unrolled"`, which is every die still sitting on its card. Added
a case for `Unrolled` dice with a `cardId`: renders `"Cost {N}"`, plus
`" · {type1}/{type2}"` for however many distinct energy types the card's
`EnergyTypes` lists (Basic Actions have none - rule 1.2.4/1.3.10 - so
they just show the cost). Confirmed `TurnEngine.Purchase` already
enforces the multi-type case correctly (one energy die per *distinct*
required type, Wild substituting for any - `TurnEngine.cs` around line
227) - rule 2.6.2.3's Fist+Shield example was already right, just never
surfaced in the UI. Not addressed: a card that excludes Wild from
satisfying its requirement entirely (user's "White Lantern" example, one
of each type with no Wild substitution) - `Purchase` has no such
exclusion flag today; noted here as a real gap for if/when such a card
gets added, not implemented speculatively.

Second: since that display is only as useful as the underlying data, and
`SampleCards.cs`'s existing "msw"-set cards all share one placeholder
cost/energy/stat-line (see the top-of-file remarks - none of the cloned
community tools have real numbers for that specific, newest set), swapped
four of them for cards pulled from the user's reference spreadsheet
(an older set that *does* record real cost/energy/per-level stats,
encoded as compact "CAD" triplets per level - e.g. "133 244 255" decodes
to L1 cost1/atk3/def3, L2 cost2/atk4/def4, L3 cost2/atk5/def5). Replaced
Agent Brand → **Big Barda** (Fist, cost 3), Black Swan → **Harley Quinn**
(Mask, cost 1), Captain Britain → **Robin** (Shield, cost 2), and Jimmy
Woo → **Starfire** (Bolt, cost 3) - all four picked for having short or
blank ability text (stay vanilla, no scripting needed) and each a
different energy type, so all four Unpurchased-cost badges could be
demonstrated at once. Only pulled this small slice into context, not the
whole spreadsheet. Die limit still isn't in that source, so it's a
labeled guess (4, typical Common rarity) rather than presented as real -
same transparency policy as the rest of the file. `Character()`'s
signature grew optional `energyType`/`levels` params (defaulting to the
existing placeholders) so every other card's declaration is untouched.
None of the four replaced cards were referenced by name in any existing
test (confirmed by grep before swapping). 51 tests still pass unchanged;
verified the new cost/energy badges visually in the browser, and the
real API response for all four new cards, before committing.

## Status update — ability-text tooltips; fixed team size to 8 characters + 2 Basic Actions

Two more small fixes from user feedback.

**Hover tooltip for ability text.** Die chips only ever showed a card's
name, which doesn't disambiguate different printings of the same
character (real Dice Masters commonly reprints a name with a different
subtitle/cost/stats/text). Added `dieHelpers.dieTooltip` - a native
`title` attribute on every die-chip button showing `Name — Subtitle` plus
the full `rawText` (or `"(blank text box)"` for the real cards, like
Colossus, that genuinely have none) - computed once per `DieGroup` since
a group is already guaranteed to share one `cardId`. Works in every zone,
not just Unpurchased, since any die might need disambiguating. No new
component - a browser-native tooltip was enough for this.

**Team size was wrong.** `TeamACharacterIds`/`TeamBCharacterIds` had 10
characters + 3 Basic Actions each (13 cards); a real team is 8 characters
+ 2 Basic Actions (10 cards) - a plain factual error, not a rules
interpretation call. Trimmed both rosters down, choosing which to cut by
grepping every test for `SampleCards.<Name>` first: anything a test reads
via `FindUnpurchased` (which requires the card to actually be on that
player's roster, or the lookup throws) had to stay - Dazzler, Apocalypse,
CaptainMarvel, CosmicCube, ShockingGrasp, Falcon, and
CasketOfAncientWinters. `TurnEngine.UseGlobalAbility` and
`EffectInterpreterTests`'s direct `SampleCards.CasketOfAncientWinters.
Abilities.Single()` read `state.CardCatalog` instead (populated by
`BuildCatalog()`'s full card list regardless of team roster), so
Distraction's Global-ability test kept passing even after dropping
Distraction from Team A's roster - team membership and catalog membership
turned out to be genuinely independent, which is also why the cut cards
(Colossus, Corvus Glaive, Distraction, Kang, King Hyperion, Escape!) were
left declared rather than deleted: still real, sourced card data, still
in `BuildCatalog()`/`/api/cards`, just not on either fixed demo team -
useful inventory for a future team-builder rather than dead code. Verified
post-fix via the live API: both teams now show exactly 10 distinct
Unpurchased card ids. 51 tests unaffected (none referenced a cut card).

## Status update — clearer error messages, and a guided step-navigation redesign

Three more fixes from user feedback, roughly small to large.

**"Clean Up" → "End Turn (Clean up)."** Just the button label, in both
the new primary control and the advanced panel below - it wasn't obvious
this was the turn-ending action.

**Error messages named the raw die id instead of the card.** Purchasing
Falcon with the wrong energy said `Purchasing teamB-falcon-1 requires at
least one Mask energy` - which team it belongs to is already visible
elsewhere in the UI, and a raw id isn't something a player should ever
need to read. Added `TurnEngine.DisplayName(state, die)` (card name via
`CardCatalog`, falling back to the raw id only if no card resolves) and
used it in every `Purchase`/`Field`/`UseActionDie`/`UseGlobalAbility`
error that used to interpolate a die id or raw card id - now reads
`Purchasing Falcon requires at least one Mask energy`. Verified with a
throwaway console script constructing the exact reported scenario (2
Fist + 1 Mask against a Mask-requiring card) rather than trying to force
a specific bad roll through the UI.

**Step navigation redesign.** The real complaint: "I thought I'd need to
Advance Step to get into the Attack step, but apparently I needed to
click Enter Attack Step" - because the old flat "Turn controls" row
showed every raw engine action unconditionally (Clear & Draw, Advance
Step, Roll, Enter/Skip Attack Step, Declare Blockers, Assign Combat
Damage, Clean Up) regardless of whether it was legal right now, with no
visual distinction between "the thing that moves the turn forward" and
"a specific step's job." `AdvanceStep` itself only ever covers two of the
turn's transitions (Clear&Draw→Roll&Reroll, Roll&Reroll→Main) - Main's
exit is a genuine fork (Enter Attack Step vs. Skip Attack Step, not a
linear "next"), which the uniform "Advance Step" button masked entirely.

Replaced it with a computed `advanceOptions` list in `App.tsx` - the
literal set of legal next actions for `game.currentStep`/`attackSubStep`
right now, mirrored from `TurnEngine`'s own guards (`canClearAndDraw`,
`canRoll`, `canAdvanceToRollAndReroll`, `canAdvanceToMain`,
`canEnterAttack`, `canSkipAttack`, `canDeclareBlockers`,
`canAssignDamage`, `canCleanUp`), rendered as one or two prominent blue
buttons next to "Step:"/"Active:" in the status bar, labeled with the
destination step (`"Roll & Reroll ▶"`, `"Main ▶"`) or the still-needed
action (`"Clear & Draw"`, `"Roll (N dice)"`) - Main step correctly shows
*two* buttons side by side ("Attack ▶" and "Clean Up (skip attack) ▶")
since both are legal simultaneously. During the Attack step's
DeclareAttackers sub-step, where there's no blanket "advance" action at
all (you have to select attacker(s) and use the Action Tray), a plain
hint string renders instead of a button.

The old flat button row still exists, moved into a collapsed-by-default
`<details className="turn-controls">` ("Manual step actions (advanced)")
- same buttons, but every one is now `disabled` unless it's genuinely
legal for the current step/sub-step (reusing the same `can*` booleans),
addressing the "if trying to Clear and Draw from the Main step would
cause chaos, let's not even allow it" request directly - the server was
always rejecting illegal calls, but the client no longer even offers
them. No "go back to a previous step" mechanism exists (rule 2.2.4
forbids it and nothing in the engine implements an undo), so the
collapsed panel is really "the same guided actions, spelled out
individually," not a distinct manual/back-step affordance - flagged here
in case that's ever revisited (an undo feature was floated as a
longer-term alternative for fixing misclicks).

Verified the whole redesign end-to-end via headless Chromium: a full
ClearAndDraw → RollAndReroll → Main (both fork buttons visible) → Attack
(DeclareAttackers hint, no button) → skip-to-CleanUp → End Turn →
next-player's-ClearAndDraw sequence, screenshotted at each step, plus the
Manual step actions panel's disabled states at the first two steps. 51
tests still pass (none touch UI); `dotnet build`/`test` and `npm run
build` both pass.

## Status update — a Global Abilities sidebar, first working Global-ability UI

First actual UI for Global abilities (previously API-only - see the
"Actionable next steps" #2 entry and the exchange earlier in this log
about Falcon). Went with a standing sidebar rather than folding it into
the Action Tray, for a real rules reason: rule 2.6.5.2 means using a
Global ability isn't tied to selecting a specific die the way every other
Action Tray entry is - either player can trigger any card's Global from
the shared catalog, regardless of who owns or controls a die of it. A
sidebar listing the catalog directly matches that model instead of
implying "click a die of this card first."

**Backend**: `CardDefDto` gained `GlobalAbilityCost` (amount + required
type, null if the card has no scripted Global) so the client can show a
price before the player commits - `TurnEngine`/`GameStore` untouched,
this is pure DTO surface.

**Frontend**: new `GlobalAbilitiesPanel.tsx`, filtering the full card
list to `abilityTriggers.includes("Global")`. Clicking "Use" starts a
small local state machine in `App.tsx` (`GlobalAbilityFlow`: cardId,
playerId, stage `"energy" | "targets"`, chosen energyIds) rather than
building a second selection model - board clicks keep populating the
exact same `selection` state (`primary`/`secondary`) the rest of the UI
already uses; the flow just reads it at each stage and clears it between
stages, and the normal `ActionTray` is swapped for a one-line status
notice while a flow is active so the two don't both try to interpret the
same click. "Confirm Energy" locks in stage 1's selection and moves to
stage 2; "Confirm Target(s)" or "Skip (no target)" submits
`api.useGlobalAbility(cardId, playerId, energyIds, targetIds)` in one
call, matching the API's existing single-request shape. "Cancel" is
available at every stage.

Deliberately left for iteration rather than solved now: no client-side
"does this ability actually need a target" signal (the target stage
always shows, with the skip button as the escape hatch - not worth
walking the `EffectNode` tree client-side for exactly one live example
right now), no filtering of *which* energy dice are shown by the chosen
payer (you can select any die on the board; the server still enforces
`ControllerId` correctly, you just find out via the error banner rather
than the UI narrowing the choices upfront), and no visual cue for "this
ability's cost is unaffordable right now." All noted as real next steps,
not oversights.

Verified via headless Chromium end-to-end, both outcomes: submitting with
the wrong energy type surfaced the exact server error ("Distraction's
Global ability requires at least one Mask energy") without breaking the
flow (stays open for retry), and submitting with a genuine Mask die
succeeded - the spent die correctly landed in Out of Play (rule 2.6.1.1)
and the flow closed cleanly. Also reconfirmed via the live API that
`GlobalAbilityCost` serializes correctly (`{"amount":1,"requiredType":
"Mask"}` for Distraction, `null` for everything else). 51 tests
unaffected (no engine changes); `dotnet build`/`test` and `npm run build`
both pass.

## Status update — Falcon's Global ability scripted; two new engine primitives

Falcon (Team B, in the sidebar since the previous update but inert - no
`AbilityDef`) is now a real second working Global, alongside Distraction.
Its text ("Global: Pay [F]. Once during your turn, each player must
field a [PAWN] from their Used Pile if able.") needed two things
Distraction's single-target single-player effect didn't:

- **`FieldSidekickForEachPlayer`** (new `EffectNode`) - a forced action on
  *both* players at once. Sidekick dice are fungible, so "if able" is
  just "does one exist in that player's Used Pile" - no real choice to
  make, so (like `DrawDice`) it bypasses the `TargetSpec`/
  `ResolveTargets` choice pipeline entirely rather than stretch
  `TargetSpec` to express "both players, no chooser, silently skip if
  none." `EffectInterpreter` iterates `[ctx.ControllerId, opponent]` and
  fields the first Used Pile die with `Status == SidekickCharacter` for
  each, if any.
- **`AbilityDef.OncePerTurn`** - a card-text limiter ("Once during your
  turn"), tracked in a new `GameState.GlobalsUsedThisTurn` hash set
  (keyed by cardId, reset in `CleanUp`, same pattern as the existing
  single-flag `EpicBasicActionUsedThisTurn`). Checked in
  `TurnEngine.UseGlobalAbility` after payment is validated but before
  it's actually spent, so a rejected attempt (wrong energy type, etc.)
  doesn't burn the once-per-turn use.

Falcon's other half - the Teamwatch keyword's own effect ("Prep a
Sidekick from your Used Pile") - is left unscripted on purpose: Teamwatch
isn't a `TriggerType` this engine models yet (it fires on being engaged
in combat, like a `WhenEngaged` variant), and Non-global/Global are
independent ability slots (rule 3.1.3, same reasoning as Distraction's
unscripted non-Global half). No UI changes were needed - the sidebar's
existing energy-then-targets flow already handles an ability with no
real target: `FieldSidekickForEachPlayer` contributes no `TargetSpec`,
so the "targets" stage's Skip button submits an empty target list and
nothing tries to resolve one.

Added a new unit test exercising the real `TurnEngine`/`EffectInterpreter`
path: fields a pre-seeded Used Pile Sidekick for the paying player,
confirms nothing happens for the opponent (no Used Pile Sidekick to find
- the "if able" no-op case), and confirms a second activation the same
turn throws. Verified live via headless Chromium too: rolled into a Fist
die, paid Falcon's cost through the sidebar, and the flow completed with
no error and the Fist die correctly leaving the Reserve Pool. 52 tests
passing (51 + the new one); `dotnet build`/`test` and `npm run build`
both clean.

## Status update — Invisible Woman's and Starfire's Globals scripted; every Global on both rosters now works

Rounding out the sidebar - all four cards across both rosters with a
Global ability (Distraction, Falcon, Invisible Woman, Starfire) are now
scripted, not just data. Two more small additions to the effect DSL:

- **`ForceBlock(TargetSpec Target)`** - Invisible Woman's "target
  character die must block this turn." Adds the resolved die id(s) to a
  new `GameState.MustBlockThisTurn` set (reset in `CleanUp`).
  `CombatEngine.DeclareBlockers` now checks that set before moving any
  dice: if a forced die is still an eligible blocker (inactive player,
  Field Zone) and wasn't included in `blockerDieIds`, it throws before
  mutating anything, rather than partially declaring blockers and then
  failing partway through.
- **`PrepFromBagIfPurchasedThisTurn`** - Starfire's "if you purchased a
  die this turn, Prep a die from your bag." The "if you..." clause reads
  a new `Player.PurchasedDieThisTurn` flag (set in `TurnEngine.Purchase`,
  reset in `CleanUp`) rather than going through `Conditional` - that node
  only checks a target's post-effect state (rule 3.1.17's "if you do"),
  not turn-scoped history, so this reads game state directly instead,
  same reasoning as Falcon's `FieldSidekickForEachPlayer`. Also, like
  `DrawDice`, the bag pick itself is fungible so there's nothing for a
  caller to choose.

Both cards' non-Global clauses stay unscripted on purpose, same
independent-ability-slots reasoning as Distraction/Falcon: Invisible
Woman's static "+1 attack for each active [F4]..." needs a
count-matching-dice stat modifier that doesn't exist yet, and Starfire's
"Recruit" needs an off-team-recruitment mechanic that doesn't exist yet.

Three new unit tests: Invisible Woman's forced block actually blocks a
declared attacker and rejects an empty blocker list first (via
`CombatEngine.DeclareBlockers`); Starfire preps a bag die after a real
`Purchase` call sets the flag; Starfire is a no-op without a purchase and
still enforces its own once-per-turn limit. Verified live via headless
Chromium: the sidebar now lists all four Globals with their correct
costs, and Starfire's flow (paid, no purchase yet, correctly a no-op)
completed with no error. 55 tests passing; `dotnet build`/`test` and
`npm run build` both clean.

## Status update — the reroll decision is now genuinely one-shot; Global sidebar shows ability text

Two small fixes, both from a user playtest pass:

**Reroll is a single decision, not a repeatable action.** Rule 2.4.3/
2.4.4's "you may reroll any of your rolled dice" is one choice made once
after seeing Roll's results (which can legally be "reroll nothing"), not
something you can do in multiple passes. `TurnEngine.Reroll` now calls
`AdvanceStep` at the end of every successful call - since nothing else is
legal in Roll & Reroll after the decision is made, this both prevents a
second reroll (a second call now fails with the ordinary "Expected
RollAndReroll step, was Main" guard - no new state needed) and removes a
redundant click, matching a specific ask: "you could auto-advance to the
Main step at that point." Two new unit tests: rerolling auto-advances to
Main, and a second `Reroll` call (even with an empty selection) throws.
Updated the "Reroll Selected" hint text and the How to Play modal's Roll
& Reroll description to match. Verified live: rerolling one die away
lands the status bar on `Step: Main` with no extra click.

**Global Abilities sidebar now shows each ability's text, not just its
cost.** Trimmed to the `"Global: ..."` clause of `rawText` when that
marker is present (so Falcon's Teamwatch clause doesn't clutter its
sidebar entry) via a new `globalAbilityText` helper in
`GlobalAbilitiesPanel.tsx`; falls back to the full `rawText` if the
marker's ever absent. No backend change - `CardDefDto` already sends the
full `rawText`.

Also answered two playtest questions without a code change: **Franklin's
Galactus's cost of 3** is `SampleCards.cs`'s `PlaceholderCost` fallback,
not real sheet data - only Big Barda, Harley Quinn, Robin, and Starfire
have been swapped for real Google Sheet stats so far; every other card
(Franklin's Galactus included) still uses `PlaceholderCost = 3` /
`PlaceholderEnergy = Mask`. **Whether there's a "no attacking on the
first turn" rule** - not modeled, and not confirmed either way; nothing
in this repo encodes it (`IsFirstTurn` only affects Clear & Draw's draw
count, rule 2.3.3), and there's no bundled rulebook text to check against
- needs the user to confirm the exact wording before it's worth adding.

Nothing engine-breaking here: 56 tests passing (55 + the 2 new reroll
tests, net +1 after a pre-existing test's expectations were folded into
the updated one); `dotnet build`/`test` and `npm run build` both clean.
