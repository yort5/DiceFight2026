# Play screen glow-up - implementation plan

Written 2026-11-12 as a handoff. The launch page and the side pages (Boss
Ladder, Card Packs, How to Play, Account) were restyled on 2026-11-11; the
mobile play screen (`/dice-kingdom/mobile`, in a game) still has the older
flat look. This plan brings it into line in six steps. Each step is its
own commit and can ship alone. **Steps 1-5 change styling and markup only -
no game logic, no API calls, no state.** Step 6 adds two small pieces of
display state.

Read this whole file before starting. Do the steps in order.

## Ground rules

- **Scope**: the mobile play screen only. Do not touch the desktop page
  (`DiceKingdomPage.tsx`), the Team Builder, or the server.
- **Files**
  - `web/src/dicekingdom/DiceKingdomMobilePage.tsx` - the screen (3,400
    lines). Components named below are all in this file.
  - `web/src/dicekingdom/dicekingdom.css` - its current styles, all under
    `.dk-mobile .dkm-*` (roughly lines 2240-3700). **Leave these rules in
    place.**
  - **New file** `web/src/dicekingdom/mobilePlay.css` - put every new rule
    here, and import it in `DiceKingdomMobilePage.tsx` on the line after
    `import "./dicekingdom.css";`. Start every selector with
    `.dicekingdom.dk-mobile` (one class more specific than the old rules,
    so the new file wins without `!important` and without depending on
    load order).
  - `web/src/dicekingdom/mobileHome.css` and `kingdomPages.css` - the
    recipes to copy from. Read `.dkh-tile` (the chunky button) in
    `mobileHome.css` and `.dicekingdom.dkp button.btn` in
    `kingdomPages.css` before writing any button.
  - `web/src/dicekingdom/kingdomArt.tsx` - shared `Crown`, `CoinIcon`,
    `Sky`, `Line`, `CheckIcon`.
- **Do not remove or rename** any `data-fly-id`, `data-pile`, or
  `data-region` attribute, and keep the elements that carry them in the
  DOM. The dice-flight animations (`dieFlights.ts`) find their start and
  end points by these. Likewise keep `ref={stageShellRef}` and the
  `key={phase}` wrapper as they are.
- **Do not** put `transform`, `filter`, or `contain` on `.dkm-root`,
  `.dkm-scroll`, `.dkm-mat` or `.dkm-card` (as a resting style). Sheets
  and flying dice are `position: fixed` inside them and would be
  mis-placed. Transforms on buttons and tiles are fine.
- **Tokens**. The screen has its own set, defined for dark and light:
  `--dkm-card`, `--dkm-raised`, `--dkm-attacker`, `--dkm-border`,
  `--dkm-hairline`, `--dkm-ink`, `--dkm-ink-2`, `--dkm-ink-dim`,
  `--dkm-on-accent`, `--dkm-cream`; plus the energies `--claw`, `--shell`,
  `--wing`, `--eye`, `--wild`, and `--display` (Anton), `--sans`. Use
  these, or `color-mix()` of them, so both themes work. The only
  hard-coded colours allowed are the jewel button colours in the table
  below and text on top of them.
- **You = orange (`--claw`), opponent = purple (`--eye`).** This pairing
  is deliberate wayfinding (see the comment in `MatCard`) and is
  independent of either Champion's energy. Keep it.
- **Touch targets**: anything tappable stays at least 40px tall (44px
  for the bottom bar).
- **Motion**: every new animation goes inside the one
  `@media (prefers-reduced-motion: reduce)` block at the bottom of
  `mobilePlay.css` with `animation: none !important`. Nothing may loop
  forever except a "your move" pulse on the primary button.
- **Code style**: match the file - comments explain *why*, dated when
  they record user feedback. Don't reformat lines you aren't changing.

### Jewel colours (from the launch page)

| Use | `--a` (top) | `--b` (bottom) | `--lip` | Text |
|---|---|---|---|---|
| Go / your move (orange) | `#ffb066` | `#dd5a2a` | `#96380f` | `#2a1206` |
| Opponent / boss (purple) | `#9468dc` | `#55299a` | `#331766` | `#fff` |
| Info (blue) | `#55aaf0` | `#2262bd` | `#153b80` | `#fff` |
| Good (green) | `#45c592` | `#18805b` | `#0c553c` | `#fff` |
| Reward (gold) | `#ffd96a` | `#f09c22` | `#a05c08` | `#3a2002` |

