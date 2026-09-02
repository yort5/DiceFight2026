# V3 — New Game Design Notes

Status: early brainstorming (started 2026-09-02). Nothing implemented yet —
this is a running record of decisions made during design conversations, kept
separate from v1/v2's Dice-Masters-fidelity engine. See `../ARCHITECTURE_REVIEW.md`
for how this relates architecturally: v2 was built along Direction B, and this
is Direction C (a new iteration of the game) made concrete.

## Premise

- Mobile app. Animal-kingdom theme instead of a licensed IP, to sidestep
  licensing issues.
- Takes the spirit of Dice Masters (dice-based team battler) but simplified
  and evolved, borrowing ideas from other games — Forest Shuffle's
  subcategory tagging (Butterflies, Pawed Animals, non-animal groups like
  Trees or Rocks), Dice Throne's Champion concept.
- v2's engine already treats dice/energy/rules as data, so this is
  mechanically buildable on the existing spine once the game design settles.

## Two axes, deliberately kept orthogonal

- **Energy** — small, closed, mechanical resource type. Named after animal
  *traits/instincts*, not species, specifically so it never collides with
  Affiliation naming.
- **Affiliation** — open-ended taxonomic/thematic tag (Mammal, Bird, Fish,
  Butterfly, Pawed, Tree, Rock, ...) used for deckbuilding synergy. Not
  designed in depth yet.

## Energy types (launch set, locked 2026-09-02)

| Energy | Icon idea | Archetype |
|---|---|---|
| Claw | claw-mark slash | Aggro / damage |
| Shell | still iterating | Defense / life |
| Wing | swept, notched feather | Tempo / evasion |
| Eye | almond + slit pupil | Control / trickery / info |

Bench (reserved for a later expansion, not in the launch set): **Fang**
(DoT/debuff — dropped for feeling too similar to Claw), **Horn**
(burst/charge), **Venom** (control/KO-condition).

Icon exploration artifact:
https://claude.ai/code/artifact/956bc357-50d0-4e80-b91d-ffe6ab6b18d8 —
Claw/Wing/Eye settled-ish; Shell still under review (the seamed-hexagon
concept read as "an empty box"; alternates being tried: domed tortoise
shell, spiral coil, banded/armadillo plates — pending a family vote).

Because a die only ever shows *its own* energy type plus Wild (never a
sampling of every type), the total energy-type count isn't constrained by
physical die face counts (d6/d8/etc.) and could grow well past 6 without any
die redesign. The one place this could resurface: if a shared "Basic
Action"-style die (drafted independent of any Champion, the real-DM
precedent) gets added later, since that die's whole point would be sampling
across colors.

## Terminology

- **Champion** — Dice-Throne-style hero unit, one per energy type to start.
- **Tardigrade** — the Sidekick-equivalent generic/basic die. Name locked in
  for now, cheap to rename later. Bonus: tardigrade biology (cryptobiosis,
  extremophile toughness) gives natural flavor hooks for the basic-creature
  line's mechanics (see the die spec below).
- Single flat rules-noun for the generic unit, no per-affiliation flavor
  synonyms — explicitly modeled on the lesson that Dice Masters' attempts at
  reskinned Sidekick names (NPC for a D&D-themed set, Superstar for a WWE
  one) never stuck; players always just said "Sidekick" regardless of theme.

## Face design: hybrid faces are allowed, not required

A single die face *can* carry both character stats and energy symbols at
once. Checked against the v2 engine and confirmed to be a non-issue:
`Face` already models `Symbols` and `Character` as independent fields;
`SpendEnergy` only reads symbols off dice still sitting, unspent, in the
Reserve Pool, and `Field()` only checks `Character != null` — the two
consumers never collide, so a hybrid face can't be double-dipped (spent for
energy *and* fielded as a body from the same roll).

Real physical Dice Masters keeps character and energy faces strictly
separate — verified against `../src/DiceFight.V2/Data/MigrationDice.cs`,
where every migrated character face has an empty `Symbols` list. So this is
a deliberate v3 departure, not something inherited from v1/v2. Reasoning:
it removes the "dead roll" feel-bad from the physical game, where landing on
a character face you can't or won't field that turn contributes nothing at
all that turn.

## Tardigrade die

Basic creature, bundled per-Champion (4 dice per Champion, matching its
energy type). d6, deliberately uneven distribution — 3 faces × 2 copies
"feels like a d3," so the top tier was split into two true extremes instead:

| Face | ATK/DEF | Energy | Copies |
|---|---|---|---|
| L1 | 0/1 | 2 of own type | 2 |
| L2 | 1/1 | 1 of own type | 2 |
| Bulwark | 1/3 | — | 1 |
| Surge | — (no character face) | 2 Wild | 1 |

- ATK/DEF axis was flipped from an initial 1/0 → 1/1 → 1/2 draft: 0 Defense
  reads as "dead on arrival" to Dice-Masters-literate players (the target
  early feedback audience, since early playtesting will lean on DM veterans
  first). 0 now sits on Attack instead, never on Defense.
- Deliberately flat across energy types: every Tardigrade line shares the
  exact same stat progression regardless of which energy it produces — all
  the differentiation lives in "which type," none in "is this type's filler
  better than another's."
- Surge's Wild pips (1-in-6) are the mechanism for guaranteeing every team
  some baseline access to energy types (and Global-style triggers) it didn't
  build around — chosen over literally cycling all energy types across one
  die, both because Wild is already fully supported by the engine's
  `SpendEnergy` logic (satisfies any purchase-type requirement) and because
  it doesn't need reworking as the energy-type count grows.
- Bulwark/Surge are two extremes, not a continuation of the L1→L2 level
  curve — worth naming them something other than "L3" if this sticks.

## Champion die

d6, 3 levels × 2 faces, same skeleton as a real Dice Masters character die.
Only the level-1 faces produce energy (2 of the Champion's own type) —
mirrors the Tardigrade pattern where the weak tier is the economy tier and
the strong tiers are the payoff, and gives an in-fiction reason to field
early instead of only stockpiling.

## Open / not yet decided

- Affiliation system specifics — how many groups, how team-building/drafting
  actually works with it.
- Champion ability template pass — nothing sketched yet against the v2
  effect vocabulary.
- Shell icon final pick (pending a family vote).
- Whether a Champion should also carry any team-wide passive/anthem effect
  (a "lord" style buff to its own Tardigrades) vs. staying fully
  self-contained — raised, not resolved.

See `PARKED_IDEAS.md` for a separate list of unvetted brainstorm items —
mechanics ideas offered in passing, not yet weighed against any of the
above.
