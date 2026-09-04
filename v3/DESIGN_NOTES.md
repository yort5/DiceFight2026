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

**Second correction, same day**: still not far enough. Direct question
asked back: "is there a reason not to just start [from v1] and then
change things?" No - checked `Zone.cs` and there isn't one. v2 uses
essentially v1's exact zone set (`Bag`, `PrepArea`, `ReservePool`,
`FieldZone`, `AttackZone`, `UsedPile`, `OutOfPlay`, `Unpurchased` - by
the file's own comment, "same as v1"), and this page was only ever
rendering 3 of those 8. Spent energy and KO'd/unblocked-attacker dice go
to `UsedPile` same as always - the page just never showed that zone, so
they looked deleted. Added Attack Zone, Used Pile, Out of Play, and Bag
to the board (Bag in particular closes the loop on "will Prep Area
reappear" - no, it stays merged into Reserve Pool display since v2
doesn't route anything through the interrupt-window split v1 needed it
for, but Bag/UsedPile/OutOfPlay are real, always-shown zones now, so
nothing a die does is ever invisible). Pattern for next time: when in
doubt about whether v1 already solved something this page is
reinventing, check what v1 actually accounts for before shipping a
smaller version and finding the gap live.

**Third correction, same day** - this time the fix, not just the excuse,
came from actually porting v1's logic. Two more real bugs: (1) a
KO'd/spent Tardigrade still showed its last rolled face ("1/4 L3 FREE")
sitting in the Used Pile - `DieTile` was gating "show a face" on the
die's raw `effectiveAttack` field rather than on which zone it's
currently in, so a stale face value never got hidden. Ported v1's actual
`ROLLED_ZONES` set (`ReservePool`/`PrepArea`/`FieldZone`/`AttackZone`
only) from `PlayerBoard.tsx` - a tile now only shows a face at all when
its zone is one of those, regardless of what the DTO still carries. (2)
Used Pile/Out of Play/Bag were one merged row with no way to tell which
die was in which - split back into three real `.zone` sections. Also
ported `../dieHelpers.ts`'s `groupDice` (adapted for v2's Die shape,
same collapse-when-not-a-rolled-zone rule) so four identical unrolled
Tardigrades read as one "Tardigrade ×4" tile instead of four separate
identical-looking ones - v1 already had this, Dice Kingdom never did.

