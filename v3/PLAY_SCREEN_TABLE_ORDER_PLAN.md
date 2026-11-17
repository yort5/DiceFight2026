# Play screen in table order - implementation plan

Written 2026-11-17 as a handoff. Read the whole file before starting. Do
the steps in order; each is its own commit and can ship alone.

## What and why

On the mobile play screen (`/dice-kingdom/mobile`, in a game) your
Reserve sits **above** your Field, so fielding a die sends it down the
screen, away from the opponent. User (2026-11-17): "when I fielded
characters - they moved down from the screen, and it felt like they
should move up." On a real table the order from your seat outward is
Reserve, Field, your attack zone, their attack zone, their Field.

The rules reason the attack zones matter (user): "the 'attack zone' is
technically part of the field zone, so that abilities that target the
field zone work for dice in the attack zone," and "my attack zone is
part of my field, but my opponent's attack zone is part of their field
... The two attack zones meet during combat, but still belong to their
owners." And, on an earlier mock: "the attack and field blend together a
bit too much ... we still need a box around the lane highlighting
attackers and blockers, even though they are in their owner's zones."
The attack zone "only expands to 'full size' during the attack step."

**The mock-up is the reference.** Open
`v3/mockups/play-screen-table-order/index.html` in a browser (it is a
static page; `shot-main.png`, `shot-attack-yours.png` and
`shot-attack-theirs.png` beside it are screenshots of its three phone
frames). It is dark-theme only and simplified (no energy totals, no
Power button, no status cues); where the mock and this file disagree,
this file wins.

### The new order of the scroll column

Top to bottom inside `.dkm-scroll`:

| # | Your turn | Their turn |
|---|---|---|
| 1 | Their panel (nameplate, piles, their Field) | same |
| 2 | - | The phase card (their tray while they roll) |
| 3 | Attack seam, or the attack strip during the Attack step | same |
| 4 | **Your Field** (its own panel) | same |
| 5 | The phase card (Draw / Tray / Buy+Reserve / End of turn) | - |
| 6 | Global abilities | same |
| 7 | **Your plate** (piles, then nameplate last) | same |
| 8 | Game links, Log | same |

The phase card follows whose turn it is because it shows the active
player's tray and Reserve: theirs belongs on their side of the seam.

## Ground rules

- **Scope**: the mobile play screen only. Do not touch the desktop page
  (`DiceKingdomPage.tsx`), the server, or game logic. No new API calls,
  no new game state. Steps 1-2 move and restyle existing elements.
- **Files**
  - `web/src/dicekingdom/DiceKingdomMobilePage.tsx` - the screen. The
    components named below are all in it.
  - `web/src/dicekingdom/mobilePlay.css` - put every new rule here.
    Start every selector with `.dicekingdom.dk-mobile` (one class more
    specific than the old rules in `dicekingdom.css`, which stay).
  - `web/src/dicekingdom/dieFlights.ts` - read it, do not change it. It
    finds where dice fly from and to by `data-fly-id`, `data-pile`,
    `data-region` and `data-lane`.
- **Do not remove or rename** any `data-fly-id`, `data-pile`,
  `data-region` or `data-lane` attribute, and keep each on the element
  that carries it now. Keep `ref={stageShellRef}` on the
  `.dkm-phase-shell` div and `key={phase}` on the `.dkm-phase-stage` div
  inside it.
- **Do not** put `transform`, `filter` or `contain` on `.dkm-root`,
  `.dkm-scroll`, `.dkm-mat` or a resting `.dkm-card`: sheets, flying dice
  and the hit flash are `position: fixed` inside them.
- **Tokens**: `--dkm-card`, `--dkm-raised`, `--dkm-border`, `--dkm-ink`,
  `--dkm-ink-2`, `--dkm-ink-dim`, `--claw` (you, orange), `--eye`
  (opponent, purple), `--display` (Anton). Both themes must work: use
  these or `color-mix()` of them. **No hard-coded colours** in this work
  - the mock's cream `#fff8ec` box becomes `var(--dkm-ink)` (white on
  dark, near-black on light).
- **You = orange (`--claw`), opponent = purple (`--eye`)**, whatever
  either Champion's energy is.
- **Motion**: any new animation goes inside the one
  `@media (prefers-reduced-motion: reduce)` block at the bottom of
  `mobilePlay.css` with `animation: none !important`.
- **Code style**: match the file. Comments say *why*, dated when they
  record user feedback (quotes above). Don't reformat lines you aren't
  changing. Hooks must stay unconditional at the top of each component.

## How to check each step

