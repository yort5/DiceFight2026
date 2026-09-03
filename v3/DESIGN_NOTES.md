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

## Champion — corrected 2026-09-02: no die at all

**Superseded.** This doc previously described a Champion die (d6, 3
levels × 2 faces) mirroring a real Dice Masters character — that was an
unconfirmed assumption on the design-assistant's part, not something the
user had actually signed off on. Re-reading the original pitch: the
double-energy face was always described as living on the *Sidekick*
(now Tardigrade) die, never a separate Champion die.

**Corrected model: Champion has no die, is never fielded, and has no
cost of any kind.** It's a single passive, always-on team-wide effect —
the user's own example: "+1A/+1D to all your Claw dice." Nothing more.
This also resolves "Open / not yet decided"'s old anthem-vs-self-contained
question from earlier in this doc: it's the anthem/passive direction,
settled, not self-contained.

That reopens a real gap the old model quietly filled: something still
needs individual fielding/purchase costs for a team to be interesting
beyond flat Tardigrades. Filled with a new tier — **Character** — sitting
between Champion and Tardigrade:

| Tier | Die? | Cost | Ability |
|---|---|---|---|
| Champion | none | none — never fielded | one passive, team-wide, always on |
| Character | yes, own d6 | its own printed fielding + purchase cost, "if brought from DM" | none yet (deferred, same as everything else) |
| Tardigrade | yes, own d6 | **free to field** (corrected 2026-09-02, was 1) | none |

A team is Champion (flavor + passive) + some Characters (real individual
creatures, e.g. pulled from `CARD_INSPIRATION.md`) + Tardigrades (free
filler). Character roster size per team not decided — the prototype below
uses exactly one per side to keep scope small.

## Open / not yet decided

- Affiliation system specifics — how many groups, how team-building/drafting
  actually works with it.
- Character ability template pass — nothing sketched yet against the v2
  effect vocabulary (Champion doesn't need one — it's a single flat aura).
- Shell icon final pick (pending a family vote).
- How many Characters a team actually carries, and whether Champion +
  Character energy types must match (the prototype assumes yes).

## Real-engine build (2026-09-03)

Instinct Clash's throwaway `/alpha` prototype did its job (proved the
mechanics, surfaced the Champion-model mixup below) but doesn't run on
the real rules engine and doesn't look like a card game - both real
complaints. Plan: `~/.claude/plans/mellow-sparking-comet.md`. Status:
**Phases 1-3 done** - `TurnEngine.RerollOwn` (player-voluntary reroll,
a real gap: `Reroll` previously only existed as a card-triggered effect),
`ChampionDef`/`ChampionRegistry` (a Champion has no die at all -
`CardDef.Die` is non-nullable - so it's registered directly into
`GameState`'s existing modifier lists rather than through
`ContinuousRegistry`'s per-die gating), `Data/InstinctClashConfig.cs`
(the whole game as one real `GameConfig`: 4 energy types, the locked
Tardigrade die spec, 4 Champions, 8 simple-ability Characters), and a
parallel `api/v2/games` controller mirroring `GamesController.cs`'s shape.
Verified with a real HTTP smoke test end to end (not just unit tests) and
908 passing tests. Deliberately skipped for this pass: win condition/
deck-out (user call - casual playtesting won't reach it).

**Phase 4 done (2026-09-03): the web client.** New `/instinct-clash`
route, `web/src/instinct/` - dedicated components rather than reusing
`App.tsx`'s (those are typed against v1's exact `GameState`/`Die`
shapes), same architectural patterns: seat-token invite links, 2s
polling, pending-choice resolution. Verified against a real
locally-running server via headless Chromium with two browser tabs each
holding one seat, clicking a full turn through to combat damage - caught
two real bugs (a hardcoded-instead-of-real Character fielding cost, and
a tautologically-false render guard that hid the Assign Blockers panel
entirely). Not yet done: deploy (Phase 5) - this is pushed to `main` but
not yet confirmed live.

## Renamed to "Dice Kingdom" (2026-09-03)

"Instinct Clash" is retired as the product name - not sold on it, and it
didn't lead with "dice" the way the reference points (Dice Masters, Dice
Throne, Dicero) all do. New name: **Dice Kingdom**. Renamed everywhere
user-facing: the route (`/instinct-clash` → `/dice-kingdom`), the page
component and its directory (`web/src/instinct/` →
`web/src/dicekingdom/`, `InstinctClashPage.tsx` → `DiceKingdomPage.tsx`),
the CSS (`instinct.css` → `dicekingdom.css`, `.instinct` → `.dicekingdom`),
the seat-storage key and invite-link path, and all on-page text. Left
alone, deliberately: the C# engine's internal naming
(`Data/InstinctClashConfig.cs`, the `InstinctClashConfig` class, card IDs
like `IC-CLAW-01`, the `Set: "Instinct Clash"` field on each card) - none
of that is user-visible (the API's `CardDef` DTO doesn't even expose
`Set`), and renaming it is pure mechanical churn across ~30 call sites for
zero visible benefit. Same pattern as "DiceFight2026" itself being an
internal project name distinct from any game it hosts.