Separately, this pass also fixed a real numbers bug the zone work made
visible: `GameSetup.SeedTeamDice` (shared v2-engine code, correctly
mirroring v1's actual "every card's dice sit Unpurchased until bought")
put ALL 4 copies of both of a player's Characters in Unpurchased - zero
starting owned, contradicting this file's own locked-ish spec ("Character
die-limit 4 (1 starting + 3 purchasable)"). Fixed in
`V2GamesController.Create` (not the shared engine method, since that
"buy everything" behavior is correct for classic Dice Masters): one copy
per Character now starts moved into Bag. A starting Character die is
shuffled into the same Bag as the Tardigrades, not held in a separate
starting hand - can be drawn turn one same as anything else.

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

## Ported from /game: auto-skip a no-op combat (2026-09-04)

Same fix as `/game`'s (DESIGN_LOG.md, 2026-09-03): `CombatEngine.
DeclareAttackers`/`DeclareBlockers` unconditionally enter `AssignBlockers`/
`ActionGlobalWindow` regardless of attacker/blocker count (lines 56/89) -
on purpose, that window exists independent of whether anyone attacked -
so a new effect in `DiceKingdomPage.tsx` auto-submits the empty answer
client-side instead, via the same `runQuiet` pattern (swallows the
expected 403 from whichever seat's browser doesn't hold the token this
particular auto-skip needed).

**A real bug found while wiring this up, not introduced by it, and NOT
fixed here**: `AssignCombatDamage` requires the caller to resupply the
exact block assignment (`assignment.BlockersOf(attacker.Id)` - see
`CombatEngine.cs` line 180) because `CombatAssignment` is never stored
server-side (`V2GameSession` only carries `PendingQueue`, no equivalent
field - confirmed by reading the class, not guessed). The existing
"Resolve Combat" button already read this from purely local React state
(`blockAssignments`) populated only by the DEFENDER's own `BlockPanel`
clicks - in a real two-browser game, the ATTACKER's browser is a
separate React tree that never sees those clicks, so its own
`blockAssignments` stays empty regardless of what was actually blocked.
The auto-skip fix above only fires when that same local state is
*already* empty, so it doesn't make this worse - but a real block, in a
real two-browser game, would submit as if unblocked today, in both v1
and v2. Same shape as the fix v1's own multiplayer stage 3 (the Range
handshake) already solved once: hold the pending assignment in the API
session layer, not client state, so the second party's request is what
actually carries it forward. Flagged for the user rather than fixed in
this pass - it touches both engines' API layers and deserves its own
session, not a bolt-on to an already-long one.

## Ported from /game: the whose-turn color cue, plus a real bug it exposed (2026-09-04)

Same green (your move)/amber-grey (waiting, never red) cue as `/game`'s
(DESIGN_LOG.md, 2026-09-03), applied to the lifebox, the phasepill, and
the acting player's whole board (a bonus of the CSS shape - `.turn-mine`/
`.turn-waiting` as bare classes style whatever carries them, not just
the lifebox they were written for, so the outer board wrapper picks up
the same outline for free).

**Found while wiring it up**: the old inline lifebox highlight only ever
checked `game.activePlayerId === game.playerOne.id` - playerTwo's box
had no active-state styling at all, in either color. New `LifeBox`
component takes `activePlayerId` generically and is used for both, so
this can't recur by construction (one component, not two copies of the
same inline JSX to keep in sync).

908 tests pass - CSS/component changes only.

## Real structural layout fix: a rail, not just isolated CSS patches (2026-09-04)

Direct pushback, and fair: the last several rounds ported /game's CSS
fixes without ever adopting its actual STRUCTURE - Dice Kingdom stayed a
single narrow column, "Draw" sitting in the `.controlcenter` strip
between the two boards read as sitting in the middle of the board
itself, and Champion sat inline ahead of each board's Field Zone, which
(direct feedback) can't work anyway since Field Zone and the Attack Zone
need to line up directly across from each other.

Adopted /game's real two-column shape this time
(`.app-layout.game-layout`'s main-column + side-column), not just its
colors: new `.dk-layout` grid (`minmax(0,1fr) 280px`), `.dk-main`
holding just the two boards' die zones, `.dk-rail` holding both
`ChampBanner`s and everything that used to be `.controlcenter` -
pendingChoice, BlockPanel, Resolve Combat, and the whole turn-control
actionrow (Draw/Roll/Field/Purchase/Attack/End Turn). `ChampBanner`
extracted as its own component (was inline JSX inside `renderBoard`,
duplicated per board) and widened `.dicekingdom` to 1100px to give the
rail room - was 940px, sized for a single narrow column.

Verified with headless Chromium: `.dk-rail` holds both Champion banners
and zero live inside either `.playerboard`, `Draw`/`Roll` render inside
the rail rather than between the boards, zero console errors through
Draw. 908 tests pass - CSS/component-position only.

**Known follow-up, not done here**: the die zones themselves don't yet
use the wider main column - `.dierow` still wraps at its old narrow
width, leaving visible blank space to the right of each zone at 1100px.
Same category of gap as the sideboard/ribbon rounds on `/game` - the
structural piece (rail exists, holds the right things) is real and
verified; using the reclaimed width well is a separate pass.

## Mirrored mats + a real shared Attack Zone (2026-09-04)

Direct, blunt feedback after the rail move: "the rest still looks
nothing like Dice Fight. For one, the play areas are not mirrored...
They need to face each other. There is no attack zone. Used Pile and
Out of Play are taking up vertical spaces, when they should be on
either side of the Reserve Pool. Just. Make it. Like. Dice Fight." Every
one of those is a real gap this session's earlier rounds never actually
closed - the "mat" restructuring for /game (the round that fixed its own
`.mat`/`.mat.mirrored` grid) never got ported to Dice Kingdom at all;
`renderBoard` was still one linear stack of `<div className="zone">`s in
source order, `mirrored` wasn't even a parameter.

Ported /game's real `.mat`/`.mat.mirrored` CSS grid verbatim (App.css
lines ~932-994), sized down to Dice Kingdom's smaller zone set (no
Prep/Intimidated/staging columns): `grid-template-areas: "field field
field" / "used reserve outofplay" / "bag bag bag"`, with `.mat.mirrored`
reversing the row order so Field sits at whichever edge is closest to
the opposing mat. `renderBoard(playerId, mirrored)` now wraps its five
zones in `<div className="mat-slot mat-X">` cells instead of one flat
list. Pulled the old per-player "Attack Zone" block out entirely -
Dice Fight's Attack Zone was never a per-player thing, it's the shared
lane both players' attackers land in - and added a new
`renderAttackZone()` (reads `game.dice.filter(d => d.zone ===
"AttackZone")` across BOTH players, returns null when empty) rendered
once, between the two `renderBoard()` calls in `.dk-main`
(`renderBoard(opponentId, true)`, `renderAttackZone()`,
`renderBoard(you, false)`) - a real shared combat lane between the two
mats, not a slot glued to whichever board rendered it.

Verified with headless Chromium (two browser contexts): measured actual
`getBoundingClientRect()` positions, not just eyeballed a screenshot -
opponent's Field Zone sits directly above "your" Field Zone (adjacent,
facing each other, confirmed by comparing `.mat-field`'s top offset on
both mats), Used Pile/Out of Play sit left/right of Reserve Pool on both
mats (confirmed by `.mat-used`/`.mat-outofplay` left-offsets flanking
`.mat-reserve`'s), and `.attackzone` is absent on an empty turn 1
(correct - nothing to show yet) but renders with the attacker's tile
once a die is actually pushed into AttackZone via a real Draw -> Roll ->
Field -> Proceed to Attack -> Confirm Attackers sequence. 908 tests
pass - CSS/component-structure only, no engine or API changes.

## Real reuse of v1's combat lane and 3D die cube, not another re-derivation (2026-09-04)

Direct feedback on the mirrored-mats round, and the sharpest version yet
of a complaint that's recurred all session: "I didn't see a visual
divider in the attack zone... In Dice Fight we had one that was blue on
one side and orange on the other. And might as well use the same die
visualization (and animation, since we have it) here as well... Can we
also get some borders around the different zones? In fact, maybe just
tear down this whole thing, COPY THE ENTIRE DANG UI from Dice Fight, and
then make it work with the Dice Kingdom back end? Because we are still
just incrementing every time." Right diagnosis: the flat attacker list
`renderAttackZone()` shipped last round was a re-derivation of what
../CombatLane.tsx already solved (attacker/blocker columns either side of
a divider seam), and every rolled-zone die on the mat was a plain text
badge, never the real 3D cube ../DieCube.tsx already draws with a working
roll animation.

Literal byte-for-byte reuse of v1's components turned out not to be
possible without also changing the C# API - checked directly by diffing
v1's `Die`/`CardDef` (types.ts) against `dicekingdom/types.ts`'s V2
counterpart, and they're a real, deliberate difference (V2Dtos.cs's own
remarks), not just drift: v1's `Die` needs a `status` field
(Energy/Character/SidekickCharacter/Action) plus a per-level card lookup
to know what it's showing; V2's `Die` already carries the true,
modifier-inclusive `effectiveAttack`/`effectiveDefense` and
`energySymbolId` directly, and `isTardigrade` instead of a status
string. So the actual move, and the honest reading of "copy the whole
UI": port v1's real rendering components into `dicekingdom/` and adapt
their internals to V2's simpler shape, rather than either re-deriving a
third version from scratch again or rewriting the engine's DTOs to force
an exact match (out of scope, much bigger, not what was asked).