### The chunky button recipe

```css
--drop: 5px;
margin-bottom: var(--drop);            /* room for the lip */
border: 0;
background:
  linear-gradient(180deg, rgba(255,255,255,.22) 0%, rgba(255,255,255,0) 52%),
  linear-gradient(160deg, var(--a), var(--b));
box-shadow:
  inset 0 1.5px 0 rgba(255,255,255,.45),
  inset 0 -2px 0 rgba(0,0,0,.12),
  0 var(--drop) 0 var(--lip),
  0 calc(var(--drop) + 7px) 12px -8px rgba(0,0,0,.5);
transition: transform .09s ease, box-shadow .09s ease;
/* :active:not(:disabled) */
transform: translateY(calc(var(--drop) - 1px));
box-shadow:
  inset 0 1.5px 0 rgba(255,255,255,.45),
  inset 0 -2px 0 rgba(0,0,0,.12),
  0 1px 0 var(--lip),
  0 4px 6px -5px rgba(0,0,0,.5);
```

## How to check each step

The sandbox has no desktop browser; use headless Chromium through
Playwright (see the memory note "DiceFight2026 dev environment"). Start
the API (`dotnet run --urls http://localhost:5284` in
`src/DiceFight.Api`) and Vite (`npx vite --port 5173 --strictPort` in
`web`), open `/dice-kingdom/mobile`, tap **Quick Match**, pick a Champion,
start. Clicking the last visible button repeatedly walks a turn: Draw
four -> Roll -> To Reserve -> Done buying -> Declare Attackers -> Resolve
Damage -> Pass Turn, then the computer plays.

For every step, screenshot **dark and light**, at **402x874, 375x667 and
320x568**, at these moments: draw, after rolling (dice in the tray), buy
phase, attack phase with at least one die fielded (play 3-4 turns), and
the opponent's turn. Check:

- `document.documentElement.scrollWidth` equals the viewport width;
- no console errors;
- the bottom button is fully visible at 375x667 and 320x568;
- text on every coloured surface is readable in both themes;
- dice still fly between zones (buy a die, field a die - watch that the
  flight lands on the right spot; a missing `data-*` attribute shows up
  as a die appearing from the top-left corner).

Then `cd web && npm run build && npm run lint && npm test` (86 tests;
the existing `only-export-components` and `exhaustive-deps` lint warnings
are normal). Add a dated entry to `v3/DESIGN_NOTES.md`
(`## YYYY-MM-DD - Title`, quoting the request), `git fetch origin main`,
commit to `main`, push.

---

## Step 1 - The bottom action bar

**Now**: `.dkm-primary-btn` is a flat 50px `--claw` slab; `.dkm-secondary-btn`
("Skip attack") is an outlined box; the bar (`.dkm-bottom-bar`) is a flat
panel with a hard shadow.

**Change** (CSS only, no markup):

- `.dkm-primary-btn`: the chunky recipe in **orange**, `height: 54px`,
  `border-radius: 18px`. Label `span`: Anton 20px, `letter-spacing: .04em`,
  colour `#2a1206`. Note `small`: 10.5px/600, `rgba(42,18,6,.7)`.
- When the button is **enabled and it is your move**, add a slow glow so
  the eye lands on it: `animation: dkmp-ready 2.4s ease-in-out infinite`
  that only animates an extra outer `box-shadow` ring
  (`0 0 0 0 rgba(255,176,102,.55)` -> `0 0 0 8px rgba(255,176,102,0)`).
  Write the keyframes with the full box-shadow list so the lip is kept.
- `:disabled` (this is the "Waiting... X is acting" state): no lip, no
  sink, no glow - `background: var(--dkm-raised)`, text `--dkm-ink-dim`,
  `box-shadow: inset 0 0 0 1px var(--dkm-border)`, `opacity: 1`,
  `margin-bottom: 5px` so the bar doesn't jump.
- `.dkm-secondary-btn`: `height: 54px`, `border-radius: 18px`,
  `border: 1.5px solid var(--dkm-border)`, sentence weight 700 13px;
  `:active` -> `transform: scale(.97)` and a faint fill. Stays flat on
  purpose (it is the quiet choice).
- `.dkm-bottom-bar`: `border-top: 0`, top corners `border-radius: 22px 22px 0 0`,
  `box-shadow: 0 -1px 0 var(--dkm-border), 0 -16px 30px -12px rgba(0,0,0,.5)`.