Headless Chromium through Playwright; see the memory note "DiceFight2026
dev environment". Start the API (`$HOME/.dotnet/dotnet run --urls
http://localhost:5284` in `src/DiceFight.Api`) and Vite (`npx vite
--port 5173 --strictPort` in `web`), then:

```
cd ~/.devtools/playwright
export LD_LIBRARY_PATH=$HOME/.devtools/chromium-libs/usr/lib/x86_64-linux-gnu
OUT=~/.devtools/screenshots/table-order/stepN node glowup-check.js 5
```

It plays a game against the computer and screenshots dark and light at
402x874, 375x667 and 320x568 at these moments: `draw`, `rolled`, `buy`,
`buy-inspect`, `flight-0..5` (a die being fielded), `opponent`, `attack`
(your attack) and `their-attack` (you are asked for blockers). It prints
`problems:` (sideways scroll, bottom button off screen) and `errors:`
(console). Both must be `none`. **Look at the screenshots** - at least
`buy`, `attack`, `their-attack` and `opponent` at 402 dark, 375 light
and 320 dark.

Useful switches: `TALL=1 BASE_H=1500` (a 1500px-tall screen, to see the
whole column and a die's whole flight); `FIELD_CLONE=7` (fills both
Fields, to check a full Field and a full attack).

Also run `OUT=... node glowup-moments.js` (life changes and game over):
it must finish with `errors: none`.

Then `cd web && npm run build && npm run lint && npm test` (86 tests;
the existing `exhaustive-deps` / `only-export-components` warnings are
normal). Add a dated entry to `v3/DESIGN_NOTES.md` (`## YYYY-MM-DD -
Title`, quoting the request), `git fetch origin main`, commit to `main`,
push.

---

## Step 1 - Reorder the column

No new look yet: the same panels in the new order, with your panel
split in two.

### 1a. `MatCard` gets a `part` prop

Add `part?: "all" | "field" | "plate"` (default `"all"`) to `MatCard`'s
props. All hooks and derived values stay exactly as they are; only the
returned JSX depends on `part`.

- `"all"` - unchanged. The opponent's panel uses it. (Its order is
  already table order: nameplate, piles, their Field last.)
- `"field"` - root `<div className={`dkm-mat mine dkm-mat-field${turnClass}`} style={{ ["--cc" as string]: accent }}>`
  containing only, in this order: the Field label + the Field row (the
  `data-region="field-mine"` div, unchanged) and the Intimidated block.
  Nothing else: no head, no piles, no `PileStrip`, no passive text, no
  hit flash.
- `"plate"` - root `<div className={`dkm-mat mine dkm-mat-plate${turnClass}${bigHitClass}`} ...>`
  containing, in this order: the piles block (the
  `!expandable || expanded ? ... : ...` expression), the `PileStrip`
  (`openPile && ...`), the Champion passive paragraph, then the whole
  `.dkm-mat-head` **last**, with the `dkm-hit-vignette` span inside it
  as now. No Field, no Intimidated.

Keep the `dkm-mat-big-hit` class on `"all"` and `"plate"` only. The
`lifeDelta` effect runs in every instance; that is fine, only `"all"`
and `"plate"` render it.

In `mobilePlay.css`, inside the plate the head's two rows swap so the
nameplate is the very last line of the screen:

```css
/* Your plate closes the screen: counters and buttons first, then the nameplate. */
.dicekingdom.dk-mobile .dkm-mat-plate .dkm-mat-head { margin: 10px 0 0; }
.dicekingdom.dk-mobile .dkm-mat-plate .dkm-plate-row { grid-row: 1; }
.dicekingdom.dk-mobile .dkm-mat-plate .dkm-plate-icon,
.dicekingdom.dk-mobile .dkm-mat-plate .dkm-plate-who,
.dicekingdom.dk-mobile .dkm-mat-plate .dkm-mat-life { grid-row: 2; }
.dicekingdom.dk-mobile .dkm-mat-plate .dkm-pile-grid { padding-top: 0; border-top: 0; margin-bottom: 0; }
```

(The pile grid's top hairline separated it from the head above it; with
the head below, drop it and check the spacing by eye.)

### 1b. The main render

In `DiceKingdomMobilePage`'s return, inside `<div className="dkm-scroll">`:

1. Pull the phase card out into a constant just before the `return`:
   ```tsx
   // The phase card shows the ACTIVE player's tray / Reserve / Buy, so it
   // sits on their side of the attack seam (table order, 2026-11-17).
   const stage = (
     <div ref={stageShellRef} className="dkm-phase-shell">
       <div key={phase} className="dkm-phase-stage">
         {/* the existing clear / roll / main / cleanup cards, unchanged */}
       </div>
     </div>
   );
   ```
   Move the long "Keyed by phase so React remounts..." comment with it.
   **Take `AttackLanesCard` out of the stage** (next point); everything
   else in the stage stays as it is.
2. New order of children (error and invite-toast paragraphs stay first):
   ```tsx
   <MatCard mine={false} ... />              {/* unchanged props */}
   {!isYourTurn && stage}
   {phase === "attack"
     ? <AttackLanesCard ... />               {/* unchanged props */}
     : <div className="dkm-attack-seam" aria-hidden="true">Attack zone</div>}
   <MatCard mine part="field" ... />         {/* the same props your MatCard has now */}
   {isYourTurn && stage}
   <SharedAbilityPanel ... />                {/* unchanged props, moved down */}
   <MatCard mine part="plate" ... />         {/* the same props again */}
   {/* game links and log, unchanged */}
   ```
   Pass both `mine` instances the identical prop list the single one has
   today (`onUsePower`, `dice={yourFieldVisibleDice}`, `fieldClickable`,
   and so on).
3. The stage is empty during the Attack step and on the opponent's Main
   step. Hide it then, so it leaves no double gap:
   ```css
   .dicekingdom.dk-mobile .dkm-phase-shell:has(> .dkm-phase-stage:empty) { display: none; }
   ```
4. The seam:
   ```css
   /* The attack zones, folded away outside the Attack step: a seam between the Fields. */
   .dicekingdom.dk-mobile .dkm-attack-seam {
     display: flex; align-items: center; gap: 8px; margin: -2px 4px;
     font: 700 8.5px/1 "Work Sans", var(--sans); letter-spacing: 0.14em;
     text-transform: uppercase; color: var(--dkm-ink-dim);
   }
   .dicekingdom.dk-mobile .dkm-attack-seam::before,
   .dicekingdom.dk-mobile .dkm-attack-seam::after {
     content: ""; flex: 1; border-top: 1.5px dashed color-mix(in srgb, var(--dkm-ink-dim) 55%, transparent);
   }
   ```

### Things that will bite

- `stage` moves between two positions when the turn changes, so React
  remounts it. That only happens at a phase change, when the keyed
  `.dkm-phase-stage` remounts anyway. If the height animation
  (`usePhaseHeight`) glitches at the turn change, leave it - do not add
  state to fix it.
- `dieFlights.ts` names a die's region by `data-region`, then
  `data-lane`, then `.dkm-phase-stage`, then `.dkm-mat`. Lanes keep
  `data-lane`, the Field keeps `data-region`, the Reserve and tray stay
  inside the stage, so nothing there needs to change. Verify with the
  `flight-*` frames: a fielded die must leave the Reserve and land on a
  Field slot **above** it. A die that appears from the top-left corner
  means an attribute was lost.
- The harness finds your panel's life with
  `.dkm-mat` + `.dkm-mat-life`; that already works with the split.

**Done when**: on your Main step the column reads their panel, seam,
your Field, Buy with Reserve, Globals, your plate; fielding flies a die
upward; on their turn their tray is above the seam; nothing else looks
different. `problems: none`, `errors: none`.

## Step 2 - The attack strip

Today `AttackLanesCard` is a `.dkm-card` titled "Attack Zone" holding
four lanes; each lane is already **one tappable box** holding that
lane's attackers and blockers, with the opponent's dice in its top part
and yours in its bottom part (see the long comments in the component).
Keep that structure and every handler. This step restyles the card as
two attack zones that meet - theirs on top, joined to their panel;
yours below, joined to your Field - and turns each lane with dice in it
into the fight box.

### 2a. Markup in `AttackLanesCard`

1. Root: `<div className={`dkm-attack-strip ${isYourTurn ? "you-attack" : "they-attack"}`}>`
   in place of `<div className="dkm-card">`.
2. Replace the `.dkm-card-head` with a top row and add a bottom row
   after `.dkm-lanes`:
   ```tsx
   <div className="dkm-zone-row top">
     <span className="dkm-zone-label">Their attack zone</span>
     <span className="dkm-card-hint">{/* the existing hint expression */}</span>
   </div>
   <div className="dkm-lanes"> ... </div>
   <div className="dkm-zone-row bottom">
     <span className="dkm-zone-label">Your attack zone</span>
   </div>
   ```
   The two rows must be the same height, so the strip's midline is the
   lanes' midline.
3. On each lane div add `engaged` to the class list when
   `attackers.length > 0`. Keep `targeted`, `closed`, `unblockable`,
   `data-lane`, `role`, `tabIndex`, `title`, `onClick`, `onKeyDown`.
4. Inside each lane, in **both** branches (`isYourTurn ? ... : ...`):
   - add `dkm-lane-half top` to the first group div's classes and
     `dkm-lane-half bottom` to the second group's (keep
     `dkm-lane-blockers` / `dkm-lane-attackers` too);
   - wrap the two chip spans (`dkm-lane-chip tappable` and
     `dkm-lane-chip reflect`) in one `<div className="dkm-lane-mid">`,
     still between the two groups.
   Do not change which dice go in which group, their order, or any
   `onTap` / `onClick`.

### 2b. `MatCard` gets `joined`

Add `joined?: boolean` to `MatCard`. When true add the class
`dkm-mat-joined` to the root. Pass `joined={phase === "attack"}` to the
opponent's `MatCard` and to your `part="field"` one.

### 2c. Styles

```css
/* ---- The attack strip: two attack zones that meet ---- */