Other names considered and kept on hand to show other people later:
**Primal Dice**, **Dice Pack**, **Dice Reign** (also floated: Wild Dice,
Feral Dice, Alpha Dice, Dice Horde, Fang Dice, Dice Instinct).

Also fixed in this pass, both found by actually looking at the deployed
page rather than trusting the build:
- **The board was reading as "off"** because `instinct.css` had ported
  the `/alpha` prototype's own CSS almost verbatim (same class shapes,
  same tiny flat tiles) - so the "real engine" version looked like the
  throwaway prototype reskinned, which was the whole thing this build was
  supposed to fix. Redone with real hierarchy: Anton (bold condensed
  display face) for headings/names/stat numbers, larger card-shaped tiles
  with shadow and a per-player energy-color accent (each player's whole
  pool is one energy family, tied to their Champion), bigger icons.
- **Board order was backwards**: the active/"your" board was rendering
  on top, opponent below - opposite of every physical CCG and most
  mobile card games, where you sit at the bottom of your own view. Now
  the opponent's board renders first (top), yours last (bottom).

## Three more real bugs, found by actually playing it (2026-09-03)

After the rename/board-order fix went live, playing it surfaced three
more: dice were rolling in a section labelled "Prep Area" rather than
"Reserve Pool" (confusing - it's the same tray to the player the whole
turn, `PrepArea` is just v2's internal pre-`FinishRoll` staging zone, so
merged the two into one display), reroll only let you pick up and reroll
one die at a time instead of a batch (the physical rule: you shake
whichever of your own dice you want together; fixed to a select-then-
"Reroll Selected (N)" flow, one `api.reroll` call for the whole batch),
and the field/purchase/reroll/blockers panels all rendered at the very
top of the page, above the opponent's board - disconnected from the die
you'd just clicked on your own board at the bottom. Fixed by moving all
of them into one `.controlcenter` strip between the two boards, modeled
on where `/game`'s CombatLane sits between its two mats - a fixed,
predictable spot near whichever board is acting, rather than jumping to
the top. `/game`'s actual components (`PlayerBoard.tsx`, `ActionTray.tsx`)
were read directly for this - not reused (their `Selection`/`onGroupClick`
model is a bigger unification than these fixes needed), but the same
layout lesson was worth copying.