- `.dkm-inspect-action` (the Buy / Field / Use buttons inside the inspect
  card in the same bar): the chunky recipe at `--drop: 3px`, radius 12px,
  **green** for the affordable action. Keep its disabled look flat.

**Done when**: pressing the main button visibly sinks; the waiting state
looks clearly inactive; nothing in the bar moves by a pixel between
enabled and disabled.

## Step 2 - Player nameplates

**Now** (`MatCard`, `.dkm-mat-head`): one wrapping row of small text -
"OPP", Champion icon + name at 10.5px, life at 20px, energy chips, Roster,
Show piles.

**Change**: turn the head into a nameplate. Markup changes are inside
`MatCard`'s `<div className="dkm-mat-head">` only.

1. Wrap the Champion icon in a tile: replace the bare `<ChampIcon size={16} />`
   with `<span className="dkm-plate-icon">{ChampIcon ? <ChampIcon size={22} /> : <PawIcon size={18} />}</span>`
   and render that span for teams without a Champion too (import
   `PawIcon` from `./kingdomArt`). Style: 38x38, `border-radius: 12px`,
   `background: linear-gradient(160deg, color-mix(in srgb, var(--cc) 80%, #fff), var(--cc))`,
   `box-shadow: 0 3px 0 color-mix(in srgb, var(--cc) 55%, #000)`,
   icon colour `#fff`. (`--cc` is already set on `.dkm-mat` - orange for
   you, purple for the opponent.)
2. Stack label and name: wrap `.dkm-mat-label` and the name text in
   `<span className="dkm-plate-who">`, label on top (9.5px caps, colour
   `--cc`, text "You" / "Opponent" - spell it out), name below at
   **14px/700** `--dkm-ink`. Keep the name inside the existing
   `<button className="dkm-champion-badge">` so tapping still opens the
   passive text. Long boss names: `overflow: hidden; text-overflow: ellipsis;
   white-space: nowrap; min-width: 0`.
3. Life becomes a badge: keep `<span className="dkm-mat-life">` and its
   content, add a heart before the number -
   `<svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor" aria-hidden="true"><path d="M12 21s-7.5-4.7-10-9.3C.3 8.300 2 4.5 5.600 4.500c2 0 3.400 1 4.400 2.400 1-1.400 2.400-2.400 4.400-2.400 3.600 0 5.300 3.800 3.600 7.200C19.500 16.300 12 21 12 21z"/></svg>`
   (check it renders as a heart; adjust the path if not) - and drop the
   `<small>life</small>` text in favour of `aria-label={`${player.life} life`}`.
   Style: pill, `padding: 4px 10px 3px 8px`, `border-radius: 999px`,
   `background: color-mix(in srgb, var(--claw) 16%, var(--dkm-raised))`,
   number Anton 22px `--dkm-ink`, heart `--claw`. Use the same red-orange
   heart for both players (life is life); only the plate icon carries the
   side's colour.
4. Row layout: `display: grid; grid-template-columns: auto minmax(0,1fr) auto;
   gap: 10px; align-items: center` for icon / who / life. Move the energy
   chips (`.dkm-reserve-anchor` - **keep its `data-pile`**) and
   `.dkm-mat-head-actions` onto a second row (`grid-column: 1 / -1`,
   flex, space-between). The Power button (`.dkm-power-btn`) sits at the
   start of that second row and gets the chunky recipe in orange at
   `--drop: 3px` - it is a real action.
5. The mat itself: `border-radius: 18px`, `padding: 12px`,
   `border: 0`, `box-shadow: inset 0 0 0 1px var(--dkm-border)`, and a
   faint side tint - `background: linear-gradient(180deg,
   color-mix(in srgb, var(--cc) 9%, var(--dkm-card)), var(--dkm-card) 70px)`.
   Whose turn it is: `.dkm-mat.active` gets
   `box-shadow: inset 0 0 0 2px var(--cc)` and its label gains a small
   "· playing" suffix in the markup (`isActivePlayer`). Remove the old
   always-on orange border on `.dkm-mat.mine` (it said "this is yours",
   which the colour and label now say; the ring should mean "whose turn").
6. `.dkm-chip-btn` (Roster) and the pile counters (`.dkm-collapsed-row
   button`): radius 10px, min-height 32px, `background: var(--dkm-raised)`,
   no border. Numbers in the pile counters Anton 16px.

**Done when**: at a glance from arm's length you can tell which panel is
yours, both life totals, and whose turn it is. At 320px nothing wraps
into three rows and the name ellipsizes instead of pushing life off.