New files, each a direct port of its `../` counterpart:
- `dieFaces.ts` - the six-face table for the 3D cube. Genuinely simpler
  to build than v1's copy since V2 already resolves the true stats
  server-side; the one real content piece is Tardigrade's locked spec
  from `InstinctClashConfig.cs`'s `TardigradeDie` (two L1 0A/1D, two L2
  1A/1D, one L3 "Bulwark" 1A/3D, one "Surge" - a pure Wild-energy face,
  no character stats at all), which isn't derived from a CardDef at all
  since Tardigrade dice have no cardId.
- `DieCube.tsx` - identical geometry/animation to v1's; the only real
  change is `faceIcon()` resolving one of Dice Kingdom's own SVG
  components (ClawIcon/ShellIcon/WingIcon/EyeIcon, plus a new WildIcon
  in `icons.tsx` for Surge) instead of v1's `<img src>` GameIcon.
- `dieHelpers.ts` - just `dieLabel`/`characterFaceInfo`, trimmed to what
  `CombatLane.tsx` needs (no per-level lookup - straight from the die).
- `CombatLane.tsx` - the real attacker/blocker lane with the blue-to-
  orange divider seam, ported near-verbatim. The one simplification: v3
  has no gang-blocking (`BlockPanel` assigns at most one blocker per
  attacker), so `assignments` is always zero or one blocker per
  engagement rather than v1's arbitrary-many.

