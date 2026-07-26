# Dice Masters Rules Engine — Design Notes (v0)

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

Only 6 of the 26 cards got a scripted `AbilityDef` (Dazzler, God Emperor
Doom, Groot, Cosmic Cube, Shocking Grasp, Casket of Ancient Winters) — the
rest are intentionally left vanilla (empty `Abilities`, real `RawText` +
`Keywords` retained) rather than simulating a partial/incorrect subset of
a more complex card's text. The scripting bar was: the *entire* ability
text maps onto existing primitives, nothing dropped.

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