## Step 3 - The buy row

**Now** (`BuyCard`, `.dkm-buy-tile`): three grey tiles - 22px line icon,
9.5px name, small cost.

**Change**: small cards with art.

1. In `BuyCard`'s map, replace the avatar span with:
   ```tsx
   <span className={`dkm-buy-art${hasPainting(cardId) ? " painted" : ""}`}>
     {hasPainting(cardId)
       ? <Painting cardId={cardId} crop="icon" alt="" />
       : Avatar ? <Avatar size={30} /> : <TardigradeIcon size={30} />}
   </span>
   ```
   (`Painting` and `hasPainting` are already imported in this file.)
   Keep `data-fly-id`, the `picked` / `unaffordable` / `dk-locked`
   classes, the `title`, the lock hatch span and `onClick` exactly as
   they are.
2. Set the card's energy colour on the tile:
   `style={{ ["--energy" as string]: `var(--${(card?.energyTypes[0] ?? "wild").toLowerCase()})` }}`.
3. Move the cost into a badge on the art's top-right corner: keep the
   `<span className="dkm-buy-cost-row">` content (energy badges +
   `CostNumber`) but position it `absolute; top: 4px; right: 4px`,
   pill, `background: rgba(15,17,23,.78)`, number Anton 15px `#fff`
   (remove the inline `color` style on `.dkm-buy-cost`).
4. Styles: tile `position: relative; padding: 0; overflow: hidden;
   border-radius: 14px; background: var(--dkm-raised);
   box-shadow: 0 0 0 1.5px color-mix(in srgb, var(--energy) 60%, var(--dkm-border)),
   0 4px 0 color-mix(in srgb, var(--energy) 45%, #000); margin-bottom: 4px`.
   Art: `display: grid; place-items: center; width: 100%; aspect-ratio: 4 / 3;
   background: color-mix(in srgb, var(--energy) 30%, var(--dkm-raised))`;
   painted `img { width: 100%; height: 100%; object-fit: cover }`.
   Name: `padding: 5px 4px 6px`, 11px/700, two lines max
   (`display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden`),
   `min-height: 2.4em` so the three tiles line up.
   `:active` sinks 3px like a button. `.picked`: ring
   `0 0 0 3px var(--claw)` plus lift `translateY(-3px)` (matches
   `.dkm-tile.picked`). `.unaffordable`: keep the dimming but make it
   `opacity: .55` with `filter: saturate(.5)` on the art only, so the
   name stays readable.
5. The card title "Buy" and "Full Roster": title Anton 17px; "Full
   Roster" becomes a `.dkm-chip-btn`-style pill rather than underlined
   text.

**Done when**: the three options read as cards, the cost is the first
thing you see on each, and an unaffordable card is obviously so. Check a
card with no painting (Basic Actions, Tardigrades) still looks right.

## Step 4 - The field

**Now**: `Field` is a label and `Nothing fielded.` in a text row inside
the mat (`.dkm-field-label`, `.dkm-tile-row[data-region^="field"]`).

**Change**: the field becomes a playmat strip inside each nameplate
panel. **Keep `data-region` on the same element.**

1. Add a class to the two field rows in `MatCard`:
   `className="dkm-tile-row wrap dkm-fieldmat"`.
2. Style `.dkm-fieldmat`: `min-height: 66px; padding: 8px; border-radius: 14px;
   align-items: center; background:
   repeating-linear-gradient(118deg, color-mix(in srgb, var(--cc) 7%, transparent) 0 14px, transparent 14px 28px),
   color-mix(in srgb, var(--cc) 10%, var(--dkm-raised));
   box-shadow: inset 0 2px 6px rgba(0,0,0,.28), inset 0 0 0 1px color-mix(in srgb, var(--cc) 30%, transparent)`.
   In light theme check the inset shadow isn't muddy; halve it under the
   light-theme selectors if so (copy the two selectors used for the
   light `--dkm-*` tokens around line 2300 of `dicekingdom.css`).
3. Empty state: replace the text with three ghost slots -
   `{field.length === 0 && <span className="dkm-empty-slots" aria-label="Nothing fielded"><i /><i /><i /></span>}`.
   Each `i`: 46x46, `border-radius: 10px`,
   `border: 1.5px dashed color-mix(in srgb, var(--cc) 45%, transparent)`.
