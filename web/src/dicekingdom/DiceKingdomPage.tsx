import { useEffect, useRef, useState } from "react";
import "./dicekingdom.css";
import { api } from "./api";
import { CHAMPION_ICONS, CHARACTER_ICONS, EnergyBadge, HelpIcon, TardigradeIcon, TardigradePhotoIcon } from "./icons";
import { claimSeatFromUrl, inviteLink, nameClaimedSeat, rememberSeats } from "./seats";
import { CombatLane } from "./CombatLane";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor } from "./dieFaces";
import { StepRibbon } from "./StepRibbon";
import { MatchLog } from "./MatchLog";
import { SettingsMenu, ThemeToggle, useTheme } from "./ThemeToggle";
import { useDiceRoll, type RollTarget } from "./useDiceRoll";
import type { BlockAssignment, CardDef, CharacterFace, Die, GameState, PlayerState } from "./types";

const POLL_INTERVAL_MS = 2000;
const CHAMPIONS = [
  { id: "Wolf", energy: "Claw" },
  { id: "Armadillo", energy: "Shell" },
  { id: "GoldenEagle", energy: "Wing" },
  { id: "GreatHornedOwl", energy: "Eye" },
];

// The one shared click-to-select model driving every board interaction -
// which energy dice pay a cost, which dice reroll together, which Field
// dice attack. Mirrors ../PlayerBoard.tsx's Selection/onGroupClick shape:
// first click on a die makes it primary, further clicks add secondaries,
// clicking primary again clears the whole selection. One model instead of
// a separate local flag per action type is what keeps a die from ever
// being shown twice (once as itself, once in a floating "selected" copy)
// and keeps the contextual action reachable from wherever the die is.
interface Selection {
  primary: string | null;
  secondary: string[];
}
const EMPTY_SELECTION: Selection = { primary: null, secondary: [] };

function rolled(d: Die): boolean {
  return d.effectiveAttack !== null || d.energySymbolId !== null;
}

// The only zones where a die is actually showing a rolled face (rule
// 1.5, mirrors ../PlayerBoard.tsx's own ROLLED_ZONES) - everywhere else
// a die is unrolled, spent, or sitting on its card, so it's shown as
// plain and collapsible even if the DTO still carries a stale face from
// before it left a rolled zone. Gating this by zone rather than trusting
// effectiveAttack directly is what fixes a spent/KO'd die still showing
// its last rolled stats in the Used Pile.
const ROLLED_ZONES = new Set(["ReservePool", "PrepArea", "FieldZone", "AttackZone"]);
// Used Pile/Out of Play tiles show only the card/Tardigrade icon (see
// DieTile's own remarks) - shared here so groupDice can group them by
// that same identity alone, ignoring whatever face they happened to be
// on when they left play (see groupDice's own comment).
const ICON_ONLY_ZONES = new Set(["UsedPile", "OutOfPlay"]);

// v3's locked Tardigrade spec (v3/DESIGN_NOTES.md), for DieTile's own
// info popover - a Tardigrade has no CardDef/`levels` of its own to
// read this from the way a Character does. Matches TardigradeDie in
// InstinctClashConfig.cs's three stat levels (each printed on 2 of its
// 6 faces, except L3/"Bulwark" on just 1 - the 6th face, Surge, is pure
// energy and called out in its own sentence instead of a 4th row here).
const TARDIGRADE_SPEC: CharacterFace[] = [
  { fieldingCost: 0, attack: 0, defense: 1 },
  { fieldingCost: 0, attack: 1, defense: 1 },
  { fieldingCost: 0, attack: 1, defense: 3 },
];

// What the rail's "Now" header says for each step - ported from
// ../TurnRail.tsx's STEP_GUIDANCE/ATTACK_SUB_STEPS. Real feedback: the
// rail was showing a bare action button with no title or description at
// all, unlike /game's Now panel.
const STEP_GUIDANCE: Record<string, { title: string; text: string }> = {
  "start-of-turn": { title: "Clear & Draw", text: "Spent dice go to the Used Pile, then draw back up to four." },
  "roll-and-reroll": { title: "Roll & Reroll", text: "Roll everything drawn. You get one reroll decision, and taking it ends the step." },
  main: { title: "Main", text: "Field a rolled creature, purchase a Character, or spend energy dice." },
  "select-attackers": { title: "Attack · Declare Attackers", text: "Choose which of your fielded dice attack." },
  "assign-blockers": { title: "Attack · Assign Blockers", text: "The defender assigns blockers - anything left unassigned is unblocked." },
  "action-global-window": { title: "Attack · Resolve Combat", text: "Last window before combat damage lands." },
  "return-to-field": { title: "Clean Up", text: "Damage clears and it becomes the other player's turn." },
};
const ATTACK_STEPS = new Set(["select-attackers", "assign-blockers", "action-global-window"]);

interface DieGroup {
  key: string;
  sample: Die;
  count: number;
  ids: string[];
}

// Collapses dice that are truly interchangeable right now into one card
// with a count badge - mirrors ../dieHelpers.ts's groupDice. Applied to
// the piles (Bag/Used Pile/Out of Play) where a small pool means several
// identical Tardigrades are common; never to a rolled zone, where each
// die's own face is the point.
function groupDice(dice: Die[], zone: string): DieGroup[] {
  if (ROLLED_ZONES.has(zone)) {
    return dice.map((d) => ({ key: d.id, sample: d, count: 1, ids: [d.id] }));
  }
  // Icon-only piles show identity alone, nothing else - direct feedback
  // (2026-09-10): "they're all the same die, so... they could be
  // grouped all together." Grouping on the rolled-face fields below
  // (still right for Bag, whose popover shows real stats) split
  // identical Tardigrades that happened to leave play on different
  // faces into separate piles, even though the tile itself no longer
  // shows any of that.
  const groups = new Map<string, DieGroup>();
  for (const d of dice) {
    const key = ICON_ONLY_ZONES.has(zone)
      ? (d.cardId ?? "tardigrade")
      : [d.cardId ?? "tardigrade", d.level, d.effectiveAttack, d.effectiveDefense, d.energySymbolId, d.energyAmount].join("|");
    const existing = groups.get(key);
    if (existing) {
      existing.count += 1;
      existing.ids.push(d.id);
    } else {
      groups.set(key, { key, sample: d, count: 1, ids: [d.id] });
    }
  }
  return [...groups.values()];
}

// Green = active AND it's you; amber-grey = active and it's not you
// (waiting); no highlight otherwise. Same cue as /game's identical
// green/amber-grey pattern (DESIGN_LOG.md, 2026-09-03) - and a real bug
// fix along the way: the old inline version only ever highlighted
// playerOne's box, never playerTwo's.
//
// Life total moved OUT to the new fixed Scoreboard below (2026-09-08) -
// direct feedback, after a first attempt merged it into one squeezed
// row here got corrected: "I said 'panel' and you translated that to
// 'row.' I meant column, like we had it previously," followed by "we'd
// always want the life totals to be visible somewhere, I don't want to
// have to scroll to see if I'm winning or losing." Life needing to be
// scroll-proof and this box's own text reading as a stacked column
// turned out to be two separate asks - this box goes back to a role
// label/name+badge/note column (no life line at all now), and
// Scoreboard (a real fixed bar, same technique as the turn-controls
// rail) is what's actually always on screen.
function ChampionBox({ player, isActivePlayer, you }: { player: PlayerState; isActivePlayer: boolean; you: string }) {
  const mine = player.id === you;
  const turnClass = isActivePlayer ? (mine ? " turn-mine" : " turn-waiting") : "";
  const champion = player.champion;
  const accent = champion ? `var(--${champion.energySymbolId.toLowerCase()})` : undefined;
  const Icon = champion ? CHAMPION_ICONS[champion.id] : null;
  return (
    <div className={`championbox${turnClass}`} style={accent ? ({ ["--cc" as string]: accent } as const) : undefined}>
      <div className="championbox-row">
        {Icon && <Icon size={30} />}
        <div className="championbox-text">
          <div className="championbox-role">{mine ? "Your Champion" : "Opponent Champion"}</div>
          {champion && (
            <div className="championbox-name-row">
              <span className="championbox-name">{champion.name}</span>
              {/* Direct feedback (2026-09-05): even once every Champion
                  has a real avatar, the energy type still needs to read
                  at a glance - a photo alone doesn't carry that the way
                  a color glyph did. Variant A badge (2026-09-08) since
                  this is exactly the kind of small, low-contrast spot
                  the earlier bare icon kept losing its own sizing bug
                  in. */}
              <EnergyBadge type={champion.energySymbolId} size={13} />
            </div>
          )}
          {champion?.passiveText && <div className="championbox-note">{champion.passiveText}</div>}
        </div>
      </div>
    </div>
  );
}