**Correction, same day**: that "bigger unification than needed" call was
wrong - playing it immediately surfaced the predictable result. The
reroll fix above kept its own separate clickable die list instead of
making the Reserve Pool tiles themselves the click targets, so the same
three dice rendered twice (once to look at, once to select) - exactly
the kind of bug a real shared selection model prevents by construction.
Rebuilt `DiceKingdomPage.tsx` on `/game`'s actual `Selection {primary,
secondary}` + `onGroupClick`-equivalent pattern for real this time: one
`toggleDie` feeding one `selection` state, board dice ARE the click
targets (Reserve Pool for reroll/field/purchase energy, Field Zone for
attackers), and one `selectionAction()` function computing the right
contextual button the way `ActionTray.tsx`'s `actions` array does,
instead of a separate ad-hoc widget per feature. `BlockPanel` was
adapted to read its attacker-pick off the same `selection` too. Kept
`/game`'s components themselves un-reused (still not type-compatible
without an adapter layer not worth building for v2's smaller shape), but
this time actually mirrored the architecture, not just the visual
layout. The "click through every sub-stage" feeling reported alongside
this wasn't literal auto-advance (checked - `/game` doesn't have that
either, every step is still its own explicit button); it was this same
duplication/instability making each step look unfamiliar. Watch whether
it still feels that way once the real fix is live.

## Prior prototype notes (superseded by the above for anything that conflicts)

### Playable prototype (2026-09-02, corrected same day)

**Public, on the deployed site: `/alpha`.** Also mirrored as a Claude
artifact for a private shareable link:
https://claude.ai/code/artifact/bb400774-5a15-4dcd-a586-c4ba64cf04bf
("Instinct Clash"). Same content, two hosts — the artifact is easiest for
this-session sharing, `/alpha` is easiest for "send anyone the link."

**`/alpha` reported broken 2026-09-03** — it was serving the React app
instead. Real bug, not a deploy fluke: `Program.cs` called
`UseDefaultFiles()`/`UseStaticFiles()` *after* `MapControllers()`, and
without an explicit `UseRouting()`, ASP.NET Core auto-inserts routing at
the very start of the pipeline the instant any `Map*()` call exists
anywhere — so `MapFallbackToFile`'s endpoint matched every extension-less
path (`/alpha`, `/alpha/`) before static files ever got a turn, and static
file middleware deliberately defers to an already-matched endpoint. Only
extension-less nested paths broke; `/alpha/index.html` worked the whole
time, and the site root only ever looked fine by coincidence (root's
fallback and root's real default file are the same `index.html`). Fixed
by adding an explicit `UseRouting()` after `UseStaticFiles()` — confirmed
against a minimal repro before touching the real file, then against the
actual built `wwwroot` (root, `/alpha`, `/alpha/`, an API route, a missing
path, the exception-handling middleware) and the full test suite (580
Engine + 10 Api tests, unchanged). Pushed; give the Cloud Build trigger a
few minutes to roll out before checking `/alpha` again.

Self-contained pass-and-play two-player game implementing the core loop
end to end: draw/roll/reroll, field/purchase (spending energy dice),
attack/block combat, KO, deck-out and life-total loss. Deliberately **zero
per-creature abilities** — only the single flat Champion passive exists —
so playtesting stays on the system, not any one card's power level.

Built independent of the real `DiceFight.V2` engine (plain client-side JS
served as a static file, not a `GameConfig`) — fastest path to something
anyone can open with zero setup. If v3 solidifies, porting this into an
actual `GameConfig` on the v2 spine is the natural next step.

**Reflects the Champion correction above**: Champion is flavor + one flat
passive only, never a die. Each team carries one **Character** (own
fielding/purchase cost, stats pulled straight from `CARD_INSPIRATION.md`)
— Honey Badger (Claw), Hippopotamus (Shell), Osprey (Wing), Barn Owl
(Eye) — plus 4 Tardigrades, now correctly **free to field** (was 1).

**Numbers invented to make it playable, none locked:** draw 4/turn, 20
life, Character die-limit 4 (1 starting + 3 purchasable), and one flat
passive per Champion — Lion (Claw): +1 ATK to all your dice; Armadillo
(Shell): +1 DEF; Golden Eagle (Wing): fielding costs −1 (min 0); Great
Horned Owl (Eye): Character purchases cost −1 (min 1). Four different
mechanisms deliberately, to see which flavor of "always-on edge" people
actually notice turn to turn. Champion/Character/Tardigrade animals were
all picked distinct from each other and from the 45 `CARD_INSPIRATION.md`
picks, so nothing in the roster reads as a placeholder for something
already designed.

**Now actually verified**, not just traced: no `claude-in-chrome` in this
environment, but headless Chromium via Playwright works once its shared
libraries are supplied manually (`~/.cache/ms-playwright` and the .NET SDK
persist across sessions; the extracted `.deb` libraries don't — redo the
`apt-get download` + `dpkg-deb -x` step each session, recipe kept in the
`dicefight2026-dev-environment` memory). Ran a full click-through against
the actual deployed file (`web/public/alpha/index.html`, not just the
artifact copy) with screenshots at every step: champion picker, free
Tardigrade fielding (instant, no prompt), paid Character fielding (correct
cost, correct dice consumed), a purchase attempt with insufficient energy
(correctly detected and backed out instead of getting stuck), combat
resolution (correct damage math), and turn handoff (board state persists
correctly). Zero console errors. Earlier passes were manual code traces
only and caught two real bugs that way (payment-selection priority, a
stale block-selection reference) plus a third fix this pass (a
zero-energy die was selectable as "payment," silently wasting it) — this
verification ran *after* all three fixes, on the corrected code.

See `PARKED_IDEAS.md` for a separate list of unvetted brainstorm items —
mechanics ideas offered in passing, not yet weighed against any of the
above. See `CARD_INSPIRATION.md` for a sourcing pass through the DPS
catalog: ~50 buildable cards sorted into the four energy types, each
reskinned to an animal with a symbol idea, plus an Affiliation naming/tag
menu.