4. Field label (`.dkm-field-label`): keep the text, restyle as a small
   tab sitting on the mat's top-left edge (Anton 12px, `--cc`, margin
   bottom 4px). Simplify the opponent's text to "Their field".
5. Do the same `dkm-fieldmat` treatment (neutral: set `--cc: var(--dkm-ink-dim)`)
   for the Reserve row inside `BuyCard` and the tray in `TrayCard`, so
   every place dice sit is a recessed surface. Their empty hints stay as
   text.

Do **not** change `AttackLanesCard` in this step beyond what it inherits;
the lanes have their own layout and rules cues.

**Done when**: an empty field looks like a place waiting for dice, a
full field (6+ dice, wraps to two rows) still fits, and dice flying to
the field land inside the strip.

## Step 5 - The phase header

**Now** (`PhaseRail`, `StepLine`, `.dkm-header`): five 38px icon squares,
the active one wide and orange with a label; below, the step title and a
"step 1/1" chip. Background is the page colour.

**Change**: a dusk bar with a progress track, matching the side pages'
bar.

1. `.dkm-header`: `background: #171427; color: #fff8ec;
   border-radius: 0 0 22px 22px; box-shadow: 0 10px 16px -14px rgba(0,0,0,.9);
   padding-bottom: 10px`. This bar is dark in **both** themes (as on the
   side pages), so everything inside uses the fixed cream palette below,
   not `--dkm-*` tokens.
2. `PhaseRail` markup: keep the five buttons and their `aria-*`, add a
   connector between them - give `.dkm-phase-rail` `position: relative`
   and a `::before` line (`top: 50%; left: 19px; right: 19px; height: 3px;
   background: rgba(255,248,236,.18)`). Pills sit above it (`position: relative`).
   - upcoming: 34px circle, `background: #2a2540`,
     `border: 1.5px solid rgba(255,248,236,.22)`, icon `rgba(255,248,236,.5)`.
   - done: same circle, `background: #3d3560`, icon `#fff8ec`, and a small
     `CheckIcon` is **not** needed - the fill is enough.
   - active: pill with the label, the orange gradient
     (`linear-gradient(160deg,#ffb066,#dd5a2a)`), text `#2a1206`,
     `box-shadow: 0 3px 0 #96380f`. Keep `flex: 1 1 auto` and the
     existing shrink rules (see the comment there about the 375px clip).
   The pills are not clickable today (`onTap={() => {}}`); set
   `cursor: default` and don't add press states.
3. Step title: Anton 26px, `#fff8ec`. Step chip: pill,
   `background: rgba(255,248,236,.1)`, `border: 1px solid rgba(255,248,236,.2)`,
   text `rgba(255,248,236,.8)`.
4. The menu button (`.screen-menu-button` inside `.dkm-header-controls`):
   44px circle, `border: 1px solid rgba(255,248,236,.2)`,
   `background: rgba(255,248,236,.08)`, `color: #fff8ec` - the side
   pages' `.dkp-round`. Remove the divider's visual weight
   (`.dkm-navigation-divider { background: rgba(255,248,236,.15) }`).
5. Whose turn: add one element to the header, right of the step title,
   replacing nothing: `<span className={`dkm-turn-flag${isYourTurn ? " mine" : ""}`}>{isYourTurn ? "Your turn" : `${oppPlayer.name}'s turn`}</span>`
   - pill, Anton 12px caps; `mine`: orange gradient with dark text;
   otherwise purple `#55299a` with white text. If the step title and
   flag don't both fit at 320px, the flag wins and the title ellipsizes
   (it already has ellipsis rules).

**Done when**: the header reads as one dark bar in both themes, you can
see how far through the turn you are, and nothing clips at 320px (the
old header had a real clipping bug at 375 - re-check that case).

## Step 6 - Moments

Two additions. Both are display-only: they watch the `game` state that
already exists and never change it.

### 6a. Life changes

1. In `MatCard`, track the previous life:
   ```tsx
   const lastLife = useRef(player.life);
   const [lifeDelta, setLifeDelta] = useState<{ n: number; key: number } | null>(null);
   useEffect(() => {
     const delta = player.life - lastLife.current;
     lastLife.current = player.life;
     if (delta === 0) return;
     setLifeDelta((d) => ({ n: delta, key: (d?.key ?? 0) + 1 }));
     const timer = window.setTimeout(() => setLifeDelta(null), 1200);
     return () => window.clearTimeout(timer);
   }, [player.life]);
   ```
   (`useRef` is already imported.)
