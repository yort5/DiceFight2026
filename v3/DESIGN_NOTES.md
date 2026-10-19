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
`ContinuousRegistry`'s per-die gating), `Data/DiceKingdomConfig.cs`
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
`web/src/dicekingdom/`, `DiceKingdomPage.tsx` → `DiceKingdomPage.tsx`),
the CSS (`instinct.css` → `dicekingdom.css`, `.instinct` → `.dicekingdom`),
the seat-storage key and invite-link path, and all on-page text. Left
alone, deliberately: the C# engine's internal naming
(`Data/DiceKingdomConfig.cs`, the `DiceKingdomConfig` class, card IDs
like `DK-CLAW-01`, the `Set: "Instinct Clash"` field on each card) - none
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
  from `DiceKingdomConfig.cs`'s `TardigradeDie` (two L1 0A/1D, two L2
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

## Match-table redesign, round 2 (2026-09-04)

User didn't like the board's existing layout and handed over a Claude
Design mockup (`design_handoff_match_table/`, a variant of `/game`'s own
match-table redesign - same README/`.dc.html` shape, `signal`/`portrait`/
`ribbon` variants) as the target look. Two new v3 rules confirmed at the
same time, both already matched by the current engine/data
(`DiceKingdomConfig.cs`) with no code change needed: a Champion carries
no die and is never fielded (already true), and a Tardigrade die's faces
may mix a character stat with an energy symbol on the same face - not
actually exercised by any current face (every real Tardigrade/Character
face is purely one or the other), so `CubeFace` in `dieFaces.ts` was
deliberately left as a `character | energy` union rather than building
hybrid rendering nothing exists to show yet.

Rebuilt `DiceKingdomPage.tsx`'s layout + `dicekingdom.css` to match the
mockup, keeping all the real plumbing from the previous round (API calls,
selection model, seat/invite, auto-skip, block assignment) untouched:

- **Real 3-column grid** (`.dk-layout`, `grid-template-areas`): a
  sideboard, the board column, and the rail share the SAME three grid
  rows (opponent / combat lane / you) across all three columns. This is
  what puts the opponent's Champion box directly under the life panels
  and the local player's Champion box with its top edge on the combat
  divider (README's exact spec) with zero JS measurement - a CSS Grid
  row auto-sizes to the tallest content assigned to it in ANY column, so
  the row boundary between "lane" and "you" IS the divider position by
  construction. Arguably a cleaner solution than the mockup's own pixel-
  offset hack, which its README explicitly flags as fragile.
- **Sideboard** - Basic Actions/Global Abilities render as real, empty
  panels (Dice Kingdom has designed neither yet - user's call, "leave
  those sections blank for now") rather than being omitted from the
  grid, so adding real content later is a content change, not a layout
  one. A third panel, "Energy in your pool," shows the local player's
  Reserve Pool energy chips (not in the original Dice Fight mockup's
  sideboard, but present in the Dice Kingdom-specific screenshot the
  user attached mid-session as the real target).
- **Roster strip** - portrait cards (mockup's chosen variant) instead of
  the old collapsible-`<details>` pill list; always visible (real
  feedback logged in the previous round, kept). Short here by
  construction - Dice Kingdom's roster is a Champion + 2 Characters, not
  Dice Masters' 8.
- **Bag inspector popover** - was a plain expanded pile; now a button
  ("click to inspect") that reveals contents grouped by card, local
  player's opens upward via `.bag-popover.up/.down`.
- **Real roll animation** - the biggest functional gap. Ported
  `useDiceRoll.ts` and `animateRolledDice` verbatim from `../App.tsx`/
  `../useDiceRoll.ts` (flight + settle CSS transitions, no physics),
  wired into `run()` so every server call that might have rolled dice
  (Roll, Reroll, a future card effect) triggers it automatically by
  diffing before/after state - same mechanism v1 uses. Verified via
  screenshot mid-flight: dice genuinely tumble off the resting face
  before settling, not just popping to the rolled result.
- **Champion box** redesigned from a horizontal banner (`.champbanner`,
  sat inline above each board) into a rail panel (`.championbox`) with
  a role label, the champion's name, and its passive as a note.
- **Selected die panel** added to the rail (name + `L{n} · {a}A/{d}D`),
  layered on top of whichever contextual panel/action-row was already
  driving the real buttons below it.
- Dashed border on Out of Play, a short "yours/theirs · moves to Used at
  end of turn" note under it, matching the mockup's low-prominence
  treatment for a zone that's rules-important but low-traffic.

**Real bug found and fixed along the way**: `index.css`'s `#root` sets
`text-align: center` (an un-cleaned Vite template default); every other
page resets it via `.app`'s own `text-align: left`, but Dice Kingdom
renders standalone with no `.app` wrapper (`Root.tsx`), so the center
alignment leaked through uncontested. Mostly invisible before this round
because the existing components that cared (`.dietile`, `.roster-chip`,
`.champ-opt`) all set their own explicit `text-align: center` - only
became visible once `.roster-head` (this round's new left-aligned label)
rendered dead-center over a left-aligned row beneath it. Fixed with an
explicit `text-align: left` on `.dicekingdom` itself, same fix `.app`
already has.

Kept deliberately unbuilt this round: hybrid character+energy die faces
(no data exercises them yet, see above), Global Abilities' "Pay & use"
flow (no Globals exist yet), Spin up/down on a fielded die (v3 has no
such action - a die's level comes only from rolling, unlike v1's manual
spin, so the mockup's Spin buttons were dropped rather than faked
against a nonexistent endpoint).

Verified with headless Chromium against the real running API (not just
`tsc`/`vite build`, both clean): a full click-through (champion pick →
Draw → Roll → Bag popover → Continue to Main → select a rolled die) in
dark mode, screenshotted at each step, zero console errors. Confirmed
the roll animation actually tumbles mid-flight, the Bag popover opens
with real grouped contents, the Selected Die panel and Energy-in-pool
chips populate from real data, and the Champion-box/divider alignment
holds without any measured offset.

## Match-table redesign, round 2 fixes (2026-09-05)

Direct feedback on the deployed round-2 page, three issues:

**"Prep Area doesn't fill up the whole area" / "a big empty space" -
a real bug.** `.mat`'s grid-template-areas left a `.` (empty) cell below
Prep Area - v1's leftover Intimidated slot, which v3 has no equivalent
for - so Prep Area sat only one row tall next to Reserve Pool's two-row
span, with a dead gap underneath it. Fixed by having `prep` span the
same two rows `reserve` does; the two boxes now read as one even pair
instead of Prep looking short with unexplained empty space below.

**"Dull... like I have my brightness turned way down" - a real design
gap, not a bug.** The previous round layered Dice Kingdom's panels on
the site's shared `--bg`/`--code-bg`/`--border`/`--text` tokens (docs-
tuned, low-contrast: panel background sat ~3% lighter than the page
background in dark mode). Replaced with a fully self-contained palette
scoped to `.dicekingdom` - panels now sit meaningfully lighter than the
page (which gets its own radial-gradient background instead of showing
whatever the host page had), borders and text both bumped well past the
site's values, and zone tint alphas raised from the original 0.06-0.2
range to 0.15-0.26 so each zone reads as a real color, not a smudge.
Left four stray `var(--bg)`/`var(--code-bg)` references broken by the
token removal (`.btn`, `.ribbon-chip.current`, `.roster-chip.picked`,
`.dietile .chip-count`, `.chip`) - added a themed `--on-fill` token for
the "text on a solid accent fill" cases and pointed the rest at
`--panel-bg-strong`/`--tile-bg`.

**"Everything stretched vertically... the box around the dice seems a
bit too big"** - `.dietile` (94px) and its 40px `DieCube` were sized
close to the reference's own dice but with more padding around them;
trimmed to 78px/34px (roster chips 82px→72px, icons down to match), and
tightened `.mat`'s gap (8px→6px) and zone header margins to read more
like the reference's compact 1420px-wide table.

Verified with headless Chromium in both dark and light (color-scheme
emulated both ways, not just the toggle button): Prep Area's box now
measures the same height as Reserve Pool's, zero console errors on a
full click-through, `tsc -b`/`vite build` clean.

## Match-table redesign, round 2: the actual root cause (2026-09-05)

The round-2 fixes above (Prep Area grid, palette, tile sizing) all
landed and were confirmed real, but the user reported it "still looks
the same - dull and vertical" with "still a big whitespace at the top."
Every screenshot taken to verify the previous rounds was quietly wrong
in the same way, and it took checking `#root`'s own CSS to find it:

**`index.css`'s `#root` is a hardcoded `width: 1126px`.** `/game` and
`/teambuilder` opt out via `#root:has(.game-layout), #root:has(.team-
builder-layout) { width: min(1800px, 100%) }` in `App.css` - Dice
Kingdom was never added to that list, so its whole 1580px-wide 3-column
grid was being squeezed into 1126px this entire session, on both the
deployed page and every local screenshot taken to check it (confirmed:
`.dicekingdom` measured 1124px wide even in a screenshot taken at a
1600px viewport - the exact number was sitting in this file's own
debug output from round 2 and wasn't connected to the cause until now).
That's what "stretched vertically instead of horizontally" actually
was: the reserve pool column and other flexible zones were being
crushed sideways the whole time, no matter how the CSS Grid itself was
tuned - a content-and-layout problem masquerading as a color problem.

Fixed in two parts, both needed: (1) added `#root:has(.dicekingdom)` to
`App.css`'s existing override list, and (2) a second, subtler bug this
surfaced - `#root` is `display: flex; flex-direction: column`, and a
flex item with an AUTO cross-axis margin (`.dicekingdom`'s own `margin:
0 auto`, meant for ordinary block-level centering) opts OUT of the
container's default `align-items: stretch` and shrink-wraps to its
content's width instead, regardless of `max-width`. `.app` (used by
`/game`/`/teambuilder`) already works around this with an explicit
`width: 100%` once `#root` is widened; `.dicekingdom` got the same
treatment. Fixing only (1) was verified insufficient on its own -
`.dicekingdom` still measured ~1096px inside a genuinely-widened
1800px `#root` until (2) landed too.

Net effect, confirmed via `getBoundingClientRect()` before/after at a
1920px viewport: `.dicekingdom` went from 1124px → 1580px (its real
max-width), and the inner grid from ~1096px → 1548px. Screenshotted in
both light and dark after the fix - the reserve pool column and other
previously-crushed zones now have real width, reading as the wide,
horizontal table the reference always was rather than a tall, narrow
one. `/game`'s own landing page reloaded and screenshotted afterward to
confirm the shared `App.css` change didn't regress it (still renders at
its normal 1126px, untouched - the new rule is purely additive via
`:has()`).

**Lesson for next time a "does this match the reference" complaint
doesn't move after a plausible-looking fix**: check the actual rendered
width/DOM geometry via `getBoundingClientRect()` before iterating on
the CSS inside the suspect component again - two straight rounds of
real, verified color/spacing fixes couldn't have worked, because the
box they were all happening inside was never the size the CSS assumed
it was.

## Match-table redesign, round 2: the "dull" label text (2026-09-05)

With the width bug fixed, user confirmed "feels much more horizontal
than vertical" but "still looks dull." Widened the search past the
panel/border/background tokens (already bumped twice) to how secondary
text was being dimmed: 37 separate `opacity: 0.4-0.85` declarations
across the file, on every zone header, roster cost, life label,
champion role tag, log line, etc. Opacity doesn't just lower contrast,
it visually blends the text WITH the background behind it - on a dark,
fairly monochrome panel that reads as a hazy, washed-out film over
everything, on nearly every label on the board at once. That's
probably what "dull" was actually pointing at more than any single
color value.

Added a third text tier, `--text-dim` (solid color, not translucent -
`#9198b0` dark / `#6e7488` light, between `--text` and the panel
background), and converted 21 of the 37 to use it instead of opacity:
every zone box header (`.zone h4` - the single highest-traffic one,
appears on every zone on both mats), roster cost/count, life/champion-
role labels, the Now panel's eyebrow, rail panel headers, log line
numbers, bag popover text, lane labels/hints, and the selected-die
stat line. Left opacity alone where it's a genuine state fade rather
than permanent secondary emphasis: disabled buttons, past/upcoming
step-ribbon chips, the unblocked-attacker pulse animation, and the die
face's card-art texture placeholder.

Verified with headless Chromium (dark, 1700px viewport, mid-roll so
real die faces are showing): zone headers and secondary labels now
read as legible solid grey-blue rather than faded, `tsc -b`/`vite
build`/`oxlint` all clean, full click-through zero console errors.

**Same-day correction**: the `--text-dim` pass above overcorrected.
User sent an actual side-by-side (reference vs. deployed) and named the
exact tell: the sideboard's "Basic Actions"/"Global Abilities" headers
(promoted to full `--text-h` in the very first token rework) read as
bright and alive, while every OTHER section title - `.zone h4` (Field,
Bag, Used Pile, ...), `.roster-head`, `.life-label`, `.championbox-
role`, `.rail-panel h3` (Log), `.lane-label-mid`/`.lane-slot-hint` - had
just been moved onto the new `--text-dim` tier and read as "inactive"
by contrast. Checking the actual reference confirms it: zone/panel
TITLES are uniformly bright in the source design, same weight as
headers - only genuine fine print below a title (costs, counts, hints,
stat lines) is dimmer. `--text-dim` was the right tool but had been
applied to the wrong tier of text; promoted the 9 title-level cases
back to `--text-h`, left the ~12 true fine-print cases (rc-cost/rc-
left, zone h4's count, dietile counts, log-seq, bag-hint, selected-die-
stats, lane-die-stats, sideboard-sub, now-eyebrow, zone-note) on
`--text-dim`.

Screenshotted after the fix (dark, mid-roll): every section title on
the board now reads at the same brightness as the sideboard's, matching
the side-by-side the user sent. `tsc -b`/`vite build`/`oxlint` clean,
full click-through zero console errors.

## Match-table redesign, round 2: the actual "big blank space at top" (2026-09-05)

Confirmed real bug, found by grepping for `details.how` in the CSS: two
separate `.dicekingdom details.how { ... }` rule blocks existed (one
correct, near the title bar's own rules, scoping panel styling to
`[open]` only; a second, further down the file, unconditional -
`padding: 14px 18px; margin-bottom: 22px; border; box-shadow` applied
even while collapsed). Dead leftover from an earlier page structure
(before "How to Play" moved into a compact title-bar toggle) that never
got deleted when the rest of the block was rewritten - same-specificity
cascade meant the later, wrong rule won regardless of the correct one
still sitting earlier in the file. Deleted the stale block outright.
Measured before/after: `.dk-titlebar` height 78px → 26px, the board's
first row (`.dk-layout`) now starts at y=60 instead of y=112.

Also asked about (not yet touched): "Drawn This Turn" always reads
empty while newly-drawn dice show up under "Prep Area" instead - traced
to `TurnEngine.ClearAndDraw` (`state.MoveToStep`, then `die.Zone =
Zone.PrepArea` - comment at line 32 confirms this is deliberate, "same
as v1"). `Zone.DiceFromBag` ("Drawn This Turn" in the UI) is a
DIFFERENT, mostly-dormant zone only certain card abilities populate
(`DrawToZone` effects - none of the current 8 Characters use one), not
where an ordinary turn-start draw goes. So this isn't a routing bug -
Prep Area is correctly where drawn-and-about-to-roll dice belong per
the v1/v2 rules and per several cards' own ability text ("draw a die
into your Prep Area") - but it's a real, understandable point of UI
confusion given both zones sit on the board as separate boxes with the
newer one staying empty all game. Needs a product decision (rename,
merge back into one display the way an earlier round briefly did, or
just explain it) before touching code - see the next conversation turn.

## Real engine fix: draw routes through DiceFromBag/DiceFromPrep, not PrepArea (2026-09-05)

User's own call on the "Prep Area vs. Drawn This Turn" question above,
stated precisely: since steps stay separated (no auto-advance), a
Clear & Draw's fresh draw belongs in Drawn This Turn, anything already
in Prep Area should move to Carried From Prep at that same moment, and
Roll should land dice straight in the Reserve Pool, not Prep Area. That
description turned out to be exactly v1's real rule (`TurnEngine.
ClearAndDraw`/`Roll` in `DiceFight.Engine`, ported and confirmed line
by line) - v2 never actually implemented it. The `Zone` enum's own
comment already admitted this ("declared here per the frozen list but
not yet routed through... a real gap to close"), and `ConditionEvaluator.
TargetWasKOd`'s comment already asserted the correct behavior in prose
("a plain draw lands rolled dice in the Reserve Pool, not this") while
the actual `ClearAndDraw`/`Roll` code contradicted it. So this was a
confirmed, real engine bug, not just a UI relabeling.

Fixed in `src/DiceFight.V2/TurnEngine.cs`:
- `ClearAndDraw` now sweeps anything sitting in `PrepArea` (a KO'd die,
  or a card's own Prep effect) into `DiceFromPrep` *before* drawing,
  matching v1's exact ordering rationale (a card that Preps a die
  *later in the same step*, e.g. reacting to `DiceDrawn`, must land in
  a freshly-emptied Prep Area, not get swept by this same sweep). Fresh
  draws now land in `DiceFromBag` instead of `PrepArea`.
- `Roll` now reads from `DiceFromBag` + `DiceFromPrep` (was `PrepArea`),
  and - matching v1's Roll exactly - rolls all of them first, then
  moves all of them to `ReservePool` immediately (no separate "rolled
  but not yet placed" zone). `RerollOwn`'s own zone check moved from
  `PrepArea` to `ReservePool` to match.
- `FinishRoll` no longer moves any dice (Roll already did) - it's now
  just the client's explicit "done deciding, advance to Main" call,
  unchanged in every other respect (still fires `TurnStepEntered(Main)`,
  still what the Energize carve-out in `EventBus.Fire` keys off).

`PrepArea` itself keeps its real, narrower meaning: where a KO'd die
lands (`EffectInterpreter.KoDie`, rule 1.5.3.2) and where a card's own
Prep-style effect (`DrawToZone(..., Zone.PrepArea, ...)`) puts a die
mid-turn - never where an ordinary turn's draw goes, now correctly.

Verified: `TurnFactKind.PrepAreaEmpty` (used by Prep-conditional card
abilities like Albatross's "if your Prep Area is empty, draw a die into
it") is now MORE correct as a side effect - it no longer reads "not
empty" just because the active player is mid-Roll like it would have
before.

Updated 10 tests across 5 files that asserted the old (wrong) zone
routing directly: `TurnCycleTests`, `DiceFightClassicConfigTests`,
`DiceKingdomConfigTests`, `RerollOwnTests` (4, including one genuine
behavior-shape rewrite - "rejects a reroll after FinishRoll" now throws
via the `RequireStep` step-guard rather than a zone mismatch, since the
die is already sitting in the Reserve Pool the whole step now),
`EnergizeTests`/`EventBusTests` (manually-constructed test dice fixed
from `Zone.PrepArea` to `Zone.DiceFromBag` so `Roll` actually touches
them), and `V2GamesControllerTests` (the one integration test covering
the whole HTTP flow). Full solution suite: 908/908 passing (315 V2 +
580 Engine + 13 Api). Verified live end-to-end with headless Chromium
against the real rebuilt API: "Drawn This Turn" now shows the fresh
draw, Prep Area stays empty, and Roll moves everything straight into
"Reserve Pool" with real 3D cube faces, zero console errors.

## Round 3: ribbon alignment, a real layout bug, one-shot reroll, starter bag (2026-09-05)

Four items from one round of feedback:

**Step ribbon now shares `.dk-layout`'s own 3-column template** (sideboard/
board/rail) instead of spanning the full page width - it sat flush
against the far-left edge, under the sideboard, when it's really the
board's own status line. `.dk-titlebar` is now a grid with the same
`200px minmax(0,1fr) 300px` columns as the layout below it; the ribbon
sits in column 2, Dark mode/How to play in column 3, column 1 simply
empty. Falls back to a plain single column under the existing 1100px
breakpoint.

**Real layout bug: selecting a die during Roll & Reroll opened a dead
gap between the combat lane and Field Zone.** Root cause: `.dk-layout`'s
row 2 is shared by the combat lane AND the rail's Now/Selected Die
panel (the same row-sharing trick that aligns the Champion box with the
divider - see the earlier round's note on why). Selecting a die grows
the rail panel (adds the Selected Die block + Reroll/Continue buttons),
which grows the whole shared row past the combat lane's own natural
height - and since every cell defaults to `align-items: start`, the
lane's fixed-height box just sat at the top of the now-taller row,
leaving the extra space empty underneath it instead of anywhere
visible as "reserved for the rail." Fixed by stretching `.dk-row-lane`
to fill its grid cell and having `.combat-lane` itself fill + vertically
center its content - extra height now grows the lane box (more
breathing room around its contents) instead of leaving a floating void
below it.

**Reroll is a one-shot decision now, matching rule 2.4.3/2.4.4 - "one
reroll decision... taking it ends the step."** `TurnEngine.RerollOwn`
previously stayed in Roll and Reroll after rerolling, so a player could
call it again with an entirely different selection - a second reroll
wave the rule doesn't allow. `RerollOwn` now advances straight to Main
itself (factored into a shared `AdvanceToMain` with `FinishRoll`),
matching v1's own single `Reroll` call exactly. This makes a second
`RerollOwn` (or even a second `Roll`) in the same step self-rejecting
via the existing `RequireStep` guard - no separate bookkeeping needed.
Frontend needed no changes: it already re-renders off `currentStepId`,
so the "Roll & Reroll" panel (Reroll Selected / Continue to Main Phase
buttons) simply disappears once the server's response comes back
already on Main.

**Starter bag doubled to 8 Tardigrades (was 4), per Champion** - "in
keeping with traditional deck building," per the user - 8 covers a full
two turns of drawing `DrawCount(4)` each without an early Used-Pile
reshuffle. `DiceKingdomConfig.Champions`' `TardigradePool` `Count`
bumped from 4 to 8 for all four Champions.

Updated tests: `RerollOwnTests` (replaced the now-impossible "call Roll
twice in one step" test with one asserting RerollOwn's own advance-and-
lock behavior, plus a new test for the same-die-listed-twice-in-one-
call case the old "twice" test accidentally stopped covering precisely;
removed a test that became a literal duplicate of the new one),
`DiceKingdomConfigTests`/`V2GamesControllerTests` (Bag count 4 → 8).
Full solution suite: 908/908 passing. Verified live end-to-end with
headless Chromium: ribbon aligns with the board column, selecting a die
no longer opens a gap (the lane box grows instead), clicking "Reroll
Selected" jumps straight to the Main tab with no second click, and both
players' starting Bag reads 8.

## Round 4: flat panels instead of gradient bleed, active-mat highlight (2026-09-06)

Three items, one of them a real color-mixing bug:

**"Clean-line borders... instead of the overlapping gradients we
currently have."** Every zone tint (`.zone-field`/`.zone-reserve`/etc.)
was a translucent `oklch(... / alpha)` layered directly over
`.dicekingdom`'s own page-level radial gradient - fine for a narrow
box, but Field Zone spans nearly the whole mat width, so the page
gradient showed through differently at each edge and read as a
gradient smear rather than the reference's flat, single-tone panels.
Same issue on `.championbox`, which used an actual `linear-gradient`
for its fill. Fixed by compositing each tint as an opaque `color-mix()`
against `--panel-bg` instead of a translucent overlay - no dependency
on whatever renders behind it, still fully theme-adaptive since
`--panel-bg` itself flips per light/dark.

**Real bug found applying that fix**: the first pass used `color-mix
(in oklch, ...)`, and several zones rendered as garish, way-too-
saturated **purple/magenta** instead of their intended hue (amber
Reserve Pool/Prep Area looked identical to violet Out of
Play/staging). Cause: `oklch`-space mixing interpolates hue along the
circle's *shorter arc* between the two colors' hues - and the shorter
arc from `--panel-bg`'s own blue-violet hue (dark mode's `#2e3548`)
to an amber target (~75°) runs *through* magenta/pink (300-360°)
rather than the expected warm direction, so every partial mix landed
in the wrong part of the color wheel. Switched to `color-mix(in oklab,
...)` - a Cartesian, non-hue-based space that blends via straight-line
interpolation and can't take the "wrong way around" - and reduced the
mix percentages (28/24/20/16/26/22% → 18-25%) since oklab's blend
reads visibly stronger than the old alpha-transparency did at the same
number. Real lesson for next time a `color-mix(in oklch, ...)` tint
looks wrong: check whether the two colors are far apart in hue before
assuming the percentage is the problem.

**"Highlight the currently active playmat"** - `.playerboard` (roster +
mat together) had no box of its own at all: no border, no padding, no
radius. `.turn-mine`/`.turn-waiting`'s border-color override had
nothing to color, and the ring's box-shadow hugged flush against the
nested zone boxes with zero breathing room, easy to miss entirely. Gave
`.playerboard` a real card shape (padding, radius, a transparent base
border for the override to color) so the active board now reads as one
clearly outlined region, matching how `.championbox.turn-mine` already
worked.

**"The 'Active teamA' probably needs a highlight around it, it kind of
disappears in dark mode"** - was plain colored text with no background.
Replaced with a real solid pill using the same fill + `--on-fill`-text
pattern `.ribbon-chip.current` already uses (proven to hold contrast in
both themes, unlike a translucent tint whose contrast against its own
text shifts with the page's light/dark background).

Verified with headless Chromium in both light and dark: zone panels are
flat and color-coded with no gradient bleed, the active player's whole
board carries a clear green/amber outline, "Active: teamA" reads as a
solid pill. `tsc -b`/`vite build`/`oxlint` clean, full click-through
zero console errors.

## First real fan art: Lion renamed to Wolf (2026-09-06)

The user's kid drew a pixel-art wolf; swapped in as the Claw Champion's
avatar, and the Champion itself renamed from "Lion" to "Wolf" to match
(`DiceKingdomConfig.cs`'s `Champions` list - id and name both, plus
every `"Lion"` reference across the frontend `CHAMPIONS` array and both
test suites - `Player.Name` is set from `champion.Name`
(`V2GamesController.BuildPlayer`), so this also fixed the match log
reading "Lion draws 3 dice" etc. to say "Wolf" instead, free).

`icons.tsx`'s `CHAMPION_ICONS` map takes any `(size) => ReactElement`,
so the new `WolfIcon` is a real `<img>` (the JPEG, `web/src/
dicekingdom/assets/wolf-champion.jpg`) rather than a `currentColor` SVG
glyph like the other three Champions - same call signature, no special-
casing needed at either call site (the pre-game picker or the in-game
Champion box). More real art from the same source is expected to
replace the placeholder SVGs over time.

Verified live with headless Chromium: the art renders at both sizes
(picker and Champion box), `tsc -b`/`vite build`/`oxlint` clean, full
908-test solution suite passing.

**Roster expansion started, paused mid-work per the user** ("hold off...
finish it up later tonight") - 6 new Claw Characters (Grizzly Bear,
Orca, Peregrine Falcon, Tiger, Stoat, Cape Buffalo) are drafted in
`DiceKingdomConfig.cs`, sourced from `CARD_INSPIRATION.md`'s
remaining Claw picks (excluding "Wolf" itself, now the Champion's
name) and simplified to the same plain templates the original 8
Characters use. They are NOT yet wired into `Catalog` or
`CharactersByEnergyType` - compiles clean as inert unused
`CardDef`s, zero behavior change. Still needed to actually finish the
"8 different animals per team" ask: 6 more each for Shell/Wing/Eye
(picks scoped, not yet written - see this session's own notes for the
per-type template mapping), wiring all 24 into `Catalog`/
`CharactersByEnergyType`, `MaxTeamCards` 2→8 and `MaxTeamDice` 8→32 in
`Config.Rules`, and updating `DiceKingdomConfigTests`'
`Every_Energy_Type_Has_Exactly_Two_Characters` (→ Eight) plus both
`Unpurchased`-count assertions (8 → 32) in `DiceKingdomConfigTests`/
`V2GamesControllerTests`. No frontend changes expected to be needed -
the roster strip already renders however many Unpurchased cards exist.

## Champion box: bigger portrait, text moved left (2026-09-06)

Direct follow-up on the wolf art: it rendered small (24px, `WolfIcon`'s
own default) with a lot of dead space in the box next to it - two real
sizing bugs, not just "make it bigger". `.championbox-body svg` sized
every OTHER Champion's icon via CSS (30px), but that selector never
matched `WolfIcon`'s `<img>` tag at all, so it silently fell back to
its own unstyled default; same gap in `.champ-opt svg` on the pre-game
picker. Fixed by adding `img` to both selectors and bumping both to a
real portrait size (72px in the Champion box, 42px stays for the
picker) - same fix, same treatment for every Champion (not a Wolf-only
special case), so a plain SVG glyph now just reads as a bigger simple
icon too.

Restructured `.championbox-body` from icon-then-text to a text column
(flex: 1, vertically centered) + the portrait (flex: none, fills the
box's full height) - JSX reordered to match (text div first, `<Icon
size={72} />` last) so the portrait naturally fills whatever space the
(now-compact) text column doesn't need, matching the box's designed
height instead of floating at a small fixed size inside a lot of empty
padding.

Verified with headless Chromium: portrait fills the box edge-to-edge,
text sits in a clean left column, checked in both the Champion box and
the pre-game picker. `tsc -b`/`vite build`/`oxlint` clean, full
click-through zero console errors.

## Roster expansion finished: 8 Characters per energy type (2026-09-07)

Picked back up where the Claw-only pass paused. Wrote the remaining 18
Characters (6 each for Shell/Wing/Eye - see this file's own two entries
above for exactly which CARD_INSPIRATION.md picks and which template
each maps to; Shell needed one original pick, Box Turtle, since the
source list only had 5 usable non-Musk-Ox/Hippo entries), then wired
all 24 new + the original 8 in:

- `Catalog` - all 32 `CardDef`s.
- `CharactersByEnergyType` - 8 ids per energy type (was 2).
- `Config.Rules.MaxTeamCards` 2 → 8, `MaxTeamDice` 8 → 32 (8 Characters
  x DieLimit 4) - both have to move together or `GameSetup`/
  `V2GamesController.BuildPlayer` would silently seed more Unpurchased
  dice than the rules configuration claims a team can hold.

Test updates: `Every_Energy_Type_Has_Exactly_Two_Characters` →
`_Eight_`, and the `Unpurchased`-count assertions in both
`DiceKingdomConfigTests` and `V2GamesControllerTests` (8 → 32, since
8 Characters x DieLimit 4 replaces 2 x DieLimit 4). Full solution
suite: 908/908 passing, including `Config_And_Catalog_Are_Structurally_
Valid`, which schema-validates every new card's ability/target-filter
wiring the same way it already covered the original 8.

No frontend changes needed or made - confirmed live: `DiceKingdomPage.
tsx`'s roster strip already renders however many Unpurchased cards
exist (`unpurchasedByCard`, built from real zone data, never hardcoded
to a count), so both rosters simply grew from 2 chips to 8 the moment
the backend started seeding them. Verified with headless Chromium: 16
`.roster-chip` elements on the board (8 per player) after Start Match,
all names/costs/counts correct, chips wrap cleanly within the mat
width, zero console errors, `tsc -b`/`vite build`/`oxlint` clean.

## Real proof abilities fire, then a genuine die-layout change (2026-09-07)

Asked directly: "do the abilities actually work yet?" `Config_And_
Catalog_Are_Structurally_Valid` only schema-validates ability/target
wiring - it never resolves one. Added `DiceKingdomNewCharactersTests.
cs`, exercising the real firing path (`TurnEngine` action -> `EventBus`
-> `AbilityQueue` -> `EffectInterpreter.DrainQueue`, same discipline
`DpsCardsTests.cs` uses for v1's migrated pool) for the effect shapes
genuinely new to this catalog: Grizzly Bear's `Ko`, Stoat's direct
damage, Cape Buffalo's ATK aura. All passed on the first real run.
Real catch along the way: the aura test's first draft expected +1 ATK
and got +2 - not a bug, `ChampionRegistry.RegisterAll` (called from
`GameSetup.NewGame`) already stacks Wolf's own +1 ATK Champion passive
on every one of the player's dice, aura included.

Then a second, separate ask: whether Character dice should actually
change to match LATER Dice Masters sets - 3 stat faces (one per level,
not doubled) + 3 energy faces of the card's own type (two double, one
single) - replacing the classic-run layout (6 doubled stat faces, 0
energy) the original 8 cards shipped with. Explicitly a real engine/
balance change, not a display tweak (confirmed with the user before
touching anything, given 32 cards' worth of blast radius): every
Character now has genuine odds of rolling energy instead of a body,
same as a Tardigrade always did.

`CharacterDie` (`DiceKingdomConfig.cs`) took a new `energyType`
parameter and rewrote its face-building loop: one `CharacterFaceData`
face per level (was two), plus 2 double- and 1 single-`SymbolAmount`
energy face of that type (mirrors `TardigradeDie`'s own energy-face
shape, just without a hybrid stat+energy face - user's call was pure
energy faces, not hybrid ones). All 32 `CharacterDie(...)` call sites
updated via a scripted per-section pass (each of the four `// Claw`/
`// Shell`/`// Wing`/`// Eye` sections inserts its own energy type) -
verified by grep, not just build success, since a script silently
tagging the wrong section wrong would still compile.

Frontend needed one matching fix: `dieFaces.ts`'s `defaultFaces` (the
LOCAL reconstruction of "what an unrolled Character die's faces
probably look like", used only for the placeholder cube before a real
roll is known) was hardcoded to the old doubled-stat-faces/no-energy
assumption. Rewritten to build 3 stat faces + 3 energy faces (2 double,
1 single) of `card.energyTypes[0]`, matching the real die exactly -
otherwise the placeholder cube would show a die shape that no longer
exists.

Added a 4th test proving the actual point of the change - a Character
die's energy face is real, spendable energy, not a display artifact:
Honey Badger sitting on its own double-Claw-energy face pays Stoat's
fielding cost through the ordinary `TurnEngine.Field` path, same as any
Tardigrade or Reserve Pool energy die would. Full solution suite:
912/912 passing (319 V2 + 580 Engine + 13 Api).

## Roster card detail popover, icon-based costs, a Wolf glyph (2026-09-07)

Three smaller asks from the same round, all frontend:

**Click a roster card to see its ability and stats.** The card was a
`<button disabled={!clickable}>` where `clickable` only turned on
during your own Main step - meaning most of the game, the card
couldn't be clicked for anything at all, viewing included. Split the
two concerns: the visible chip now always toggles a detail popover
(`openCardId`, one page-level id so only one is ever open regardless of
which side's roster it's on), and the actual purchase action moved to
a real "Select to Purchase" button *inside* the popover, shown only
when `canPurchaseNow`. The popover shows the name, purchase cost, all
three levels' `ATK/DEF` + fielding cost, a line noting the other 3
faces (always 2 double + 1 single of the card's own type now - nothing
per-card to fetch, per the die-layout change above), and the ability
text.

Real layout bug found building it: the level rows were first an HTML
`<table>` with `display: flex` on the `<td>`s (to lay out the stat +
cost-icon pair inline) - setting `display: flex` on a table cell drops
it out of table layout entirely, so rows rendered in a scrambled stack
instead of aligned columns. Replaced the whole table with a plain flex
column of flex rows - no table semantics needed for 3 rows anyway.

**Icons instead of words for energy costs** - `PipBadge` (the sideboard's
"Energy in your pool" chips) and the roster chip's cost label both
spelled the type out ("2 Claw"). Both now render the type's own
`ENERGY_ICONS` glyph next to the number instead (a new small `CostIcon`
helper, reused by the popover's cost/level rows and energy-faces note
too).

**A vector Wolf glyph alongside the photo** - `WolfIcon` only ever
rendered the real JPEG, which can't pick up an accent tint the way the
other three Champions' `currentColor` SVGs can, and doesn't shrink to a
tiny badge as cleanly. Added `WolfGlyphIcon`, a plain path-based icon
in the same minimal style as `ArmadilloIcon`/`GoldenEagleIcon`/
`GreatHornedOwlIcon` - not wired into `CHAMPION_ICONS` (the photo stays
the champion box/picker's real icon, confirmed correct in the last
round), just available for whatever small/tinted context comes up next
that a flat raster image doesn't suit.

Verified live with headless Chromium: opened several cards' popovers
(Honey Badger, Grizzly Bear) in both a non-Main step (view-only, no
Purchase button) and Main step (Purchase button appears, clicking it
selects the die the same way the old direct click used to), ran a full
purchase through to completion (energy dice selected, Purchase button
enabled, die moved to Used Pile, log entry correct, roster count
decremented) - real proof the popover's button reaches the same
`toggleDie`/`selectionAction` path the rest of the board already uses,
not a parallel one. `tsc -b`/`vite build`/`oxlint` clean, full
click-through zero console errors.

## 2026-09-05 - Seven-item feedback pass on the popover/rail/board polish

Direct feedback on the previous batch, in order:

1. **Wolf champion box: icon AND photo.** "Eventually all Champions will
   have a cool animal avatar, but we still need to know at a glance
   which energy they are." Added the Claw glyph next to the "WOLF" name
   text (`.championbox-name-row`), alongside (not instead of) the real
   photo - every Champion gets this now, not just Wolf, since it's keyed
   off `player.champion.energySymbolId` generically.

2. **"The slashes for Claw get washed out in dark mode."** Root cause:
   `CostIcon` (roster/popover cost labels) inherited its color from
   context, and every caller sat inside `--text-dim` (a muted grey) -
   fine for the word "Claw", illegible for a thin 3-path glyph at 11px
   in dark mode. Fixed by coloring `CostIcon` to the energy's own accent
   variable directly, matching how `PipBadge`'s solid pill already reads
   fine.

3. **"Tardigrades should still have a 0 in the upper left for fielding
   cost."** `DieCube`'s `die-cube-cost` span was gated on `> 0`, so a
   free (0-cost) face printed nothing there at all - looked unfinished
   rather than "free". Now always prints, including 0.

4. **Purchase popover covering the Reserve Pool.** Real bug: the
   popover's up/down direction was keyed to `mirrored` in a way that
   pointed it AT this board's own mat instead of away from it - on your
   own board (mat-then-roster), "up" grew the popover right over the
   Reserve Pool exactly when a purchase asked you to click into it to
   pay energy. Inverted the ternary so it opens away from the mat on
   both boards instead.

5. **"We lost our lines between zones."** A real CSS specificity bug,
   not a design regression: `.mat-slot .zone { border: 1px solid
   transparent }` (3 classes deep counting `.dicekingdom`) was more
   specific than every per-zone tint rule (`.zone-used`, `.zone-field`,
   etc., 2 classes), so the transparent border always won regardless of
   source order - only the tinted backgrounds were ever actually
   visible. Fixed by adding `.zone` to each tint selector to tie
   specificity and let cascade order (they come later) decide. Borders
   are crisp and distinct again in both themes.

6. **Energy pool total.** Added a running total to the sideboard's
   "Energy in your pool" panel - the existing pip list plus a vertical
   divider plus a solid circle badge with the summed amount.

7. **Skip Attack Step was missing entirely**, and the story behind
   getting it back turned out bigger than a missing button: V2's
   `TurnEngine.SkipAttackStep` was already wired end-to-end (API route,
   client method) but its actual implementation just called
   `EnterAttackStep` - i.e. "skip" landed you IN Select Attackers, not
   past it, so it still cost a "Confirm Attackers (0)" click and never
   matched the name. v1's real `SkipAttackStep` (Engine project) jumps
   straight from Main to Clean Up in one shot. V2 has no single
   transition to jump to that way (`CleanUp` requires `TurnStep.Attack`,
   and every one of Select Attackers/Assign Blockers/Action-Global/
   Return-to-Field maps to that same coarse `TurnStep`), so the fix runs
   the real Combat pipeline once, synchronously, with nobody attacking,
   nothing blocked, and no damage assigned - `DeclareAttackers([])` ->
   `DeclareBlockers([])` -> `AssignCombatDamage([])` - landing on
   `ReturnToField` exactly like a real empty combat would, with every
   step's own events still firing along the way for any future ability
   that hooks one. No forced-attack keyword exists in this catalog yet,
   so there's nothing to reject the way v1's `MustAttackThisTurn` check
   does; `CombatEngine.DeclareAttackers`'s own check still applies if
   that ever changes. Verified live: click Skip Attack Step from Main ->
   log shows "declares no attackers" / "leaves every attacker unblocked"
   / "ends their turn" -> turn passes to the other player in one click.

8. **"The whole thing needs to be obviously active or inactive."** The
   thin `turn-mine`/`turn-waiting` ring wasn't enough on its own. Added
   a real three-state treatment to `.playerboard`: a tinted background
   wash for whichever board is actually live right now (on top of the
   existing ring), and a new `.turn-inactive` state - 0.66 opacity +
   `saturate(0.55)` - for whichever board ISN'T. Verified live via
   computed style after a real Skip Attack Step -> End Turn round trip:
   the board that was `turn-mine` at full opacity becomes
   `turn-inactive` at 0.66 opacity the instant the turn passes, and the
   other board picks up `turn-waiting` at full saturation - a real,
   visible swap, not just a border color change.

All eight verified live via headless Chromium in both themes (full
purchase-popover flow, a real Skip Attack Step -> Clean Up -> End Turn
turn-pass, zone border contrast, sideboard total) plus the full 912-test
C# suite and `tsc -b`/`oxlint`/`vite build`, all clean.

## 2026-09-05 (later) - Four bug reports: first-turn draw, popover lifetime, blocking UX, selection contrast

1. **First-turn draw was wrong twice over.** Direct feedback: "First
   player should 'draw four dice and then place one of them in Out of
   Play.' Currently it just draws three, leaving five in the bag." Real
   bug, and a real gap, found together:
   - `TurnEngine.ClearAndDraw`'s first-turn handling was `DrawCount - 1`
     - the 4th die was never drawn at all, just left sitting undrawn in
     the Bag. v1's real rule 2.3.3 draws the normal count, then draws
     ONE MORE and sets it straight to Out of Play - ported that exactly
     (`TurnEngine.cs` now draws `DrawCount`, then an extra 1 on
     `IsFirstTurn`, moved to `Zone.OutOfPlay`).
   - Along the way: **the Bag never refilled from the Used Pile when it
     ran short mid-draw** - a gap this file's own header comment had
     flagged as unported. Also ported from v1's `DrawFromBag`: a shared
     internal helper both draws use, reshuffling the Used Pile back into
     the Bag whenever it empties mid-loop, stopping short only once both
     are genuinely empty. New test:
     `ClearAndDraw_Refills_The_Bag_From_The_Used_Pile_When_It_Runs_Short`.
   - Six existing tests across `TurnCycleTests`, `DiceKingdomConfigTests`,
     `DiceFightClassicConfigTests`, and `V2GamesControllerTests` had their
     draw-count assertions updated to the new (correct) numbers.

2. **The roster card popover stuck around indefinitely.** Direct
   feedback: it should close once the die is purchased, or the moment
   the player clicks anything else. Two fixes together: `run()` (every
   server action) now clears `openCardId` on success, and a
   `pointerdown` listener on `document` closes it the instant a click
   lands outside `.roster-chip-wrap` (the trigger chip and its own
   popover both count as "inside," so clicking the Purchase button
   inside the popover doesn't self-close it).

3. **Blocking was a separate right-hand-pane picker.** Direct feedback:
   "it shouldn't be necessary to 'click an attacker, then a defender to
   block it'... I should be able to just click whichever dice I want and
   click the zone to add it to... not intuitive having to select
   blockers from the right hand pane." Real redesign, not just a wording
   change: the `BlockPanel` component (a chip-list picker) is gone.
   Instead:
   - The defender's own Field Zone dice become clickable during Assign
     Blockers (same shared `toggleDie`/`selection.primary` every other
     board interaction already uses - just a widened `clickable` gate).
   - `CombatLane`'s blocker slot - the actual empty/filled box drawn
     right across from each attacker - is now the real drop target:
     `canAssignBlockers`/`onSlotClick` props, an `onClickCapture` handler
     (capture phase, so a filled slot's own blocker-die button doesn't
     also fire its own toggle-select underneath), and a `.targeting`
     highlight state that only lights up once a candidate blocker is
     actually selected ("assign here").
   - `handleBlockerSlotClick` in `DiceKingdomPage.tsx` builds the same
     local `blockAssignments` record as before (still submitted via one
     "Confirm Blocks" call) - clicking a slot with a die selected moves
     that die there (clearing it from wherever it was previously
     assigned, so one physical die is never double-booked); clicking an
     already-filled slot with nothing selected clears it.
   - Verified live with a real two-seat scenario (Playwright driving one
     page, re-navigating via each seat's own invite-link token to flip
     `yourPlayerId` between clicks - the only way to genuinely exercise
     the DEFENDER's perspective, since a single unclaimed session is
     always seated as player one): fielded a creature each side, teamA
     declared an attacker, teamB clicked their own Field die then the
     lane's "assign here" slot, Confirm Blocks - server accepted it,
     log read "Armadillo assigns 1 blocker," and the die correctly
     stayed in the Field Zone list (not yet AttackZone) right up until
     that real Confirm Blocks call, only then moving - confirming the
     local/pending vs. confirmed staging is intact under the new UI.

4. **Selection highlight invisible in light mode.** Direct feedback:
   "when I select a die... it changes the background color... if that
   happens in light mode, I can't see it." Root cause: `--tile-bg` and
   `--panel-bg` are BOTH `#ffffff` in light mode, so the old `.picked`
   state (a background swap between those two tokens) was reading fine
   only in dark mode's much larger --tile-bg/--panel-bg gap. Fixed the
   same way `turn-mine`/`turn-waiting` already reads reliably in both
   themes: a real ring via `box-shadow`, colored from the tile's own
   `--cc` accent (already set inline per die/card) rather than a theme
   background token - never subtle regardless of theme.

All four verified live via headless Chromium (both themes where
relevant), plus the full 913-test C# suite and `tsc -b`/`oxlint`/
`vite build`, all clean.

## 2026-09-05 (correction) - The going-first die comes FROM the 4, not on top of it

Direct feedback caught a real misread of my own fix above: "the first
player should 'draw 4 and place one out of play', leaving 4 in the bag.
I.e., the one that goes to Out of Play is from the four that were drawn.
This preserves the standard deck-building trope of 'the first two hands
are all starter cards' - or in our case, dice."

What shipped a moment earlier drew `DrawCount` normally, then drew ONE
MORE on top and set that one Out of Play - `DrawCount + 1` total,
draining the Bag by 5 on an 8-die starting pool instead of 4. Fixed to
match exactly what was asked: draw `DrawCount` once, then set the LAST
of those SAME dice Out of Play (no second `DrawFromBag` call at all) -
so "drawn this turn" reads `DrawCount - 1`, Out of Play reads 1, and the
Bag ends the turn down by exactly `DrawCount`. Which of the `DrawCount`
dice gets set aside is arbitrary (`DrawFromBag`'s own random pick
already decided the whole draw's order), so simplest is fine.

Six test files' draw-count assertions (and two `ScriptedRoller` face
counts) went from the wrong DrawCount+1 numbers back down to the correct
DrawCount ones: `TurnCycleTests`, `DiceKingdomConfigTests`,
`DiceFightClassicConfigTests` (both its base and its DrawCount=6 variant
test), `V2GamesControllerTests`, and the dedicated bag-refill regression
test (which also needed its "sweep to Used Pile" step scoped to exclude
the die still legitimately sitting in the Bag, not yet drawn).

Verified live: Bag 8 -> 4, Drawn This Turn 3, Out of Play 1, log now a
single line - "Wolf draws 4 dice, setting 1 Out of Play for going
first." Full 913-test suite green.

## 2026-09-05 (later still) - Playtesting experiment: Surge to 1 Wild, real partial-spend

Direct feedback: "it feels a little too easy to spend energy... let's
change the Surge die to just be one wild and see how that feels... we
need to be able to partially spend energy - so if we spend half of the
double energy die, it should spin to the L2 side with single energy."
Explicitly framed as an experiment (before touching monochromatic
teams), not a rules-accuracy fix - unlike the last several passes.

1. **Surge: 2 Wild -> 1 Wild.** One line in `TardigradeDie` -
   `DiceKingdomConfig.cs`. A second copy of the same table exists on
   the frontend purely for the 3D cube's other 5 (currently-hidden)
   faces during a roll/reroll animation (`TARDIGRADE_FACES` in
   `dieFaces.ts`, explicitly commented as mirroring the backend) - found
   and fixed to match, or the badge would have kept reading "2" whenever
   a die happened to be resting on a different face mid-animation. At
   amount 1 the small in-cube amount badge doesn't render at all
   (`DieCube.tsx`'s own `amount > 1` gate) - correct, matching how a
   plain single-pip anywhere else never gets one either.

2. **Real partial-spend / spin-down** (rule 2.6.1.4), ported from v1's
   `TurnEngine.SpendEnergy` at last - this was an explicitly flagged gap
   in V2's own header comment ("an overspent die is simply consumed
   whole"), and now closed. Two behavior changes to V2's `SpendEnergy`,
   both real, not just the spin-down itself:
   - It now stops consuming dice the instant `amountNeeded` is met -
     anything offered beyond that is left completely untouched (still
     spendable later this turn), rather than every offered die being
     spent regardless of whether it was needed.
   - The one die that ends up only partially needed (always the last
     one included, by construction) spins to whichever OTHER face of
     that SAME physical die shows exactly the leftover amount of the
     same energy type (`TrySpinDown` - a straight search of
     `GetDieDefinition(die).Faces` for a single-symbol match), and stays
     right where it is instead of moving to `spentZone`. For a
     Tardigrade this means exactly what was asked: spend 1 of an L1
     die's 2 Claw, and it spins to the matching L2 face (1 Claw, and a
     real 1A/1D creature face too, not just a number change) still
     sitting in the Reserve Pool. No matching lower face (Bulwark, a
     Character's own single-energy face, the now-1-Wild Surge face) -
     or a face offering more than one symbol type at once, which
     nothing in this catalog does yet - just falls back to a full
     consume, same as before.
   - Deliberately NOT ported: v1's virtual-generic-energy banking for a
     Generic double with no single face to spin to (rule 2.6.1.6) - V2
     has no virtual-energy concept at all yet, and nothing in this
     catalog's faces are Generic, so there's nothing for that path to
     ever reach.

   One existing test's assertion flipped from "moves to Out of Play" to
   the new correct "stays in the Reserve Pool, now showing the single-
   energy face" outcome (it happened to spend a double-energy die for
   exactly half its value); rewritten against a fielding cost that
   fully matches the die's face instead, to keep its own original
   "fully spent" intent, plus a brand new dedicated test locking in the
   spin-down mechanic itself precisely (face index, symbol amount, and
   the resulting creature stats all asserted).

Verified: the full 914-test suite green, a live purchase (full-spend
path) still completing cleanly with zero console errors across several
random rolls, and the frontend's stale Surge-face mirror caught and
fixed by the live check itself (not by inspection).

## 2026-09-05 (yet later) - Die-face polish: identity, overlap, and a real spin animation

Direct feedback on the Surge/spin-down work above, from actually looking
at it:

1. **Spin-down looked exactly like a real roll.** The player asked for
   it explicitly: "probably need a different animation for spin-down/
   spin-up, it shouldn't look the same as an actual randomized roll."
   Root cause: `animateRolledDice`'s own "did this die's face change"
   fallback (there specifically so a reroll landing the SAME face still
   animates) doesn't distinguish WHY the face changed - a spin-down
   tripped the identical fallback a real roll's explicit `rolledDieIds`
   does, and got the same toss-and-tumble. Added `useDiceRoll.spinTo` -
   no flight phase, no multi-360 tumble, no random tilt, just one direct
   turn to the new face - and split `animateRolledDice`'s targets into
   `rolledTargets` (explicit -> `launchRoll`) vs. `spunTargets` (the
   implicit-only case -> `spinTo`). The "tray shakes on a roll"
   (`rolling` state) deliberately stays untouched by a spin - a partial
   spend isn't a dice-tray event.

2. **Energy icons were covering up the character die faces.** Real
   layout bug, not a taste call: the energy TYPE icon (Claw/Shell/etc.)
   was rendered dead-center at over half the die's size - exactly where
   a hybrid Tardigrade face's OWN avatar/identity would need to live,
   and visually competing with anything else in that space. Shrunk it
   (size 0.52 -> 0.3) and moved it into the same top-right corner slot
   a character face's own ATK number uses (the two kinds never share a
   face, so there's no real conflict reusing the position) - the amount
   badge stays where it always was, bottom-right.

3. **No way to tell which die is which.** "I don't really know which
   dice are Tardigrades and which one is a Pangolin, etc." Real gaps,
   not just a wish: an energy-only face (Surge, or a Character's own
   pure-energy face) carried NO identity at all - not even the old
   external avatar badge, which only ever showed on STAT faces
   (`die.effectiveAttack !== null` gated it out entirely for anything
   else). And the "Surge" text label itself was wrong for a non-
   Tardigrade: `effectiveAttack === null` is true for ANY pure-energy
   face, Tardigrade or not, so a Character's own energy face was
   mislabeled "Surge" too. Fixed both: added a generic `TardigradeIcon`
   glyph (icons.tsx) for the one die shape with no CardId/card avatar of
   its own, and every `CubeFace` (dieFaces.ts) now carries an optional
   `avatar` - the die's OWN identity icon, independent of which face is
   currently showing - which DieCube renders centered on every face,
   stat or energy alike. Real Character cards still only show an avatar
   for the 8 already-drawn icons (`CHARACTER_ICONS`) - the other 24 of
   the 32-card roster have no icon yet (a content gap, not a bug) and
   just show no avatar, same as before this pass. The now-redundant
   external avatar badge in `DieTile` is gone; the "Surge" label is now
   gated on `die.isTardigrade` specifically, falling back to the card's
   own name otherwise.

4. **"Character stat faces should definitely have that character's
   symbol in the center."** The same `avatar` field/rendering above
   covers this directly - it replaces the old generic diagonal-stripe
   `die-cube-art` placeholder texture that carried no identity at all.

`facesFor()`'s live/static merge (the static per-die table's printed
values overlaid with the DTO's real, modifier-inclusive numbers) now
also carries `avatar` across that merge - the freshly-built "showing"
face from the live DTO never has one of its own, so it has to come from
whichever static slot it landed in.

Verified live via headless Chromium across several purchases, including
one deliberately ordered to force a real overspend (an L2's 1 energy
plus an L1's 2, against a cost of 2): the L2 fully consumed to Out of
Play, the L1 correctly spun to its own L2 face (1 energy, a real 1A/1D
creature face) and stayed in the Reserve Pool - screenshotted mid- and
post-animation, zero console errors across every run. `tsc -b`/`oxlint`/
`vite build` clean; the full 914-test C# suite (untouched this pass)
still green.

## 2026-09-05 (final) - The other 24 character icons

Direct feedback: the user had already generated a full 45-mark avatar
sheet as its own Claude Artifact earlier in this project's life ("Forty-
five personal marks") - only the original 8 characters (from before the
roster expanded to 8-per-type) had ever actually been ported into
`icons.tsx`. Read that artifact back and ported the 24 remaining
matches by name: Grizzly Bear, Orca, Peregrine Falcon, Tiger, Stoat,
Cape Buffalo (Claw); Pangolin, Hermit Crab, Opossum, Queen Termite,
Snapping Turtle (Shell); Hummingbird, Mountain Goat, Monarch Butterfly,
Homing Pigeon, Greyhound, Albatross (Wing); Anglerfish, Cowbird, Magpie,
Raven, Elephant, Fox (Eye) - same conversion the original 8 already
used: `var(--seal-ink)` -> `currentColor`, `var(--seal-bg)` -> the
existing `CREAM` cutout constant.

One real gap: the sheet has 45 marks total (drawn before this roster's
final 32 names were locked in) and never covered Box Turtle at all -
Snapping Turtle already claimed its one turtle concept (a snapped twig,
for the bite). Drew a new one instead: a domed shell with hinge lines,
standing in for a box turtle's actual defining trait (a plastron hinge
that lets it seal shut completely, which a snapper's can't do) - same
plain-path style as everything else in the file, not an attempt to
match the sheet's own hand.

All 32 Characters across all four energy types now show a real per-card
icon, both in the roster strip and (via dieFaces.ts's `avatar` field
from the previous pass) centered on their own die's cube face. Verified
live across all four rosters (Claw/Shell/Wing/Eye, one full 8-card strip
each) - every roster chip renders an `<svg>`, zero console errors.
`tsc -b`/`oxlint`/`vite build` clean.

## 2026-09-08 - Reserve Pool compression, a side-profile Tardigrade, Champion energy size

Four small requests, one real bug found along the way:

1. **Compress the Reserve Pool tile.** Direct feedback: "the Level
   probably isn't need-to-know information, we could provide that on a
   click that would bring up the full character information (much like
   clicking on the character itself)." The die-cube face already prints
   the real attack/defense/fielding-cost numbers directly (a prior
   pass's own work), so the "L1"/"L2" text line underneath was pure
   repetition - dropped entirely for a stat face (kept only for an
   energy face, where "Surge"/the card's name is the one thing the face
   itself doesn't say), shrinking every rolled tile by a full text row.
   In its place: `DieTile` now opens its own info popover on click -
   name, per-level stat table (current level highlighted), the "plus 2
   faces of 2/1 faces of 1" energy note, and ability text, mirroring the
   roster's own card-popover almost exactly (a Tardigrade gets its fixed
   3-level spec instead, since it has no CardDef to read one from).
   Deliberately scoped to a die that ISN'T currently part of an active
   selection (`clickable`) - selecting a die (to pay energy, attack,
   block, ...) stays the one thing a click does there, unchanged; the
   popover only opens where a click would otherwise do nothing at all,
   which in practice is most of a Reserve Pool's on-screen lifetime
   (opponent's pool, your own outside Main, Field Zone outside Attack,
   ...). Own local open/closed state per tile (not lifted to page level
   like the roster's `openCardId`) - there is no reason only one die's
   info can be open at once - with the same outside-click-closes
   behavior, scoped to that one tile's own wrapper.

2. **Real bug, found while touching this area again:** a leftover
   `.dietile svg { width:26px; height:26px; }` rule - written for the
   external avatar badge this tile used to draw directly, removed two
   passes ago - was still a plain descendant selector, so it ALSO
   reached every svg inside the die-cube itself (DieCube.tsx's own
   avatar and per-face energy-type icon), forcing both to ~26px
   regardless of their real, deliberately different inline sizes. That
   silently undid the previous pass's "shrink the energy icon so it
   stops covering the stats" fix. Removed - DieCube's own icons are all
   explicitly sized already and need no blanket override.

3. **A side-profile Tardigrade.** Direct feedback: "I think that's more
   recognizable (acknowledging that we don't have a lot of space to work
   with)." Replaced the front-on blob-plus-four-dots glyph with a side
   silhouette - a rounded snout, a plump tapering body, four stubby legs
   trailing along the bottom - the classic "water bear" profile,
   simplified down to what still reads at 15-20px.

4. **Champion energy icon size.** "Pull the size... down just a tad...
   more on par with the size of the word." 15px -> 12px next to the
   Champion's name.

Also noted for later: the user may send in-house animal drawings to
match against or swap in for existing roster art (a bigger image
becomes a Champion portrait, a smaller one a Character's card art) -
store first if there's no obvious match rather than guessing, and don't
build/push a slotted image in unless asked to.

Verified live: Reserve Pool tiles visibly shorter with no console
errors; a Field Zone die's info popover opens with its Tardigrade spec
correctly highlighting L2 (the die's actual current level) and closes
on an outside click; the Champion box's energy glyph now reads closer
in weight to the name text beside it. `tsc -b`/`oxlint`/`vite build`
clean.

## 2026-09-08 (correction) - The Champion energy icon resize never actually took effect

Direct feedback: "It doesn't look like the champion energy changed at
all." It hadn't - same bug class as the die-cube one two entries above,
just a different rule: `.championbox-body svg { width:72px; height:72px;
... }` (written for the big Champion portrait/photo) is a plain
descendant selector, so it ALSO reached the small energy icon nested two
levels deeper inside `.championbox-name-row`, forcing it to 72px no
matter what the component's own `size` prop said. The earlier 15px ->
12px change was real in the JSX the whole time; it just never had any
visible effect. Fixed by scoping that rule to `.championbox-body >
svg`/`> img` (direct child only) - the portrait is a direct child of
`.championbox-body`, the energy icon isn't. Confirmed live: the icon's
own rendered `<svg>` now genuinely measures 12x12, not 72x72, and reads
right at a glance next to the name text.

## 2026-09-08 (later) - Variant A everywhere: a shared EnergyBadge

Direct feedback after comparing the two corner-badge color directions
live: "I like Variant A... Let's implement Variant A wherever we have
energy symbols and push to see how it looks live."

New shared component - `EnergyBadge({ type, size })` in `icons.tsx`: a
circle filled with the energy type's own color, its icon in white/cream
on top, sized however the call site needs. Replaces every ad-hoc energy
icon treatment in the app with the one look:

- **`PipBadge`** (a die's own rolled energy amount - DieTile below the
  cube, and the sideboard's "Energy in your pool" list): was a colored
  pill printing "N + icon"; now renders `amount` EnergyBadge circles in
  a row, overlapping when a die is worth two - the exact "no number,
  just symbols" idea the corner-badge mockup tested, now live wherever a
  die's energy actually shows, not just the exploration.
- **`CostIcon`** (roster chip costs, the roster/die-info popover's cost
  and per-level costs and its "plus 2 faces of..." note): was a bare
  colored icon with no backing shape - upgraded to the same badge,
  small, single circle, the printed number staying right next to it
  exactly as before (costs aren't capped at 2 the way a rolled face is,
  so these don't get the "repeat the icon" treatment - just the color
  treatment).
- **`ChampionBox`**'s energy glyph next to the Champion's name and
  **`DieCube`**'s own top-right energy-type corner icon (a pure-energy
  face like Surge) both switch to the same badge too - the die-cube
  corner one in particular had NO color coding at all before this
  (it inherited the die face's fixed cream text color, not its type).

Two more of the same "blanket CSS rule reaching through a new wrapper"
bugs this session has already hit twice, caught and fixed before they
shipped:
- `.rc-cost svg` / `.card-popover-cost svg` / etc. were forcing
  CostIcon's inner icon back to a fixed 11px regardless of the badge's
  own size prop, the same class of bug as the `.dietile svg`/
  `.championbox-body svg` ones from earlier passes - removed.
- `.championbox-name-row svg { color: var(--cc) }` would have won over
  EnergyBadge's own inherited cream color (inheritance loses to ANY
  matching rule regardless of specificity), turning the badge's white
  icon back to the accent hue - removed before it ever rendered wrong.

Verified live: champion box, roster chips, the roster/die-info popover,
Reserve Pool tiles, and the sideboard energy pool all show the new
circular badges consistently; a real purchase completes cleanly end to
end; a 4-energy sideboard pool (2 Wild singles + one 2-Claw pair)
renders as 4 correctly-grouped, correctly-overlapped circles matching
its own printed total. Zero console errors across every check.
`tsc -b`/`oxlint`/`vite build` clean.

## 2026-09-09 - Three real bugs in the last pass, fixed

Direct feedback on the just-shipped EnergyBadge work:

1. **The upper (opponent's) roster popover went off-screen entirely.**
   Real bug in the "open away from this board's own mat" rule from
   2026-09-05: for the opponent's board (roster-then-mat, sitting at the
   very TOP of the page), "away from the mat" meant opening upward -
   straight off the top of the screen, into nothing. Fixed to always
   open downward, per the correction: "that one is probably fine to show
   up underneath, since you won't be needing to click in the opponent's
   Reserve Pool while reading a character info." Your own roster
   (bottom of the page) has nothing below it either way, so this is safe
   there too - the mirrored ternary is gone entirely, just one direction
   now.

2. **The new EnergyBadge icon was too washed out.** "The white... needs
   to be BRIGHTER. It fades into the rest of the color." Two fixes: the
   icon's fill moved from the cream every other cutout/badge text on
   this page uses (`#fff8ec`) to pure white, and a 4-direction
   `drop-shadow` stack now outlines the icon's whole rendered silhouette
   - including its own internal cutout details, like Shell's spine line
   - without touching any icon's own stroke attributes (a plain CSS
   `stroke` override would have fought those, the same class of bug this
   session keeps finding in blanket selectors).

3. **The energy landed outside the die, on the wrong side.** "It's
   supposed to be in the lower left hand corner INSIDE the die itself."
   Two things were wrong at once: DieCube's own pre-existing top-right
   corner badge (for a pure-energy face like Surge) was never moved, and
   the NEW hybrid-face badge (added via an external wrapper span around
   DieCube) was pushed outside the die's own visible box entirely - a
   real bug, not just a wrong corner: `.die-cube-box` carries its own
   trailing `margin: 0 auto 3px`, so the wrapper's total height was 3px
   taller than the die itself, and every percentage-based position
   inside it landed on the WRONG box's edges. Fixed properly, not just
   patched: `DieCube` now takes an `energyCorner` prop and draws the
   badge itself, as a plain sibling of the rotating cube INSIDE
   `die-cube-box` - the same box the stat corners already position
   against - instead of a separate wrapper with its own sizing quirks.
   This also deleted the old, now-fully-redundant top-right badge/amount
   code for energy-kind faces, since the one true energy indicator now
   lives in one place for every face kind.

Verified live: the opponent's popover now renders fully on-screen
(positive y-coordinate, confirmed via bounding box, not just a
screenshot); a die's energy badge, zoomed to actual rendered size,
sits cleanly inside the die's own rounded corner with a crisp white
outlined icon; a full purchase still completes with zero console
errors across three separate rolls. `tsc -b`/`oxlint`/`vite build` clean.

## 2026-09-09 (later) - "Energy in your pool" moved to the rail

Direct feedback: "move the 'Energy in your Pool' to underneath the
Champion box, on top of the log." Moved from the left sideboard (where
it sat below the still-empty Basic Actions/Global Abilities panels) to
the right rail, between your own Champion box and the match log -
specifically YOUR energy, now sitting right above the log that already
tracks everything you've done with it, instead of grouped with two
panels that have no content in this game yet. Same panel/markup, just
relocated - `.sideboard-panel`'s styling (border/radius/background, no
fixed width) already adapts to whichever column width it sits in.

Verified live: sideboard now holds only Basic Actions and Global
Abilities; the rail reads Champion box -> Energy in your pool -> Log,
zero console errors.

(Separately, still open: comparing outline treatments for EnergyBadge's
icon after direct feedback that the shipped 4-direction drop-shadow
"just makes it muddy" - a quick side-by-side [no outline / the current
drop-shadow / a crisp SVG feMorphology outline / a soft centered glow]
was sent for a pick; the badge's own coloring is unchanged pending that.)

## 2026-09-09 (later still) - The muddy shadow becomes a real outline

Direct feedback: "I don't think the shadow makes it pop, I think it
just makes it muddy?" Rather than guess again, built a quick side-by-
side of the actual icon at its actual shipped sizes (12/14/16px, plus a
2x zoom for detail): plain white with no outline at all, the shipped
4-direction drop-shadow, a crisp SVG `feMorphology`-based outline, and a
soft centered glow. The zoomed comparison made the difference obvious -
the drop-shadow's blur reads as a soft, uneven halo, while
`feMorphology` (dilate the icon's own alpha mask into a hard-edged
shape, flood it solid, then merge the real icon on top) gives a
genuinely crisp line around the WHOLE silhouette, including internal
cutout details like Shell's spine line, without touching any icon's own
stroke attributes. User picked this one ("Let's go with D").

One filter def, not one per badge - SVG filters only need to exist once
per document; every `EnergyBadge` instance just references it via
`filter: url(#energy-badge-outline)`. Rendered once in
`DiceKingdomPage`'s own JSX (a hidden 0x0 svg, same pattern index-doc
symbol sheets use), since that's the branch every `EnergyBadge` call
site actually lives inside.

Also worth recording, since it's the reason none of this had to be
perfect: "the shield and the eye would have different color
backgrounds, right? So the color is the main differentiator when it's
really small, thus it's ok if the symbol isn't perfect." Confirmed -
each energy type's badge is colored to its own accent, so the outline's
job is just to keep the white symbol from visually fusing with that
color, not to make the symbol itself legible in isolation.

Verified live at real size (not just zoomed): the corner badge on a
Reserve Pool die, the Champion box's glyph, and the "Energy in your
pool" pips all show a crisp, non-muddy outline; a full purchase still
completes cleanly, zero console errors across two rolls. `tsc -b`/
`oxlint`/`vite build` clean.

## Die tile chrome removed, Bag rejoins Drawn This Turn, Used/Out of Play go icon-only (2026-09-10)

Three more compaction requests, all direct feedback in the same
message, now that every die's own face carries its full identity
(avatar, stats, energy corner):

1. **`.dietile`'s box was pure overhead.** It used to need a border,
   accent border-top, tinted background and shadow to read as a
   distinct object; now that the die-cube itself is the object, that
   chrome is gone by default (`border: 1.5px solid transparent`,
   `background: transparent`, no box-shadow) and reserved entirely for
   `.picked` - "we no longer need so much space in the box around the
   die... we can use it to help indicate selection." `.clickable:hover`
   also switched from a flat `var(--text-h)` border to `var(--cc,
   var(--text-h))`, so the hover cue uses the same accent color the
   selected ring does.
2. **Bag rejoins Drawn This Turn.** Direct feedback: "one thing we lost
   from the design was that 'Bag' was supposed to go underneath the
   Reserve Pool with 'Drawn this Turn'." Bag moved out of the left
   column (where it sat stacked under Used Pile/Out of Play) into a new
   `.mat-stash` grid cell in the middle column, alongside Drawn This
   Turn - the vacated left-column cell is now `.` (empty), matching the
   grid's own existing convention for a deliberately unused cell. Both
   zones inside `.mat-stash` get tighter padding and a `.dierow` that
   scrolls sideways instead of wrapping, so the whole cell reads as "one
   die row tall" rather than a full independent zone - "those areas...
   normally don't need their exact contents seen."
3. **Used Pile/Out of Play show just the symbol.** `DieTile`'s
   `!isRolled` branch now checks `zone === "UsedPile" || zone ===
   "OutOfPlay"` and renders only the card's `CHARACTER_ICONS` avatar (or
   `TardigradePhotoIcon` for a basic-pool die) - no name text, no stat
   placeholder, no energy. Bag/Drawn/Carried keep the existing
   name+stat text (they're either collapsed behind Bag's own button or
   rarely accumulate enough dice to need it).

Verified live: default reserve-pool tiles now show only the die cube
(no box), the selected-ring still reads clearly against that plain
background, both mats' Bag+Drawn cells sit correctly under Reserve
Pool with the bag popover still opening and overlaying correctly, and
an Out of Play die renders as just its Tardigrade sketch icon. Zero
console errors. `tsc -b`/`oxlint`/`vite build` clean.

## One more compaction pass: Bag/Drawn/Carried to a single line, real Tardigrade symbol, quieter selection (2026-09-10, later)

The previous round's Bag+Drawn cell still read as "way too much vertical
space." Four more pieces of direct feedback, same session:

1. **Bag, Drawn This Turn and Carried From Prep onto one line.** All
   three collapse into a single `.mat-tray` grid row spanning the mat's
   full width (previously Bag+Drawn were a two-zone cell in the middle
   column, Carried still separate on the right) - "the whole thing
   should be only as vertically high as one line of text." Each is now
   just `trayItem(label, count)` - plain text, no dice tiles, no "click
   to inspect" hint ("we'll let them figure that out"). Bag alone keeps
   its click-to-open inspector popover (contents are public info), now
   anchored off `.tray` itself rather than a `.zone-bag` wrapper that no
   longer exists. Found and fixed a real bug in the same pass: the
   button's own CSS used `font: inherit`, but a `<button>` doesn't
   reliably inherit font from its parent across browsers - "Bag" was
   rendering in the page's default button font while "Drawn This Turn"/
   "Carried From Prep" (plain `<span>`s) used the intended compact
   style. Fixed by giving `.tray-item`/`.tray-item-btn` the same
   explicit font declaration instead of leaning on inheritance.
2. **Out of Play/Used Pile's icon was the wrong icon.** The previous
   pass used `TardigradePhotoIcon` (the house-drawn sketch photo) for a
   basic-pool die's icon-only tile - direct feedback: "I was thinking
   just the same tardigrade symbol that is on the dice in the reserve
   pool," i.e. the vector `TardigradeIcon` `dieFaces.ts` already puts on
   every Tardigrade face's `avatar`. Swapped to that (the photo icon
   stays where it was for the bigger card-info popover header).
3. **"Surge" and "Selected" text dropped.** `DieTile`'s label fallback
   no longer prints "Surge" for a Tardigrade's energy face - the corner
   badge already says what it is. `rolledZone`'s Roll & Reroll label
   override no longer prints "Selected" either - "the bright... border
   should be plenty." Only "rerolled" survives as a labelOverride value.
4. **The tile itself shrank to match.** `.dietile`'s `width: 78px` (a
   leftover from when it held level text and a name label) is now
   `width: auto` with 3px padding - "that border can be a lot smaller,
   just slightly bigger than the die itself." The tile sizes to
   whatever it's actually showing (the 34px die, a 30px icon, or a
   label) instead of reserving fixed space no longer needed once
   Surge/Selected's text was removed. `.picked`'s ring dropped from 3px
   to 2px to match the smaller box.

Verified live: the tray reads as one line under Reserve Pool/Prep Area
on both mats (mat height dropped visibly in a full-page screenshot),
"Bag" now matches the other two labels' font after the fix, a picked
Reserve Pool die shows a tight ring with no "Selected" text and no
"Surge" label, and an Out of Play die shows the same vector Tardigrade
symbol as the die's own face. Zero console errors. `tsc -b`/`oxlint`/
`vite build` clean.

## Out of Play's icon needed a frame back, and the right rail no longer jumps on selection (2026-09-10, later still)

Two more pieces of the same compaction thread:

1. **Icon-only tiles got their "die" back.** Direct feedback: "we lost
   the 'die' part of the die in Out of Play - we probably want to keep
   that or things will get messy when there's multiple things in
   there." The bare icon (no frame at all) read as just a floating
   symbol once more than one sat in a pile. `.tile-icon` is now a flat
   34px tinted square (`color-mix` against the tile's own `--cc` accent,
   the same custom property `.dietile`'s style prop already sets) - a
   real "die-shaped" object again without bringing back the 3D cube
   (these never roll, so that would be misleading).
2. **The right rail no longer grows when a die is selected.** This was
   a known, previously-documented tradeoff (see `.dk-row-lane`'s own
   remarks above) - picking a die used to insert a whole "Selected die"
   panel above the action buttons, shifting them down and reading as
   jarring rather than informative. Direct feedback confirmed the panel
   itself is no longer load-bearing now that the die-cube shows its own
   stats and the selection ring is unambiguous: removed outright (along
   with `dieLabel`/`characterFaceInfo`, now unused in this file). The
   "Now" panel's eyebrow moved onto the same line as the step title
   (`<span>` inside the `<h3>` instead of its own line above it) -
   "the 'Now' box could at least combine the 'NOW' on the same line as
   ...the stage." And the rail's action-row buttons shrank
   (`.controlcenter button.btn` - scoped there, not global, so the
   pre-game "Start Match" button and card-popover buttons keep their
   normal size) plus three long two-button pairs got shorter labels so
   they'd actually fit the 300px rail on one line instead of wrapping:
   "Reroll Selected (n)" → "Reroll (n)", "Continue to Main Phase" →
   "Continue", "Proceed to Attack"/"Skip Attack Step" → "Attack"/"Skip
   Attack". Padding alone wasn't enough here - the button text itself
   was wider than the whole rail column, confirmed by measuring actual
   rendered button widths rather than guessing at a padding value.

Verified live: selecting a Reserve Pool die during Roll & Reroll no
longer changes `.controlcenter`'s measured height at all (127.6px
before and after, previously grew by 40px), "Reroll (1)"/"Continue"
and "Attack"/"Skip Attack" both render as one row instead of wrapping,
and an Out of Play die shows its Tardigrade icon inside a tinted
square frame. Zero console errors. `tsc -b`/`oxlint`/`vite build`
clean.

## Two small bugs from the icon-only piles: bad grouping, and a lopsided Wing badge (2026-09-10, later still)

1. **Out of Play wasn't grouping identical dice.** Direct feedback: "the
   spent tardigrade dice went to Out of Play, [and] grouped into
   separate piles. They're all the same die, so... they could be
   grouped all together... this does work properly when they go to the
   Used Pile." Real bug, not a display quirk: `groupDice`'s key included
   `level`/`effectiveAttack`/`effectiveDefense`/`energySymbolId`/
   `energyAmount` - fields describing whatever face a die was rolled to
   right before it left play, which differ die-to-die even for
   identical Tardigrades, and which the icon-only tile doesn't show
   anyway. Used Pile "worked" by coincidence (its dice more often
   happened to share a face); Out of Play just as often didn't. Added
   `ICON_ONLY_ZONES` (the same `UsedPile`/`OutOfPlay` set `DieTile`
   already checks for its icon-only rendering, now shared instead of
   duplicated) and grouped by card identity alone there, ignoring the
   rolled-face fields entirely.
2. **Wing's badge had a stray black sliver.** "Something a little off
   with the circle border for Wing energy. Didn't notice it with Claw."
   Measured it rather than guessing: every other energy glyph's
   farthest point from center sits around 25-26 units (well inside the
   64-unit viewBox's own 32-unit inscribed circle), but Wing's
   unscaled path reached about 34 - just far enough that the outline
   filter's dilate poked a sliver past the badge's circular edge,
   invisible at Claw's size but visible once dilated on a shape that
   already grazed the boundary. Wrapped `WingIcon`'s path in a
   `scale(0.75)` about center, bringing its own farthest point in line
   with the other three glyphs.

Verified live: Used Pile shows one grouped `×3` Tardigrade tile after
several were sent there across three turns (screenshot confirms real
in-game state, not a synthetic count), and Golden Eagle's champion box
energy badge renders as a clean circle with no black sliver outside
its edge, sitting alongside Wolf's Claw badge which was already clean.
Zero console errors (aside from an expected, pre-existing 403 from the
other seat's auto-skip attempt - unrelated to this change). `tsc -b`/
`oxlint`/`vite build` clean.

## First real mobile check: two responsive bugs, one of them long-standing (2026-09-07)

User checked the live site on an iPhone SE (375px CSS width) for the
first time this session and sent two screenshots.

1. **The step ribbon collapsed into a stack of near-circular blobs.**
   Measured rather than guessed (`.step-ribbon`'s own bounding box and
   computed styles at 375px): its width was 0px, and `.dk-titlebar`'s
   `grid-template-columns` computed to `200px 0px 300px` - the DESKTOP
   3-column template, not the `1fr` mobile override that was supposed
   to apply under 1100px. Root cause: that override (`@media
   (max-width: 1100px) { .dicekingdom .dk-titlebar { grid-template-
   columns: 1fr; } ... }`) sat near the TOP of the file, textually
   BEFORE `.dk-titlebar`'s own unconditional base rule further down
   (~line 357). Equal specificity, so cascade order decides - and a
   later unconditional rule beats an earlier media-conditional one
   regardless of whether the media query matches. That override had
   never actually applied at ANY viewport width since it was written;
   only `.dk-layout`'s neighboring override in the same block worked,
   because IT (unlike `.dk-titlebar`/`.step-ribbon`/`.dk-titlebar-
   right`) sits after its own base rule. With the middle column
   collapsed to 0px, `.step-ribbon` (a flex container with `flex-wrap:
   wrap`) had zero width to wrap chips into, so each chip's pill text
   wrapped onto 2-3 lines inside a barely-wider-than-tall blob instead.
   Fixed by moving the three misplaced overrides into a second `@media
   (max-width: 1100px)` block positioned right after their real base
   rules (next to `.step-ribbon`'s own definition), leaving only
   `.dk-layout`'s (correctly-placed) override in the original block.
2. **The mat's own 3-column layout fully reflowed on mobile instead of
   shrinking.** Not a bug exactly - a deliberate `@media (max-width:
   900px)` fallback that had never been checked on a real phone - but
   direct feedback rejected the design: "these should keep their
   position, just be smaller." The old fallback swapped
   `grid-template-areas` entirely, turning Used Pile/Out of Play
   (previously the left column) and Reserve Pool/Prep Area (previously
   middle-right) into one flat list of full-width rows in a different
   order - losing the whole left/middle-right relationship, not just
   shrinking it. Replaced with two narrower breakpoints (900px, then
   480px) that keep the EXACT SAME `grid-template-areas` as desktop and
   only shrink the two fixed side columns (196px → 130px → 88px) plus
   the grid gap - the shape survives, just smaller, matching what was
   asked.

Verified live at 375×667 (iPhone SE's real CSS viewport, not just
"under 900px" in the abstract): `.step-ribbon`'s bounding box is now
341px wide (full titlebar width, not 0), and `.dk-row-you .mat`'s
computed `grid-template-columns` reads `88px 139px 88px` - narrower,
same 3-column shape, not a single-column stack. Zero console errors.
`tsc -b`/`oxlint`/`vite build` clean.

## Mobile round 2: a fixed bottom action bar, and shrinking the empty Attack Zone (2026-09-08)

Confirmed CI/CD deploys automatically on push to `main` (corrects an
earlier assistant assumption this same round - the trigger lives
outside the repo, not as a `.github/workflows` file, so its absence
here isn't evidence there's no pipeline; saved to memory so this
doesn't get asserted wrong again). Two more pieces of direct feedback
from the live mobile check:

1. **The three page sections (sideboard, board, rail) stacking meant
   the turn controls sat far below the board itself.** "I have to click
   Roll then scroll up to see what happened, then scroll down to do the
   next action." The existing mobile order was opp/lane/you/railtop/
   railmid(turn controls)/railbottom/side - with a full board (both
   players' mats plus the combat lane) all stacked above it, `.dk-rail-
   mid`'s natural position could be several screens down. `position:
   sticky` wouldn't fix this - sticky only starts holding an element in
   place once the page has scrolled TO roughly its own natural resting
   position, which does nothing for someone reading content further up
   the page (e.g. still on the opponent's board, screens above where
   the controls naturally sit). Used `position: fixed` instead - pinned
   to the viewport's own bottom edge unconditionally, reachable from
   anywhere in the scroll - with `max-height: 42vh` + internal scroll
   for the rare tall state (a pending-choice chip list, block-
   assignment instructions) so it can never eat the whole screen, and
   220px of extra bottom padding added to the page so the fixed bar
   never permanently covers the sideboard's last panel.
2. **The Attack Zone's empty slots were much taller than their own
   content.** "Attack zone can probably be much smaller." `.lane-grid`'s
   two rows had a flat `minmax(84px, auto)` - a figure inherited from
   when a DieTile was a 166px (then 94px) card; today's tile is a
   ~40px auto-width die, so an empty "NO BLOCKER" slot was carrying
   nearly twice the height its own label needs. Dropped to `minmax(50px,
   auto)` (and the seam's middle row 34px → 26px to match), plus
   tightened `.combat-lane`'s own padding/margin. Also acted on the
   more general "there might be more space than necessary between
   [dice]" - `.dierow`'s gap dropped from 9px to 6px, sized for the old
   bigger tiles, now more than these ~40px auto-width ones need.

Verified live at 375×667: `.dk-rail-mid` computes `position: fixed;
bottom: 0px` regardless of scroll position - screenshotted at the very
top of the page (before any scrolling) and again after scrolling to
the opponent's board, both show the Now panel/action button still
pinned at the bottom without needing to scroll to it. The Attack Zone's
"NO BLOCKER" placeholders read visibly tighter. Zero console errors.
`tsc -b`/`oxlint`/`vite build` clean.

## Mobile round 3: one-line turn controls, collapsed Attack Zone/opponent roster, icon titlebar (2026-09-08)

Four more compression requests, all landing together:

1. **Turn controls collapsed to one line.** Direct feedback: "move the
   reminder text of what the turn is to an info icon... combine the
   turn name and the button on the same line, so that the whole turn
   control box is one thin line." The old `.now-header` (eyebrow+title,
   then a full guidance sentence on its own line, THEN a separate
   action-row block below) is gone. `STEP_GUIDANCE[step].text` moved
   behind a new `NowInfoButton` - a small icon that opens a popover with
   the reminder text, instead of always printing it. The whole render
   chain for "what does this step need from the player" was pulled out
   of JSX into a plain `stepContent`/`stepContentIsPanel` pair of values
   computed once, so it can be placed either inline next to the title
   (`.now-bar`, the common case: a button or two, or a short "waiting
   on..." note) or on its own line below (only for a REAL situational
   panel - a pending-choice picker, the Assign Blockers paragraph -
   which isn't the generic per-step reminder NowInfoButton now hides,
   and genuinely can't fit on one line). Verified the common case
   (Roll & Reroll, Main, even "Attack · Declare Attackers") all render
   as one row; the panel cases still show their real instructions below
   the one-line title bar.
2. **The opponent's roster collapses to icon-only.** "For now, I would
   make the whole thing one section, and tapping anywhere in there
   would expand to show the full details of all eight cards." Your own
   roster (what you're actually shopping from) is untouched;
   `renderBoard` now branches on `playerId !== you` to render a single
   full-width `.roster-collapse-toggle` button (icons only) in place of
   the detailed grid, toggled by one new `oppRosterOpen` state (not
   per-board - only the opponent's side ever needs it).
3. **"Dark mode"/"How to play" became icon buttons sharing the ribbon's
   row.** Two new small glyphs in `icons.tsx` - `HelpIcon` (a plain "?"
   circle) and `SettingsIcon` (a sliders glyph, not a literal gear - "a
   hand-drawn gear tooth shape is fussy at this size... reads as
   'settings' just as clearly"). `ThemeToggle.tsx` gained `SettingsMenu`
   (icon + popover holding the existing light/dark toggle); the old
   `<details className="how">` became `HowToPlayMenu`, same pattern.
   `.dk-titlebar`/`.step-ribbon`/`.dk-titlebar-right` all go `display:
   contents` on mobile so the ribbon chips and the two icon buttons
   become direct flex items of one shared row, wrapping together
   instead of each claiming its own full-width block. Real bug found
   measuring rather than guessing: the icon buttons' own popover was
   still anchored (`right: 0`) to their own tiny 26px wrap, which could
   land anywhere along the wrapped row - a 220px popover anchored to a
   button near the LEFT of the row ran off the left edge of the
   viewport. Fixed by making `.dk-titlebar` itself (a wide, stable box)
   the positioned ancestor instead, scoped narrowly (`.dk-titlebar
   .icon-menu-wrap { position: static }`) so NowInfoButton's own,
   differently-anchored popover elsewhere on the page is untouched.
4. **The Attack Zone collapses outside the three combat steps.** "Attack
   zone can also probably be collapsed until we get to those stages."
   Reverses an earlier decision (`renderAttackZone`'s own comment used
   to argue for "always visible, reads as a permanent part of the
   table") in favor of compression - collapses to a single-line
   `.combat-lane-collapsed` bar unless the current step is one of
   `select-attackers`/`assign-blockers`/`action-global-window`, OR a
   real attacker is still sitting in the zone (never hides actual game
   state, only the three-empty-placeholder-column view nobody's using
   yet).

Also fixed along the way: `.dk-rail-mid`'s `overflow-y: auto` (added
last round to cap a rare tall panel) would have clipped NowInfoButton's
popover, which has to open UPWARD out of that same fixed bar on mobile
(downward would run off the bottom of the screen). Moved the
scroll/max-height onto a new `.now-panel-scroll` wrapper around ONLY
the panel-case content, leaving `.dk-rail-mid` itself unclipped.

Verified live at 375×667: the titlebar renders as ribbon-chips-then-
icons all wrapping together; the opponent's roster toggle expands to
the full 8-card grid on tap; Roll & Reroll's title+button and even the
longer "Attack · Declare Attackers" + "Confirm Attackers (0)" both
render as one row; the Attack Zone shows a collapsed one-line bar
during Clear & Draw/Roll & Reroll/Main and the real lane once Attack is
reached; NowInfoButton's and the titlebar icons' popovers both measured
fully inside the 375px viewport (a real off-screen bug in the titlebar
case, caught by measuring bounding boxes rather than trusting the
screenshot). Zero console errors throughout. `tsc -b`/`oxlint`/`vite
build` clean.

## Mobile round 4: a possible tap-swallowing bug, the collapsed opponent mat, and a merged Champion+life panel (2026-09-08)

1. **"If I scroll at all, then trying to tap 'Draw' or 'Roll' resets
   the screen and does not actually Draw or Roll."** This is a known
   class of iOS Safari bug, not something reproducible in this sandbox
   (headless Chromium doesn't share WebKit's specific scroll/tap
   handling) - a `position: fixed` element can lag a repaint behind the
   page during/right after a scroll gesture, so the first tap right
   after scrolling can hit-test against a stale position instead of
   reaching the button, and gets swallowed by Safari settling its own
   scroll/chrome state. Applied the two standard mitigations: promoting
   `.dk-rail-mid` to its own GPU compositor layer (`transform:
   translateZ(0)` + `will-change: transform`, so it's genuinely pinned
   rather than repainted late) and `touch-action: manipulation` on its
   buttons (removes the ~300ms tap-vs-double-tap-zoom delay that makes
   a tap easy to misread as part of a scroll). Can't confirm from here
   whether this fully resolves it on a real device - worth a direct
   follow-up check.
2. **The user's own suggested fallback, implemented as a genuine
   improvement regardless of (1):** "can we default the focus to the
   active player's mat?" A new effect scrolls whichever board belongs
   to `game.activePlayerId` into view after every action (keyed on
   `game.version`, so it fires on Draw/Roll too, not just an actual
   turn change - the exact case (1) was about), using `block: "nearest"`
   so it's a no-op whenever that board is already adequately visible -
   only a genuinely bad scroll position gets corrected.
3. **The opponent's mat compresses to Field Zone + one-row Reserve Pool
   when it isn't their turn.** "The main thing I would want to know
   about my opponent's mat on my turn, aside from the characters in
   their field zone, is if they have any energy in their Reserve Pool
   to spend on globals." `renderBoard` now builds a `collapsedMat`
   alongside the full `mat` (sharing the same `fieldZone` JSX so Field
   Zone's own clickable/picked behavior - needed for Assign Blockers
   too - isn't duplicated) and picks between them via `collapsedOpponent
   = isOpponentBoard && !isActivePlayer`. Used Pile/Out of Play/Prep
   Area/the Bag tray/all drop out entirely rather than shrinking further
   - none of them inform a decision on your own turn. Uses the same
   `activePlayerId`-based definition of "active" the rest of the page
   already uses for turn-mine/turn-waiting styling, so it stays
   collapsed through Assign Blockers (activePlayerId is the attacker
   for the whole turn, not the momentary defender) - Field Zone (what
   blocking actually needs) is still fully visible and clickable there
   either way, so this doesn't block play.
4. **ChampionBox merged with the life total it used to sit next to.**
   "Since we've moved the turn controls, [the Champion panel] would
   just be the score and the champion details - can we try to squeeze
   that into a thin panel?" Dropped the standalone `LifeBox`/
   `.life-panels` grid and the old role-label-then-72px-portrait layout
   entirely; `ChampionBox` is now one row (a 30px icon, role, name,
   energy badge, and the life total at the far end) with the passive
   text as a second, smaller line underneath - measured at 45px tall
   total, down from well over 100px.

Verified live at 375×667: the merged Champion box renders as one
45px-tall row per player; the opponent's mat collapses to Roster (icon
bar, from last round) + Field 0 + Reserve Pool 0 while it's the user's
own turn, and your own mat stays fully expanded (Used Pile/Out of
Play/Prep Area/roster all present) at the same time; ending the turn
and reaching Assign Blockers keeps the opponent's mat collapsed
(matches the existing `activePlayerId` semantics the rest of the page
already uses) while Field Zone stays clickable. Zero console errors
(aside from the expected, pre-existing 403 from the other seat's
auto-skip attempt). `tsc -b`/`oxlint`/`vite build` clean.

## The Champion panel, round 2: a real miscommunication, sorted out with a preview question (2026-09-08)

The merged Champion+life "thin panel" from last round wasn't what was
meant: "I said 'panel' and you translated that to 'row.' I meant
column, like we had it previously." Asked a clarifying question with
two ASCII mockups rather than guess a third time (this was already a
second miss on the same feature) - the user couldn't pick from
description alone, and gave the actual requirement instead: "we'd
always want the life totals to be visible somewhere, I don't want to
have to scroll to see if I'm winning or losing... the 'energy in your
pool' is only helpful if it's visible when I'm purchasing a character."
Two genuinely separate asks, not one:

1. **Life totals need to be scroll-proof, not just "compact."** New
   `Scoreboard` component - a real `position: fixed` bar pinned to the
   TOP edge on the narrow layout (same technique `.dk-rail-mid` already
   uses for the bottom, mirrored), showing just both players' life
   totals. Nothing else lives here on purpose: champion name/ability is
   reference material worth looking up occasionally, not something
   that earns a permanent pin the way "am I winning" does. `.dicekingdom`
   gained matching `padding-top` so the fixed bar doesn't cover the
   ribbon, same as the bottom bar's own `padding-bottom`; the CSS block
   sits after `.scoreboard`'s base rule, not up with `.dk-layout`'s
   override - the ordering bug from `.dk-titlebar`'s fix two rounds ago
   would have silently repeated here otherwise.
2. **"Column, like we had it previously" was about ChampionBox's own
   text, not a request to keep life inside it.** With life now living
   in Scoreboard, ChampionBox went back to a real stacked column - role
   label, then name+badge, then the passive note, each its own line -
   instead of the squeezed single row from last round. No life line in
   here at all any more.
3. **"Energy in your pool" gated to the Main step**, where spending it
   is actually on the table, instead of a permanent sideboard panel -
   direct feedback confirmed it's only useful during purchasing.

Verified live at 375×667: `.scoreboard` computes `position: fixed; top:
0px` and measures 55px tall regardless of scroll position (screenshot
at the initial view and again after scrolling past several boards'
worth of content both show it pinned); ChampionBox reads as three
stacked lines (ROLE, then NAME+badge, then the ability note) with no
life number in it; "Energy in your pool" is absent outside Main and
present with the right total once there. Zero console errors. `tsc
-b`/`oxlint`/`vite build` clean.

## Scoreboard, round 3: from a top bar to a right-edge column (2026-09-09)

Direct feedback on the fixed top bar from the previous round: "That
could work... but vertical space will be at a premium. I'd like to try
it with that information in a thin column on the right side." Right -
a full-width top bar spends the one resource this page is shortest on
(height, with everything already stacked into one long scroll);
`.scoreboard` turns sideways instead on the narrow layout, same fixed
technique, different edge: `position: fixed; top: 0; right: 0; bottom:
0;`, 54px wide, both life totals stacked vertically instead of side by
side. "Opponent" shortened to "Opp" in the JSX (matches "You"'s own
length) - it doesn't fit a 54px column at any reasonable size.
`.dk-rail-mid` (the bottom action bar) now stops at `right: 54px`
instead of running the full width, so the two fixed elements sit flush
in the corner instead of overlapping; `.dicekingdom` swapped its
`padding-top` (needed for the old top-bar version) for a matching
`padding-right`. Real bug caught before it shipped, by measuring
rather than eyeballing: `.scoreboard`'s `width: 54px` didn't include
its own padding (default `box-sizing: content-box`), so the rendered
column was actually 62px wide while the page had only reserved 58px
and the bottom bar only backed off 54px - an 8px sliver where the two
fixed elements genuinely overlapped. Added `box-sizing: border-box`,
confirmed via bounding boxes: column now spans exactly 321-375px,
bottom bar exactly 0-321px, no gap and no overlap, and
`document.documentElement.scrollWidth` stays at the true 375px (no
horizontal scrollbar introduced).

Verified live at 375×667: screenshotted at the top of the page and
again after scrolling well past the roster/mat/attack-zone content -
both life totals stay pinned to the right edge throughout, spending no
vertical space at all. Zero console errors. `tsc -b`/`oxlint`/`vite
build` clean.

## Everything moves into the column, and a two-column champion picker (2026-09-09)

Direct feedback: "move the other stuff from that panel into that
column as well - the active team text and the champions. We may not be
able to fit the Champion name on there, but we should be able to fit
the Avatar, the symbol, and the energy vertically. 'Invite' and 'Copy
link' can stay on the bottom for now." Plus, separately: "can we do the
two column approach with the 'select Champion' screen as well, so I
don't have to scroll?"

1. **ChampionBox and "Active: X" both folded into Scoreboard.** The
   two standalone ChampionBox panels (dk-rail-top/-bottom) and the
   `.active-line` "Active: X" text are gone. Each `ScoreboardSide` is
   now a tappable summary - label (You/Opp), avatar, energy badge, life
   - with a popover for the one thing that doesn't fit a 54px column:
   name and ability text (same "always-visible summary, full detail on
   tap" split this session already used for NowInfoButton and How to
   Play). "Active" is a highlight on the live side now
   (`.scoreboard-side.turn-mine`/`.turn-waiting`, the same green/amber-
   grey the rest of the page already uses for this) instead of separate
   text - the column's own You/Opp labels already say which side is
   which. The popover opens LEFTWARD on the narrow layout (`right:
   calc(100% + 6px)`, vertically centered on its own trigger rather
   than anchored to a top/bottom edge) - the column is pinned at the
   viewport's own right edge, so a normal downward-right popover would
   run off-screen, and a segment can sit anywhere along the column's
   full height (unlike the titlebar/turn-controls popovers, which
   always open from a predictable edge).
2. **The pre-game champion picker went two columns too** (Player 1 |
   Player 2, side by side) - also collapsed the two players' near-
   identical JSX blocks into one `.map()` over `[{label, value,
   setValue}, ...]` along the way, since they'd only ever differed by
   which setup state they wrote to. Real bug caught by measuring, not
   assuming the restructure alone was enough: `document.body.
   scrollHeight` was still 824px against a 667px viewport after the
   two-column change. Root cause wasn't the picker at all - `.dicekingdom
   { padding-bottom: 220px }` and `{ padding-right: 58px }` (reserved
   for the in-game fixed action bar and Scoreboard column) apply to
   BOTH screens, since the pre-game screen shares the exact same
   `.dicekingdom` wrapper class, but the pre-game screen has neither
   fixed element - that padding was pure dead space there. Fixed with
   `:has()`: `.dicekingdom:has(.dk-rail-mid)` and `.dicekingdom:has(.scoreboard)`
   scope each padding to instances that actually contain the element
   it's clearing space for, no JSX changes needed. Also trimmed the
   intro text (h1/dek) and `.panel` padding at the same breakpoint,
   and `.champ-opt`'s own icon/padding/font, since even the narrower
   per-column champ-pick grid was taller than it needed to be.

Verified live at 375×667: the pre-game screen's `document.body.
scrollHeight` now reads exactly 667px (was 824px) - the picker fits
with zero scrolling, confirmed against a real measurement rather than a
screenshot alone. In-game, both `.scoreboard-side`s show label/avatar/
badge/life stacked correctly, the "You" side (active) shows the green
turn-mine glow, tapping it opens a popover with the champion's real
name and ability text fully inside the viewport, and `.championbox`
count is 0 (confirms the old panels are actually gone, not just
hidden). Zero console errors. `tsc -b`/`oxlint`/`vite build` clean.

## Scoreboard polish: a divider, a mirrored column, and another blanket-selector bug (2026-09-09)

Three quick pieces of direct feedback on the last round's work:

1. **A vertical divider between Player 1 and Player 2** on the pre-game
   picker - "let's put a vertical bar or some visual distinction
   between them." `.champ-pick-column:first-child { border-right: 1px
   solid var(--border); }` - two side-by-side 4-option grids with the
   same options read as one continuous block without it.
2. **The scoreboard column mirrored so both life totals sit adjacent.**
   "Mirror the life, energy, etc on the bottom, so that both life
   totals are directly next to each other." Opponent (top of the
   column) still reads label→avatar→badge→life top-to-bottom, so its
   life lands at the bottom of its own segment; `.scoreboard-side.mine
   .scoreboard-tap { flex-direction: column-reverse; }` flips only
   "mine" (the bottom segment) to life→badge→avatar→label, so ITS life
   lands at the TOP of its segment - the two numbers now sit right next
   to each other across the gap, with both labels pushed out to the
   column's outer top/bottom edges instead.
3. **The Claw energy badge was bleeding outside its circle.** Same
   recurring bug class as `.championbox-body`'s own fix earlier this
   session, caught on sight this time rather than needing a side-by-
   side comparison: `.scoreboard-tap svg` (written for the champion
   avatar, a DIRECT child) was a descendant selector, so it also
   reached EnergyBadge's own inner icon svg two levels deeper inside
   `.energy-badge`, forcing it to 26px regardless of EnergyBadge's own
   14px sizing and ballooning it past its own circle's edge. Scoped to
   `.scoreboard-tap > svg`/`> img` (direct children only) - the same
   fix shape as every other instance of this bug pattern this session.

Verified live at 375×667, deviceScaleFactor 4 for a close look at the
badge specifically: the picker shows a visible rule between the two
columns; the scoreboard screenshot shows both "20"s adjacent across the
gap with OPP/YOU pushed to the outer edges; a cropped screenshot of
just `.energy-badge` shows a clean circle with the Claw icon fully
contained, no bleed. Zero console errors. `tsc -b`/`oxlint`/`vite
build` clean.

## Champion packs mix energies now, not monochrome (2026-09-12)

Direct feedback: "mix up the energies a bit. So for the champion packs
of eight characters, I'd like to keep four of them the same energy type
as the champion, two of them be a 'symbiotic' energy type, and then one
of each of the others," plus a cost-curve rule for the four own-type
picks ("at least one 2-cost and one 3-cost... a mid-range and a higher
cost power card" - the other four "can be more or less random"). Every
Champion's pack had been all 8 of its own energy type since the
2026-09-06/07 roster expansion - this was the first real break from
that.

**Symbiotic pairing, confirmed with the user**: Claw<->Wing (aggro +
tempo - hit fast) and Shell<->Eye (defense + control - grind it out).
Chosen partly for archetype fit, partly because it makes the
redistribution math come out exactly even: each energy pool has 8
cards, and 4 (own) + 2 (partner) + 1 + 1 (the other two) = 8 - a clean
partition with nothing left over, if every pool has exactly 8.

**The real cost data didn't support that on its own**, checked before
touching anything: Claw's 8 costs were 2/4/4/5/5/6/6/6 (no 3-cost),
Wing's were 3/3/4/4/4/4/4/5 (no 2-cost), Eye's were 3/3/4/4/5/6/6/7 (no
2-cost) - only Shell already had a full curve. Direct instruction on
how to close the gap: "I'd rather find cards in the catalog to fill out
the decks that [than] change the numbers on existing cards. Shouldn't
be too hard to find another 2 or 3 cost to port to our engine" - so
three new Characters were mined from the wider 145-card migrated DPS
pool (`DpsCards.cs`, the same "already buildable against the closed
vocabulary" source CARD_INSPIRATION.md's original 45 picks came from),
not from renumbering any of the existing eight:

| New Character | Energy | Cost | Ability | Source |
|---|---|---|---|---|
| Mongoose | Claw | 3 | Whenever this levels up: deal 2 damage to a target creature. | Toad, "Secondary Mutation" (DPS054) - dropped its affiliation-gated "Teamwatch" second clause |
| Swift | Wing | 2 | On attack: draw a die into your Prep Area. | Beast, "Combat Ready" (DPS098) - dropped its one-time meta-cost "Founder" clause; printed cost was already 2 |
| Cuttlefish | Eye | 2 | On attack: spin a target opposing level 1 creature to an energy face. | Iceman, "Icy Interference" (DPS034) - cost discounted from its printed 4 (no closer-fitting Eye pick prints at 2; same "cost is a starting point, not balanced" latitude CARD_INSPIRATION.md's Method section already claims) |

That grows Claw/Wing/Eye to 9 cards each (Shell stays at 8), so each of
those three pools has exactly one card left over after the 4+2+1+1
split - Orca, Monarch Butterfly, and Anglerfish are still real,
findable `Catalog` entries, just not on any Champion's team yet.

**`DiceKingdomConfig.CharactersByEnergyType` (per-energy-type) was
replaced outright with `CharactersByChampion` (per-champion)** - the
old shape had no way to express "borrow 2 from another pool." Only one
real call site needed updating (`V2GamesController.BuildPlayer`, keyed
on `champion.Id` now instead of `champion.EnergySymbolId`) -
`GameSetup.SeedTeamDice` was already fully generic (just seeds
`DieLimit` copies of whatever's in `TeamCardIds`, no energy-matching
assumption), so no engine change was needed at all, confirmed by
reading it before assuming otherwise.

Final packs (own 4 first, then the 2 symbiotic, then 1 + 1):

- **Wolf (Claw)**: Honey Badger(2)/Mongoose(3)/Wolverine(4)/Tiger(6),
  Mountain Goat(3)/Greyhound(4) [Wing], Hippopotamus(4) [Shell],
  Elephant(6) [Eye].
- **Armadillo (Shell)**: Hermit Crab(2)/Pangolin(3)/Musk Ox(4)/Snapping
  Turtle(5), Fox(5)/Cowbird(3) [Eye], Cape Buffalo(6) [Claw],
  Hummingbird(4) [Wing].
- **Golden Eagle (Wing)**: Swift(2)/Barn Swallow(3)/Osprey(4)/
  Albatross(5), Grizzly Bear(5)/Peregrine Falcon(6) [Claw], Box
  Turtle(3) [Shell], Barn Owl(4) [Eye].
- **Great Horned Owl (Eye)**: Cuttlefish(2)/Magpie(3)/Hyena(4)/Raven(7),
  Opossum(3)/Queen Termite(4) [Shell], Stoat(4) [Claw], Homing
  Pigeon(4) [Wing].

Bonus, not a design goal going in: this gives the Tardigrade Surge
face's Wild pip - previously just a rare bonus, since every card in a
team was the same type anyway - a real reason to matter. It's now the
one guaranteed way to pay an off-type splash card's purchase cost.

**Two new tests, not just updated old ones**: `Every_Champion_Has_
Exactly_Eight_Characters_With_No_Duplicates` (each pack is 8, no
repeats within one, no card shared across two Champions) and
`Every_Champions_Own_Energy_Characters_Cover_A_Real_Cost_Curve`
(asserts the RULE - a 2, a 3, and a 5+ present in each Champion's own-
energy subset - not today's specific picks, so a future roster edit
that breaks the curve fails loudly instead of silently). Also fixed
`V2GamesControllerTests`' now-wrong `Create_Builds_Each_Team_From_Its_
Champions_Energy_Type` (asserted every card `StartsWith("DK-CLAW-")`,
the exact invariant this change intentionally breaks) to check "matches
`CharactersByChampion` exactly" instead. 322 V2 + 580 Engine + 13 Api
tests pass.

Verified against the real running API, not just the test suite: a
`POST /api/v2/games` for Wolf vs. Great Horned Owl returned exactly the
8+8 card ids listed above (not the old all-Claw/all-Eye lists), and a
headless-Chromium screenshot of Wolf's actual roster strip shows all
four energies' icons side by side on one team.

## Basic computer opponent (2026-09-15)

Desktop `/dice-kingdom` only - added a "Play vs Computer" checkbox on
the setup screen that makes Player Two a rule-based bot instead of a
second human seat, so the game is playable solo. Deliberately not
smart: no lookahead, no combo/keyword awareness, just per-step
heuristics (`web/src/dicekingdom/bot.ts`, pure decision functions taking
`GameState` + `CardDef` lookup and returning what to do next) - field
the best affordable character or buy the priciest affordable card,
attack with every fielded die that has nonzero attack, block using a
kills-then-survives greedy match, satisfy a pending choice's minimum
count. `DiceKingdomPage.tsx` drives it: a `setInterval` heartbeat
(`BOT_MOVE_DELAY_MS` = 700ms, deliberately a heartbeat and not a one-
shot re-schedule off `game`'s own version - see below) calls
`performBotAction` whenever `bot.ts`'s `decisionOwner()` says it's the
computer's move.

**Two real "stuck forever" bugs found only by an actual multi-turn
Playwright playthrough** (`~/.devtools/playwright/vs-computer.js`, not
kept in the repo - ad hoc), neither caught by `tsc`/`oxlint`/a quick
click-through:

1. The first scheduler design rescheduled itself by reacting to
   `game.version` changing - seemed right (every real move changes the
   version) until the very first `runBot` failure, which by definition
   changes nothing, so nothing ever re-triggered the effect and the
   computer's turn just stopped. Fixed by switching to a `setInterval`
   heartbeat that re-checks and retries every tick regardless of
   whether the previous attempt did anything, rather than a `setTimeout`
   chained off state changes.
2. Bigger one: this app's pass-and-play was never actually "one browser,
   both seats, freely" - `seats.ts`'s `tokenFor()` is ONE stored
   "who am I" flag per browser tab (real two-human pass-and-play here
   means a second browser CONTEXT via the invite link, not sharing a
   tab), so the shared `api` client could only ever act as Player One.
   Every computer action 403'd with "It is not your turn" until `api.ts`
   grew `apiAs(gameId, playerId)` - the same client shape bound to one
   specific seat's own remembered token (both tokens are already saved
   locally for a vs-computer game, same `rememberSeats` call ordinary
   pass-and-play already made) - and `performBotAction` switched to it.
   The pre-existing generic "auto-submit an empty Assign Blockers/
   Action-Global-Window" effect had the exact same bug (it also used the
   shared `api`) and got the same fix, resolving correctly identifies
   the required actor via `decisionOwner()` now instead of assuming
   "whoever this tab is" is always right - true in ordinary pass-and-
   play, false here. Both fixes are provably no-ops for ordinary two-tab
   play (verified with a normal-hotseat Playwright run): `apiAs` finds
   the same single token `tokenFor` would have, or finds nothing and
   fails exactly as silently as before.

Also worth knowing about the response DTO: `yourPlayerId` reflects
whichever seat token the REQUEST carried (`V2GamesController.Result`),
so a bot action submitted via `apiAs` flips it to Player Two for that
one response. Patched back to Player One (always the human, by this
mode's own construction) inside `runBot`/`runQuiet` rather than left to
self-correct on the next poll - otherwise the You/Opp boards visibly
swap for up to `POLL_INTERVAL_MS` after every computer move.

Known gap, not a bug: the bot only ever buys/fields dice it directly
owns or controls - it can't purchase a shared/community Basic Action die
even though the rules allow it, because `CardDef` (the client's card
shape) doesn't carry a card-type/community flag to tell those apart.
DiceKingdomConfig doesn't have any Basic Actions among the launch
Characters yet, so this doesn't currently cost the bot anything real;
revisit if that changes. Verified end-to-end via
`vs-computer.js`: a ~150-action scripted playthrough (computer fully
autonomous, a human side that only ever skips/blocks nothing) ran many
full turns with real purchases, fields, attacks, and damage (life went
to -25) with zero stuck steps and zero uncaught errors.

## Bug: KO'd dice never left the Attack Zone on mobile (2026-09-19)

Root cause was client-side, not the engine: `run()` cleared `blockAssignments`
whenever the step wasn't `assign-blockers`, and `declareBlockers` lands on
`action-global-window`, so "Resolve Damage" sent `[]` and the server (which
doesn't persist pairings) treated every attacker as unblocked - no combat
damage to blockers, no KOs. Now cleared only outside assign-blockers /
action-global-window (mobile and desktop pages both had the line).

Follow-up (2026-09-19): with pairings now surviving, the bot's heartbeat effect
(deps `[vsComputer, gameId]`) still saw its first-render `blockAssignments`
(`{}`), so it never resolved damage after the human blocked -> stuck on
"Waiting…". Bot now reads pairings via `blockAssignmentsRef` (both pages).

## Gang-block hang, Used/Prep styling, Bag contents (2026-09-19)
- Stuck "Waiting… Armadillo is acting" after Wolf put 2 blockers on one attacker:
  `V2GamesController.AssignCombatDamage` threw "more than one blocker" (bot
  swallowed it). It now auto-splits: lethal (remaining Defense) to each blocker
  in order, remainder on the last. Not yet a player choice.
- Mobile mat: Used = dimmed/dashed tiles with ✕; Prep = lit panel with ↻.
- Mobile mat: Bag count is a button that lists the bag's contents by card.

## Piles as tappable panels, bot pacing/purchasing/blocking (2026-09-19)
- Mobile: Used/Prep/Out/Bag (yours AND opponent's, incl. the collapsed
  "N used / N prep…" row) open a `PileDetail` panel of glyph+name+count chips.
- Bot heartbeat 700ms -> 2000ms; the "no blockers -> auto Resolve Damage" also
  waits 2s so the player can see what happened.
- Bot never bought: it fielded first (Tardigrades cost 0, so always) and passed
  energy in arbitrary order. Now buys first ~2/3 of the time when affordable,
  with a type-matching cheapest-energy picker (`pickEnergy` in bot.ts).
- Bot blocking was intentional-but-dumb (blocked only if it killed or
  survived, so 0A/2D vs a 2+ attack never blocked). Now chump-blocks any
  attacker with Attack > 0 as a fallback (KO'd dice just go to Prep).

## Pile sheets + a broken deploy (2026-09-19)
- The previous commit ("tappable pile contents") had a leftover JSX fragment and
  did NOT build (`vite build` failed) - its deploy would have failed, so the user
  kept seeing the old UI. **`tsc --noEmit -p .` checks nothing in this repo; use
  `npx tsc --noEmit -p tsconfig.app.json` and `npx vite build`.**
- Used/Prep/Out/Bag on either mat now open a bottom `PileSheet` of real die
  tiles (FacedownTile for unrolled dice) with names; state lifted to the page.
  Verified headless: `~/.devtools/playwright/pile-sheet-check.js`.

## Pile strip + smarter energy spend (2026-09-19)
- Pile view is now an inline `PileStrip` inside the owning mat (36px tiles, no
  overlay) - the full-screen sheet blacked out the game.
- `pickEnergy` (bot.ts, shared by the human Buy/Field auto-select and the bot)
  now searches subsets and orders the offered dice so the LAST (partially spent)
  die is the one that spins down usefully: Tardigrade double -> its 1A/1D single
  face (best), Character double -> bare single energy, else the pip is lost.
  Engine spends in offered order (TurnEngine.SpendEnergy/TrySpinDown).

## Attack Zone combat preview - defence meters (2026-09-19)
Design option 6a (Claude Design handoff, "Defence meters") implemented on the
mobile Attack Zone: under every die with a blocker relationship, a bar of its
own Defense (grey = damage already marked, red = damage this combat would add)
plus "-N" and a KO tag when lethal. `combatPreview()` mirrors the server's
split (attacker's Attack goes to its blockers IN ORDER, lethal to each, rest on
the last; each blocker hits the attacker for its full Attack), so re-ordering
blockers changes who is KO'd. New `V2DieDto.Damage` carries marked damage.
Approximation: ignores Fast's two-wave ordering and on-damage abilities.
Verified headless (gang-block playthrough). Blocker-side "damage dealt" is the
attacker's own meter; option 6b's threads were NOT used (clutter).

## Motion pass: flying dice + resizing phase card (2026-09-19)
Direct feedback: Roll -> Main was a hard cut; dice blipped between zones.
- `web/src/dicekingdom/dieFlights.ts`. `useDieFlights` snapshots every
  `[data-fly-id]` element (dice `die:<id>`, Buy tiles `card:<cardId>`) after each
  commit and, next commit, launches a fixed-position ghost clone from the old
  spot to the new one when a die's zone/lane/region changed. Ghosts track the
  destination live each frame; the real destination die stays hidden until
  landing. No element any more (spent/KO'd/bought) -> flies into
  `[data-pile="mine|opp-used|prep|out|bag|reserve"]`; a KO first shakes + flashes
  red (~420ms, `dkKoShake`) then flies to Prep. A fresh draw flies out of its pile.
- `usePhaseHeight` animates the persistent `.dkm-phase-shell` height when the
  phase card changes (Tray -> Buy "expands"); the keyed card inside still fades in.
- Cosmetic only; disabled under prefers-reduced-motion. KO flight path is the
  same code as the purchase/spent-energy path but was not exercised end-to-end
  headless (needs a real combat KO) - watch for it in play.
- Gotcha: hooks must sit ABOVE the page's `if (!game) return` early return.

## Motion pass follow-ups (2026-09-20)
- Draw-to-Tray: a die leaving a small pile now starts as a full-size die at ~0.35
  scale centred on the pile (was a pile-sized box holding a full-size clone ->
  ballooned then snapped). Ghosts now scale about their centre.
- Declare Attackers replayed the flight: `run()` committed the cleared local
  state (pending attackers) urgently and `setGame` in a transition, so for one
  frame the dice were back in Field (lane->Field->lane). All of that state now
  updates inside the same `startTransition`. Also only a real change of region
  (field/lane N) or the phase card swapping under a stage die triggers a flight
  - a zone change alone no longer does.
- Spent energy dice (Reserve -> Used) now use a distinct quick shrink-and-fade
  "spend" flight so they don't read as a second die being fielded.
- Opponent purchases fly from their Roster button into their Used pile.
- Debug: `window.__dkFlights = []` in the console logs why each flight launched.
- NOT verified: an opponent die's KO shake-and-fly. Code path is the same as my
  own dice's (vanish -> `opp-prep` target); the headless harness never produced a
  bot-die KO (bot dice leave play when unblocked; harness blocking loop stalled).

## Opponent Reserve row + Prep styling (2026-09-20)
- Opp mat now shows a "Their reserve" row of their rolled Reserve dice (was only
  the energy total in the header, so fielded creatures appeared from nowhere).
  Fielding/spending now flies out of/into that row via the existing flight layer;
  flight regions are now `data-region` named (field-mine/field-opp/opp-reserve)
  so reserve -> field inside one mat still counts as a move.
- Prep looked odd because only that cell had padding/box; all pile cells now share
  the same box and only Prep is tinted.
- Observed, not fixed: during the opponent's Roll step the phase stage still
  shows MY "Tray - tap to select for reroll" card.


## Lane combat, log, fielding payment (2026-09-21)

Direct feedback from playing a vs-Computer match:

- **A lane is one fight.** Attackers declared into the same lane pool their
  Attack against the lane's blockers, and the lane's blockers pool their
  Attack back across its attackers (both lethal-first in lane order,
  remainder on the last - automatic, not yet a player choice). The UI only
  ever attached a lane's blocker to the FIRST attacker, which left the
  second one "unblocked"; `CombatEngine.AssignCombatDamage` now groups by
  `Lane` and ignores which attacker a blocker was assigned to. Overcrush
  is per lane: 2+ attackers, or any attacker with the keyword, and the
  leftover (combined Attack minus blockers' Defense) hits the player once
  every blocker is gone. Deadly engagement, Unblockable and MinBlockers are
  lane-aware too. Mobile lane chip/breakdown/preview mirror it.
  Desktop still declares `i % 4` lanes, so 5+ attackers would share one.
- **Ability effects are logged** (damage, KO, life, draw, combat flags,
  moves, spins) via `LogAbility` in `EffectInterpreter`; each turn opens
  with a `IsTurnStart` log entry (rendered as a divider); the cap is 5000
  and mobile's Log header expands to the whole game.
- **Computer opponent only attacks with 2+ Attack** (`BOT_MIN_ATTACK`).
- **Fielding asks which energy dice to spend** (`PaymentSheet`): dice are
  spent in tap order, the last can be partly spent. Skipped when free or
  when every energy die is needed anyway. Purchases still auto-pay.
- **The opponent's Roll & Reroll now shows THEIR tray** (it used to show
  the viewer's own leftover dice).

## Mobile quick-buy strip: most expensive affordable first (2026-09-24)

- The three-tile Buy strip used to show the first three roster cards in
  roster order, so with 6 energy it suggested the 2- and 3-cost cards.
  Now it shows the affordable cards by cost, highest first. If fewer than
  three are affordable, the remaining slots go to the cheapest cards you
  can't afford yet, i.e. the ones closest to buyable. The Full Roster sheet is
  unchanged.
- (2026-09-25) The Buy strip is hidden on the opponent's Main phase too.
  It only showed YOUR roster, which you can't act on during their turn, and their
  Reserve row in the opponent mat already shows what they have to spend.

## Lane order, phantom blocker log line, per-level cost on dice (2026-09-25)

- **Lane damage goes outward from the middle.** Within a lane, combat
  damage goes to each side's first die first. The side drawn above the
  middle is now stacked in reverse, so that first die sits next to the
  middle line where the teams meet. Before, the top side took damage
  from the outer edge in.
- **Attackers keep their declared order.** New `DieInstance.AttackOrder`
  (set in `DeclareAttackers`, cleared on return to the field, on the DTO
  as `attackOrder`). `CombatEngine` walks each lane in that order, and
  mobile stacks by it. Before, confirming the declaration re-sorted a
  lane into internal die order (Mongoose-then-Tardigrade came back as
  Tardigrade-then-Mongoose), and the damage order followed that too.
- **No "leaves every attacker unblocked" after "declares no attackers".**
  The step is still entered and auto-passed by the client (see the
  2026-09-15 notes). It just doesn't log when there was nothing to block.
- **Die tiles show the rolled level's fielding cost.** `facesFor` copied
  slot 0's (level 1) cost onto whatever face was showing, so a level-3
  Mongoose read 0 while fielding it asked for payment. This was missed
  when costs became per-level.
- **Mobile can answer pending choices now (real bug, game-breaking).**
  Mobile had no UI for `pendingChoice`; only the bot's code path could
  answer one. Fielding Honey Badger ("On Field: deal 1 damage to a
  target creature") with more than one legal target left the server
  waiting on a choice the player couldn't see. Every later action then
  failed with "Resolve the pending choice before taking another
  action", so the game was stuck. New `ChoiceSheet`: a bottom sheet
  with the candidate dice split into Theirs/Yours, min/max-aware, and
  no cancel (the ability has already triggered). Checked by fielding a
  Honey Badger through the UI: the sheet appeared, confirming dealt
  the damage, and play continued.
- **Targets are picked on the board, not in a sheet** (same day, direct
  feedback: "There will be times when placement is important, such as
  when dice are attacking or blocking, so we'll want to be consistent").
  While a pending choice is yours and every candidate is a Field or
  Attack Zone die, the page goes into targeting mode (`.dkm-targeting`).
  Legal targets pulse where they sit, in either field or in the lanes.
  Everything else, including the Buy strip, dims and ignores taps, and
  the bottom bar becomes "Confirm target" with the card's prompt. The
  engine prompt now names its source ("Honey Badger: choose 1
  target."). `ChoiceSheet` is kept only as the fallback for candidates
  that aren't dice on the board (e.g. a player). Desktop still uses its
  own `PendingChoiceChips` panel.

## Basic Actions + Globals: one shared action per Champion (2026-09-26)

User call: finish the prototype's base phase by exercising Globals and
Basic Actions. Instead of each team drafting two Basic Actions, each
**Champion brings one**. Both are community dice (either player can buy
either set), and each doubles as that game's Global, usable by either
player for **1 energy of its Champion's type**. Texts are the real Dice
Masters cards the user named, trimmed to the closed vocabulary:

| Champion | Action | Action die use | Global (1 energy) |
|---|---|---|---|
| Wolf (Claw) | Anger Issues | target creature +3A and Overcrush this turn | target creature +1A this turn |
| Armadillo (Shell) | Distraction | opponent picks 2 of their creatures; they can't block this turn | move one attacker from the Attack Zone back to its field |
| Golden Eagle (Wing) | Resurrection | choose a die in your Used Pile, roll it into Reserve | once per turn: draw a die from your bag into Prep |
| Great Horned Owl (Eye) | Mutation | swap one of your fielded creatures with a non-Tardigrade creature from your Used Pile, at level 1 (no On Field) | spin one of your creatures down a level to spin another up one |

Decisions made along the way (placeholders, easy to change):
- **Die:** same layout as a Character die: 3 action faces plus energy faces
  of the Champion's type (2, 2, 1). So buying the opponent's action die is
  also a way to get their energy, which is what their Global costs.
- **Purchase:** 3 energy of any type, 3 dice per card. (Changed
  2026-09-27 to the real Dice Masters costs, 4/4/4/3; see below.)
- **Timing:** the active player can use action dice and Globals in Main or
  the Attack Step's action window. The other player can use Globals only
  in that window, paid from Reserve energy left over from their own
  turn. Enforced in `V2GamesController`; the engine alone allowed any
  Attack sub-step.
- **Resurrection's Global** is once per turn per player. The real card's
  "on your turn" part isn't modelled.
- **No burst faces.**

Engine/API changes:
- `DiceKingdomConfig.ActionByChampion` adds each Champion's card to its
  team.
- `TurnEngine.UseAction` now requires an action face. It never checked
  before, so a die that rolled energy could be "used".
- Both `UseAction` and `UseGlobal` now log.
- A Global's effects name their card in prompts ("Anger Issues: choose 1
  target.") via `QueuedAbility.SourceName`, since a Global has no source
  die.
- New endpoints `use-action` and `use-global`. `use-global` rejects cards
  not in this game, because `UseGlobal` accepts anything in the catalog.
- The card DTO has `isAction`/`actionText`/`global`/`dieEnergyType`; the
  die DTO has `isActionFace`.

Mobile UI:
- **Plant art:** thistle (Anger Issues), flower (Distraction), seedling
  (Resurrection), mushroom (Mutation). On a die it's a larger, leaf-green
  emblem with no stats.
- **Global rail:** the old empty "Global" rail now lists this game's
  Globals (name, cost badge, Use button with a reason when disabled; tap
  the name for the text), plus "Your actions": your action dice showing an
  action face, each with Use. It's always on screen, so it works in the
  attack window too. Tapping an action die in Reserve also offers "Use".
- **Buy:** the Buy strip and roster include the opponent's action dice.
- **Attack window:** it no longer auto-resolves when nothing was blocked
  if anyone could still act (an action die on the active side, or a
  payable Global for either player).
- **Targets:** they're picked on the board as before. Used Pile targets
  (Resurrection, Mutation) use the fallback sheet.

Checked in a headless browser (Wolf vs Owl):
- The Anger Issues Global, paid with Claw, gave +1A to a creature picked
  on the board.
- A rolled Anger Issues action face appeared under "Your actions" and
  gave +3A.
- On two devices, the attack window waited for Resolve Damage, and the
  defender's rail allowed Globals there.
- API tests cover seeding, buying the opponent's action, action-face-only
  use, Global cost/timing/this-game-only, and the defender's
  window Global.

Not done:
- The computer opponent never uses actions or Globals. It can buy its own
  action dice as a purchase, but it just spends their energy.
- When the computer attacks, it resolves damage on its normal delay, so
  the human defender's window is short.
- Desktop has no Actions/Globals UI.
- No "pass" button for the defender: the attacker ends the window with
  Resolve Damage whenever they choose.

## Priority, as in Dice Masters (2026-09-26)

User call: use the real Dice Masters priority rules in Main and the Attack
Step's Action/Global window. This replaces yesterday's looser rule ("the
opponent may use Globals only in the attack window"). Rules 2.6.5.7, 2.6.6
and 2.7.3.4 of the comprehensive rules; new `src/DiceFight.V2/Priority.cs`:

- The **active player** holds priority first and may do as many things as
  they like: buy, field, use action dice, use Globals. Then they pass.
- The **inactive player** may then do ONE thing (use a Global), which
  hands priority straight back to the active player. Or they pass.
- The **inactive player passing right after the active player** ends the
  window. Rule 2.6.6.6 gives the active player one more chance first, but
  nothing has changed since they passed, so it's the same thing.
  Ending Main goes to the attack step, or skips it if that's what the
  active player chose when they passed. Ending the attack window resolves
  damage from the declared blocks.
- **Auto-pass:** an inactive player who can't pay for any Global passes
  automatically, so nobody waits on a choice that doesn't exist.

Implementation notes:
- `GameState.PriorityPlayerId` is exposed as `priorityPlayerId`.
- `Priority.Sync` opens a window with the active player holding priority,
  and runs after every API action.
- `enter-attack-step`, `skip-attack-step` and `assign-combat-damage` are
  now the active player's pass, carrying what should happen when the
  window ends. So the desktop page and the computer opponent needed no
  flow changes. New `pass` endpoint.
- Purchase, field, use-action and use-global all require holding
  priority.

Checked the rules while doing this: leftover energy DOES stay in your
Reserve through the opponent's turn (rule 2.8.4, cleared at your own
Clear and Draw, 2.3.1). That's what pays for the inactive player's
Globals, and the engine already did it. A stale code comment said
otherwise. One real gap fixed: Clean Up swept every action die out of the
Reserve, but rule 2.8.3 only sweeps those on an action face. One that
rolled energy is energy, and stays.

UI:
- **Mobile:** the player handed priority gets a "Pass" button ("Wolf
  passed - use one Global, or pass") and a live Globals rail. The other
  player sees "Waiting… Owl may use a Global".
- **Desktop:** gets the Pass button and waiting note (no Globals UI
  there).
- **Computer opponent:** passes whenever it's handed priority, since it
  never uses Globals.

Checked on two browser devices, Wolf vs Owl with Owl holding Wild energy:
1. Wolf pressed "Done buying" and Owl got priority.
2. Owl used Mutation's Global, and priority returned to Wolf.
3. Wolf passed again, Owl (out of energy) auto-passed, and the attack step
   began.

An API test walks the full sequence in Main and the attack window,
including the one-thing limit and the auto-pass.

(The Dice Masters comprehensive rules, as text, now live at
`~/.devtools/poppler/dm-comprehensive-rules.txt`.)

- **Follow-up (same day):** the user read the two-device run as "Owl's
  Global counted as its pass". It didn't. Owl's second turn was an
  automatic pass (its only energy went on Mutation), and automatic passes
  weren't logged. Now logged ("Owl passes (no energy left for a
  Global).") whenever the inactive player already acted in that window.
  Not logged every turn, to keep the log readable. The rule itself
  stands: the window ends only on two passes in a row with no action
  between them. An API test replays the exact scenario.

## Auto-pay keeps Wilds for last (2026-09-26)

Direct feedback: with 3 Claw and 2 Wild in Reserve, auto-buying a 4-cost
Wolverine spent BOTH Wilds, leaving nothing to pay for the opponent's
Global. `pickEnergy` (bot.ts) scored options only on leftover pips and dice
count, so "Claw 2 + Claw 1 + Wild" and "Claw 2 + Wild + Wild" tied and it
took the first found. Now it spends the fewest Wild pips first, then goes
by leftover and dice count. A Wild can pay for anything, so it's the
last to go. The greedy fallback (15+ dice) does the same. This applies
everywhere auto-pay is used: purchases, fielding, Globals, and the
computer opponent. The manual payment sheet is unchanged. Checked the
reported case plus three others against the real function.

## Playtest fixes: Resurrection, discounted costs, copy link (2026-09-27)

- **Resurrection's Global "did not work".** It draws from your bag, and
  with an empty bag it drew nothing. Rule 2.3.2 refills the bag from the
  Used Pile whenever you need to draw and it's empty; the turn's own draw
  did that, ability draws didn't. `DrawToZone` from the Bag now uses the
  same refilling `TurnEngine.DrawFromBag`, and logs "has no dice left to
  draw" if both are empty. (It did work with dice in the bag, checked
  against the live API.)
- **"Paid 1 to field, the double-energy Tardigrade didn't spin down."**
  The engine charges Owl correctly (new test). The likely cause is Golden
  Eagle's -1 fielding discount: the client showed and asked for the
  PRINTED cost, while the server charged the discounted one, 0, so
  nothing was spent. Owl's -1 purchase discount was invisible the same
  way.
  - The server now sends real costs: `V2DieDto.FieldingCost` and
    `V2GameStateDto.PurchaseCosts` (per card, for the viewing player).
  - Both pages and the computer opponent use them.
  - A discounted fielding cost shows in green on the die. A discounted
    price shows the printed one struck through (Buy strip, roster).
  - Checked: an Eagle creature printed at 1 shows a green 0 and fields
    without touching energy; Owl's roster shows 2→1, 3→2...
- **Copy link** now reads "Copied ✓" for two seconds, or "Couldn't copy".
- **KO'd die "did not go to Prep":** not reproduced. Every KO in a probe
  game went to its owner's Prep (Out of Play only for unblocked
  attackers, which is correct). Asked the user for specifics.
- **KO'd blocker "went back to his field" (follow-up with details):** 2A +
  1A in one lane vs a 3D Tardigrade, and the preview said KO. The engine
  resolves that exact case correctly (new API test, through the new
  priority flow). The likely cause was **Distraction's Global**. Its
  target was "any creature in the Attack Zone", and blockers stand there
  too, so an Armadillo player could pull their own doomed blocker back to
  the field. New `TargetFilter.AttackersOnly` (the active player's Attack
  Zone dice; documented in V2_VOCABULARY.md) restricts it to attackers,
  as the card says. Test: its choice now offers only the attackers.

## Action costs and clearer picks (2026-09-27)

- **Costs:** Basic Actions now cost what the real cards do: Anger Issues 4,
  Distraction 4, Resurrection 4, Mutation 3. Any energy; Owl's discount
  still applies. They were a flat placeholder 3.
- **Prompts:** every pick an action makes now says what it's for, via the
  new `TargetFilter.Prompt` (V2_VOCABULARY.md). The trigger was Mutation's
  Global: "it wasn't clear which die I was supposed to be selecting
  when".
  - Mutation's Global now asks "first, choose one of YOUR creatures to spin
    DOWN a level" (level 2+ only), then "now choose a creature to spin UP a
    level" (level 1-2 only).
  - The other actions and Globals got prompts too ("choose a creature to
    get +3A and Overcrush this turn", "choose an attacker to send back to
    its Field Zone", ...).

## Great Horned Owl: Foresight replaces the purchase discount (2026-09-27)

Playtest: "characters cost 1 less" was "probably too much". User picked
**Foresight**: once per turn, in your Main Step (with priority), reroll one
die in your Reserve Pool. Fits Eye (control/insight).

- New `ChampionPassiveKind.Foresight`. It's an action, not a modifier, so
  ChampionRegistry registers nothing.
- `TurnEngine.UseForesight` handles it, tracked in
  `GameState.ForesightUsedThisTurn` (cleared at Clear and Draw).
- New `foresight` endpoint; `V2PlayerDto.ForesightAvailable`.
- Mobile: while it's available, every Reserve die is tappable and offers
  "Foresight: reroll this die". Desktop has no UI for it yet; the
  computer opponent doesn't use it.
- `PurchaseCostDiscount` stays in the engine, just unused by any Champion.

## Roster ATK/DEF rebalanced - the split, not the Homash formula (2026-09-27)

Playtest feedback: "the vast majority of the characters seem to have only
1 or 2 attack, while it seems most of them have 4D or higher... the
elephant has a 12D, which doesn't exist in Dice Masters."

Checked against the ~3,200 three-level Characters in the bulk card
catalog: real Dice Masters averages 3.7 ATK / 4.0 DEF per level (ratio
1.07, max DEF 10 anywhere in the catalog). The 2026-09-21 Homash
recalibration (115b2dd) had pushed Dice Kingdom to 1.8 ATK / 5.8 DEF
(ratio 3.3) - half the roster at 1 ATK or less, Elephant's L3 at 12 DEF.
**The formula was fine; the ATK/DEF split inside each level's fixed total
was the bug** - Homash only ever reads `sum(ATK + DEF)` per level, never
how it's divided, so nothing about the totals, the purchase costs, the
fielding costs, or the bands in COST_MODEL.md needed to change.

Fix, applied to all 35 Characters in `DiceKingdomConfig.cs`: for each
level, keep the exact same ATK+DEF total and re-split it roughly evenly,
then nudge 1 point toward the Champion pairing's own lean (the
"symbiotic pairing" from the roster-mixing note: Claw+Wing lean 1 point
toward ATK, Shell+Eye lean 1 point toward DEF), stopping short of ATK
going negative or DEF hitting 0. Elephant's L3 is now 7A/10D (was
5A/12D); Honey Badger's L1 is now 1A/1D (was 0A/2D).

Because only the split changed: `DiceKingdomCostModelTests` (Homash
bands) passed unchanged, no new test needed there. `COST_MODEL.md`'s
roster table regenerated from the new data (same Homash column, new
field/ATK/DEF column) with a note on the fix. One existing test broke
for a real reason - `CapeBuffalo_Aura_Boosts_Its_Teams_Attack_While_Active`
hardcoded Honey Badger's base ATK (was 0, now 1) - updated its expected
total accordingly. Checked live: `/api/v2/games/cards` and the mobile
roster sheet both show the new numbers (confirmed Elephant, Honey Badger,
Hippopotamus, and the whole Owl roster against the regenerated table).
336 V2 + 29 Api tests pass; `tsc -b`/`vite build` clean (no frontend
change needed - stats are never hardcoded client-side, always read live
from the card DTO).

## Champion round-robin simulator, powers off (2026-09-28)

The headless simulator (previously only in a session scratchpad, never
on disk in the repo) now lives at `tools/Simulator` - a console app
project-referencing DiceFight.V2, like `tools/CardCatalogDocs`. It plays
every Champion pairing (all six), 200 games each, seats alternated, same
bot policy on both sides. Env switches are documented in its header
(`SIM_POWERS`, `SIM_BUY`, `SIM_ORDER`). Seeding now uses a stable string
hash - it used `string.GetHashCode()`, which .NET randomizes per process,
so two runs of identical code gave different numbers.

**Caveat: teams got Characters only - no Basic Actions, no Globals, no
rerolls, random answers to pending choices.**

Powers-off win rates (1,200 games per policy) flip with the bot's
buy policy alone:

| Bot policy | Wolf | Armadillo | Owl | Eagle |
|---|---|---|---|---|
| Field everything, then buy cheapest (original) | 38.5 | 42.5 | 46.3 | 62.7 |
| Field everything, then buy most expensive | 44.8 | 44.0 | 46.8 | 56.5 |
| Buy most expensive first, then field | 62.8 | 39.2 | 64.7 | 31.5 |

So balance conclusions are currently dominated by the bot. Root cause:
Tardigrade faces carry energy AND free stats, and a field-first bot
fields ~55 Tardigrades/game, burning the energy it'd need to buy - under
the original policy Silverback/Rhinoceros/Phoenix/Basilisk were fielded
in 0% of games. Buy-first overcorrects (buys all 4 copies of every cheap
card, diluting the bag).

Findings that held across all three policies (worth revisiting once the
bot is better):
- Off-type splash cards (Hippopotamus, Cape Buffalo, Stoat, Cowbird, Box
  Turtle, Barn Owl, Mountain Goat, Peregrine Falcon) are fielded in
  0-6% of games. User's own play didn't have much trouble buying off-type,
  so likely a bot problem (no reroll toward Wild faces, never buys the
  opponent's Basic Action for its energy) rather than a design one.
- Games run 35-48 turns for 20 life (~0.5 damage/turn); 1-5% hit the
  50-turn cap.
- Finishers win big when they land (win% in games where fielded:
  Basilisk 84-85, Rhinoceros 66-84, Silverback 68-81, Phoenix 53-81) -
  partly selection bias (they only land in long games). Basilisk is the
  one to watch: ~2.2 unblockable end-of-turn damage/game.
- Armadillo's cards never cleared 45% without its passive.
- First seat wins ~53-54% under every policy.
- The old powers-ON pass (original bot) had Wolf at 99.5% vs Owl and
  Eagle - its +1 ATK turns every 0-ATK Tardigrade into an attacker.

Next (user direction): one shared bot policy for the web opponent and
the simulator (they had drifted into two different policies), with
rerolls (prefer stat faces for non-Tardigrade Characters, some
preference for Wild), and buying the opponent's Basic Action for
off-type energy. Per-Champion "personas" (e.g. aggressive Wolf) later -
not urgent while testing with powers off.

## One shared bot: web opponent = simulator (2026-09-28)

User call: the web "vs computer" opponent and the simulator must be the
same bot (per-Champion "personas" - e.g. an aggressive Wolf - wanted
later, not urgent while testing powers-off). Done by moving the policy
server-side:

- `src/DiceFight.V2/Bot/` - `DiceKingdomBot.Decide(state, playerId)`
  returns the single next move (one engine call's worth), `BotDriver`
  applies it locally exactly as the matching controller endpoint does
  (priority, draining, the pending-choice queue), `BotEnergy` is the
  payment picker (bot.ts's pickEnergy port: Wilds last, and prefers
  spending bare energy faces over Tardigrades it could still field).
  `BotPersona` holds the taste knobs (only `Default` exists).
- `GET /api/v2/games/{id}/bot-decision[?skip=...]` (204 when it isn't
  that seat's decision). `bot.ts` lost its own TypeScript policy; both
  pages' `performBotAction` now fetch the decision and carry it out via
  `botDecisionCall`. The auto-skip effect no longer answers for the
  computer's seat - auto-passing its Attack window took away its best
  play (pumping an unblocked attacker).
- `tools/Simulator` now drives both seats through BotDriver, teams include
  the Champion's Basic Action (as the API builds them), and the
  SIM_BUY/SIM_ORDER switches are gone (that policy lives in the bot).
  New switch: `SIM_NO_GLOBALS=cardId,...` strips a card's Globals.
- `PendingChoice.Intent` (Harmful/Beneficial/Unknown), set per effect in
  EffectInterpreter, so the bot aims damage/KO/can't-block at the
  opponent and pumps at itself - the old simulator answered with random
  candidates.

Bot behavior (details in DiceKingdomBot's header): rerolls Characters
that rolled energy; fishes for Wild with 1-pip Tardigrades when an
off-type card is wanted; fields real Characters, then buys the best-value
plan (up to 3 buys; repeat copies worth less; an opponent's Basic Action
worth more when its energy pays for an off-type card), then fields
leftover Tardigrades; uses Basic Actions/Globals; attacks only where no
blocker kills it for free, keeps enough home to survive the crack-back,
all-in on lethal (an unblocked attacker leaves play, rule 2.7.4.3.1 -
bot.ts's old "no defensive cost to attacking" comment was wrong); blocks
by trade value, chumps only to survive or at low life.

Powers-off round-robin with the shared bot (1,200 games):

| | Wolf | Armadillo | Owl | Eagle |
|---|---|---|---|---|
| Win rate | 52.7 | 25.3 | 49.0 | 72.2 |

Worst pairing: Armadillo vs Eagle 10/90. Games are 15-30 turns (was
35-48), first seat wins 56%, ~4% hit the 50-turn cap. Off-type splash
cards are now bought (Cowbird 41%, Box Turtle 41%, Opossum 55%, Queen
Termite 51% of games fielded - were 0-15%), matching the user's own
experience; Hippopotamus, Elephant, Cape Buffalo, Peregrine Falcon still
rare. Finishers still land in only 4-23% of games (Silverback 4%) -
7-cost is hard to reach with 4 dice in a 15-25-turn race.

Eagle's edge is NOT mainly Resurrection's Global (1 Wing: draw a die,
used ~2x/game): stripping it (SIM_NO_GLOBALS=DK-ACT-03) only moved Eagle
72.2 -> 69.8. It's the cheap draw engines (Swift / Barn Swallow) in what
is now a racing game. Armadillo's walls (Hermit Crab 1 ATK) are weak in a
race.

Found along the way:
- Honey Badger's mandatory "deal 1 damage to a target creature" hits
  itself (or another own creature) when the opponent's board is empty -
  it's the only legal target. Card-design question for the user.
- Real stalls happen: a player with every die on the Field draws 0 each
  turn (no deck-out rule), and at low life neither side attacks. The bot
  test caps turns rather than requiring a winner.
- Both pages auto-pass the Active player's Attack window when nothing was
  blocked (mobile only if they can't pay any Global), which also means a
  human can't use Anger Issues on an unblocked attacker on desktop. Left
  as-is for humans; flagged.

## Deck-out, generic Virtual energy, no auto-pass while a Global is usable (2026-09-28)

User answers to the previous entry's open items:

1. Honey Badger hitting itself on an empty board is intended - the
   tradeoff for a cheap Character with that ability. No change.
2. **Deck-out rule** (was missing): in Clear and Draw, for each die a
   player can't draw, they take 1 damage and gain 1 generic "Virtual"
   energy - not a die, can't spin, just a count, gone after that Main
   Step. So fielding everything eventually kills you, which gives a
   reason to keep dice in the bag. Implemented as
   `GameState.VirtualEnergy` (granted in `TurnEngine.ClearAndDraw`,
   expired in `EnterAttackStep`), spent automatically and first by
   `SpendEnergy` (it can't be saved past Main, so there's no reason to
   hold it). Being generic, it counts toward an amount but never toward a
   type, so a typed purchase still needs one matching pip from a die;
   `SpendEnergy` picks the amount of Virtual that keeps the payment legal
   with the least overspend, so a client offering dice for the full cost
   still works (surplus dice are left untouched).
   `Priority.CanUseAnyGlobal` counts it too. DTO: `PlayerDto.virtualEnergy`.
   UI (mobile): shown as a plain "N generic" chip in the reserve summary -
   user call: no separate Virtual styling, it's spent first and can't
   spin anyway; affordability checks and the field payment sheet account
   for it. Bot: plans purchases/fielding/Globals with it, and won't field
   a die that would leave fewer than a full draw circulating (leftover
   Tardigrades need a 2-die margin). Tests: `DeckOutTests`.
3. **No auto-pass while the Active player can act**: both pages' auto-
   skip of an unblocked Attack window now uses the shared
   `activeCouldAct` (moved to bot.ts) - desktop used to pass regardless,
   so a human could never Anger Issues an unblocked attacker there.

Simulator after the deck-out rule: win rates essentially unchanged (Wolf
51 / Armadillo 24 / Owl 52 / Eagle 72); turn-cap games 44 -> 25 per
1,200; deck-out damage ~0.05 life/game - the bot's circulation guard
means it almost never decks itself (user: "a real player would avoid
that situation").

### Bot: deck-out burn as a budget, not a wall (2026-09-28)

User: "a real player would strategically take burn damage from not having
enough dice to draw, especially in the early game. But they would almost
never allow themselves to actually die from it." The bot's first version
refused any burn; now `BurnBudget` allows being up to 2 dice short (life
after the opponent's whole board swings >= 14), 1 (>= 8), 0 below that,
and at 3 life or less keeps a spare die (-1). Leftover Tardigrades get
one point less budget than real Characters.

The simulator's new "lost to deck-out burn" counter found 12/1,200
games lost that way at first. Two real bot bugs, not the budget:
- `Circulating` counted Prep Area dice, but Prep dice come back ON TOP
  of the 4-die Bag draw and never cover a shortfall.
- On Attack draws (Mountain Goat/Swift) and Resurrection's draw Global
  move a die Bag -> Prep AFTER the fielding check. Both are now
  counted: pending on-attack draws come off circulation before fielding,
  a drawing attacker is held back if its draw would exceed the budget,
  and draw Globals pass the same check.
Result: 0/1,200 games lost to deck-out, win rates unchanged. Burn itself
stays rare (~0.01/game) - purchases keep the bag well above a full draw,
so the situation seldom comes up at all.

### Rule 3.1.10: no initiating a Global/Action for no result (2026-09-28)

User correction: only the Clear and Draw step's draw burns / grants
Virtual energy (already the case - `TurnEngine.ClearAndDraw` only); an
ability's draw does as much as it can and stops. And per rule 3.1.10
("Energy cannot be spent, or an ability initiated, ... [if] there would be
no result or potential result that changes the game state; or there are
no legal targets") and 2.6.4.6 (same for Action dice), a Global or Action
die with nothing to act on can't be used at all - e.g. Resurrection's
draw Global with an empty Bag and Used Pile. A TRIGGERED ability
(Mountain Goat's On Attack draw) isn't gated: the die attacks normally and
the draw fizzles, which the interpreter already did.

New `AbilityPreview.CouldHaveResult` (deliberately permissive - only
rules out an empty draw or a target filter that matches nothing; a card
with no modelled use ability isn't judged), checked by `UseGlobal`,
`UseAction`, `Priority.CanUseAnyGlobal` (so no priority stop is offered
for a Global that can't do anything - e.g. Distraction's Global in Main,
with no attackers yet), and the bot. Tests: `NoResultAbilityTests`; one
API test updated for the dropped priority stop.
Known gap: the mobile Global rail can still show "Use" enabled for such a
Global - the server rejects it with a rule-3.1.10 message.

### Why Armadillo loses (powers off) - roster swap experiments (2026-09-28)

New simulator switch `SIM_SWAP=Champ:oldId=newId;...`. Armadillo's
average ATK (2.50) is no lower than Owl's (2.46), and Owl wins ~50%, so
"low ATK" alone isn't it. One-card swaps, 1,200 games each:

| Armadillo swap | Armadillo win % |
|---|---|
| baseline | 24.3 |
| Rhinoceros -> Basilisk (stronger finisher) | 28.3 |
| Musk Ox (+1 DEF aura) -> Queen Termite (+1 ATK aura, identical cost/stats) | 36.3 |
| Hermit Crab (1/3 wall) -> Opossum (Deadly) | 48.5 |

The finisher barely matters (finishers land in only ~15% of games). What
matters is the cheap end: Hermit Crab is Armadillo's most-bought card
(2.8/game, fielded in ~97% of games) and its 1 ATK means it never KOs
anything - blocks prevent damage but never punish, so opponents attack
into it for free and the board never tips Armadillo's way. Its only
removal is Hummingbird (off-type Wing), and two of its eight slots are
+DEF auras, which only buy survival in a game where blocked damage
clears every turn anyway. (Opossum is 3-cost vs Crab's 2, so part of
that jump is cost.)

### Lockout Pangolin prototype, and the real Armadillo problem (2026-09-29)

User direction: Deadly's big swing is probably partly the bot (a real
player lets a weak Deadly die through or chump-blocks with a doomed
Tardigrade); Hummingbird stays off-type (strong for a 4-drop); try a
same-type Blob/Drax-style lockout for a defense-oriented team.

Prototyped simulator-only (`Prototypes` in tools/Simulator, reached via
SIM_SWAP - NOT in the live catalog): Pangolin's slot/die/stats with "On
Field: name an opposing character; while Pangolin is active your
opponent can't field [or purchase] it" (existing `RememberCard` +
`Lockout` vocabulary). Engine/bot support added: `ChoiceIntent.NameCard`
(RememberCard's choice names a card, not a die), and the bot scores it
by the copies the opponent already owns and has circulating - weighting
by cost alone named unbought finishers (Basilisk/Silverback/Phoenix)
almost every time, which dropped Armadillo to 19.8%.

Armadillo win %, 1,200 games each, powers off:

| Change | Armadillo |
|---|---|
| baseline | 24.3 |
| Pangolin -> lockout (can't field), naming workhorses | 23.0 |
| Pangolin -> lockout (can't field or buy) | 22.2 |
| Pangolin -> Box Turtle (On Block: 1 dmg) | 24.2 |
| Hermit Crab -> Box Turtle | 41.7 |
| Hermit Crab -> Opossum (Deadly) | 48.5 |
| Hermit Crab -> Hippopotamus (vanilla 4-cost) | 55.0 |

The lockout is roughly neutral: with 8 Characters plus Tardigrades per
team, shutting off one card leaves plenty to field (and a locked die
still pays as energy). The ability swaps in Pangolin's slot do nothing;
every Hermit Crab replacement - even a vanilla one - is a big gain. The
problem is Hermit Crab itself: the only vanilla 2-drop, never above 1
ATK, and the most-bought, most-fielded card on the team. Other teams'
2-drops (Honey Badger, Swift, Cuttlefish) all do something. (Caveat:
the bot buys 2-drops heavily, which amplifies whatever the 2-drop is.)

### Armadillo: roster changes, a Rhino-racing persona, and the Rhino stalemate (2026-09-29)

User calls: keep the lockout on Pangolin (now live - "can't purchase or
field", Blob/Drax-style); give Hermit Crab Pangolin's old "On Field: gain
1 life" and lower its DEF (now 1/2 · 1/3 · 1/3, Homash 2.00 -> 1.57);
tune an Armadillo persona to race to Rhinoceros; and - question - does the
bot ever save Shell for Distraction against a big attacker? (No: it only
ever used Distraction's Global with incidental leftovers, ~0.2/game.)

- `BotPersona` is now per Champion (`BotPersona.ForChampion`, used by both
  the web opponent and the simulator; `SIM_PERSONAS=off` to compare).
  Armadillo: RushCardId = Rhinoceros (+5 value on its first copy, bought
  before fielding when affordable, rerolls favor energy until owned) and
  HoldForGlobalCardId = Distraction (keeps one Shell energy-face die or
  Wild back through the opponent's turn when they have a 4+ ATK
  attacker - only energy faces can be held; any creature-faced die, which
  is every Tardigrade Shell pip, is swept to the Used Pile when Main ends).
- The pick for the lockout is card-scoped: `V2PendingChoiceDto.Intent`
  now reaches the client, and both pages show one labeled option per card
  (name, cost, owned/unbought) instead of every die of it (mobile showed
  32 unlabeled stat tiles; checked in headless Chromium).
- The bot never actually played Rhinoceros: it swung it in, the opponent
  let a 1-3 ATK attacker through, and it left play. Now (generic, read off
  "DieDamaged -> DealDamage to the opposing player", not the name) a
  retaliator stays home to block, blocks score the reflected damage, the
  opponent won't block it with big hitters, and only swings wide into it
  when more gets through than comes back.

Armadillo win %, 1,200 games, powers off (Champion win% also includes
games decided by the 50-turn-cap life tiebreak):

| | Armadillo total | ...by turn-cap tiebreak | outright |
|---|---|---|---|
| old roster | 24.3 | - | - |
| new roster, no persona | 36.5 | 7.0 | 29.5 |
| new roster, Rhino-racing persona | 54.8 | 27.7 | 27.1 |

With the persona Rhinoceros lands in ~80% of Armadillo games (was ~19%)
and turn-cap games jump to ~270/1,200: nobody can attack profitably into
a Rhino plus high-DEF walls, Armadillo won't swing its low-ATK board, and
the game stalls with Armadillo usually ahead on life. The roster change
is a real improvement; the persona mostly converts games into stalemates
rather than wins. Open design question for the user: Rhinoceros as a
near-unkillable (up to 3/9) blocker that punishes every attack may make
stalemates structural - e.g. reflect only when blocking, a cap on
reflected damage, or lower DEF. `DiceKingdomBotTests`' pairing test now
only requires 1 of 5 seeds to finish (Armadillo vs Owl walls up in some).

### Energy faces survive Main; Hermit Crab forces a block; Archnemesis replaces Distraction (2026-09-29)

User calls, all engine-level:
- **End-of-Main sweep keeps any face with energy.** Sweeping every
  creature-faced die was a side effect of Dice Kingdom's hybrid
  stat+energy Tardigrade faces: such a die is still energy (spendable on a
  Global during the opponent's turn). Only a stats-only face is swept now
  (`TurnEngine.EnterAttackStep`).
- **Hermit Crab:** "On Field: target character die must block this turn"
  (replacing the same-day "gain 1 life"; stats unchanged, Homash 1.57).
  The engine's MustBlock check is now "if able" - it no longer rejects an
  empty block declaration when nothing is attacking. Bot: forced blockers
  are placed in the least-bad lane; with a forced blocker on their side
  and a retaliator on ours, Rhinoceros swings alone, so the forced block
  has to land on it.
- **Archnemesis replaces Distraction** as Armadillo's Basic Action (user:
  Distraction's Global - send an attacker back - worked against the
  Rhinoceros plan). Distraction stays in the catalog, on no team. The real
  card, finally built in full (DPS001 only ever modelled its Global): the
  action binds both fighters first, then deals each one's bind-time ATK to
  the other (simultaneous - a die KO'd by the first hit still hits back);
  Global: 1 Shell, a creature's D becomes its A this turn. Web icon: a
  Venus flytrap (DK-ACT-05). `PendingChoice.Effect` lets the bot judge
  picks the intent can't (a "set D to A" helps or hurts depending on the
  die); it uses the fight only when a pairing comes out ahead and the
  Global when it moves a combatant's D 2+ the right way. The Armadillo
  persona no longer holds Shell (nothing to hold it for).

Armadillo, 1,200 games, powers off:

| | total win % | by turn-cap tiebreak | outright | turn-cap games |
|---|---|---|---|---|
| lifegain Crab + Distraction, persona (previous entry) | 54.8 | 27.7 | 27.1 | 272 |
| must-block Crab + Archnemesis, no persona | 25.7 | 3.0 | 22.7 | 62 |
| must-block Crab + Archnemesis, persona | 58.3 | 7.7 | **50.6** | 91 |

With the persona Rhinoceros lands in ~76% of Armadillo's games and wins
~75% of them - and this time mostly outright: the forced block turns the
stalemate into a way to hit the opponent. Other Champions: Wolf 38.8,
Owl 43.3, Eagle 58.2. Without the persona, Armadillo is back at ~26% -
the roster now depends on playing toward Rhinoceros.
Tests: `ArmadilloRosterTests` (sweep, forced block if able, simultaneous fight).

## Open online games: pick only your own Champion (2026-09-30)

User request: starting an online game took picking BOTH Champions, Start
Match, then copying the invite link from inside the match. Now Player 2's
column has an "Opponent picks" option (not shown for vs Computer):
- Server: `POST /api/v2/games/open {championId}` makes a pending game (a
  `V2Lobby` in `V2GameStore`) holding the host's Champion and both seat
  tokens; `GET .../{id}/lobby` says waiting/started (either seat);
  `POST .../{id}/join {championId}` (invited seat only, once) builds the
  real game under the SAME id and tokens, so the link stays valid.
- Web (`lobby.tsx`, shared by both pages): the host gets a waiting screen
  with the link + Copy button (mobile also copies it on Start), polling
  until the guest joins; opening an invite to a pending game shows the
  host's pick and a Champion grid (`resolveInvite` replaces the old
  claim-then-GET join). Pass-and-play and vs Computer are unchanged.
Checked with two real browser contexts on both pages: both land in the
same match. Tests: `V2OpenGameTests`.

## Watching the opponent roll (2026-09-30)

User request: "when watching your opponent roll and re-roll, it'd be nice
to see the animations instead of the dice just changing." A polled update
(the other player's move) used to be adopted with no animation at all,
and the computer's moves only got the quick face spin. Both pages now
compute `remoteRolledIds` - dice newly rolled into the active player's
Reserve Pool, or a face change there during Roll & Reroll - and give those
the full tumble. Mobile's `adoptRemote` also holds the Roll & Reroll step
on screen through a reroll's tumble (the reroll ends the step, which would
otherwise unmount the tray mid-tumble) - the same trick `runWithReveal`
already used for your own reroll; the computer's moves go through it too.
Checked with two browsers: the watcher's tray tumbles 3 dice on the roll
and the rerolled die on the reroll, then moves on to Buy & field.

## Playtest UI fixes: forced blocks, panel buttons, reflect, priority, damage (2026-09-30)

All from direct feedback, each checked in headless Chromium against real
game states (driven through the API by the shared bot until the situation
came up - `~/.devtools/playwright/block-panel-check.js` and friends):
- **Must block:** the engine's rejection named an internal die id
  ("teamA-TardigradeClaw-1 must block this turn"). Now it names the card,
  dice carry a `mustBlock` flag, both pages badge the die, and mobile's
  "Blockers set" is disabled with the reason until it's assigned.
- **Bottom-panel buttons (mobile):** a blocker's panel action was a
  do-nothing "tap an attacker" label - now "Block lane N" per attacked
  lane, or "Pull back" once assigned. "Declare into lane" now deselects
  after declaring (it used to flip into "Pull back" under your finger, so
  it read as not working), same as tapping the lane.
- **Rhinoceros's reflect** gets its own "↩ N reflect" lane chip and a
  breakdown line naming who takes it, apart from "N to face" (card DTO:
  `reflectsDamage`, read off the ability).
- **"Pass Priority" vs "Resolve Damage":** the state DTO's
  `passGivesPriority` (Priority.CanUseAnyGlobal for the other player) picks
  the Active player's label in the attack window, on both pages.
- **Damage marked on a die** (Honey Badger's ping, a survived block) had no
  visual; both pages now show a red "−N" badge that pops when it lands and
  clears at Clean Up. (The cube's own damage slot shares the lower-left
  corner with the energy marker, so it's a badge outside the face.)
Noted, not changed: the desktop page's side panels still say "no Basic
Actions / no Globals yet" - desktop never got the Globals UI mobile has.

## The game ends: rule 2.9 win condition (2026-09-30)

A playtest screenshot showed Wolf at -5 life with the game still going -
there was no win condition at all (deliberately deferred back in early
September as not mattering for short casual games; now games run to the
end). Rule 2.9.1: "As soon as a player's Life total reaches 0 or below the
game ends immediately and the player with Life remaining wins. Even when
an ability causing one player to reach 0 Life trigger other abilities...
those other abilities would not occur"; 2.9.3: both at once is a tie.
- `GameState.IsGameOver`/`WinnerId`, set by `CheckGameOver()`, called
  after every life change: unblocked combat hits (once, after all lanes -
  they're simultaneous), Overcrush, ability damage to a player, Reroll's
  damage-per-moved, deck-out burn. Logs "<name> wins!" (or the tie).
- Once over, `EffectInterpreter.DrainQueue` resolves nothing more (so a
  Rhinoceros reflect queued by the same combat never happens), the API
  refuses every POST ("The game is over."), the bot decides nothing, and
  BotDriver refuses to apply.
- DTO `gameOver`/`winnerId`; both pages show a Game Over overlay (winner -
  "You win!" from your own seat - both Life totals, "New game" back to the
  setup screen) over the dimmed board; mobile's action button reads "Game
  over". Checked with games played to the end through the API on both pages.
Tests: `GameOverTests` (lethal combat stops a queued reflect, deck-out can
end it, nothing plays on after).

### What-if: Bulwark Tardigrade at 1/2 instead of 1/3 (2026-09-30)

User question: would a weaker Bulwark (the Tardigrade's stats-only L3
face) reduce stalemates? New simulator switch `SIM_BULWARK_DEF=N`
(verified to reach the live dice). 1,200 games each, current roster,
personas on: turn-cap games 91 (1/3) vs 93 (1/2); every Champion's win
rate within ~1 point (Wolf 38.8/39.2, Armadillo 58.3/59.2, Owl 43.3/42.5,
Eagle 58.3/58.8). No effect. The Bulwark is one face in six on a die the
bot mostly spends as energy; the remaining stalls are the Rhinoceros
walls (Armadillo wins ~8% of games on the turn-cap tiebreak; Armadillo vs
Owl averages 36 turns, every other pairing 15-24).

## Intimidate and Infiltrate, on two new animals (2026-10-03)

User request: "implement a few more keywords, like Intimidate or
Infiltrate." Design calls, asked up front:

- **Intimidate: Dice Masters rule, as-is.** When fielded, remove a target
  opposing creature from the Field until end of turn. Chosen over a
  simpler "target can't block" version, which would just have been an
  unrestricted Barn Owl. New `Zone.Intimidated` (appended last). While
  there the die isn't active, so it can't block, can't be targeted, and
  its "while active" text is off (tested with Musk Ox's aura). It keeps
  its face, and `TurnEngine.CleanUp` puts it back on the Field without
  "fielding" it (no On Field, no cost). It loses damage and this turn's
  modifiers on the way out.
- **Infiltrate: Dice Masters rule, as-is** (user: keep the DM ability; it
  isn't really "safe" from post-block effects, it just moves from the
  Attack Zone to the Field Zone). When an attacker's lane has no blockers,
  its controller may return it to the Field and deal the opponent 1 damage
  instead of its full ATK. In Dice Kingdom the payoff is the body: an
  unblocked attacker otherwise leaves play. Queued in
  `CombatEngine.DeclareBlockers` behind the On Block abilities, so it
  resolves before the Action/Global window, guarded by a new
  `InZone(AttackZone)` condition in case an On Block ping already KO'd it.
- **Both are engine keywords** (`src/DiceFight.V2/KeywordAbilities.cs`):
  the card lists just the keyword, like Deadly. Intimidate's ability comes
  in through `QueryEngine.AbilitiesOf`, so a granted Intimidate works, and
  blanking turns it off.
- **New animals, kept off the current teams** (user: "leave the current
  teams for now - maybe a fifth Champion once we get enough new
  abilities"). **Frilled Lizard** (Eye, buy 4, 1/2/4 · 1/3/4 · 2/4/5,
  Homash 1.38, Intimidate: the threat display) and **Flying Squirrel**
  (Wing, buy 3, 0/2/2 · 1/3/3 · 1/5/3, Homash 1.64, Infiltrate: glides in,
  glides back out). Both appear in a new "Not on a team yet" section of the
  card reference (`tools/CardCatalogDocs`), which never listed benched
  cards before.
- Two other evasion ideas from the same talk went to `PARKED_IDEAS.md`
  (#8 "unblocked bonus", #9 "can't be blocked by anything bigger").

Smaller changes along the way:
- `MayPay` gained an optional `Prompt`. Infiltrate's offer used to read a
  bare "You may.", and now says what it's for.
- The desktop choice panel could not decline a "you may": Confirm stayed
  disabled until something was picked. That was a real, older bug for
  every optional choice, not just Infiltrate. It now has a Decline button
  when `minCount` is 0. Mobile already had Skip.
- `MoveToZone` clears `Lane`/`AttackOrder` when a die leaves the Attack
  Zone by any route.
- Readable log lines: "Frilled Lizard intimidates Box Turtle off the Field
  until end of turn.", "Flying Squirrel slips back to the Field."
- Bot: takes Infiltrate when the full hit is 2 or less and not lethal (or
  when 1 damage is itself lethal). Intimidate uses the existing
  harmful-target scoring.
- UI: Intimidated dice show dimmed under each Field ("Intimidated · back
  at end of turn") on both pages. New SVG icons for both animals.

Simulator (300 games/matchup, powers off), like-for-like what-if swaps:

| Swap | Champion win% before -> after |
|---|---|
| Eagle: Barn Swallow -> Flying Squirrel | 57.3 -> 62.3 (vs Wolf 61 -> 72.5) |
| Owl: Hyena -> Frilled Lizard | 43.1 -> 45.1 |

Flying Squirrel looks a little strong for its slot. Coming home keeps the
body every time the lane goes unblocked, and the Homash model doesn't
price that. Worth a stat trim (e.g. L3 5/3 -> 4/3) before it joins a
team. Frilled Lizard sits about where Hyena did. Verified in the browser
by having both seats play bot-vs-bot through the real API until each
keyword came up (`~/.devtools/playwright/keyword-check.js`, with the
cards swapped into packs locally for the run only), then screenshotting
mobile and desktop.

### Flying Squirrel trimmed: L3 5/3 -> 4/2 (2026-10-03)

User call, after the simulator showed the first pass lifting Golden Eagle
about 5 points: lower the ATK, "might lower D by one as well, as that
makes attacking and trying to get the Infiltrate more of a risk tradeoff
as the die is also easier to KO." Read as the L3 face only (taking a point
off every level would land it at Homash 1.27, strong-ability pricing for a
keyword-only card). Now 0/2/2 · 1/3/3 · 1/4/2, Homash 1.45.

Same what-if swap (Eagle: Barn Swallow -> Flying Squirrel, 300 games per
matchup, powers off): Eagle 57.3% baseline -> 62.3% first pass -> 60.0%
now. vs Wolf: 61.2 -> 72.5 -> 67.7. Still a modest upgrade over Barn
Swallow, inside the range where a fifth Champion's own pack would decide
whether it needs more.

## Attune (N) and Obscure, on two more new animals (2026-10-03)

User request: "Let's do Attune next, since we have action dice now."
Decisions:

- **Action dice only.** "A Keyword that fires off Globals would be a
  different, distinct keyword." The DieUsed event only fires from
  `TurnEngine.UseAction`, so Globals never count. "Action die" is the
  broad category, so any action die qualifies, Basic or not.
- **Attune keeps the Dice Masters effect, with a parameter.** While
  active, each time you use an action die, deal 1 damage to the opponent
  or a target character die. "Attune N" deals N (user: "Boom Boom is
  effectively Attune 2"). New `TargetKind.CharacterDieOrOpponent`, because
  `DieOrPlayer` would have offered your own face as a target. Each Attune
  keyword on a die fires separately, so a granted Attune 2 on an Attune
  die deals 1 + 2.
- **Obscure, built alongside it.** User: Dice Masters started with
  open-ended "when you use an action die, ..." abilities (Yuan-Ti's
  unblockable) and later turned that one into the Obscure keyword, so keep
  each payoff its own keyword rather than an open "Attune: <anything>"
  trigger. While active, each time you use an action die, this creature
  can't be blocked this turn (`CombatFlag Unblockable` on itself).
- **Don't judge them from the simulator's action usage.** Today's ~0.5-0.85
  action uses per player per game reflect four expensive Basic Actions,
  not the cheap ping/ramp/churn actions real decks buy. A payoff on the
  team also makes buying any action more attractive. Priced in the
  weak-ability band (~1.55) for now.
- **New animals, still off the teams:** **Electric Eel** (Claw, buy 3,
  0/2/1 · 1/3/3 · 1/4/4, Attune) and **Chameleon** (Eye, buy 4,
  1/2/4 · 1/3/5 · 2/5/6, Obscure).

Both are engine keywords in `KeywordAbilities`, like Intimidate. Bot:
- values action dice +1.5 when its team has an Attune/Obscure creature;
- uses an action in Main, before declaring attackers, when an Obscure
  creature could attack;
- aims Attune at the best opposing creature the damage actually KOs,
  otherwise at the opponent's face.

**A bug the simulator caught:** the first Chameleon run threw in 20 of
~1,200 games. A lane holding an unblockable attacker can't be blocked at
all (`CombatEngine.ValidateUnblockable` - a lane is the unit of combat),
but the bot only dropped the unblockable attacker itself and still tried
to block its lane-mate. Nothing in Dice Kingdom granted Unblockable
before, so this never came up. The bot now skips the whole lane; there's
a regression test.

**Open question for the user:** under that same lane rule, Obscure makes
every attacker sharing its lane unblockable too. The bot doesn't exploit
this on purpose (attackers get their own lanes until there are 5+), but a
human player can: put the Chameleon in a lane with the biggest attacker.
That is either a nice combo or a rules hole. The alternative is "a lane
can still be blocked; the blocker just can't engage the unblockable die."

UI:
- New `Unblockable` flag on the die DTO, shown as a badge like "Must
  block" on both pages. A blocker's only clue before was the server's
  rejection, which named an internal die id; that message now uses the
  card name and says the lane can't be blocked.
- The desktop choice chips can now show a player as a target. Before, a
  player candidate rendered as "Tardigrade undefined/undefined"; mobile
  already showed a name button.
- New icons for both animals.
- Cleaned up a stale "no creature has Infiltrate" comment in the mobile
  attack-chain code.
- Verified in the browser the same way as Intimidate/Infiltrate: the
  bot-driven API run until an Attune choice and an Obscure trigger came
  up (`keyword-check.js`, now with `WANT`/`CHAMPS` env vars), then
  mobile + desktop screenshots.

## Seven more keywords: Energize, Energy Drain, Tag Out, Sacrifice, Range, Aftershock, Breath Weapon (2026-10-03)

User's list, with the instruction to use the Dice Masters rules
throughout. Two calls asked up front:
- **Breath Weapon's payment: pick the dice.** You tap Reserve dice, or
  pick none to decline. Paid like a Global.
- **Open-ended effects: DM-style defaults.** Aftershock deals 2 to a target
  character die; Energize deals 2 to a target character die or the
  opponent; Sacrifice is On Field, sacrifice one of your creatures to deal
  its ATK to a target creature.

All seven are on new animals, still off the teams: Firefly (Energize,
Wing), Leech (Energy Drain, Shell), Meerkat (Tag Out, Shell), Army Ant
(Sacrifice, Claw), Archerfish (Range, Wing), Bombardier Beetle
(Aftershock, Shell), Komodo Dragon (Breath Weapon, Claw). Stats and Homash
are in COST_MODEL.md. Keywords with an X read "Range" as X = 1 and
"Range 2" as 2 (`KeywordAbilities.ParamOf`/`MaxParam`; Attune now uses it
too).

How each is built:
- **Energize**: `KeywordAbilities.Energize(effect)` (moved from
  DpsCards). **A real bug fixed along the way:** a Roll-and-Reroll reroll
  onto a double face fired it twice, once for the reroll and once at the
  start of Main, because the helper dates from before rerolls were
  interactive. New `EventFilter.ExcludeStep` fixes it for the 15 DPS
  Energize cards too. A test proves the old shape dealt 4 instead of 2.
- **Energy Drain X**: in `CombatEngine.DeclareBlockers`, lane-wide like
  Deadly. Every spin is worked out first, then applied together. New
  shared `EffectInterpreter.SpinLevel`.
- **Tag Out**: queued after Infiltrate for every Field Zone die with it,
  the Active player's first. `MayPay` -> Prep itself -> +2A/+2D to a
  target. The Meerkat recycles a lot: it costs 0 at L1/L2, so it's
  refielded about 7 times a game in the simulator. That is the DM design.
- **Sacrifice**: new `Sacrifice` effect node, used as `MayPay.Cost`. Out
  of Play on the owner's turn, the Used Pile otherwise, never a KO.
- **Range X**: new `RangeShot` / `ResolveRangeShots` nodes. Each Range die
  records its target (Active player first), then one step deals every
  shot, so the defender's Range die fires even if the attacker's Range KO'd
  it (rule 1). Only when a Range creature attacks.
- **Aftershock**: new `TriggerKind.DieRemovedByOpponent`. A `causedBy` now
  runs through `ApplyDamage`/`TryResolveKO`/`KoDie`/`MoveDie`: combat and
  Deadly KOs are the opponent's, and ability effects belong to the
  ability's controller. Your own Sacrifice or Tag Out never triggers it;
  their Intimidate does.
- **Breath Weapon X**: new `MayPayEnergy` node, queued once per unique
  attacking creature, and skipped when the pool can't cover it. A short
  pick re-asks with a note rather than throwing (the choice is already
  cleared by then). New `TurnEngine.PayAbilityEnergy` spends like a
  Global: partial spend, Out of Play on your turn.

Bot:
- Range aims at a kill, counting the shots already called on a target.
- Breath Weapon fires for lethal or at least one KO, paying with
  BotEnergy's usual pick.
- Tag Out only when one of its dice is in a fight, and boosts that die.
- Sacrifice only for a clearly better KO. In simulator games the Army
  Ant was fielded 34 times and never found one, which is by design; unit
  tests cover both branches. Energize reuses the Attune targeting.
- New `PendingChoice.DamageHint` lets it see "damage equal to its ATK".

UI:
- The desktop choice chips label Reserve dice by their energy
  ("Tardigrade · 1 Wild").
- The mobile sheet says "Pay energy" instead of "Choose a target" when
  every option is your own Reserve die.
- Seven new icons.
- Browser-checked by bot-driven API runs (`keyword-check.js`, which gains
  `FORCE=1` to steer seat one into a Komodo or Archerfish attack, since
  the bot rarely lines those up), with the cards swapped into packs
  locally for the run only. A 900-game simulator run with all seven
  swapped in: 0 errors. Every keyword except Sacrifice and Breath Weapon
  fired in bot play; those two were covered by the steered run and unit
  tests.

## Reroll showed the landed face before tumbling (2026-10-04)

User report: "you can sometimes see the new die face on a reroll before
the animation kicks in." Two causes:
- The cube switched to the new face as soon as its tumble was scheduled,
  so each die after the first sat flat on its landed face through its
  70ms-per-die stagger delay.
- On a fast machine the state update also rendered a frame before the
  tumble started (the rAF split from the 2026-09-16 choppiness fix).

The tumble now carries a `HeldFace` (the pre-roll face and energy pips).
`DieCube` shows it until the die's delay plus 30% of the tumble, then
switches mid-air. Rerolls also start the tumble before committing the new
state. Measured with a per-frame probe
(`~/.devtools/playwright/reroll-flash-probe.js`): old code, 6 of 9
changed dice showed the landed face for 2-6 frames before moving; new
code, 0 of 7. That only reproduced once the probe compared against each
die's own landing frame; comparing against the final screen measured
different tiles after the step changed.

## Die status cues: Claude Design's "face frame" (2026-10-04)

The design pass that `v3/STATUS_CUES_BRIEF.md` set up. Claude Design
explored three systems and the user picked **1b, "face frame"**: every
cue becomes part of the die itself, so nothing hangs wider than the tile.
The handoff (README spec, the HTML mockups, cue metadata) is in
`v3/design_handoff_die_status_cues/`; open `Die Status Cues.dc.html`
through a local web server. Built in three commits.

**1. Server data.** One `statuses` list per die (`DieStatuses`): kind,
source, duration ("turn" / "cleanup" / "whileActive") and keyword. The
tile and its explainer both read it, so they can't disagree. To name
sources, the engine now records:
- who applied each combat flag (`CombatFlagSources`);
- `GrantedTag.Source` and `IntimidatedBy`;
- a per-die `LastSpin`, with which drainer it was for Energy Drain;
- a lockout's `SourceName`.

The game DTO adds `UnblockableLanes` and `LockedCards`.

**2. The frame**, drawn in `DieCube`:
- Combat rules are an amber border on the cube's front face (solid =
  must, dashed = can't, double = unblockable), so the frame tumbles with
  the die. A glyph tab sits on the lane edge: below for yours, above for
  theirs. Can't block beats Must block.
- Text blanked: faded, struck-through art. A granted keyword: a cream
  "+OC" tab. Deadly-engaged: a red bottom-left dog-ear. Changed ATK/DEF:
  cream (up) or red (down) number chips. Intimidated: a grey dashed frame
  and greyscale.
- Pangolin's lockout hatches the card in the mobile buy strip and the
  desktop roster.
- An unblockable lane gets a double amber border, a "»" tag and a
  "can't be blocked" note.
- Tap to explain: any cued die is tappable. Mobile's inspect bar adds a
  "Your die · Attack · Lane 2" line and one row per cue ("From {source}.
  {rule}", with a duration). Desktop shows the top cue's word under the
  tile, and the die's popover gets the same rows (`CueRows`). The old
  "Must block"/"Unblockable" pills are gone.

**3. Motion and legend.**
- The spin flash (`SpinFlash`) is the one cue that moves: a fading white
  ring plus a chip such as "▼ L2 · Energy Drain". The page remembers which
  spins it has shown per *die*, not per tile. A drained attacker usually
  changes tiles (lane back to Field) before a polling page sees it;
  per-tile memory caught 1 of 4 spins in a bot-played check, per-die caught
  5 of 5. The chip names only the effect: with the card name too, it ran
  off-screen for a die at the edge.
- The "Reading a die's frame" legend (`DieFramesLegend`) uses real
  DieCubes. It opens the first time any cue appears (a localStorage flag)
  and can be reopened from desktop's Help (?) and mobile's step pop-out,
  since mobile has no Help menu.

Deviations from the design, both deliberate:
- **Stat chips ignore the Champion passive.** Wolf's +1 ATK and
  Armadillo's +1 DEF apply to every die all game, so following the spec
  literally chipped every die (seen on a real board) and buried the
  changes that matter. The tap breakdown still lists the passive.
- **Desktop tiles stay 34px.** The mockup assumed 64px. The frame values
  work at 34, and resizing desktop tiles is a separate layout change.

Verified on bot-played boards with the steered capture scripts
(`keyword-check.js` gained `status:<kind>`, which taps the die so its
explainer shows; `spin-flash-check.js` keeps both seats' pages open while
the bots play). Sources came through right for Hermit Crab, Opossum,
Chameleon, Frilled Lizard, Anger Issues and Pangolin.

## Champion powers: once per turn instead of always on (2026-10-04)

User direction: replace the always-on passives with once-per-turn powers
("instead of Wolf giving everyone +1A, it could grant +3A to one die once
per your turn"). Baseline first (simulator, 200 games per matchup, same
bot both sides):

| Champion | No powers | Old passives | Effect of the old one |
|---|---|---|---|
| Wolf (+1 ATK to all) | 38.8 | 68.3 | +29.5 |
| Armadillo (+1 DEF to all) | 58.3 | 50.5 | -7.8 |
| Golden Eagle (-1 fielding) | 58.3 | 52.5 | -5.8 |
| Great Horned Owl (Foresight) | 43.3 | 27.3 | -16.0 |

The user picked one power per Champion (`ChampionPowers`, new
`ChampionPassiveKind`s):
- **Wolf**: once per your turn, one of your creatures gets +N ATK.
- **Armadillo**: once per turn (either turn), after blocks, prevent all
  combat damage to one of its creatures that's in a fight (new
  `CombatFlagKind.PreventCombatDamage`; ability damage and Deadly still
  apply).
- **Golden Eagle**: once per your turn, field one creature free
  (`TurnEngine.Field(free: true)`).
- **Great Horned Owl**: once per your turn, spin one creature a level -
  yours up, theirs down (new `SpinByOwner` effect). Replaces Foresight.

Wolf, Armadillo and Owl are used like a free, owner-only Global: a new
`champion-power` endpoint queues the effect, and its target is an
ordinary pending choice (the pick UI and bot already handle it). The
Inactive player gets priority for Armadillo's shield only when it could
matter (after blocks, with one of its dice actually fighting), so an
Armadillo player isn't asked to pass every window.

**Tuning: Wolf's +3 became +1.** As proposed, +3 won 84% (+2: 76%, +1:
60%). An unblocked attacker turns it straight into face damage, and this
game's combat is low-damage (~0.5 damage a turn). Making it Main-only, so
the opponent can block around the pumped die, didn't help (84% at +3). Owl
spinning 2 levels gave about the same overall rate but made matchups
lopsided (Owl beat Armadillo 81%, lost to Wolf 75%), so it stays at 1.
The simulator has knobs for all of this: `SIM_LEGACY_POWERS`,
`SIM_WOLF_PUMP`, `SIM_WOLF_MAIN_ONLY`, `SIM_OWL_LEVELS`.

The bot uses every power:
- Wolf pumps an unblocked attacker in the window, else a blocked one.
- Armadillo shields a die combat would KO, and plans one "free wall"
  block around its shield.
- Owl spins the opponent's best L2+ creature down, else its own up.
- Eagle fields its costliest creature free.

**Result (300 games per matchup, defaults):**

| | Old passives | New powers |
|---|---|---|
| Wolf | 68.3 | 61.0 |
| Eagle | 52.5 | 57.0 |
| Armadillo | 50.5 | 40.7 |
| Owl | 27.3 | 40.4 |

The spread is tighter, but matchups are still lopsided: Wolf beats Owl 81%,
Eagle beats Owl 76% (both existed before), and Owl now beats Armadillo 79%
by spinning down its walls. Armadillo's shield only comes up about once a
game. All of this is bot-measured, so treat it as direction, not truth.

UI: a "Pump" / "Shield" / "Spin" button next to your Champion's name
(mobile mat head; desktop scoreboard) while the power is usable, and
"Field free (Golden Eagle)" on the field action. A shielded die shows a
cream "SH" tab and explains itself ("Shielded"). **A bug found in the
browser:** the client auto-passes the action window when nothing was
blocked and the active player "couldn't act" (`activeCouldAct`). It
didn't know about Champion powers, so Wolf's best play - pumping an
unblocked attacker - got skipped on the web. Fixed, and verified that the
window now waits with the button showing.

### Where Wolf's wins come from; shield vs Overcrush (2026-10-04)

User questions:
- Does Wolf win through the "2+ attackers in a lane get Overcrush" rule,
  or by going wide for chip damage?
- Does Armadillo's shield correctly stop Overcrush (it should: Overcrush
  only carries through once every blocker is gone)?

The simulator now breaks combat damage down from the match log (300
games per matchup, current powers):

| | Unblocked / game | Overcrush carry / game | Lanes with 2+ attackers / game |
|---|---|---|---|
| Wolf | 17.5 | 0.04 | 0.06 |
| Armadillo | 7.5 | 1.5 | 1.6 |
| Owl | 14.1 | 1.1 | 0.8 |
| Eagle | 16.7 | 0.3 | 0.3 |

**Wolf doesn't use the lane rule at all.** It's chip damage: 1.8
attackers per attack, almost all unblocked. Its pump lands on an
unblocked attacker 4.6 times a game (0.1 on a blocked one), about 4.6
face damage, roughly a quarter of its total. The defending bot doesn't
account for a pump arriving after it declines to block. Armadillo is the
one that stacks lanes.

**The shield does stop Overcrush**, both kinds (keyword and 2+
attackers): a shielded blocker takes no combat damage and stays in the
lane, so carry-through never triggers. Pinned by two tests in
`ChampionPowersTests` - the unshielded case deals the expected 4, the
shielded one 0.

### The bot learns the bag: chump value, un-culling, expected threats (2026-10-04)

User input, from Dice Masters: letting a weak sidekick through un-culls it
into the attacker's bag (it clogs their draws), and a chump block is often
good because the KO'd die is rolled again next turn. Also: "keep enough
blockers so that I can block the higher attack characters I expect my
opponent to have next turn" - all zones are public, so the odds are known.

**Blocking** (`BlockScore`):
- A dying blocker isn't a full loss: `PrepBonus`, since a KO'd die goes to
  Prep and is rolled on top of next turn's draw. That's worth more for a
  Tardigrade, whose faces nearly all carry energy (one way the Tardigrade
  redesign shifted this math from DM's sidekicks).
- KO'ing an attacker hands its owner that same extra die.
- Blocking a Tardigrade that survives forfeits un-culling it (`UncullBonus`,
  once its owner has bought Characters).
- Wolf's unused pump is assumed to be coming.

**Measured: almost no effect**, because Wolf's attacks met NO available
blocker 95% of the time. The real issue was the attack side: the bot's
crack-back check read only the opponent's current Field, which right after
their own swing is usually empty. So every game was a mutual all-in race:
unblocked attackers leave play, which empties the Field again.

**Attacking** (`DecideAttackers`): the crack-back now uses
`ExpectedThreats`: their Field creatures plus every die likely to come up
next turn (Prep certain, Bag at draw odds, Used/Out of Play for whatever
the Bag can't cover), each weighted by its creature-face odds and average
ATK, plus their Wolf pump. Each creature swings only if its ATK, as a share
of the opponent's life, beats the threat it would block next turn, as a
share of its own life (tilted by persona Aggression).

**Results (300 games per matchup, current powers):**

| | Race bot | Expected-threat bot |
|---|---|---|
| Wolf | 61 | 19 |
| Armadillo | 41 | 56 |
| Owl | 40 | 67 |
| Eagle | 57 | 57 |
| Turn-cap games | ~7% | 14% |

Defenders now have blockers when Wolf attacks: no blocker available 43% of
the time, down from 95%. Wolf's +1 was tuned against the race bot.
(Earlier, a blunt "never swing a 1-ATK Tardigrade" experiment landed in a
similar place - Wolf 17, Armadillo 67 - and was replaced by this.) Balance
numbers are dominated by bot policy; the next tuning pass should be done
on this bot.

**Follow-up, same day: Wolf back to +3.** Re-tuned on the expected-threat
bot (200 games per matchup):

| Wolf pump | Wolf | Armadillo | Owl | Eagle |
|---|---|---|---|---|
| +1 | 19 | 56 | 67 | 57 |
| +2 | 37 | 53 | 62 | 47 |
| +3 | 47 | 49 | 60 | 44 |

+3, the user's original number, is the most even setting yet. The +1 had
been fit to the race bot, where every pump was free face damage. **Owl
(~60%) is now the outlier**; its spin is used ~11 times a game under this
bot, the next thing to look at. Turn-cap games are up to ~15%, since
boards that keep blockers stall more.

## 2026-10-04 - Tardigrade face layouts (simulator only, die unchanged)

User ask: try a 1/2 Bulwark, and fewer faces you can field - Dice Masters'
sidekick had 1 fieldable face in 6, ours has 5 ("might have been too much").
New simulator knob `SIM_TARDIGRADE` rebuilds the die from a face spec
(`energy[:level/atk/def]`). Champion powers on, expected-threat bot,
600 games per matchup (shortlist from a 200-game pass of 10 layouts):

| Layout | Wolf | Arm | Owl | Eagle | Spread | Turn cap | Turns | Tardigrades fielded/g |
|---|---|---|---|---|---|---|---|---|
| Today: 0/1 ×2 (2E), 1/1 ×2 (1E), Bulwark 1/3, Wild | 47 | 47 | 57 | 47 | 9.9 | 17% | 29.0 | 8.5 |
| Bulwark 1/2 | 48 | 48 | 55 | 48 | 7.3 | 18% | 29.2 | 8.4 |
| Only Bulwark 1/2 + Wild (L2 1/1) have stats | 50 | 46 | 53 | 48 | 6.9 | 14% | 27.6 | 7.3 |
| Only Bulwark 1/2 has stats | 54 | 49 | 49 | 47 | 6.6 | 13% | 26.7 | 6.3 |
| Only Bulwark 1/3 has stats | 53 | 53 | 48 | 45 | 8.6 | 12% | 26.8 | 6.5 |

| 1-energy faces lose stats (L1 0/1 ×2, Bulwark 1/3) | 52 | 49 | 53 | 45 | 8.6 | 13% | 27.0 | 7.1 |
| Every face has stats (Bulwark 1/2, Wild L2 1/1) | 45 | 48 | 57 | 49 | 12.3 | 19% | 29.9 | 9.1 |

200-game-only: 2-energy faces losing stats (Bulwark 1/3) gave 47/49/57/44,
14% cap - about today's.

The last two rows first errored out: two engine assumptions about dice
shape, not Owl's power. `SpinLevel` assumed contiguous levels (an L1+L3
die threw spinning L1 up), and `SpinToEnergy` (Cuttlefish's On Attack)
threw on a die with no pure energy face. Now a spin steps to the next
level the die has, and a spin-to-energy on such a die does nothing.

Reading: fewer fieldable faces mostly takes Owl down (its spin has fewer
Tardigrade bodies to work on) and cuts stalls; every 1/2-Bulwark layout
lands at a ~7 point spread. The 200-game pass had "Bulwark only, 1/2" at
3.9 - that was noise. Choice between the last three rows is a feel call
(the Wild-with-stats row keeps a field-or-spend decision on the Wild).

**Follow-up: two Wilds.** User layout - Bulwark 1/2, two Wilds (L2 1/1),
two single, one double (`2,1,1,0:3/1/2,W:2/1/1,W:2/1/1`). 600 games/matchup:

| Layout | Wolf | Arm | Owl | Eagle | Spread | Turn cap |
|---|---|---|---|---|---|---|
| User's two-Wild layout (6 pips/die) | 60 | 28 | 57 | 53 | 32.0 | 24% |
| ...second Wild without stats | 61 | 29 | 58 | 49 | 32.4 | 22% |
| ...two doubles + one single instead (7 pips/die, as today) | 49 | 46 | 58 | 45 | 13.0 | 18% |
| Two-Wild layout, every seat on the Default persona | 67 | 13 | 58 | 62 | 53.7 | 18% |
| Bulwark + Wild layout, Default persona (control) | 62 | 23 | 53 | 60 | 39.3 | 9% |

Armadillo's collapse is the pip count, not the Wilds or the extra body:
the layout has 6 energy per die against today's 7, and Armadillo - a ramp
team whose bot races to Rhinoceros (BotPersona.Armadillo) - is the team
most starved by it. Even with 7 pips back, two Wilds with stats keep Owl
high (more bodies to spin) and stall more than the one-Wild layout.
Armadillo is weak under the Default persona in every layout, so its
numbers lean on the Rhinoceros rush.

**Adopted (user call): 2 double, 1 single, two Wild + L2 1/1, Bulwark L3 1/2.**
"That gives you a 1/6 chance of a crappy roll, everything else should be
useful." Still 7 energy per die; 3 faces in 6 can be fielded (was 5).
Tardigrade levels now run 2-3 only, which broke the old "every die has
levels 1-3" assumption in two places: Mutation's Global offered an L2
Tardigrade to spin down, and the bot's Owl spin aimed at dice that
couldn't move. Both now use new StatKinds LevelsBelow / LevelsAbove ("is
there a level on this die to spin to"). Side effects: a Tardigrade is
never a level 1 creature now (Cuttlefish's spin-to-energy can't hit one;
Basilisk already excluded them).

Live result (600 games/matchup, powers on): Wolf 48, Armadillo 43, Owl 57,
Eagle 50 - spread 13.4, turn cap 19%. Armadillo vs Owl is 20/80, the worst
single matchup; the other five are within 43-59. Armadillo next: a cheap
ramp Character or a ramp Global (user, same day).

## 2026-10-04 - Musk Ox gets Kree Captain's Global (Armadillo ramp)

User call: Armadillo needs ramp. Musk Ox (4-cost Shell, +1 DEF aura) adds
Dice Masters' Kree Captain Global: "Pay 2 Shell. Once per turn, the next
creature you purchase this turn costs 3 less (minimum 1)." Built from the
existing PurchaseModifier; buy costs shown to a player now include it
(QueryEngine.GetPurchaseCostNow), and it's only usable by the player who
can still buy this turn (AbilityPreview). The bot pays for it when the
purchase plan it enables beats the plan without it (ties go to keeping the
energy). Like every Global it's usable by either player once the card is
in the game - opponents use it ~0.6 times a game, paying with Wilds.

| | Wolf | Arm | Owl | Eagle | Turn cap |
|---|---|---|---|---|---|
| Before (new Tardigrade die) | 48 | 43 | 57 | 50 | 19% |
| With Musk Ox's Global | 42 | 54 | 56 | 47 | 13% |

Armadillo uses it 5.3 times a game; Rhinoceros buys 1.1 -> 1.65 a game.
Matchups: Armadillo beats Wolf 75/25 (was 57) and Eagle 65/35 (was 55),
but still loses to Owl 23/77 - the ramp doesn't fix that one.

Web: the mobile Global rail listed Basic Actions only; it now lists any
card in the game with a Global. (Desktop has no Global rail at all yet.)

## 2026-10-04 - Bot: Hermit Crab + Rhinoceros combo fixed

User question: does Armadillo force a block into Rhinoceros, using the
forced blocker to (a) take reflected damage and (b) give the lane Overcrush?
Owl-matchup logs (200 games): the bot did swing Rhinoceros alone into a
forced blocker, but then used Armadillo's own shield on Rhinoceros 174
times - preventing the very damage it reflects, so the combo did nothing.
Fixes: the shield never goes on a retaliator (and the blocking planner no
longer counts reflection on one it shields); with a forced blocker out,
Rhinoceros leads ONE lane and everything else attacking stacks behind it
(declared first, it soaks the blockers' lethal-first damage; 2+ attackers
have Overcrush).

| 600 games/matchup | Wolf | Arm | Owl | Eagle | Turn cap |
|---|---|---|---|---|---|
| Musk Ox Global only | 42 | 54 | 56 | 47 | 13% |
| + combo fix | 42 | 61 | 50 | 46 | 9% |

Armadillo vs Owl 23 -> 44. Armadillo now beats Wolf 75/25 and Eagle 68/32,
so it's the strongest team - the Musk Ox ramp was partly making up for the
bot throwing away its main combo, and may now be too much.

**Same day: discount taken back off Musk Ox** (user: "take the discount off
for now and see how some humans play"). Kept as DiceKingdomConfig.
KreeCaptainGlobal / WithKreeCaptainGlobal for the simulator
(SIM_MUSKOX_GLOBAL=on) and its tests. Live roster, combo-fixed bot, no
discount (600 games/matchup):

| Wolf | Arm | Owl | Eagle | Spread | Turn cap |
|---|---|---|---|---|---|
| 48 | 54 | 48 | 49 | 5.6 | 15% |

Most even spread so far. Matchups: Arm-Wolf 58/42, Arm-Owl 48/52, Arm-Eagle
58/42, Wolf-Owl 48/52, Wolf-Eagle 54/46, Owl-Eagle 41/59. Next: real human
games.

## 2026-10-04 - Game recording (for tuning on human play)

User call: record human games, cheaply, to tune teams and teach the bot;
Cloud Storage chosen (one JSON file per game). Built and tested locally,
waiting on the one-time Cloud Console setup - see v3/GAME_RECORDS.md.
Every game now draws all its randomness from one seeded generator, so a
recorded game replays exactly; tools/GameRecords replays records and
compares every human move with the bot's choice in the same spot.

## 2026-10-08 - Seats survive a closed tab; game links folded away

User lost one side of a game: seat tokens lived only in the tab's
sessionStorage, so closing the host's tab made that seat unplayable. Now
every game's seats are also kept in localStorage (web seats.ts) and the
start screen lists them under "Resume a game" (lobby.tsx ResumeGames) -
games the server has since forgotten are dropped with a note. The tab's
current game stays in sessionStorage, so a normal and a private window
can still play the two sides. In-game, "Invite" and a new "Your seat"
link (back into your own side) sit behind one small "Game links" toggle
(user: rarely used, so it can take a click). Also: stat changes are now
logged ("Wolf: Wolverine gets +3 ATK this turn.").

## 2026-10-08 - Bot bag dilution (knob only, default unchanged)

User, on not buying Honey Badger early: "you don't want too many dice in
your bag, or it will take forever to get around to the dice you just
bought." The bot's purchase plan sums value across every die it can
afford, so two cheap dice beat one big one. New BotPersona.DilutionPerDie
charges each purchased die; default stays 0 until there's more human data
(user: "wait for more than one partial game's worth of data").
Simulator (SIM_DILUTION, 400 games/matchup):

| Per-die cost | Wolf | Arm | Owl | Eagle | Spread | Turn cap | Turns | Buys/game |
|---|---|---|---|---|---|---|---|---|
| 0 (live) | 48 | 55 | 46 | 50 | 8.3 | 15% | 28.2 | 11.5 |
| 1 | 45 | 52 | 51 | 50 | 6.6 | 12% | 26.3 | 10.6 |
| 1.5 | 46 | 53 | 48 | 51 | 7.0 | 10% | 24.9 | 9.8 |
| 2 | 46 | 52 | 48 | 53 | 6.3 | 11% | 24.7 | 9.5 |
| 3 | 51 | 35 | 50 | 63 | 27.7 | 17% | 26.2 | 8.0 |

1-2 gives shorter games and fewer stalls; 3 starves Armadillo's Rhinoceros
plan. In the one human game recorded (5607496d), only ~4 matched the
human's buys (Wolverine over Honey Badger + Mongoose, which tie at 2).
tools/GameRecords --dilution N compares human buys against a given value.

## 2026-10-10 - Players: anonymous by default, accounts without passwords

User: work without logging in, using an identifier from the device. Anyone
who wants a record of their games, or the same account on several devices,
can make an account with no password, using a code instead. The user's picks:
no email yet (link devices with a short code instead), store players in the
game-records bucket, and keep a games list plus W/L.

- **Every browser is a player.** It registers on first use (`POST
  /api/players`) and keeps the token `<playerId>.<secret>` in localStorage
  (`web/src/dicekingdom/player.ts`). Every Dice Kingdom request sends it as
  `X-Player-Token`. The server keeps only a SHA-256 of the secret.
- **A name makes an account.** `/dice-kingdom/account` (AccountPage.tsx)
  is a real route, linked from both start screens ("Playing as ...").
- **Linking devices.** A named account shows a 6-character code (10 minutes,
  single use, in memory). Typing it on another device merges that device's
  player into the account: devices, games, and seats in live games. The old
  player is left as a `MergedInto` tombstone, so the second device's token
  keeps working unchanged. Ten wrong codes locks out linking for ten
  minutes. Email sign-in can come later as one more way to add a Device.
- **Game history.** `PlayerAccounts` keeps each owner's `PlayedGame` in
  step with the game: Create gives both seats to the creator, Open/Join make
  it an online game, and a seat played from another player's device changes
  hands (invite links). A `bot-decision` call marks the computer's seat, and
  the result is recorded at game over. W/L counts only when the player held
  exactly one human seat, so pass-and-play shows the winner without counting
  it. Seat tokens are kept so unfinished games show under "Resume a game" on
  every device (lobby.tsx `adoptServerGames`; `?resume=<id>` resumes one
  directly). Unfinished games the server has forgotten are pruned.
- **Storage.** `dice-kingdom/players/<id>.json`, with background writes
  like GameRecorder's. An anonymous player isn't written until it has a game
  or a name. Production keeps players in memory only until
  `GameRecords__Bucket` is set (see GAME_RECORDS.md).
- UI check: `~/.devtools/playwright/account-check.js` (a fixture player
  file, laptop shows code, fresh phone links, resumes the laptop's game).

## 2026-10-10 - Keyword reference

The card reference (`tools/CardCatalogDocs`, live at
`/dice-kingdom/cards.html`) now ends with a Keywords table: every keyword
in `DiceKingdomConfig.Keywords`, WizKids' Dice Masters wording (copied once
from wizkids.com/dicemasters/keywords as a fixed baseline - user: "we might
change how they work down the line"), what Dice Kingdom does differently,
and which cards carry it. `KeywordDef` gained `Original` and
`WrittenOnCard` (a label like On Field or Awaken, where each card writes
its own ability, vs. an engine-backed keyword a card only has to list).
A test now checks every keyword on a Dice Kingdom card is declared and
described - nothing checked Dice Kingdom's catalog against its own list
before. Of the 11 engine-backed keywords, only Fast, Overcrush and Deadly
are on a team today; the other 8 sit on benched cards.

## 2026-10-11 - Custom teams (PR #4) and the follow-ups

Stormy's PR #4 merged: `/teambuilder` is now the Dice Kingdom builder
(user: fine to retire the Dice Masters builder; it can come back later),
with a Tardigrade mix and "Play with this team", which creates a game with
`playerOneCustomTeam` (exact dice per card, Tardigrades per energy type, no
Champion - Champions in custom teams come later). Online invites with a
custom team aren't supported yet. Follow-ups on top:
- **Server ceiling, not format rules.** The builder enforces the format
  (house formats change them, e.g. 12 Tardigrades / 30 dice / 3 Basic
  Actions), so the server only refuses more than 100 character dice or 100
  Tardigrades (`V2GamesController.MaxCustomDice`) - without that, one
  request could ask for billions of dice and take the server down.
- **Records.** `RecordedSeat.ChampionId` is null for a custom team, and
  the seat carries `CardDiceCounts` and `Tardigrades` so GameReplayer
  deals the same team; the replay test now also plays a custom team.
- The staged team survives blocked `sessionStorage` (kept in memory).

## 2026-10-11 - First look at real games: purchases and rerolls

48 live games from the `dice_kingdom_playlog` bucket (Oct 7-11; some are
Stormy's quick tests, still real decisions), downloaded with a read-only
key (`~/.devtools/gcs.py`) into `game-records/prod/`. All 48 replay
exactly. 21 finished, all human vs bot: humans won 15-6. New
`tools/GameRecords --dump FILE` writes every human decision as a JSON line
in card/face names with the board around it.

**Purchases.** When both bought, the human bought the pricier card in 99
of the 124 disagreements - the bot's plan sums cost+1 per die, so two
2-cost dice beat one 4 (Wolverine vs Honey Badger, Musk Ox vs Hermit
Crab, Hyena vs Cuttlefish...). Against `DilutionPerDie`:

| Per-die cost | Human buys matched | Same card when both buy | Human pricier / cheaper |
|---|---|---|---|
| 0 (live) | 20% | 55/179 | 99 / 13 |
| 1 | 25% | 69 | 69 / 23 |
| 2 | 31% | 84 | 43 / 30 |
| 2.5 | - | 86 | 28 / 33 |
| 3 | 22% | - | - |

**Set as the default (DilutionPerDie = 2) the same day, user call.** 2 is where human choices balance out, and it's also the best of the
2026-10-08 simulator runs (spread 6.3, fewer turn-cap stalls, shorter
games). Much of what's left is order (people field first, then buy).

**Rerolls.** Humans reroll weak Tardigrade creature faces the bot always
keeps (L2 1/1: 28% vs 0%; L3: 39% vs 16%), and keep card-die energy more
often (68-79% rerolled vs the bot's 88-94%). 26 of 211 human rerolls threw
back a 2-energy Tardigrade face, spread over 12 games - unclear whether
deliberate or UI confusion.

## 2026-10-11 - Spun to energy leaves the Field

User bug: "Spinning a die to energy leaves it in the field. It can be
selected as a blocker (though it won't let you finish assigning it), it
can't be spent, and it doesn't clear." Cuttlefish's On Attack spin (and any
reroll to energy without a stated destination) only changed the face.
Rules 2.5.1-2.5.2: an energy face can't be in the Field Zone, so the die
goes to its controller's Reserve Pool - spendable on Globals during the
attacker's turn, cleared at that player's Clear & Draw - dropping damage
and modifiers (2.5.7), not a KO (2.5.3). EffectInterpreter
`LeaveFieldIfEnergy`. It happened in 5 of the 48 live games; those
records now drift at that move (expected - they recorded the old rule).

## 2026-10-12 - Random packs and collections (first pass)

User brief: the Champion teams stay as free "starter rosters"; custom-team
builders collect through random packs. As in Dice Masters, rarity must not
equal power ("decently competitive with just a common/uncommon team"), but
collecting should still pay - later ideas: trade-ins (5 C -> U, 4 U -> R,
3 R -> SR) and cosmetic badges/outfits for full sets. Starting numbers
(user's): 100 coins for a new player, packs 10 coins, 5 coins a day.

- Rarities: one table, `V2/Data/DiceKingdomPacks.cs` - 46 Characters, 18
  Common / 14 Uncommon / 10 Rare / 4 Super Rare. Basic Actions aren't in
  packs (they come with Champions). Strong cards at every tier on purpose:
  Hummingbird (4-cost On Field KO) is Common while Orca (same text, 5) is
  Rare. Super Rares are the showpieces: Silverback, Phoenix, Basilisk,
  Komodo Dragon. Every starter roster is C/U apart from its power card
  (Armadillo: Pangolin and Rhinoceros, both Rare) - a test holds it there.
  My first pass, for the user to adjust.
- What you collect is dice: each pulled card is one die of that character.
  A team plays up to DieLimit (4); extras are kept for trade-ins.
- A pack is 5 dice: 3 different Commons, 1 Uncommon, and a slot that's
  Uncommon 55% / Rare 40% / Super Rare 5%. Per card: each Common ~1 pack in
  6, Uncommon 1 in 9, Rare 1 in 25, Super Rare 1 in 80; ~40% of players
  see a Super Rare in their first 10 packs.
- Server-side and atomic: `PlayerAccounts.OpenPack` takes the coins and
  adds the dice under the players lock, with a cryptographic roll. The
  collection lives on the player file (`PlayerAccount.Collection`).
- Daily coins: the first `/me` or `/me/collection` of each UTC day (so the
  reset is 7-8pm US time). The start screen's "Playing as" line shows the
  coins and "+5 today".
- Linking a device merges collections but keeps only one starting grant
  (coins = mine + theirs - 100, floor 0); otherwise every throwaway browser
  linked in would bring 100 more. A brand-new anonymous browser still gets
  its own 100 - harmless while collections gate nothing.
- Page: `/dice-kingdom/packs` (cards flip over one by one; the collection
  grid by rarity with die pips). Not yet: the Team Builder doesn't limit a
  custom team to owned dice, and starter rosters don't add to the
  collection - both open questions for the user.

## 2026-10-12 - Trade-ins and starter rosters in the collection

User: "No limits yet. Starter roster is added to your collection. Let's do
trade ins next." So custom teams still use any card.

- Starter rosters: the first time a player finishes a game (win or lose)
  with a Champion, its 8 cards join the collection at DieLimit (4 each -
  the dice that team plays with); `PlayerCollection.Starters` keeps it to
  once. At the end, not the start, because which seat the computer plays
  is only known once it has asked for a move (`BotSeat`) - at creation
  both seats look like the creator's. Pass-and-play games add both
  Champions. Players from before packs get the starters of the finished
  games in their history when their collection is first made.
- Trade-ins (user's rates): 5 Commons -> 1 Uncommon, 4 Uncommons -> 1 Rare,
  3 Rares -> 1 Super Rare. The die you get is random (no picking a Super
  Rare outright) but only from that rarity's cards you don't have 4 of
  yet, so a trade always fills a gap; once all are full, any. Server takes
  any owned dice (`POST /api/players/me/trade-in`, one card id per die);
  the Packs page offers spares only - dice past 4 of a card, most
  duplicated first - so a trade never breaks up a playable set.

Found on the way: `PlayerAccountsTests.Players_Survive_A_Restart...` failed
on and off (every time when run alone). At shutdown the background writer
cancelled the write it was in the middle of, and that write was already
off the queue StopAsync drains - so a player file (or game record, same
code in GameRecorder) saved just before a redeploy could be lost. The
in-flight write now finishes; only waiting for the next one is cancelled.

## 2026-10-14 - Card sweep: a screen for overpowered imports

User call: a programmatic check for cards that are too strong, used "as
input and not as a rule" - the bot plays simply, and the real danger is
two or more cards together, which a one-card test can't see.

`SIM_SWEEP=all` (tools/Simulator) puts each Catalog card that's on no
starter roster into the roster of its energy's Champion, replacing the
own-energy card nearest in cost, and plays that team 200 games against
each of the four starter rosters (Champion powers on, same seeds as the
unmodified roster). A Basic Action replaces each Champion's Basic Action
in turn. Reports the win-rate change; one standard error is about 2.5
points, so +/-5 is noise and +10 is worth a look.

First run, 52 cards (34 imports, the off-roster natives, 4 Basic Actions):
- Above +10: Fire Ant +20 (2-cost Claw, 2/1 free at L1 up to 4/3, plus
  1 damage whenever you field a Claw creature - Wolf's team is mostly
  Claw), Frigatebird +14 (Gladiator's printed dice: 5/5 for 0 to field
  at L1, 7/7 for 0 at L3, plus Intimidate), Electric Ray +13 (free 2/3 at
  L1, blanks opposing On Field), Deathstalker Scorpion +12 (2-cost, 3/4
  at L3, Deadly). Saiga (+6) has the same Gladiator dice as Frigatebird.
- No engine errors in about 51,000 games.
- Basic Actions: nothing outside the noise (best Thorn Shot on Wolf +8;
  their dice are shared, so the opponent can buy them too).
- Big negatives mostly describe the slot, not the card: Bison -33
  (4/1 glass cannon in for Armadillo's 1/5-3/9 Rhinoceros wall), Meerkat
  and Planarian -16/-17 (both in for Hermit Crab).

## 2026-10-16 - Lane stacking rule

User call (from a suggestion, then simplified for intuitiveness): **a lane
takes another attacker only if some other lane already has as many.** A
second attacker needs another lane with one, a third another lane with
two; an empty lane is always open. Whatever order they're added in, that
comes to: the fullest lane is at most one ahead of the next-fullest. The
point: pairing two big attackers costs at least one more sent alone,
which gets through unblocked (or soaks a blocker) and cycles back into
the bag. Considered first: "every other lane as full" - with four lanes
that kept stacking out of anything under 5 attackers (13 of 156 real
declarations), so lane fights and 2+ Overcrush would all but vanish.

- Engine: `CombatEngine.CanAddToLane` / `LanesAllowed`; `DeclareAttackers`
  refuses a declaration that breaks it.
- Bot: spreads one per lane first anyway; only Armadillo's Rhinoceros +
  forced-blocker stack changed - an attacker joins Rhinoceros's lane only
  when the rule allows, else the emptiest lane.
- Mobile: with one of your creatures selected, lanes it can't join grey
  out (dashed, tapping does nothing) and the Attack Zone hint says why;
  "Declare into lane N" picks an open lane (was always the targeted
  lane - lane 1 by default); Declare Attackers is disabled with
  a note if pulling one back leaves a stack with no lane as full.
  `web/src/dicekingdom/laneRule.ts` mirrors the engine check. Desktop
  already declares round-robin (`i % 4`), which always satisfies it.
- Found on the way: since 2026-10-08 tapping a lane didn't work at all on
  mobile - the root's tap-outside-to-close handler (onClickCapture)
  cleared the selected die before the lane's own handler could use it, so
  "Declare into lane 1" was the only way to attack (and blocking had to
  go through the die tiles). `.dkm-lane` now counts as inside.
- Real games before the rule: 24 of 156 human declarations (17 games)
  would have been refused - all of them every attacker in one lane, up to
  11. Given the bug above, most of that was likely the lane 1 default, not
  a choice. Replaying those records now disagrees at that step, as
  expected.
- Simulator (1,200 games per Champion, powers on, same seeds before and
  after): Armadillo 52.6% -> 47.3% (its Overcrush carry-through drops
  3.8 -> 3.0 a game), Eagle 52.2 -> 55.2, Wolf 46.2 -> 48.2, Owl 48.1
  unchanged. Armadillo is the one that stacks; worth watching.
- Belongs in the upcoming Help/Instructions screen.

## 2026-10-16 - How to Play page

`/dice-kingdom/how-to-play` (`web/src/dicekingdom/HowToPlayPage.tsx`): the
basic rules on one page - its own route, so it works for a first game, a
table game with physical dice, or a link sent to someone. Sections: goal,
team, dice (incl. the Tardigrade's faces), a turn step by step, attacking
and blocking (lanes, the stacking rule, Overcrush, where dice go after a
fight), the four Champion powers, the zones, keywords, packs.

- The keyword list is every `KEYWORD_RULES` entry that some card in
  `/cards` actually carries, so it grows with the catalog.
- Linked from the start screens' "Playing as ..." line and the screen
  menu; mid-game the menu opens it in a new tab so the game stays put.
  `?from=mobile` sends "← Dice Kingdom" back to the mobile page.
- Written against the engine as of today. A rule change in TurnEngine,
  CombatEngine, ChampionPowers or laneRule.ts needs this page updated too.

## 2026-10-17 - How to Play pictures

Six figures on the How to Play page (`web/src/dicekingdom/HowToPlayFigures.tsx`): a labelled creature face plus an energy and an action face, a card's and a Tardigrade's six faces, the turn steps, four stacking-rule examples, a worked lane fight, and a map of where dice go. The dice are real DieCubes drawn from `cardDieFaces`/`tardigradeFacesOf` (now exported from dieFaces.ts), so they follow the board's look and the printed faces. The example creature is picked from the live catalog (the first card with an icon and a face with a cost and unequal ATK/DEF - Honey Badger today). The lane fight's numbers are fixed and worked by hand against CombatEngine (pooled ATK, lethal-first onto the first-declared attacker, Overcrush leftover); recheck it if lane combat changes.

## 2026-10-19 - Champions in custom teams, custom teams online

User: build a team from a Champion, and start an online game with a custom team. "For now, we can let them build any champion they want, except that the Basic Action they pick must be the same energy as the Champion."

- `CustomV2TeamRequest.ChampionId` (optional). The engine already handled a Champion with custom dice (GameSetup reads CustomCardDiceCounts/CustomBasicDicePool before the Champion's own pool), so the server change is validation: an unknown Champion, or a Basic Action whose die energy (`V2GamesController.BasicActionEnergy`, the same reading as the DTO's DieEnergyType) isn't the Champion's, is a 400. No other limits - any cards, any Tardigrade mix.
- Online: `POST open` and `POST {id}/join` take `{championId}` or `{customTeam}`. The lobby keeps the host's team and a `HostLabel` ("Wolf", "Wolf - custom team", "a custom team") for the waiting and invite screens. A bad team on Join is refused and the lobby stays open. The invited player can use the custom team they staged from the Team Builder.
- Team Builder: a Champion row (None + the four) above the Tardigrades, saved and in the share link (`&champion=`). With a Champion, off-energy Basic Actions can't be added, and one already on the team is flagged and blocks Play. Picking a Champion moves an all-one-type Tardigrade mix to its energy; a hand-made mix is left alone.
- Records/replay: a seat with CardDiceCounts is a custom team whether or not it has a ChampionId.
- Starter rosters: finishing a game with a Champion custom team still adds that Champion's starter cards (the seat has the Champion). Left as is - it's no different from playing the preset once.