`renderAttackZone()` now renders `<CombatLane>` (fed the same
`blockAssignments` state `BlockPanel` already builds) instead of a flat
die list. `DieTile` now renders a real `<DieCube>` for anything in a
rolled zone (Field/Reserve/Attack) instead of a flat stat badge -
piles (Bag/Used Pile/Out of Play) stay flat chips, matching v1's own
ROLLED_ZONES-gated distinction. Added zone-tint borders
(`.zone-field`/`.zone-reserve`/`.zone-used`/`.zone-bag`/`.zone-outofplay`)
recolored onto Dice Kingdom's own palette rather than reusing v1's
red/blue faction hues verbatim, plus the full `.combat-lane`/`.lane-*`/
`.die-cube*` CSS ported from App.css.

Verified with headless Chromium, two real browser contexts holding
opposite seats (not one tab pretending to be both - this session's
standing lesson about `DeclareBlockers`-shaped actions): A fielded and
attacked with a Tardigrade, B's browser showed `.combat-lane`/
`.lane-seam` with a real `<DieCube>` for the attacker, B confirmed an
empty block ("Confirm Blocks" with nothing assigned), and the unblocked
1 damage landed on B's life total (20 -> 19) after both browsers' polls
caught up - confirms the visualization isn't just decorative, it's
reading the same real combat state the engine resolved. One test-script
bug caught and fixed along the way, not an app bug: reloading a joined
browser loses its already-consumed one-time invite URL (the app has no
resume-from-sessionStorage-alone path), so the verification script polls
instead of reloading. 908 tests pass - CSS/component changes only.

**Deliberately left alone this round**: the rail's turn/action controls
(`TurnRail`-equivalent inline logic), `BlockPanel`, the pending-choice
chips - these already work and weren't part of the complaint. "Copy the
entire UI" was scoped to what was actually broken (the divider, the die
visualization, zone borders), not a wholesale rewrite of working,
already-verified control flow.

## Server-side fix: stat modifiers only apply in play, and a real screenshot-driven layout pass (2026-09-04)

Two things landed together, both from the same message. First, a real
correctness bug the user caught by reading a screenshot closely: a
Reserve Pool die was showing `effectiveAttack`/`effectiveDefense` with
Champion passives already baked in, before the die was ever fielded.
"We probably don't want the computed attack stats to show up on the die
when it's in the Reserve Pool... applied and static buffs are not a
guarantee - the Champion's ability could get turned off, or another
boost it may be getting from a character could disappear when that
character is KO'd. I should be making the choice to field or not based
on the die's _actual_ stats." Checked `V2Dtos.cs`'s `V2DieDto.From` and
confirmed it: `QueryEngine.GetAttack`/`GetDefense` (continuous modifiers
included) ran unconditionally for any die showing a character face,
regardless of zone - a real bug, not a documented design choice; nothing
in Phase 1's Champion plan called for modifiers to apply outside of
play. Fixed by gating on zone: `FieldZone`/`AttackZone` (in play) get
`QueryEngine.GetAttack`/`GetDefense` (modifiers included); every other
zone gets `QueryEngine.GetBaseAttack`/`GetBaseDefense` (the printed
face value plus only one-shot `AppliedModifiers`, no continuous team
auras). Verified directly against the wire data (not the DOM): a rolled
L2 Tardigrade sitting in the Reserve Pool now reports 1/1 (its true
printed stat) under a Lion (+1 ATK) Champion, and reports 2/1 only once
actually fielded into the Field Zone. 908 tests pass - this call site
had no existing test coverage asserting the old (wrong) behavior.

**Recorded, not built this round**: a hover/tap breakdown of WHERE a
buff or debuff on an in-play die's stat is coming from ("nothing more
frustrating than wondering why a die you thought had 8 attack suddenly
has only 3"). Real, worth doing, but `IDieStatModifier`/`StatAura` etc.
carry no source/label today (`AppliesTo`/`GetDelta` only) - needs a
modifier-breakdown API shape, not a client-side guess. Next session.