// A real fixed bar, not just a panel somewhere in the rail - direct
// feedback (2026-09-08): "we'd always want the life totals to be
// visible somewhere, I don't want to have to scroll to see if I'm
// winning or losing." Same `position: fixed` technique .dk-rail-mid
// already uses for the turn controls, pinned to the opposite edge
// (top) so the two fixed bars don't compete for the same space. Just
// the two life totals - not a fuller scoreboard - since that's the one
// thing that's genuinely useful to see with zero scrolling on every
// single screen of the game; champion name/ability is reference
// material you look up occasionally, not something worth a permanent
// pin (see ChampionBox above, back in normal flow).
function Scoreboard({ opponent, mine }: { opponent: PlayerState; mine: PlayerState }) {
  const oppAccent = opponent.champion ? `var(--${opponent.champion.energySymbolId.toLowerCase()})` : undefined;
  const mineAccent = mine.champion ? `var(--${mine.champion.energySymbolId.toLowerCase()})` : undefined;
  return (
    <div className="scoreboard">
      <div className="scoreboard-side" style={oppAccent ? ({ ["--cc" as string]: oppAccent } as const) : undefined}>
        <span className="scoreboard-label">Opp</span>
        <span className="scoreboard-life">{opponent.life}</span>
      </div>
      <div className="scoreboard-side mine" style={mineAccent ? ({ ["--cc" as string]: mineAccent } as const) : undefined}>
        <span className="scoreboard-label">You</span>
        <span className="scoreboard-life">{mine.life}</span>
      </div>
    </div>
  );
}

// Ported from ../TurnRail.tsx's InvitePanel - one compact row, not a
// full panel, since this is a one-time convenience most of a game
// doesn't need once the other seat has joined.
function InviteRow({ link }: { link: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="invite-row" title={link}>
      <span className="invite-row-label">Invite</span>
      <button
        type="button"
        className="invite-row-button"
        onClick={async () => {
          try {
            await navigator.clipboard.writeText(link);
            setCopied(true);
            window.setTimeout(() => setCopied(false), 2000);
          } catch {
            // Clipboard blocked - the link is still in the title tooltip.
          }
        }}
      >
        {copied ? "Copied!" : "Copy link"}
      </button>
    </div>
  );
}

// One EnergyBadge circle per energy point, overlapping when a die is
// worth more than one - direct feedback (2026-09-08), after comparing
// this against the old "N + icon" pill in a published mockup: "I like
// Variant A... let's implement it wherever we have energy symbols."
function PipBadge({ type, amount }: { type: string; amount: number }) {
  return (
    <span className="pip-stack" title={`${amount} ${type}`}>
      {Array.from({ length: Math.max(1, amount) }, (_, i) => (
        <EnergyBadge key={i} type={type} size={16} />
      ))}
    </span>
  );
}

// A cost as a number + the energy's own badge, instead of spelling the
// type out - direct feedback (2026-09-07): "can we get the icons in
// there instead of the words," then (2026-09-08) upgraded from a bare
// colored icon to the same EnergyBadge circle every other energy symbol
// now uses, for the same reason: a bare icon in --text-dim (as roster/
// popover cost labels sit in) went a dim grey in dark mode, easy to
// lose against the panel - a filled circle doesn't have that problem.
function CostIcon({ energyType }: { energyType: string }) {
  return <EnergyBadge type={energyType} size={14} />;
}

function DieTile({
  die,
  zone,
  count,
  cardsById,
  onClick,
  clickable,
  picked,
  accent,
  mine,
  label: labelOverride,
  spin,
  turnOffset,
}: {
  die: Die;
  /** Which zone this tile represents - gates whether a rolled face shows
   *  at all (see ROLLED_ZONES) rather than trusting the die's raw data. */
  zone: string;
  /** >1 draws a "×N" badge - see groupDice. */
  count?: number;
  cardsById: Map<string, CardDef>;
  onClick?: () => void;
  clickable?: boolean;
  picked?: boolean;
  accent?: string;
  /** Tints the die-cube's faces apart from the opponent's - see
   *  ../DieCube.tsx. Only matters in a rolled zone, where the cube shows. */
  mine?: boolean;
  /** Overrides the bottom label - used for "already rerolled" during Roll & Reroll. */
  label?: string;
  /** Mid-roll transform and accumulated turn count - see useDiceRoll.ts. */
  spin?: CubeSpin;
  turnOffset?: number;
}) {
  const [showInfo, setShowInfo] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  // Closes on a click anywhere outside this one tile - same pattern the
  // roster's own card-popover uses, just scoped locally (this popover's
  // open/closed state lives on the tile itself, not page-level, since
  // there's no reason only one die's info can be open at a time).
  useEffect(() => {
    if (!showInfo) return;
    function handlePointerDown(e: PointerEvent) {
      if (!(e.target instanceof Node) || !wrapRef.current?.contains(e.target)) setShowInfo(false);
    }
    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [showInfo]);
  const isRolled = ROLLED_ZONES.has(zone) && rolled(die);
  const card = die.cardId ? cardsById.get(die.cardId) : undefined;
  const name = die.cardId ? (card?.name ?? die.cardId) : "Tardigrade";
  const cls = ["dietile", clickable ? "clickable" : "", picked ? "picked" : ""].filter(Boolean).join(" ");
  const style = accent ? ({ textAlign: "center", ["--cc" as string]: accent, color: accent } as const) : { textAlign: "center" as const };
  // Direct feedback (2026-09-08): "the Level probably isn't need-to-know
  // information... provide that on a click." The die-cube itself already
  // prints the real attack/defense/cost numbers on its face (see
  // DieCube.tsx), so the level number below it was pure repetition -
  // dropped for a stat face entirely to shrink the tile. A Tardigrade's
  // energy face used to print "Surge" here too, but direct feedback
  // (2026-09-10) dropped that as well - the energy corner circle already
  // says what it is. A Character's own energy face still prints its
  // name, since that's the only thing on that face saying whose die it is.
  const label = labelOverride ?? (die.effectiveAttack === null && !die.isTardigrade ? name : null);
  const Avatar = die.cardId ? CHARACTER_ICONS[die.cardId] : null;
  const energyType = card?.energyTypes[0];
  // A rolled die that ISN'T currently part of an active selection has
  // nothing else a click would do - direct feedback (2026-09-08): "much
  // like clicking on the character itself" (the roster's own card-
  // popover). Deliberately NOT wired up for a clickable die: selecting
  // it (to pay energy, attack, block, ...) stays the one thing a click
  // there does, unchanged.
  const canShowInfo = isRolled && !clickable;
  // Direct feedback (2026-09-10): "when in 'Out of Play' or 'Used Pile'
  // rather than take up space with the word we should just put the
  // character symbol... just the symbol, though, no stats or energy."
  // Unlike Bag/Drawn/Carried, these two piles accumulate the most dice
  // over a game, so they're the ones that actually benefit from
  // dropping the text row.
  const iconOnly = ICON_ONLY_ZONES.has(zone);
  return (
    <div ref={wrapRef} className={`dietile-wrap${showInfo ? " info-open" : ""}`}>
      <button
        type="button"
        className={cls}
        onClick={clickable ? onClick : canShowInfo ? () => setShowInfo((v) => !v) : undefined}
        style={style}
      >
        {count && count > 1 && <span className="chip-count">×{count}</span>}
        {!isRolled ? (
          iconOnly ? (
            <div className="tile-icon">{Avatar ? <Avatar size={24} /> : <TardigradeIcon size={24} />}</div>
          ) : (
            <>
              <div className="lbl">{name}</div>
              <div className="stat">—</div>
            </>
          )
        ) : (
          <>
            {/* The same 3D cube /game's board uses (../DieCube.tsx), not a
                flat stat badge - a rolled die is a physical object showing
                a real face, not text about one. In the die's own lower-
                left corner now, not a separate box below it - direct
                feedback (2026-09-09): "the energy ended up in the wrong
                place. It's supposed to be in the lower left hand corner
                INSIDE the die itself." */}
            <DieCube
              {...facesFor(die, cardsById)}
              size={34}
              mine={mine ?? true}
              spin={spin}
              turnOffset={turnOffset}
              energyCorner={
                die.energySymbolId && die.energyAmount > 0
                  ? { type: die.energySymbolId, amount: die.energyAmount }
                  : undefined
              }
            />
            {label && <div className="lbl">{label}</div>}
          </>
        )}
      </button>
      {showInfo && (
        <div className="card-popover down">
          <div className="card-popover-head">
            {Avatar ? <Avatar size={28} /> : <TardigradePhotoIcon size={28} />}
            <div>
              <div className="card-popover-name">{name}</div>
              {card && (
                <div className="card-popover-cost">
                  Cost {card.purchaseCost} <CostIcon energyType={card.energyTypes[0] ?? "Wild"} />
                </div>
              )}
            </div>
          </div>
          <div className="card-popover-levels">
            {(card ? card.levels : TARDIGRADE_SPEC).map((level, i) => (
              <div className={`card-popover-level-row${die.level === i + 1 ? " current" : ""}`} key={i}>
                <span className="lvl-label">L{i + 1}</span>
                <span className="lvl-stats">
                  {level.attack}A / {level.defense}D
                </span>
                <span className="lvl-cost">
                  {level.fieldingCost} {energyType && <CostIcon energyType={energyType} />}
                </span>
              </div>
            ))}
          </div>
          {card ? (
            <>
              <p className="card-popover-energy-note">
                Plus 2 faces of 2 <CostIcon energyType={card.energyTypes[0] ?? "Wild"} /> and 1 face of 1{" "}
                <CostIcon energyType={card.energyTypes[0] ?? "Wild"} />
              </p>
              <p className="card-popover-text">{card.rawText}</p>
            </>
          ) : (
            <p className="card-popover-text">
              Two L1, two L2, one L3 (&ldquo;Bulwark&rdquo;), one Surge face - a fixed spec every Tardigrade shares.
            </p>
          )}
        </div>
      )}
    </div>
  );
}

// The live board's compact stand-in for the old <details className="how">
// text toggle - same content, an icon+popover instead so it can share the
// ribbon's row (see SettingsMenu in ThemeToggle.tsx, the same pattern,
// same reason).
function HowToPlayMenu() {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    function handlePointerDown(e: PointerEvent) {
      if (!(e.target instanceof Node) || !wrapRef.current?.contains(e.target)) setOpen(false);
    }
    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [open]);
  return (
    <div ref={wrapRef} className="icon-menu-wrap">
      <button type="button" className="icon-btn" aria-label="How to play" onClick={() => setOpen((o) => !o)}>
        <HelpIcon size={16} />
      </button>
      {open && (
        <div className="icon-menu-popover">
          <ul>
            <li>Draw, then Roll - each die may be rerolled once, together with any others you select, before Continue.</li>
            <li>Field a rolled creature (Tardigrades are free; your Character costs energy, any type) or Purchase another copy of your Character (matching type or Wild only).</li>
            <li>Proceed to Attack, pick attackers; the other seat assigns blockers, then Resolve Combat.</li>
          </ul>
        </div>
      )}
    </div>
  );
}