/* Each player's attack zone is the front of their own Field (user,
   2026-11-17) - so the strip is two halves: theirs on top continuing
   their panel, yours below continuing your Field. A die never leaves
   its owner's half. */
.dicekingdom.dk-mobile .dkm-attack-strip {
  /* -9px cancels .dkm-scroll's gap, so the strip touches both panels. */
  margin: -9px 0;
  padding: 0 12px;
  background: linear-gradient(180deg,
    color-mix(in srgb, var(--eye) 14%, var(--dkm-raised)) 0 50%,
    color-mix(in srgb, var(--claw) 14%, var(--dkm-raised)) 50% 100%);
  box-shadow: inset 1px 0 0 var(--dkm-border), inset -1px 0 0 var(--dkm-border);
  position: relative;
}
/* The attacker's side carries the "whose turn" ring down the strip's edges. */
.dicekingdom.dk-mobile .dkm-attack-strip.they-attack { box-shadow: inset 2px 0 0 var(--eye), inset -2px 0 0 var(--eye); }
.dicekingdom.dk-mobile .dkm-attack-strip.you-attack { box-shadow: inset 2px 0 0 var(--claw), inset -2px 0 0 var(--claw); }
/* Where the two zones meet. */
.dicekingdom.dk-mobile .dkm-attack-strip::after {
  content: ""; position: absolute; left: 12px; right: 12px; top: 50%;
  border-top: 1.5px dashed color-mix(in srgb, var(--dkm-ink) 35%, transparent);
  pointer-events: none;
}
.dicekingdom.dk-mobile .dkm-zone-row {
  display: flex; align-items: center; justify-content: space-between; gap: 8px; height: 22px;
}
.dicekingdom.dk-mobile .dkm-zone-label { font: 400 10.5px/1 var(--display); letter-spacing: 0.08em; text-transform: uppercase; }
.dicekingdom.dk-mobile .dkm-zone-row.top .dkm-zone-label { color: var(--eye); }
.dicekingdom.dk-mobile .dkm-zone-row.bottom .dkm-zone-label { color: var(--claw); }