2. Render inside `.dkm-mat-life`:
   `{lifeDelta && <b key={lifeDelta.key} className={`dkm-life-delta${lifeDelta.n < 0 ? " loss" : " gain"}`}>{lifeDelta.n > 0 ? `+${lifeDelta.n}` : lifeDelta.n}</b>}`
   and add `key`-driven class `dkm-life-hit` on the life pill while a
   negative delta is showing.
3. CSS: `.dkm-life-delta` is absolutely positioned above the pill, Anton
   20px, `--claw` for loss / `--shell` for gain, animation `dkmp-float`
   1.1s (rise 22px, fade out). `.dkm-life-hit` shakes the pill:
   `dkmp-shake` .35s (translateX -3px / 3px, three times). A loss of 5
   or more also flashes the whole mat's inset ring red-orange once
   (`dkmp-flash` .5s on `box-shadow`).
4. No new sound - `useGameSounds` already plays `hit`.

### 6b. Game over

`GameOverOverlay.tsx` is shared with the desktop page, and its classes
are `.dicekingdom .dk-gameover*` in `dicekingdom.css`. Restyle it for
**mobile only** by scoping new rules in `mobilePlay.css` to
`.dicekingdom.dk-mobile .dk-gameover...`. One markup change, safe for
both pages: add a result class and a crown -

```tsx
const won = !!winner && !!you && winner.id === you;
<div className={`dk-gameover${won ? " is-won" : winner ? " is-lost" : ""}`}>
  {won && <Crown className="dk-gameover-crown" />}
```

(import `Crown` from `./kingdomArt`; give `.dicekingdom .dk-gameover-crown`
a base rule of `display: none` in `dicekingdom.css` so desktop is
unchanged, and show it under `.dk-mobile`.)

Mobile styles:

- Backdrop: `background: radial-gradient(120% 75% at 50% 42%, #55307c 0%, #231a3c 55%, rgba(18,14,32,.96) 100%)`
  for a win; the plain dark `rgba(10,10,16,.82)` for a loss.
- Card: radius 24px, padding `26px 20px 22px`, `animation: dkh-pop .45s ease backwards`
  (`dkh-pop` is defined in `mobileHome.css`, already loaded on this page).
- Title (`h2`): Anton 44px caps. Win: the gold outlined-letter treatment
  from `.dkp-title` in `kingdomPages.css` is ideal but needs a
  `data-text` attribute; the simpler acceptable version is
  `color: #ffd45f; text-shadow: 0 3px 0 #96380f, 0 6px 14px rgba(0,0,0,.5)`.
  Loss: `--dkm-ink`, no effects.
- Crown (win only): 64px wide, `#ffd45f`, centred above the title,
  `animation: dkh-nod 4.5s ease-in-out 1s infinite`.
- Life line: two pills side by side (name + Anton number), the winner's
  with a gold ring.
- Buttons (`.btn`, `.btn.ghost`): the chunky recipe - "New game" orange,
  full width, 52px, Anton 20px; "Boss Ladder" purple when present. Stack
  them vertically with 10px gap (override the existing
  `.btn + .btn { margin-left: 8px }`).
- Boss reward text (`.dk-gameover-boss`): gold-tinted panel,
  `background: color-mix(in srgb, #f0a81e 16%, transparent)`, radius 14px,
  padding 10px 12px.

**Done when**: taking damage is impossible to miss but over in about a
second; winning feels like winning; the desktop game-over dialog looks
exactly as before (open `/dice-kingdom` and compare).

To reach game over quickly in a test, intercept the game-state response
in Playwright (`page.route`) and set `gameOver: true`, `winnerId` to
either player's id, and a life of 0 - don't play a whole game.

---

## Order, size, and where judgment is needed

| Step | Size | Risk | Needs an eye? |
|---|---|---|---|
| 1 Bottom bar | small, CSS only | low | no |
| 2 Nameplates | medium, markup in one component | medium (wrapping at 320px) | some |
| 3 Buy row | small-medium | low | no |
| 4 Field | small | medium (dice flights land here) | **yes** - the mat texture and empty slots are easy to overdo |
| 5 Header | medium | medium (the 375px clip) | some |
| 6 Moments | medium, adds state | low-medium | **yes** - timing and how loud the effects are |

If something in a step looks worse than before in a screenshot, prefer
the plainer option and note it in `DESIGN_NOTES.md` rather than adding
more decoration to fix it.
