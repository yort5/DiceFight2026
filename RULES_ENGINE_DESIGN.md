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
1. ~~Keyword *behavior*~~ - see "Implemented keywords" below for the
   full alphabetical list (now including Range) with rule summaries and
   example cards; full citations and design rationale are in each
   keyword's own "Status update" section further down.
   BlackPanther's Energize is fully scripted; Robin's Energize
   (a purchase-cost discount) and all three Alfred Pennyworth printings'
   Ally effects (each a "Batman die OR Sidekick" compound target) are
   deliberately left unscripted - the former needs a purchase-cost-
   modifier mechanism, the latter an affiliation-based `TargetSpec`
   filter plus an either-of-two-specs union, neither built yet (note:
   Attune's own "target player or Character die" union IS now built -
   `TargetSpec.PlayersAllowed`/`CharacterDieOrPlayer` - but that's a
   fixed die-or-player choice, not the general N-arbitrary-specs union
   Alfred's text would need). The Rock's Global ("Sacrifice a Superstar
   die, reduce the next purchase by 2") is the same purchase-cost-
   modifier gap - left vanilla for now, see the Sacrifice status update.
   Next up, per the user's own framing: work through the rest of the
   Dice Masters keywords on wizkids.com/dicemasters/keywords one at a
   time, each scripted against a real example card (user- or
   randomly-picked from the stats sheet).
2. ~~Global ability UX~~ - done as a standing sidebar (`GlobalAbilitiesPanel`)
   with its own energy-then-targets flow; every Global on both rosters
   (Distraction, Falcon, Invisible Woman, Starfire) is scripted, and the
   three rough edges called out here are now addressed (see the status
   update) - still no legal-target filtering on the *targets* stage
   specifically (would need the server to expose real legal-target
   queries, not just whether a target exists at all).
3. Legal-target exclusions for captured dice / per-die "cannot be
   targeted" abilities - blocked on Capturing (rule 3.8) not being built.
4. Team-construction legality (die-limit-sum-to-20, unique-card-name
   checks, rule 2.1.1/2.1.3) - currently unenforced; every card gets its
   full die limit regardless of team size (see `TeamSetup.cs`'s remarks).
5. Auth/login in front of the API - currently wide open, matches the
   deployment note above.
6. Real priority-passing (turn player acts, passes to the non-turn
   player for one window, who may act-or-pass, turn ends when the
   non-turn player passes and the turn player then passes with nothing in
   between) - this engine currently just has an open, un-timed action
   window per step instead. A concrete symptom: `OncePerTurn` Global
   limiters like Falcon's "Once during your turn" are enforced as a flat
   once-per-turn-cycle limit usable by *either* player, not scoped to
   whoever's turn it actually reads as - see the status update where this
   was found. A real, separate architectural piece (new state, a "pass"
   action, every Main Step action needing to reason about whose window it
   is), not a one-line fix - deferred pending the user's prioritization.
7. Virtual generic energy currently only expires at Clean Up, not at the
   end of the Main Step where rule 1.4.5 ("must be spent by the end of
   the Main Step, or it will be lost") actually puts it - found during
   the full turn-sequence review (see the status update) and confirmed
   reachable: a player can bank virtual energy in the Main Step and still
   spend it on a Global during the Attack Step's Action/Global window
   today. Deliberately left as a known gap rather than fixed immediately
   (the user's call) - fix shape when picked up: purge
   `IsVirtualEnergy` dice for the active player in `EnterAttackStep` and
   `SkipAttackStep`, not just `CleanUp`.
8. ~~`TurnEngine.CleanUp` never clears `AppliedModifiers`~~ - fixed (see
   the status update): now demonstrably reachable (Wasp's real Attune
   buff), not just hypothetical, so no longer deferred.
9. ~~`GamesController`'s `/declare-attackers` endpoint has no
   `TargetDieIds`~~ - fixed for `/declare-attackers` specifically (see
   the Increment 6 status update): `DeclareAttackersRequest` now carries
   `TargetDieIds`, threaded into `Drain`, with a real web client flow
   (`DeclareAttackersPanel`) that asks for a target whenever a declared
   attacker has Call Out. The flat-single-target-list-per-batch
   limitation this item originally flagged is still real (unfixed) - it
   just doesn't matter yet since Black Widow is the only Call Out card
   on either roster. The `/clear-and-draw` half (Cosmic Cube/Rip
   Hunter's `WhenDrawn`/`ClearAndDraw` abilities) turned out not to need
   a `TargetDieIds` field at all - see the "pending mid-resolution
   choice" status update: the real blocker wasn't a missing request
   field, it was that the player can't answer this in the same request
   that triggers it (the candidates don't exist, or can't be seen, until
   the draw already happened). Closed via a general pause/resume
   mechanism instead (`GameState.PendingChoice`), not a `TargetDieIds`
   addition.
10. ~~Rip Hunter's "Navigate the Sands of Time"~~ - implemented (see the
    status update). Turned out to need a new `TriggerType.ClearAndDraw`
    rather than reusing `WhenDrawn` (the gate is "while active," not
    "while this specific die is drawn"), and the "once during your Clear
    and Draw Step" limiter needed no new state at all - the Step itself
    only runs once per turn. Its own post-draw choice is now answered
    through `GameState.PendingChoice` (see item #9 and the "pending
    mid-resolution choice" status update), same as Cosmic Cube's.
11. ~~The web client's Attack Step UI has no case for `AttackSubStep.
    InfiltrateWindow`, `TagOutWindow`, or `RangeWindow`~~ - fixed (see
    the Increment 2/3/4/5 status updates): `attackSubStep` is now a real
    `AttackSubStep` union type, and `InfiltrateWindowPanel`/
    `TagOutWindowPanel`/`RangeWindowPanel` all exist and are wired into
    `App.tsx`'s panel-swap chain via `canResolveInfiltrate`/
    `canResolveTagOut`/`canResolveRange`. Verified end-to-end in a real
    browser session for all three (Ricochet/Big E/Starfire "Starbolts").

## Implemented keywords

Every Appendix 1 keyword built so far, alphabetically. Each has its own
"Status update" section below (in build order) with full rule citations
and design rationale - this is just the scannable index.

- **Ally** (Alfred Pennyworth - all three printings) - a Character die
  with Ally counts as a Sidekick while in the Field/Attack Zone, in
  addition to its own attributes.
- **Amplify** (Ant-Man) - each time you use an Action die, spin every
  active Amplify die up one level (if able).
- **Attune** (Wasp) - while active, each time you use an Action die,
  deals 1 damage to a target player or Character die.
- **Awaken** (Cyclops) - fires once per die, every time an Awaken die
  spins up one or more levels, whatever caused it.
- **Call Out** (Black Widow) - when this die attacks, target an
  opposing Character die; only that die (or none) may legally block it.
- **Corrupt** (Polaris) - target player draws X dice from their bag,
  refilling from the Used Pile if needed, then a real choice of which
  one goes to the Used Pile - answered through `GameState.PendingChoice`
  (see its own status update), since the candidates don't exist until
  the draw actually happens mid-effect.
- **Darkseid's keyword grant** (Darkseid) - "while active, your
  Sidekicks gain Swarm" - not itself an Appendix 1 keyword, but the
  first live, continuously-recomputed grant (as opposed to a discrete
  triggered ability).
- **Deadly** (Deathbird) - a die engaged with a Deadly die is KO'd at
  Clean Up, regardless of what happened to either die in between.
- **Energize** (Black Panther, "Clutching Reality") - triggers once a
  die with this keyword ends Roll and Reroll on a double-energy face.
- **Energy Drain** (Madalyne Pryor) - after blockers are assigned,
  spins every Character die engaged with an Energy Drain die down a level.
- **Experience** (Jamilah "Shipwrecked on Chult," D&D set only) - if you
  KO'd an opposing Monster-affiliation die this turn, every active
  Experience card gets a permanent +1A/+1D token at Clean Up.
- **Fast** (Wasp Pixie) - deals its combat damage in an earlier wave
  than non-Fast dice, so a KO'd non-Fast target never gets to hit back.
- **Infiltrate** (The Spot; Ricochet reacts to it) - an unblocked
  attacker may remove itself from combat to deal 1 damage directly and
  return to the Field Zone.
- **Intimidate** (Scarlet Spider) - when fielded, removes a target
  opposing Character die from the Field Zone until end of turn.
- **Obscure** (Drow Mercenary) - using any Action die makes every die
  of this card unblockable until end of turn.
- **Overcrush** (Apocalypse) - if the attacker KOs or otherwise removes
  all its blockers, any leftover attack damage hits the opponent directly.
- **Range** (Starfire "Starbolts") - when a Range attacker attacks,
  every active Range die on both sides simultaneously deals its own
  damage to a target opposing Character die.
- **Regenerate** (Beast) - a die that would be KO'd rolls instead;
  landing on a character face saves it (back to the Field Zone, not the
  Attack Zone), an energy face doesn't.
- **Retaliation** (Superman "Kal-El"; Black Manta "Deep Sea Deviant"
  scales the amount) - if an active Retaliation character shares an
  affiliation with one of your Character dice that's KO'd, deal damage
  to your opponent.
- **Sacrifice** (Spidey's Last Stand; The Rock is cataloged but left
  vanilla) - moves a Character die from the Field Zone to Out of Play
  (owner's turn) or the Used Pile (otherwise), never counting as a KO.
- **Strike** (Bizarro) - the sole Character die you field this turn
  gets +2A/+2D and Overcrush for the rest of the turn.
- **Swarm** (Parademon) - while active, drawing another copy of that
  card during Clear and Draw pulls an extra die.
- **Tag Out** (Big E) - after blockers are declared, you may Prep a die
  sitting in the Field Zone to give a target Character die +2A/+2D
  until end of turn.
- **Teamwatch** (Falcon; Black Panther shares his real "Avengers"
  affiliation) - fielding a different, affiliation-matching Character
  die while a Teamwatch die is active triggers its ability.

Not Appendix 1 keywords, but bespoke card text built alongside this
work using the same infrastructure: Cosmic Cube's mid-Clear-and-Draw
redraw and Rip Hunter's "while active" Clear and Draw reaction (see
their own status updates) - both also answered through `GameState.
PendingChoice` for the same reason Corrupt is.

Found but deliberately not built: **Heist** (a real Basic Action card -
"target opponent draws 2 dice from their bag, place one in their Prep
Area, roll the other and place it in your Reserve Pool, at end of turn
place it in your opponent's Used Pile"). Same draw-then-choose shape as
Corrupt/RedrawFromBag (would reuse `PendingChoice`), but also moves a
die into an *opponent's* Reserve Pool under your control - cross-player
die placement/control isn't modeled anywhere in this engine yet (see
the "Not yet designed" note on `Controlling`/`Copying`/`Swapping` up
top), so this is real, separate, additional work, not a drop-in use of
what Corrupt/RedrawFromBag already built. Not authored in
`SampleCards.cs`.

Also done, out of order (the user asked for it explicitly once the above
was clear): a visual pass matching the physical mat's zone layout and
simple die-face icons - see the latest status update. Big Barda's
placeholder stats (and any other data cleanup like it) are still
deliberately deferred to a later batch, per the user's own call.

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

## Design log

The full chronological history of every session's work — what was
built, why, what broke, and how it got fixed — lives in
[`DESIGN_LOG.md`](DESIGN_LOG.md), split out separately so this doc stays
a quick orientation read. Each keyword's entry in "Implemented keywords"
above, and each next-steps item, points at a specific log entry by name
(e.g. "see the Call Out status update") — search `DESIGN_LOG.md` for
that heading text to find it.