Second: the user pasted an actual screenshot of `/game` and said,
bluntly, "make it look like this" - the sharpest, most concrete version
yet of a complaint that ran through the whole session. Direct structural
gaps checked against the screenshot and fixed:
- No step ribbon at all -> ported `StepRibbon.tsx` (`dicekingdom/
  StepRibbon.tsx`), step ids grouped since V2's `currentStepId` is
  finer-grained than v1's `currentStep` (three real ids inside Attack).
- Title/description/How-to-play/a full-width life+phase topbar sat above
  the board, permanently - none of that exists once a v1 game is live
  (title/nav lives in a slim header outside the game view entirely, How
  to Play is a modal-opening button). Removed the title block from the
  live view; `.how` is now a small collapsed toggle in a `.dk-titlebar`
  row next to the ribbon, not a full-width panel.
- Life totals moved off the table into the rail: `.active-line` (Active
  + Invite, one line) then `.life-panels` (a real 2-column grid, ported
  from `TurnRail.tsx`), both above the `ChampBanner`s (kept - v3-only,
  v1 has no Champion concept so the screenshot has nothing to compare
  against here).
- The mat grid was still a scaled-down 5-zone version, not the real
  thing: Out of Play was flanking Reserve Pool as its own column
  (direct feedback: "Out of Play is on the Right side") when v1 actually
  stacks it in the LEFT column with Used Pile/Bag; Prep Area didn't
  exist as its own zone at all ("No Prep Area at all") because an
  earlier round had merged it into Reserve Pool display for being
  "confusing to show separately" - reverted per direct instruction, it's
  its own zone now, matching v1's real 9-zone grid (`field`/`used`/
  `reserve`/`prep`/`outofplay`/`bag`/`drawn`/`carried`, minus
  `intimidated` - v3 has no such rule, so that grid cell is just `.`,
  explicitly empty CSS grid syntax, not a fabricated zone).