/* The panels on either side lose the rounded corners that face the strip. */
.dicekingdom.dk-mobile .dkm-mat.dkm-mat-joined:not(.mine) { border-radius: 18px 18px 0 0; }
.dicekingdom.dk-mobile .dkm-mat.dkm-mat-joined.dkm-mat-field { border-radius: 0 0 18px 18px; }

/* A lane is one box across both zones. */
.dicekingdom.dk-mobile .dkm-attack-strip .dkm-lanes { position: relative; z-index: 1; }
.dicekingdom.dk-mobile .dkm-attack-strip .dkm-lane {
  position: relative;
  display: grid;
  grid-template-rows: minmax(74px, 1fr) minmax(74px, 1fr);
  gap: 0;
  border: 1.5px dashed color-mix(in srgb, var(--dkm-ink) 22%, transparent);
  border-radius: 12px;
}
.dicekingdom.dk-mobile .dkm-lane-half.top { justify-content: flex-end; padding-bottom: 12px; }
.dicekingdom.dk-mobile .dkm-lane-half.bottom { justify-content: flex-start; padding-top: 12px; }
/* The fight box (user: "a box around the lane highlighting attackers
   and blockers, even though they are in their owner's zones"). Ink, not
   orange or purple: it belongs to the fight, not to a side. */