// The rail's per-step reminder text ("Roll everything drawn...") used to
// sit under the step title as its own line - direct feedback (2026-09-08):
// "move the reminder text of what the turn is to an info icon that can be
// tapped." Opens UPWARD (unlike Help/Settings above, which open down) -
// this control bar is a fixed strip pinned to the viewport's bottom edge
// on mobile (see .dk-rail-mid's own CSS), so a downward popover would run
// off the bottom of the screen.
function NowInfoButton({ text }: { text: string }) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    function handlePointerDown(e: PointerEvent) {
      if (!(e.target instanceof Node) || !wrapRef.current?.contains(e.target)) setOpen(false);
    }
    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [open]);
  return (
    <div ref={wrapRef} className="icon-menu-wrap">
      <button type="button" className="icon-btn" aria-label="What this step means" onClick={() => setOpen((o) => !o)}>
        <HelpIcon size={13} />
      </button>
      {open && <div className="icon-menu-popover up">{text}</div>}
    </div>
  );
}

export function DiceKingdomPage() {
  // Applied unconditionally, before either screen below renders - a
  // stored preference has to re-apply on the pre-game setup screen too,
  // not just once a game exists (see useTheme's own remarks).
  const [theme, setTheme] = useTheme();
  const [game, setGame] = useState<GameState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  // Scrolled into view after every action - see the effect below, near
  // the other early hooks (both need to run unconditionally, before
  // the `!game` early return further down).
  const oppRowRef = useRef<HTMLDivElement>(null);
  const yourRowRef = useRef<HTMLDivElement>(null);
  const [setupA, setSetupA] = useState<string | null>(null);
  const [setupB, setSetupB] = useState<string | null>(null);

  const [selection, setSelection] = useState<Selection>(EMPTY_SELECTION);
  // Dice that already used their one reroll this Roll & Reroll step - the
  // server doesn't say, so this is tracked client-side (see
  // TurnEngine.RerolledThisStep) and reset whenever the step changes.
  const [rerolledIds, setRerolledIds] = useState<string[]>([]);
  // Built up one attacker at a time via the shared selection (primary =
  // attacker, secondary = blocker(s) for it), same shape as
  // ../CombatPanel.tsx's DeclareBlockersPanel - kept separate from
  // `selection` because it accumulates ACROSS several picks rather than
  // being replaced by each one.
  const [blockAssignments, setBlockAssignments] = useState<Record<string, string | null>>({});
  const [cardsById, setCardsById] = useState<Map<string, CardDef>>(new Map());
  // The dice-cube roll animation - ported verbatim from ../useDiceRoll.ts.
  // README calls this "the single most important piece to port
  // faithfully" - see animateRolledDice below for how a roll is detected.
  const { spins, offsets, rolling, launch: launchRoll, spinTo: spinDie } = useDiceRoll();
  const [bagOpen, setBagOpen] = useState(false);
  const [oppBagOpen, setOppBagOpen] = useState(false);
  // The opponent's roster collapses to icon-only until tapped - direct
  // feedback (2026-09-08): "for now, I would make the whole thing one
  // section, and tapping anywhere in there would expand to show the
  // full details of all eight cards." Only the opponent's needs this;
  // your own roster is the thing you're actually shopping from every
  // turn, so it stays expanded (see renderBoard's own remarks).
  const [oppRosterOpen, setOppRosterOpen] = useState(false);
  // Which roster card's detail popover (ability + per-level stats) is
  // open, if any - a single page-level id rather than per-board state,
  // since only one should reasonably be open at a time regardless of
  // which side's roster it's on. Deliberately separate from `selection`:
  // viewing a card's detail has to work even when it isn't purchasable
  // right now (most of the game), which selection's own clickable gate
  // would otherwise block entirely.
  const [openCardId, setOpenCardId] = useState<string | null>(null);

  // Direct feedback (2026-09-05): the popover stuck around indefinitely -
  // it needs to close once the die it was showing is actually purchased
  // (handled in run(), below) or the moment the player clicks anywhere
  // else on the page. `closest` walks up from whatever was clicked
  // (which for a click INSIDE the popover or its own trigger chip is
  // still under .roster-chip-wrap) rather than requiring an exact target
  // match, so clicking the popover's own Purchase button doesn't
  // immediately reopen/close it out from under itself.
  useEffect(() => {
    if (!openCardId) return;
    function handlePointerDown(e: PointerEvent) {
      if (!(e.target instanceof Element) || !e.target.closest(".roster-chip-wrap")) {
        setOpenCardId(null);
      }
    }
    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [openCardId]);

  useEffect(() => {
    api.getCards().then((cards) => setCardsById(new Map(cards.map((c) => [c.id, c]))));
  }, []);

  // Invite-link join, same shape as ../App.tsx's own effect.
  useEffect(() => {
    const claim = claimSeatFromUrl();
    if (!claim) return;
    api
      .getGame(claim.gameId)
      .then((joined) => {
        if (joined.yourPlayerId) nameClaimedSeat(claim.gameId, joined.yourPlayerId);
        setGame(joined);
      })
      .catch((e) => setError(`Could not join that game: ${e instanceof Error ? e.message : String(e)}`));
  }, []);

  // Poll for the other player's moves - same version-compare shape as v1.
  const gameId = game?.gameId ?? null;
  const gameVersion = game?.version ?? 0;
  useEffect(() => {
    if (!gameId) return;
    let cancelled = false;
    const timer = window.setInterval(async () => {
      if (busyRef.current) return;
      try {
        const latest = await api.getGame(gameId);
        if (!cancelled && latest.version !== gameVersion) setGame(latest);
      } catch {
        // quiet - the next poll in two seconds either works or it doesn't matter yet
      }
    }, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [gameId, gameVersion]);

  // Keeps the active player's own board in view after every action -
  // direct feedback (2026-09-08), offered as an alternative to actually
  // fixing "if I scroll at all, then trying to tap 'Draw' or 'Roll'...
  // does not actually Draw or Roll": "can we default the focus to the
  // active player's mat?" `block: "nearest"` makes this a no-op when
  // that board is already adequately in view, so a normal tap from a
  // sensible scroll position doesn't get an unwanted scroll-jump on top
  // of it - this only actually moves anything when the view really was
  // left somewhere unhelpful.
  useEffect(() => {
    if (!game) return;
    const activeId = game.activePlayerId;
    const meId = game.yourPlayerId ?? game.playerOne.id;
    const ref = activeId === meId ? yourRowRef : oppRowRef;
    ref.current?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [game?.version, game?.activePlayerId]);

  // v2's CombatEngine.DeclareAttackers unconditionally enters
  // AssignBlockers regardless of attacker count, and DeclareBlockers
  // unconditionally enters ActionGlobalWindow regardless of block count
  // (CombatEngine.cs, lines 56/89 - same shape as /game's engine and the
  // same reason: the Action/Global window is a real window independent
  // of whether anyone attacked, not something to skip server-side).
  // Real feedback from /game's identical gap: with nothing to block or
  // split, still having to click through both steps for a combat that
  // never happened reads as stuck, not deliberate. Auto-submits the
  // empty answer instead - every rule still formally fires, it just
  // doesn't wait on a click for an answer that was never going to be
  // anything but "nothing."
  const assignBlockersAttackerCount = game
    ? game.dice.filter((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId).length
    : 0;
  useEffect(() => {
    if (!gameId || !game) return;
    if (game.currentStepId === "assign-blockers" && assignBlockersAttackerCount === 0) {
      runQuiet(() => api.declareBlockers(gameId, []));
    } else if (
      game.currentStepId === "action-global-window" &&
      Object.values(blockAssignments).filter(Boolean).length === 0
    ) {
      runQuiet(() => api.assignCombatDamage(gameId, []));
    }
  }, [gameId, game?.version, game?.currentStepId, assignBlockersAttackerCount, blockAssignments]);

  function clearSelection() {
    setSelection(EMPTY_SELECTION);
  }

  function toggleDie(id: string) {
    setSelection((sel) => {
      if (sel.primary === id) return EMPTY_SELECTION;
      if (sel.secondary.includes(id)) return { ...sel, secondary: sel.secondary.filter((x) => x !== id) };
      if (sel.primary === null) return { primary: id, secondary: [] };
      return { ...sel, secondary: [...sel.secondary, id] };
    });
  }

  // Every server call that might have rolled dice comes through run() -
  // comparing state before/after is enough to catch it wherever it came
  // from (Roll, a reroll, a future card effect), matching ../App.tsx's
  // identical animateRolledDice. `rolledDieIds` names dice an action
  // deliberately rolled (reroll knows its own ids up front); without it,
  // a reroll landing the same face again wouldn't animate at all.
  //
  // A face can also change WITHOUT the caller naming it a roll at all -
  // partially spending a double-energy die spins it down to its single-
  // energy face (TurnEngine.SpendEnergy's TrySpinDown). Direct feedback
  // (2026-09-05): that should read as a distinct "twist," not the same
  // toss-and-tumble a real roll gets - so any die whose face changed but
  // ISN'T in `rolledDieIds` goes through spinDie instead of launchRoll.
  function animateRolledDice(previous: GameState, next: GameState, rolledDieIds?: string[]) {
    const before = new Map(previous.dice.map((d) => [d.id, d]));
    const explicit = new Set(rolledDieIds ?? []);
    const rolledTargets: RollTarget[] = [];
    const spunTargets: RollTarget[] = [];
    for (const die of next.dice) {
      const was = before.get(die.id);
      if (!was) continue;
      if (!rolled(die)) continue;
      const changedFace =
        was.level !== die.level || was.effectiveAttack !== die.effectiveAttack ||
        was.energySymbolId !== die.energySymbolId || was.energyAmount !== die.energyAmount;
      if (!explicit.has(die.id) && !changedFace) continue;
      const { index } = facesFor(die, cardsById);
      (explicit.has(die.id) ? rolledTargets : spunTargets).push({ dieId: die.id, faceIndex: index });
    }
    launchRoll(rolledTargets);
    spinDie(spunTargets);
  }

  async function run(fn: () => Promise<GameState>, rolledDieIds?: string[]) {
    setBusy(true);
    busyRef.current = true;
    setError(null);
    try {
      const previous = game;
      const next = await fn();
      setGame(next);
      if (previous) animateRolledDice(previous, next, rolledDieIds);
      clearSelection();
      setOpenCardId(null); // e.g. a completed Purchase - see openCardId's own remarks
      if (next.currentStepId !== "roll-and-reroll") setRerolledIds([]);
      if (next.currentStepId !== "assign-blockers") setBlockAssignments({});
      return next;
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      throw e;
    } finally {
      setBusy(false);
      busyRef.current = false;
    }
  }

  // Same shape as run(), for an auto-fired action THIS browser may not
  // actually hold the seat for - see the auto-skip effect below. Both
  // seats' browsers evaluate the same "nothing to decide" condition, but
  // only the one holding the required seat's token can legally submit;
  // the other gets a real 403, which is expected and shouldn't show as
  // an error banner (matching ../App.tsx's runQuiet).
  async function runQuiet(fn: () => Promise<GameState>) {
    if (busyRef.current) return;
    busyRef.current = true;
    try {
      const next = await fn();
      setGame(next);
    } catch {
      // Expected on whichever browser doesn't hold the seat this
      // particular auto-skip needed.
    } finally {
      busyRef.current = false;
    }
  }

  async function startMatch() {
    if (!setupA || !setupB) return;
    await run(async () => {
      const created = await api.createGame(setupA, setupB);
      rememberSeats(created.game.gameId, created.seats);
      return created.game;
    });
  }

  if (!game) {
    return (
      <div className="dicekingdom">
        <div className="dk-titlebar-right" style={{ float: "right" }}>
          <ThemeToggle theme={theme} setTheme={setTheme} />
        </div>
        <p className="eyebrow" style={{ opacity: 0.6, fontSize: 12, textTransform: "uppercase", letterSpacing: "0.1em" }}>
          DiceFight v3
        </p>
        <h1>Dice Kingdom</h1>
        <p className="dek">
          Pass-and-play, or send the other seat an invite link once the match starts. Runs on the real rules
          engine - a small pool, simple abilities, mostly for reacting to how the system feels.
        </p>
        {error && <p className="error">{error}</p>}
        <div className="panel">
          <h3 style={{ margin: "0 0 10px" }}>Player 1</h3>
          <div className="champ-pick">
            {CHAMPIONS.map((c) => {
              const Icon = CHAMPION_ICONS[c.id];
              return (
                <button
                  key={c.id}
                  type="button"
                  className={`champ-opt${setupA === c.id ? " selected" : ""}`}
                  style={{ ["--sel" as string]: `var(--${c.energy.toLowerCase()})`, color: `var(--${c.energy.toLowerCase()})` }}
                  onClick={() => setSetupA(c.id)}
                >
                  <Icon />
                  <div className="cname" style={{ color: "var(--text-h)" }}>
                    {c.id.replace(/([A-Z])/g, " $1").trim()}
                  </div>
                </button>
              );
            })}
          </div>
          <h3 style={{ margin: "0 0 10px" }}>Player 2</h3>
          <div className="champ-pick">
            {CHAMPIONS.map((c) => {
              const Icon = CHAMPION_ICONS[c.id];
              return (
                <button
                  key={c.id}
                  type="button"
                  className={`champ-opt${setupB === c.id ? " selected" : ""}`}
                  style={{ ["--sel" as string]: `var(--${c.energy.toLowerCase()})`, color: `var(--${c.energy.toLowerCase()})` }}
                  onClick={() => setSetupB(c.id)}
                >
                  <Icon />
                  <div className="cname" style={{ color: "var(--text-h)" }}>
                    {c.id.replace(/([A-Z])/g, " $1").trim()}
                  </div>
                </button>
              );
            })}
          </div>
          <button className="btn" disabled={!setupA || !setupB || busy} onClick={startMatch}>
            Start Match
          </button>
        </div>
      </div>
    );
  }

  const you = game.yourPlayerId ?? game.playerOne.id;
  const isYourTurn = you === game.activePlayerId;
  const opponentId = you === game.playerOne.id ? game.playerTwo.id : game.playerOne.id;
  const step = game.currentStepId;

  function diceFor(playerId: string, zone?: string) {
    return game!.dice.filter((d) => d.controllerId === playerId && (!zone || d.zone === zone));
  }

  const primaryDie = selection.primary ? game.dice.find((d) => d.id === selection.primary) ?? null : null;

  // What the current selection actually costs / requires, for the cost
  // line shown next to the contextual action button. Field: any energy
  // type; Purchase: only the card's own type or Wild.
  function costFor(die: Die): { amount: number; matchType: string | null } {
    if (die.zone === "Unpurchased") {
      const card = die.cardId ? cardsById.get(die.cardId) : undefined;
      return { amount: card?.purchaseCost ?? 0, matchType: card?.energyTypes[0] ?? null };
    }
    if (!die.cardId || die.level === null) return { amount: 0, matchType: null }; // Tardigrade - free
    const card = die.cardId ? cardsById.get(die.cardId) : undefined;
    return { amount: card?.levels[die.level - 1]?.fieldingCost ?? 0, matchType: null };
  }

  // Whether a Reserve Pool die can currently be clicked, and what
  // clicking it means, depends only on the step and what's already
  // selected - not on a separate per-feature flag. Mirrors
  // ../ActionTray.tsx's "any die can become primary; once one is
  // primary, others become secondary" permissiveness.
  function reservePoolClickable(d: Die): boolean {
    if (d.controllerId !== you || !isYourTurn) return false;
    if (step === "roll-and-reroll") return rolled(d) && !rerolledIds.includes(d.id);
    if (step === "main") {
      if (selection.primary === null) return rolled(d) && d.effectiveAttack !== null; // start a Field
      if (d.id === selection.primary) return true; // toggle off
      return d.energyAmount > 0; // candidate energy payment
    }
    return false;
  }

  function renderBoard(playerId: string, mirrored: boolean) {
    const player = playerId === game!.playerOne.id ? game!.playerOne : game!.playerTwo;
    const accent = player.champion ? `var(--${player.champion.energySymbolId.toLowerCase()})` : undefined;
    const field = diceFor(playerId, "FieldZone");
    // Every zone a die can actually end up in gets shown, even at 0,
    // matching ../PlayerBoard.tsx's own full 9-zone mat exactly (minus
    // Intimidated, which v3 has no equivalent rule for) rather than
    // merging zones for a "simpler" board - direct feedback that doing so
    // just reads as "fundamentally different from Dice Fight". PrepArea
    // is v2's staging zone for dice mid-roll (during Roll & Reroll these
    // ARE the tiles you click to build a reroll selection - see
    // reservePoolClickable, which is zone-agnostic already);
    // DiceFromBag/DiceFromPrep are what a card that moves dice through
    // the Bag/Prep Area this turn would populate.
    const reserve = diceFor(playerId, "ReservePool");
    const prep = diceFor(playerId, "PrepArea");
    const used = diceFor(playerId, "UsedPile");
    const outOfPlay = diceFor(playerId, "OutOfPlay");
    const bag = diceFor(playerId, "Bag");
    const drawn = diceFor(playerId, "DiceFromBag");
    const carried = diceFor(playerId, "DiceFromPrep");
    const unpurchased = diceFor(playerId, "Unpurchased");
    const unpurchasedByCard = new Map<string, Die[]>();
    for (const d of unpurchased) {
      if (!d.cardId) continue;
      unpurchasedByCard.set(d.cardId, [...(unpurchasedByCard.get(d.cardId) ?? []), d]);
    }

    // Green = this player's move right now AND it's you; amber-grey =
    // this player's move and it's not you (you're waiting). Never red -
    // that reads as "something's wrong," not "waiting your turn". Same
    // two hues as /game's identical cue (DESIGN_LOG.md, 2026-09-03).
    const isActivePlayer = playerId === game!.activePlayerId;
    // A real "turn-inactive" state for whichever board ISN'T live right
    // now, not just "no highlight" - direct feedback (2026-09-05): a
    // thin ring around the active board wasn't obvious enough; the whole
    // board needs to visibly change when the turn passes. See
    // .playerboard.turn-inactive.
    const turnClass = isActivePlayer ? (playerId === you ? " turn-mine" : " turn-waiting") : " turn-inactive";

    // A single die-count zone, matching v1's PlayerBoard.tsx's mat-slot
    // shape - used for the two grid cells that show a group of dice as
    // real tiles (Used Pile, Out of Play), so the grid markup below reads
    // as "which zone goes where" rather than repeating this each time.
    const ZONE_TINTS: Record<string, string> = { UsedPile: "used", OutOfPlay: "outofplay", PrepArea: "prep" };
    function pileZone(title: string, zoneName: string, dice: Die[], note?: string) {
      return (
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "plain"}`}>
          <h4>
            {title} <span className="count">{dice.length}</span>
          </h4>
          {note && <span className="zone-note">{note}</span>}
          <div className="dierow">
            {dice.length === 0 && <span style={{ opacity: 0.5, fontSize: 12 }}>empty</span>}
            {groupDice(dice, zoneName).map((g) => (
              <DieTile key={g.key} die={g.sample} zone={zoneName} count={g.count} cardsById={cardsById} accent={accent} />
            ))}
          </div>
        </div>
      );
    }

    // Bag/Drawn This Turn/Carried From Prep, all on one line under Reserve
    // Pool - direct feedback (2026-09-10): "those areas... normally don't
    // need their exact contents seen" is true of all three, not just Bag,
    // so name+count is all any of them show now - no dice tiles, no hint
    // text ("we'll let them figure that out" re: the old "click to
    // inspect"). Bag alone stays clickable, opening the same inspector
    // popover it always has (contents are public info - see the popover's
    // own remarks below); Drawn/Carried are plain text, nothing to expand.
    function trayItem(label: string, count: number, onClick?: () => void) {
      const content = (
        <>
          {label} <span className="count">{count}</span>
        </>
      );
      return onClick ? (
        <button type="button" className="tray-item tray-item-btn" onClick={onClick}>
          {content}
        </button>
      ) : (
        <span className="tray-item">{content}</span>
      );
    }
    function bagTray(dice: Die[]) {
      const mine = playerId === you;
      const open = mine ? bagOpen : oppBagOpen;
      const setOpen = mine ? setBagOpen : setOppBagOpen;
      return (
        <>
          {trayItem("Bag", dice.length, () => setOpen((o) => !o))}
          {open && (
            <div className={`bag-popover ${mine ? "up" : "down"}`}>
              <h5>Contents known, order is not</h5>
              <div className="dierow">
                {dice.length === 0 && <span style={{ opacity: 0.5, fontSize: 12 }}>empty</span>}
                {groupDice(dice, "Bag").map((g) => (
                  <DieTile key={g.key} die={g.sample} zone="Bag" count={g.count} cardsById={cardsById} accent={accent} />
                ))}
              </div>
            </div>
          )}
        </>
      );
    }

    // Reserve Pool and Prep Area behave identically during Roll & Reroll -
    // reservePoolClickable is zone-agnostic (it only reads step/selection/
    // rolled state), and these are the tiles a player clicks to build a
    // reroll selection either way. Each die shown individually (not
    // grouped) since a rolled zone is about each die's own face, not a
    // count - see ROLLED_ZONES.
    function rolledZone(title: string, zoneName: string, dice: Die[], compact?: boolean) {
      const isRollingHere = rolling && dice.some((d) => spins[d.id]);
      return (
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "reserve"}${isRollingHere ? " rolling" : ""}${compact ? " compact" : ""}`}>
          <h4>
            {title} <span className="count">{dice.length}</span>
          </h4>
          <div className="dierow">
            {dice.map((d) => {
              const picked = d.id === selection.primary || selection.secondary.includes(d.id);
              const already = step === "roll-and-reroll" && rerolledIds.includes(d.id);
              return (
                <DieTile
                  key={d.id}
                  die={d}
                  zone={zoneName}
                  cardsById={cardsById}
                  accent={accent}
                  mine={playerId === you}
                  clickable={reservePoolClickable(d)}
                  picked={picked}
                  label={already ? "rerolled" : undefined}
                  onClick={() => toggleDie(d.id)}
                  spin={spins[d.id]}
                  turnOffset={offsets[d.id]}
                />
              );
            })}
          </div>
        </div>
      );
    }

    const fieldZone = (
      <div className="zone zone-field">
        <h4>
          Field <span className="count">{field.length}</span>
        </h4>
        <div className="dierow">
          {field.map((d) => {
            // Attack: pick your own attackers. Defend: pick a
            // candidate blocker from your own Field Zone dice - see
            // handleBlockerSlotClick for where that selection goes.
            const clickable =
              d.controllerId === you &&
              ((isYourTurn && step === "select-attackers") || (!isYourTurn && step === "assign-blockers"));
            const picked = d.id === selection.primary || selection.secondary.includes(d.id);
            return (
              <DieTile
                key={d.id}
                die={d}
                zone="FieldZone"
                cardsById={cardsById}
                accent={accent}
                mine={playerId === you}
                clickable={clickable}
                picked={picked}
                onClick={() => toggleDie(d.id)}
              />
            );
          })}
        </div>
      </div>
    );

    const mat = (
      <div className={`mat${mirrored ? " mirrored" : ""}`}>
        <div className="mat-slot mat-field">{fieldZone}</div>
        <div className="mat-slot mat-used">{pileZone("Used Pile", "UsedPile", used)}</div>
        <div className="mat-slot mat-reserve">{rolledZone("Reserve Pool", "ReservePool", reserve)}</div>
        <div className="mat-slot mat-prep">{rolledZone("Prep Area", "PrepArea", prep)}</div>
        <div className="mat-slot mat-outofplay">
          {pileZone("Out of Play", "OutOfPlay", outOfPlay, playerId === you ? "yours only · moves to Used at end of turn" : "theirs · moves to Used at end of turn")}
        </div>
        <div className="mat-slot mat-tray">
          <div className="tray">
            {bagTray(bag)}
            {trayItem("Drawn This Turn", drawn.length)}
            {trayItem("Carried From Prep", carried.length)}
          </div>
        </div>
      </div>
    );

    // Collapsed stand-in for `mat` when this is the opponent's board and
    // it isn't their turn - direct feedback (2026-09-08): "the main
    // thing I would want to know about my opponent's mat on my turn,
    // aside from the characters in their field zone, is if they have
    // any energy in their Reserve Pool to spend on globals... the
    // opponent's mat could probably be compressed to be much shorter."
    // Used Pile/Out of Play/Prep Area/the Bag tray all drop out entirely
    // here - none of them matter to a decision you'd make on your own
    // turn, unlike Field Zone (what you're attacking/being blocked by)
    // and Reserve Pool (energy they could spend on a shared Global).
    const collapsedMat = (
      <div className="mat-collapsed">
        {fieldZone}
        {rolledZone("Reserve Pool", "ReservePool", reserve, true)}
      </div>
    );

    // Always visible for your own board (README's Column 2 roster strip -
    // a player checks "what's left to buy" constantly, so this isn't
    // hidden behind a <details> the way the compact-chip version had
    // it). Portrait cards per README's chosen variant, one per Character
    // (Dice Kingdom's roster is a Champion + 2 Characters, not Dice
    // Masters' 8, so this reads as a short strip rather than the
    // reference's full row). The opponent's own board collapses this
    // row behind a tap instead - see the `isOpponentBoard` branch below.
    const isOpponentBoard = playerId !== you;
    const rosterRow = (
      <div className="roster-row">
          {unpurchasedByCard.size === 0 && <span style={{ opacity: 0.5, fontSize: 12 }}>nothing left to buy</span>}
          {[...unpurchasedByCard.entries()].map(([cardId, dice]) => {
            const card = cardsById.get(cardId);
            const Avatar = CHARACTER_ICONS[cardId];
            const dieId = dice[0].id;
            const energyType = card?.energyTypes[0] ?? "Wild";
            const canPurchaseNow = isYourTurn && playerId === you && step === "main";
            const picked = selection.primary === dieId;
            const detailOpen = openCardId === cardId;
            return (
              // Not a <button> disabled outside Purchase's own window -
              // direct feedback (2026-09-07): viewing a card's ability
              // and stats has to work all game, not just when it's your
              // Main step. The actual purchase click moved into a real
              // button inside the popover below, which IS gated on
              // canPurchaseNow.
              <div key={cardId} className="roster-chip-wrap">
                <button
                  type="button"
                  className={`roster-chip${detailOpen ? " open" : ""}${picked ? " picked" : ""}`}
                  style={accent ? ({ ["--cc" as string]: accent } as const) : undefined}
                  onClick={() => setOpenCardId((c) => (c === cardId ? null : cardId))}
                >
                  {Avatar && <Avatar size={18} />}
                  <span className="rc-name">{card?.name ?? cardId}</span>
                  <span className="rc-cost">
                    {card?.purchaseCost} <CostIcon energyType={energyType} />
                  </span>
                  <span className="rc-left">×{dice.length} left</span>
                </button>
                {detailOpen && (
                  // Always down, not away-from-mat - direct feedback
                  // (2026-09-09): the opponent's roster sits at the very
                  // TOP of the page (roster-then-mat), so the previous
                  // "away from the mat" rule opened it upward straight
                  // off the top of the screen, invisible. Down still
                  // covers their Reserve Pool, same as it would have
                  // before - "that one is probably fine to show up
                  // underneath, since you won't be needing to click in
                  // the opponent's Reserve Pool while reading a character
                  // info." Your own roster (mat-then-roster, at the
                  // bottom) still has nothing below it either way.
                  <div className="card-popover down">
                    <div className="card-popover-head">
                      {Avatar && <Avatar size={28} />}
                      <div>
                        <div className="card-popover-name">{card?.name ?? cardId}</div>
                        <div className="card-popover-cost">
                          Cost {card?.purchaseCost} <CostIcon energyType={energyType} />
                        </div>
                      </div>
                    </div>
                    <div className="card-popover-levels">
                      {card?.levels.map((level, i) => (
                        <div className="card-popover-level-row" key={i}>
                          <span className="lvl-label">L{i + 1}</span>
                          <span className="lvl-stats">{level.attack}A / {level.defense}D</span>
                          <span className="lvl-cost">
                            {level.fieldingCost} <CostIcon energyType={energyType} />
                          </span>
                        </div>
                      ))}
                    </div>
                    {/* Later-Dice-Masters layout (2026-09-07): every
                        Character's other 3 faces are always exactly this -
                        2 double + 1 single energy of its own type - so
                        there's nothing per-card to fetch here. */}
                    <p className="card-popover-energy-note">
                      Plus 2 faces of 2 <CostIcon energyType={energyType} /> and 1 face of 1 <CostIcon energyType={energyType} />
                    </p>
                    <p className="card-popover-text">{card?.rawText}</p>
                    {canPurchaseNow && (
                      <button
                        type="button"
                        className="btn"
                        disabled={selection.primary !== null && selection.primary !== dieId}
                        onClick={() => toggleDie(dieId)}
                      >
                        {picked ? "Selected - pay energy above" : "Select to Purchase"}
                      </button>
                    )}
                  </div>
                )}
              </div>
            );
          })}
      </div>
    );
    const roster = isOpponentBoard ? (
      <div className="roster">
        <button
          type="button"
          className={`roster-collapse-toggle${oppRosterOpen ? " open" : ""}`}
          onClick={() => setOppRosterOpen((o) => !o)}
        >
          <span className="roster-head">Roster</span>
          <span className="roster-collapse-icons">
            {[...unpurchasedByCard.keys()].map((cardId) => {
              const Avatar = CHARACTER_ICONS[cardId];
              return Avatar ? <Avatar key={cardId} size={16} /> : <TardigradeIcon key={cardId} size={16} />;
            })}
          </span>
        </button>
        {oppRosterOpen && rosterRow}
      </div>
    ) : (
      <div className="roster">
        <div className="roster-head">Roster</div>
        {rosterRow}
      </div>
    );

    // The roster sits on the OUTER edge of each board, away from the
    // shared Attack Zone between the two mats - real bug, found by
    // direct feedback: rendered as a fixed mat-then-roster sequence
    // regardless of `mirrored`, it landed BELOW the mat every time,
    // which for the mirrored board is the edge next to Field Zone and
    // the Attack Zone (the mirrored mat's rows run the opposite order -
    // see the .mat.mirrored CSS), not away from it.
    const collapsedOpponent = isOpponentBoard && !isActivePlayer;
    const shownMat = collapsedOpponent ? collapsedMat : mat;
    return (
      <div key={playerId} className={`playerboard${turnClass}`}>
        {mirrored ? (
          <>
            {roster}
            {shownMat}
          </>
        ) : (
          <>
            {shownMat}
            {roster}
          </>
        )}
      </div>
    );
  }

  // A single shared lane between the two mats, matching v1's real Attack
  // Zone/CombatLane.tsx: attackers from BOTH players sit here at once
  // (that's the whole point of it facing across the table), each paired
  // against whatever's blocking it, with the same blue-to-orange divider
  // seam ../CombatLane.tsx draws between the two halves.
  //
  // Used to always render the full lane, even with nothing declared, so
  // it read as "a permanent part of the table" rather than appearing/
  // disappearing through the turn - direct feedback (2026-09-08)
  // reversed that call in the name of compression: "Attack zone can
  // also probably be collapsed until we get to those stages." Collapses
  // to a single-line bar outside the three combat steps UNLESS there's
  // still a real attacker sitting in the zone (a lingering post-combat
  // die before Clean Up processes it) - never hides actual game state,
  // only the empty three-placeholder-column view nobody's using yet.
  function renderAttackZone() {
    const assignments: BlockAssignment[] = Object.entries(blockAssignments)
      .filter((entry): entry is [string, string] => !!entry[1])
      .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId }));
    const hasAttackers = game!.dice.some((d) => d.zone === "AttackZone");
    if (!ATTACK_STEPS.has(step) && !hasAttackers) {
      return <div className="combat-lane-collapsed">Attack Zone</div>;
    }
    return (
      <CombatLane
        dice={game!.dice}
        cardsById={cardsById}
        assignments={assignments}
        nearPlayerId={you}
        selection={selection}
        onGroupClick={(ids) => toggleDie(ids[0])}
        spins={spins}
        turnOffsets={offsets}
        canAssignBlockers={step === "assign-blockers" && !isYourTurn}
        onSlotClick={handleBlockerSlotClick}
      />
    );
  }

  // Direct feedback (2026-09-05): blocking used to be a separate right-
  // hand-pane picker ("click an attacker, then a defender to block it"),
  // not the board itself. Now: click one of your own Field Zone dice
  // (made clickable during this step - see the Field Zone map below),
  // which becomes `selection.primary` through the same shared toggleDie
  // every other selection uses, then click the lane's blocker slot
  // across from whichever attacker you want it to block (CombatLane's
  // onSlotClick, wired above). Reselecting an already-assigned die and
  // clicking a different slot MOVES it rather than double-booking it;
  // clicking a filled slot with nothing selected clears it. Purely
  // local state until "Confirm Blocks" actually submits it - unchanged
  // from before, only how it gets built changed.
  function handleBlockerSlotClick(attackerDieId: string) {
    if (selection.primary) {
      const die = game!.dice.find((d) => d.id === selection.primary);
      if (die && die.controllerId === you && die.zone === "FieldZone") {
        const blockerId = selection.primary;
        setBlockAssignments((prev) => {
          const next: Record<string, string | null> = {};
          for (const [aid, bid] of Object.entries(prev)) next[aid] = bid === blockerId ? null : bid;
          next[attackerDieId] = blockerId;
          return next;
        });
      }
      clearSelection();
    } else if (blockAssignments[attackerDieId]) {
      setBlockAssignments((prev) => ({ ...prev, [attackerDieId]: null }));
    }
  }

  const link = inviteLink(game.gameId);

  // The contextual action available for whatever's currently selected -
  // computed once, the same way ../ActionTray.tsx builds its `actions`
  // list from the primary die's zone and the current step, instead of a
  // different bespoke panel per feature.
  function selectionAction(): { label: string; run: () => Promise<GameState>; rolledIds?: string[] } | null {
    if (!primaryDie) return null;
    const secondaryIds = selection.secondary;
    if (step === "roll-and-reroll" && (primaryDie.zone === "PrepArea" || primaryDie.zone === "ReservePool")) {
      const ids = [primaryDie.id, ...secondaryIds];
      return {
        label: `Reroll (${ids.length})`,
        rolledIds: ids,
        run: async () => {
          const next = await api.reroll(game!.gameId, ids);
          setRerolledIds((r) => [...r, ...ids]);
          return next;
        },
      };
    }
    if (step === "main" && primaryDie.zone === "Unpurchased") {
      return { label: "Purchase", run: () => api.purchase(game!.gameId, primaryDie.id, secondaryIds) };
    }
    if (step === "main" && primaryDie.zone === "ReservePool" && rolled(primaryDie) && primaryDie.effectiveAttack !== null) {
      return { label: "Field", run: () => api.field(game!.gameId, primaryDie.id, secondaryIds) };
    }
    return null;
  }
  const action = selectionAction();
  const cost = primaryDie ? costFor(primaryDie) : null;
  const spent = primaryDie
    ? selection.secondary.reduce((sum, id) => sum + (game.dice.find((d) => d.id === id)?.energyAmount ?? 0), 0)
    : 0;

  // Whatever the current step needs from the player - pulled out of the
  // rail's JSX into a plain value (2026-09-08) so it can be placed EITHER
  // inline next to the step title (the common case: one or two buttons,
  // or a short "waiting on..." note - "combine the turn name and the
  // button on the same line, so the whole turn control box is one thin
  // line") OR on its own line below it (stepContentIsPanel: real,
  // situational instructions - a pending-choice picker, or the Assign
  // Blockers paragraph - that can't honestly fit on one line and aren't
  // the generic per-step reminder text NowInfoButton now hides).
  const stepContentIsPanel =
    (!!game.pendingChoice && you === game.pendingChoice.controllerId) || (step === "assign-blockers" && !isYourTurn);
  const stepContent =
    game.pendingChoice && you === game.pendingChoice.controllerId ? (
      <div className="panel">
        <p>
          <b>{game.pendingChoice.description}</b>
        </p>
        <PendingChoiceChips
          candidateIds={game.pendingChoice.candidateIds}
          max={game.pendingChoice.maxCount}
          dice={game.dice}
          cardsById={cardsById}
          onSubmit={(ids) => run(() => api.resolvePendingChoice(game.gameId, ids))}
        />
      </div>
    ) : game.pendingChoice ? (
      <span className="now-bar-note">Waiting on the other player's choice…</span>
    ) : step === "assign-blockers" && !isYourTurn ? (
      <div className="panel">
        <p>
          <b>Assign blockers.</b> Click one of your Field Zone dice below, then
          click the open slot across from the attacker you want it to block.
          Click a filled slot again (nothing selected) to clear it. Anything
          left unblocked hits you directly.
        </p>
        <button
          className="btn"
          disabled={busy}
          onClick={() => {
            const assignments = Object.entries(blockAssignments)
              .filter(([, v]) => v)
              .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId: blockerDieId! }));
            run(() => api.declareBlockers(game.gameId, assignments));
          }}
        >
          Confirm Blocks
        </button>
      </div>
    ) : step === "assign-blockers" && isYourTurn ? (
      <span className="now-bar-note">Waiting on the other player to assign blockers…</span>
    ) : step === "action-global-window" && isYourTurn ? (
      <button
        className="btn"
        disabled={busy}
        onClick={() =>
          run(() =>
            api.assignCombatDamage(
              game.gameId,
              Object.entries(blockAssignments)
                .filter(([, b]) => b)
                .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId: blockerDieId! })),
            ),
          )
        }
      >
        Resolve Combat
      </button>
    ) : !isYourTurn ? (
      <span className="now-bar-note">Waiting on the other player…</span>
    ) : (
      <div className="actionrow">
        {step === "start-of-turn" && (
          <button className="btn" disabled={busy} onClick={() => run(() => api.clearAndDraw(game.gameId))}>
            Draw
          </button>
        )}

        {step === "roll-and-reroll" && !diceFor(you).some((d) => (d.zone === "PrepArea" || d.zone === "ReservePool") && rolled(d)) && (
          <button className="btn" disabled={busy} onClick={() => run(() => api.roll(game.gameId))}>
            Roll
          </button>
        )}
        {step === "roll-and-reroll" && diceFor(you).some((d) => (d.zone === "PrepArea" || d.zone === "ReservePool") && rolled(d)) && (
          <>
            {action && (
              <button className="btn" disabled={busy} onClick={() => run(action.run, action.rolledIds)}>
                {action.label}
              </button>
            )}
            {/* Shortened from "Continue to Main Phase" (2026-09-10) so it
                fits beside the Reroll button on one line - the step title
                right next to this already says "Roll & Reroll," so
                "Continue" alone still reads as "move on from this step." */}
            <button className="btn ghost" disabled={busy} onClick={() => run(() => api.finishRoll(game.gameId))}>
              Continue
            </button>
          </>
        )}

        {step === "main" && (
          <>
            {primaryDie && action && cost && (
              <span style={{ alignSelf: "center", fontSize: 13 }}>
                {action.label} {cost.amount > 0 ? `— cost ${cost.amount}${cost.matchType ? ` ${cost.matchType}` : ""} (${spent}/${cost.amount} selected)` : "— free"}
              </span>
            )}
            {action && (
              <button className="btn" disabled={busy || (cost !== null && spent < cost.amount)} onClick={() => run(action.run, action.rolledIds)}>
                {action.label}
              </button>
            )}
            {primaryDie && (
              <button className="btn ghost" disabled={busy} onClick={clearSelection}>
                Cancel
              </button>
            )}
            {!primaryDie && (
              <button className="btn" disabled={busy} onClick={() => run(() => api.enterAttackStep(game.gameId))}>
                Attack
              </button>
            )}
            {/* Ported from ../App.tsx's identical "Clean Up (skip attack)
                ▶" - dropped during the redesign, direct feedback
                (2026-09-05) asked for it back. The server
                (TurnEngine.SkipAttackStep) still rejects this with a real
                error if a forced attacker is outstanding; no client-side
                gating needed beyond the step check. Shortened from
                "Proceed to Attack"/"Skip Attack Step" (2026-09-10) so this
                pair fits on one line, same reason as Roll & Reroll's
                Reroll/Continue pair. */}
            {!primaryDie && (
              <button className="btn ghost" disabled={busy} onClick={() => run(() => api.skipAttackStep(game.gameId))}>
                Skip Attack
              </button>
            )}
          </>
        )}

        {step === "select-attackers" && (
          <button
            className="btn"
            disabled={busy}
            onClick={() =>
              run(() => api.declareAttackers(game.gameId, primaryDie ? [primaryDie.id, ...selection.secondary] : []))
            }
          >
            Confirm Attackers ({primaryDie ? 1 + selection.secondary.length : 0})
          </button>
        )}

        {step === "return-to-field" && (
          <button className="btn" disabled={busy} onClick={() => run(() => api.cleanUp(game.gameId))}>
            End Turn
          </button>
        )}
      </div>
    );

  const oppPlayer = opponentId === game.playerOne.id ? game.playerOne : game.playerTwo;
  const yourPlayer = you === game.playerOne.id ? game.playerOne : game.playerTwo;

  return (
    <div className="dicekingdom">
      {/* EnergyBadge's outline (2026-09-09, replacing a 4-direction
          drop-shadow stack that read as "muddy" rather than crisp) -
          feMorphology dilates the icon's own alpha silhouette (including
          any internal cutout details, like Shell's spine line) into a
          hard-edged mask, then fills that mask solid and merges the real
          icon on top - a genuinely crisp outline, not a blurred one.
          Rendered once here (referenced via CSS `filter: url(#...)`),
          not per-badge - an SVG filter def only needs to exist once per
          document regardless of how many elements point at it. */}
      <svg width="0" height="0" style={{ position: "absolute" }} aria-hidden="true">
        <defs>
          <filter id="energy-badge-outline" x="-50%" y="-50%" width="200%" height="200%">
            <feMorphology operator="dilate" radius="1.1" in="SourceAlpha" result="dilated" />
            <feFlood floodColor="#1a1006" floodOpacity="0.9" result="black" />
            <feComposite in="black" in2="dilated" operator="in" result="outline" />
            <feMerge>
              <feMergeNode in="outline" />
              <feMergeNode in="SourceGraphic" />
            </feMerge>
          </filter>
        </defs>
      </svg>
      {error && <p className="error">{error}</p>}

      <Scoreboard opponent={oppPlayer} mine={yourPlayer} />

      {/* Once a game is live, /game shows almost no chrome above the
          table at all - title/description/How-to-Play live only on the
          pre-game screen or behind a small toggle, never as a persistent
          block. Direct feedback: the old title+description+How-to-play+
          topbar stack here was costing real vertical space /game never
          spends once a match starts. Text buttons ("Dark mode"/"How to
          play") swapped for icon+popover ones (2026-09-08 direct
          feedback) so they can share the ribbon's row instead of
          claiming their own. */}
      <div className="dk-titlebar">
        <StepRibbon game={game} />
        <div className="dk-titlebar-right">
          <HowToPlayMenu />
          <SettingsMenu theme={theme} setTheme={setTheme} />
        </div>
      </div>

      {/* README's real 3-column shape: a shared sideboard, the table
          (opponent board / combat lane / your board, each its own grid
          row), and a rail sharing those SAME three rows - see
          .dk-layout's CSS comment for why sharing rows is what aligns
          each Champion box with its board without any JS measuring. */}
      <div className="dk-layout">
        <div className="dk-sideboard">
          <div className="sideboard-panel">
            <h4>Basic Actions</h4>
            <span className="sideboard-sub">shared pool · both may buy</span>
            <p className="sideboard-empty">Dice Kingdom has no Basic Actions yet.</p>
          </div>
          <div className="sideboard-panel">
            <h4>Global Abilities</h4>
            <span className="sideboard-sub">either player, any window</span>
            <p className="sideboard-empty">No Globals designed yet.</p>
          </div>
        </div>

        <div className="dk-row-opp" ref={oppRowRef}>{renderBoard(opponentId, true)}</div>
        <div className="dk-row-lane">{renderAttackZone()}</div>
        <div className="dk-row-you" ref={yourRowRef}>{renderBoard(you, false)}</div>

        <div className="dk-rail-top">
          {/* Active + Invite on one line - ../TurnRail.tsx's own shape.
              Life totals used to sit in their own grid here (LifeBox);
              merged into ChampionBox below instead (2026-09-08 direct
              feedback). */}
          <div className="active-line">
            <span className={isYourTurn ? "whose-turn mine" : "whose-turn waiting"}>
              <strong>Active:</strong> {game.activePlayerId}
            </span>
            {link && <InviteRow link={link} />}
          </div>
          <ChampionBox
            player={oppPlayer}
            isActivePlayer={game.activePlayerId === opponentId}
            you={you}
          />
        </div>

        <div className="dk-rail-mid">
          <div className="controlcenter">
            {/* Title + info icon + (usually) the action button(s), all on
                one line - direct feedback (2026-09-08): "combine the turn
                name and the button on the same line, so that the whole
                turn control box is one thin line." The per-step reminder
                text lives behind NowInfoButton now instead of printed
                underneath (same feedback). A genuine situational panel
                (a pending-choice picker, the Assign Blockers paragraph -
                stepContentIsPanel, computed above) still needs its own
                line below; everything else - the common case - fits here. */}
            <div className="now-bar">
              {STEP_GUIDANCE[step] && (
                <>
                  <NowInfoButton text={STEP_GUIDANCE[step].text} />
                  <span className="now-bar-title">{STEP_GUIDANCE[step].title}</span>
                </>
              )}
              {!stepContentIsPanel && <div className="now-bar-actions">{stepContent}</div>}
            </div>
            {stepContentIsPanel && <div className="now-panel-scroll">{stepContent}</div>}
          </div>
        </div>

        <div className="dk-rail-bottom">
          <ChampionBox
            player={yourPlayer}
            isActivePlayer={game.activePlayerId === you}
            you={you}
          />
          {/* Moved off the sideboard and onto your own rail, right under
              your Champion box - direct feedback (2026-09-09): this is
              specifically YOUR energy, sitting right above the log that
              already tracks everything you've done with it.
              Shown only during Main (2026-09-08 direct feedback):
              "'Energy in your pool' is only helpful if it's visible when
              I'm purchasing a character" - every other step it's just
              permanent clutter, so it's gone entirely outside the one
              step where spending it is actually on the table. */}
          {step === "main" && (
            <div className="sideboard-panel">
              <h4>Energy in your pool</h4>
              {(() => {
                const yourEnergy = diceFor(you, "ReservePool").filter((d) => d.energySymbolId);
                if (yourEnergy.length === 0) return <p className="sideboard-empty">nothing to spend</p>;
                const total = yourEnergy.reduce((sum, d) => sum + d.energyAmount, 0);
                return (
                  // A running total, separated from the individual pips by
                  // a vertical divider - direct feedback (2026-09-05): the
                  // pip list alone didn't say "how much do I actually
                  // have" at a glance.
                  <div className="sideboard-pool-row">
                    <div className="sideboard-pool">
                      {yourEnergy.map((d) => (
                        <PipBadge key={d.id} type={d.energySymbolId!} amount={d.energyAmount} />
                      ))}
                    </div>
                    <span className="pool-divider" />
                    <span className="pool-total" title={`${total} total energy`}>
                      {total}
                    </span>
                  </div>
                );
              })()}
            </div>
          )}
          <MatchLog entries={game.log} nearPlayerId={you} />
        </div>
      </div>
    </div>
  );
}

function PendingChoiceChips({
  candidateIds,
  max,
  dice,
  cardsById,
  onSubmit,
}: {
  candidateIds: string[];
  max: number;
  dice: Die[];
  cardsById: Map<string, { name: string }>;
  onSubmit: (ids: string[]) => void;
}) {
  const [picked, setPicked] = useState<string[]>([]);
  return (
    <>
      <div className="chiprow">
        {candidateIds.map((id) => {
          const die = dice.find((d) => d.id === id);
          const name = die?.cardId ? (cardsById.get(die.cardId)?.name ?? die.cardId) : "Tardigrade";
          return (
            <span
              key={id}
              className={`chip${picked.includes(id) ? " on" : ""}`}
              onClick={() =>
                setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : p.length < max ? [...p, id] : p))
              }
            >
              {name} {die?.effectiveAttack}/{die?.effectiveDefense}
            </span>
          );
        })}
      </div>
      <button className="btn" disabled={picked.length === 0} onClick={() => onSubmit(picked)}>
        Confirm Choice
      </button>
    </>
  );
}