- The combat lane only rendered when an attacker existed ("Still no
  Attack Zone" - it disappeared entirely most of the turn). Now always
  renders, matching v1: `CombatLane` itself draws three "no blocker"/
  "open slot" placeholder columns when nothing's declared.

Verified with headless Chromium: `.mat-used`/`.mat-outofplay` share a
left-edge x-offset (same column) rather than flanking `.mat-reserve`,
`.mat-prep` sits right of Reserve Pool, `.combat-lane` is present on a
fresh turn 1 with zero attackers, `.step-ribbon` and no `<h1>` in the
live view. 908 tests pass - CSS/component-structure only besides the
one C# fix above.

## Three real gaps in the previous round, fixed - plus a real V2 log (2026-09-04)

Same message, three more concrete things checked against the screenshot,
none of them excuses: "There's a big white space at the top... is that
where the character roster should be? The right pane only has a button,
not the full step description. There's no log."

1. **The white space was exactly that** - the Unpurchased roster/
   purchase panel only rendered `isYourTurn && playerId === you && step
   === "main"`, so it was blank for the opponent's board always and for
   your own board most of the turn. Ported v1's real behavior: `<details
   className="roster" open>` always renders for both boards (matching
   `../PlayerBoard.tsx`'s own roster exactly), showing every unpurchased
   card regardless of whose turn it is; only the Buy button's
   `clickable` state is still gated to "your board, your turn, Main
   step" - the list itself is always there to check.
2. **The bare button** - `controlcenter` went straight from the
   pendingChoice/BlockPanel ternary into plain actionrow buttons with no
   step context at all, unlike `../TurnRail.tsx`'s Now panel (title +
   one-line guidance). Added a `STEP_GUIDANCE` map (mirrors
   `TurnRail.tsx`'s own STEP_GUIDANCE/ATTACK_SUB_STEPS text, collapsed
   to V2's coarser step set) and a `.now-header` block that renders
   above whichever contextual panel is active, always, not tied to one
   ternary branch.
3. **No log** - a real, structural gap, not a display oversight: V2's
   `GameState` had no `Log` field at all, `TurnEngine.cs`/
   `CombatEngine.cs` never called anything like v1's `LogEvent`, and
   `V2Dtos.cs` had nothing to expose. Added the whole path, mirroring
   v1's shape exactly: `Model/GameLogEntry.cs` (V2's own copy - can't
   reuse v1's record, different namespace/type even though identical
   shape), `GameState.Log`/`LogEvent`/`NameOf` (same 200-entry cap), a
   `V2GameLogEntryDto`, and `Log` on `V2GameStateDto`. Call sites added
   at the real action boundaries - not v1's full per-die-roll
   granularity, but every player-visible thing: draw count, roll,
   reroll (or "takes no reroll"), purchase, field, declare attackers,
   declare blockers, unblocked damage landing on the player directly,
   KO, end turn. `dicekingdom/MatchLog.tsx` is a straight port of
   `../MatchLog.tsx`, wired into the rail below `.controlcenter`.

Verified end to end with headless Chromium, not just that each piece
renders: fielded a Tardigrade and read back `.roster[open]` (both
boards), `.now-title`/`.now-guidance` text, and the live log producing
real lines ("Lion draws 3 dice.", "Lion rolls.", "Lion fields a
Tardigrade.") - confirms the log is reading real engine events, not a
static string. 908 tests pass - the new `LogEvent` call sites had no
prior test coverage asserting their absence.

## Roster mirroring bug (again), compact roster chips, and a real theme override (2026-09-04)

"Oh for heaven's sake... The unpurchased roster for the opponent is now
in the middle of the dang play area between field zone and attack zone
again. Any why are they so big? ... Still the huge gap on the top. At
the very least can we go with the dark theme?" Diagnosed by measuring
the live page rather than guessing again (`.roster`'s `getBoundingClientRect()`
against `.mat`'s and `.combat-lane`'s): confirmed all three.

1. **Roster position** - the exact bug class this session already hit
   and fixed once for `/game`'s own roster (`DESIGN_LOG.md`): rendered
   as a fixed mat-then-roster JSX sequence regardless of `mirrored`, so
   it always landed BELOW the mat - which for the mirrored (opponent)
   board is the edge next to Field Zone and the shared Attack Zone
   (mirrored rows run the opposite order), not the outer edge away from
   it. `/game` fixed this with CSS `order` (its DOM order is fixed,
   flex `order` repositions per `mirrored`); Dice Kingdom's `renderBoard`
   fully controls its own JSX, so the actual fix is simpler - swap which
   literally renders first, `roster` then `mat` when `mirrored`, `mat`
   then `roster` otherwise.
2. **Size** - the roster reused the same `.dietile` component as an
   in-play die on the mat (94×166px stacked card), when it's reference
   content glanced at constantly, not the focal point a rolled die is.
   New `.roster-chip` - a single-row compact pill (icon, name, cost,
   ×left) - replaces it; confirmed via measurement, 166px tall down to
   28px.
3. **The gap at top** - not a separate bug, the same one: with the
   roster correctly repositioned to the outer edge AND shrunk to a
   compact row, the opponent's board now opens with a small roster row
   immediately below the ribbon instead of the mat's mostly-empty
   Bag/Drawn/Carried row filling that space alone.
4. **Theme** - a real accessibility complaint ("this light, faded stuff
   is horrible for people who don't do well with colors"), not a
   preference one, and the site had no way to get dark mode besides the
   OS setting. Added an explicit three-state theme (`system`/`light`/
   `dark`) to `index.css` and `dicekingdom.css`: every existing
   `@media (prefers-color-scheme: dark)` block gained a
   `:root:not([data-theme="light"])` guard (system dark still applies
   unless overridden to light) plus a parallel `:root[data-theme="dark"]`
   block (wins regardless of system preference). New `ThemeToggle.tsx`
   exports `useTheme()` (persists to `localStorage`, applies the
   `data-theme` attribute) and a toggle button. Real bug caught in this
   same pass: the toggle button only lived inside the live-game view, so
   applying dark then reloading straight back to the pre-game setup
   screen (where nothing was mounted to reapply the attribute) silently
   reverted to system - fixed by calling `useTheme()` unconditionally at
   the top of `DiceKingdomPage`, before either screen's early return,
   confirmed by reading `data-theme` back after an actual page reload
   (not just after the toggle click).

Verified with headless Chromium: `.roster`'s bottom edge sits above
`.mat`'s top edge for the opponent board (not between `.mat` and
`.combat-lane`), roster chips measure 28px tall, and `data-theme`
persists across a real `page.reload()` (the bug above would have shown
green right up until this specific check). 908 tests pass - frontend/
index.css only, no engine changes this round.