.dicekingdom.dk-mobile .dkm-attack-strip .dkm-lane.engaged {
  border: 2px solid var(--dkm-ink);
  box-shadow: 0 0 0 3px color-mix(in srgb, var(--dkm-ink) 16%, transparent);
  background: color-mix(in srgb, var(--dkm-ink) 5%, transparent);
}
/* The lane your next attacker goes to keeps its orange edge. */
.dicekingdom.dk-mobile .dkm-attack-strip .dkm-lane.targeted { border-color: var(--claw); }
/* The chips sit on the meeting line. */
.dicekingdom.dk-mobile .dkm-lane-mid {
  position: absolute; left: 50%; top: 50%; translate: -50% -50%; z-index: 2;
  display: flex; flex-direction: column; align-items: center; gap: 2px;
}
.dicekingdom.dk-mobile .dkm-attack-strip .dkm-lane-chip:not(.reflect) {
  background: var(--dkm-ink); color: var(--dkm-card); border-radius: 999px; white-space: nowrap;
}
.dicekingdom.dk-mobile .dkm-attack-strip .dkm-lane-number {
  position: absolute; right: 5px; bottom: 3px; font-size: 8px;
}
```

`translate` on `.dkm-lane-mid` is fine (it is not one of the forbidden
elements). The existing rules for `.dkm-lane.closed`,
`.dkm-lane.unblockable`, `.dkm-lane-role`, `.dkm-lane-tile.empty` and
the defence meter keep working; check each still reads.

### Things that will bite

- **Stacked lanes.** A lane can hold 2-3 attackers (tile size drops to
  42 / 34) and several blockers. The halves must grow (`1fr` rows with a
  `minmax` floor do that); the meeting line then no longer sits at
  exactly 50% of a taller lane's content, which is acceptable. Check
  with `FIELD_CLONE=7`: declare several attackers and look.
- **The empty attacker placeholder** (`.dkm-lane-tile.empty.attacker-empty`)
  shows in every empty lane while declaring. Keep it, in the attacker's
  half.
- **Light theme.** The mock is dark only. In light, check the two tints
  are visible but pale, the ink box is dark and crisp, and chip text
  (card colour on ink) is readable.
- **Their turn before blocks**, your turn before any attacker is
  declared, and the action window after blocks all use this same strip;
  look at each.
- The strip's side edges are drawn with an inset shadow, and each panel
  keeps its own inset ring, so a 1px line of the panel's ring crosses
  between panel and strip. That line is wanted: it separates Field from
  attack zone.

**Done when**: during the Attack step the two panels and the strip read
as one piece - their Field, their attack zone, the meeting line, your
attack zone, your Field. A lane with dice in it has the ink box round
attacker and blocker together; empty lanes are faint dashed outlines. On
your attack your dice are in the orange half and their blockers in the
purple half; on theirs the reverse. Declaring, blocking, the "to face"
chip (tap opens the lane breakdown) and the lane-stacking rule all work
as before. Dice fly from a Field up or down into their own half.
`problems: none`, `errors: none` at all three sizes in both themes.

## Step 3 - Fit and finish

Only after 1 and 2 are pushed. Each item is optional; do the ones the
screenshots call for and say which in `DESIGN_NOTES.md`.

1. **Height.** In the mock the Main step runs about 36px taller than a
   402x874 screen (your nameplate ends under the bottom bar). If the
   real screen is much worse than that, tighten: `.dkm-mat-field` and
   `.dkm-mat-plate` padding to 10px, and the Buy card's
   `.dkm-card-head` margin. Do not shrink dice, buy cards, or touch
   targets.
2. **Their tray above the seam.** On the opponent's Roll step, check the
   tray card reads as theirs (its title already says whose it is).
3. **`TALL=1 BASE_H=1500` pass.** Look at the whole column once per
   phase for anything orphaned: a gap, a doubled border, a label with
   nothing under it.

## Not in this plan

- A dedicated buy view. The user ruled it out: buying needs the
  opponent's Field and both sets of piles in sight.
- Changing the Global abilities row's look, the log, the lane breakdown
  panel, or the bottom bar.
- Continuous action dice in the attack zone ("we can ignore continuous
  action dice for now").
- Any server or rules change. The zones are a display grouping; the
  engine already treats a blocker as its controller's die.

## Order, size, and where judgment is needed

| Step | Size | Risk | Needs an eye? |
|---|---|---|---|
| 1 Reorder | medium: one prop, one render block, ~30 lines of CSS | medium - dice flights and the split panel | some: spacing of the plate, the empty stage |
| 2 Attack strip | medium: class names and one wrapper in the lanes, ~80 lines of CSS | medium - stacked lanes, light theme | **yes** - how strong the two tints and the fight box are |
| 3 Fit and finish | small | low | yes |

If something in a step looks worse than before in a screenshot, prefer
the plainer option (for example: keep the strip's own rounded corners
instead of joining the panels) and note it in `DESIGN_NOTES.md` rather
than adding decoration to fix it.
