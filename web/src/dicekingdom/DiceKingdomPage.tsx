import { startTransition, useEffect, useRef, useState } from "react";
import "./dicekingdom.css";
import { api, apiAs } from "./api";
import { CHAMPION_ICONS, CHARACTER_ICONS, EnergyBadge, HelpIcon, TardigradeIcon } from "./icons";
import { describeSavedGame, forgetSeats, inviteLink, myLink, rememberSeats } from "./seats";
import { GameOverOverlay } from "./GameOverOverlay";
import { OPPONENT_PICKS, PickYourChampion, ResumeGames, WaitingForOpponent, resolveInvite } from "./lobby";
import { ChampionPicker } from "./ChampionPicker";
import { CombatLane } from "./CombatLane";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor } from "./dieFaces";
import { explainRows, tileCues } from "./statusCues";
import { CardDetailPopover } from "./CardDetailPopover";
import { DieFramesLegend, legendSeen } from "./DieFramesLegend";
import { SpinFlash, useSpinFlash } from "./SpinFlash";
import { StepRibbon } from "./StepRibbon";
import { MatchLog } from "./MatchLog";
import { SettingsMenu, ThemeToggle, useTheme } from "./ThemeToggle";
import { useDiceRoll, type RollTarget } from "./useDiceRoll";
import { useDieFlights } from "./dieFlights";
import { classifyDieMotion, remoteRolledIds } from "./dieMotion";
import { activeCouldAct, botDecisionCall, decisionOwner, rolled } from "./bot";
import { basicActionStock, executeAbility, getAbilityOptions, type AbilityCommand } from "./sharedAbilities";
import { isReservePaymentDie } from "./reservePayment";
import { SharedAbilityPanel } from "./SharedAbilityPanel";
import type { BlockAssignment, BotDecision, CardDef, Die, GameState, LobbyStatus, PlayerState } from "./types";

const POLL_INTERVAL_MS = 2000;
// Pause before each computer-opponent move, so a Main Step full of
// purchases reads as a sequence you can follow rather than one jump-cut.
const BOT_MOVE_DELAY_MS = 2000;
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

// The only zones where a die is actually showing a rolled face (rule
// 1.5, mirrors ../PlayerBoard.tsx's own ROLLED_ZONES) - everywhere else
// a die is unrolled, spent, or sitting on its card, so it's shown as
// plain and collapsible even if the DTO still carries a stale face from
// before it left a rolled zone. Gating this by zone rather than trusting
// effectiveAttack directly is what fixes a spent/KO'd die still showing
// its last rolled stats in the Used Pile.
const ROLLED_ZONES = new Set(["ReservePool", "PrepArea", "FieldZone", "AttackZone", "Intimidated"]);
// Used Pile/Out of Play tiles show only the card/Tardigrade icon (see
// DieTile's own remarks) - shared here so groupDice can group them by
// that same identity alone, ignoring whatever face they happened to be
// on when they left play (see groupDice's own comment).
const ICON_ONLY_ZONES = new Set(["UsedPile", "OutOfPlay", "PrepArea", "DiceFromBag", "DiceFromPrep"]);

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
// green/amber-grey pattern (DESIGN_LOG.md, 2026-09-03).
//
// The old standalone ChampionBox panels (in dk-rail-top/-bottom) and
// the "Active: X" text are both gone now, folded into this one column
// instead (2026-09-09 direct feedback): "move the other stuff from
// that panel into that column as well - the active team text and the
// champions. We may not be able to fit the Champion name... but we
// should be able to fit the Avatar, the symbol, and the energy
// vertically." Name and ability text don't fit a ~54px column, so
// they're a tap away instead (ScoreboardPopover) rather than dropped -
// same "always-visible summary, full detail on demand" split this
// session already used for the per-step reminder text and How to Play.
// "Active" is now a highlight on whichever side is live, not separate
// text - the column's own You/Opp labels already say which side is
// which, so a color cue says the rest.
// The Champion power button's label (2026-10-04) - see the mobile page's twin.
const POWER_LABELS: Record<string, string> = { Wolf: "Pump", Armadillo: "Shield", GreatHornedOwl: "Spin" };

function ScoreboardSide({ player, mine, isActivePlayer, onUsePower }: { player: PlayerState; mine: boolean; isActivePlayer: boolean; onUsePower?: () => void }) {
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
  const champion = player.champion;
  const accent = champion ? `var(--${champion.energySymbolId.toLowerCase()})` : undefined;
  const Icon = champion ? CHAMPION_ICONS[champion.id] : null;
  const turnClass = isActivePlayer ? (mine ? " turn-mine" : " turn-waiting") : "";
  return (
    <div
      ref={wrapRef}
      className={`scoreboard-side${mine ? " mine" : ""}${turnClass}`}
      style={accent ? ({ ["--cc" as string]: accent } as const) : undefined}
    >
      <button type="button" className="scoreboard-tap" onClick={() => setOpen((o) => !o)} disabled={!champion}>
        <span className="scoreboard-label">{mine ? "You" : "Opp"}</span>
        {Icon && <Icon size={26} />}
        {/* Direct feedback (2026-09-05): even once every Champion has a
            real avatar, the energy type still needs to read at a
            glance - a photo alone doesn't carry that the way a color
            glyph did. */}
        {champion && <EnergyBadge type={champion.energySymbolId} size={14} />}
        <span className="scoreboard-life">{player.life}</span>
      </button>
      {onUsePower && champion && (
        <button type="button" className="btn dk-power-btn" onClick={onUsePower} title={champion.passiveText}>
          {champion.name}: {POWER_LABELS[champion.id] ?? "Power"}
        </button>
      )}
      {open && champion && (
        <div className="scoreboard-popover">
          <div className="scoreboard-popover-name">{champion.name}</div>
          {champion.passiveText && <p className="scoreboard-popover-note">{champion.passiveText}</p>}
        </div>
      )}
    </div>
  );
}

// A real fixed column, not just a panel somewhere in the rail - direct
// feedback: "we'd always want the life totals to be visible somewhere,
// I don't want to have to scroll to see if I'm winning or losing," then
// (once a full-width top bar version proved too tall) "vertical space
// will be at a premium... a thin column on the right side." Same
// `position: fixed` technique .dk-rail-mid already uses for the turn
// controls, pinned to the opposite edge on mobile so the two fixed
// pieces don't compete for the same space - see the CSS for the rest
// of that story (and why it's a column, not a bar, there).
function Scoreboard({
  opponent,
  mine,
  opponentActive,
  mineActive,
  onUsePower,
}: {
  onUsePower?: () => void;
  opponent: PlayerState;
  mine: PlayerState;
  opponentActive: boolean;
  mineActive: boolean;
}) {
  return (
    <div className="scoreboard">
      <ScoreboardSide player={opponent} mine={false} isActivePlayer={opponentActive} />
      <ScoreboardSide player={mine} mine={true} isActivePlayer={mineActive} onUsePower={onUsePower} />
    </div>
  );
}

// The game's links, folded behind one small toggle (user, 2026-10-08:
// "it's unlikely to be used very often, so it could require a click to get
// to"). Invite = the other seat's link; Your seat = a link back into your
// own side, to keep or to carry on from another device.
function GameLinks({ invite, own }: { invite: string | null; own: string | null }) {
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState<"invite" | "own" | null>(null);
  async function copy(url: string, which: "invite" | "own") {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(which);
      window.setTimeout(() => setCopied(null), 2000);
    } catch {
      // Clipboard blocked - the link is still in the button's tooltip.
    }
  }
  return (
    <div className="invite-row">
      <button type="button" className="invite-row-button" onClick={() => setOpen((v) => !v)}>
        Game links {open ? "▾" : "▸"}
      </button>
      {open && invite && (
        <button type="button" className="invite-row-button" title={invite} onClick={() => copy(invite, "invite")}>
          {copied === "invite" ? "Copied!" : "Copy invite"}
        </button>
      )}
      {open && own && (
        <button type="button" className="invite-row-button" title={own} onClick={() => copy(own, "own")}>
          {copied === "own" ? "Copied!" : "Copy your seat's link"}
        </button>
      )}
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
  fieldPrompt,
  onStartField,
  actionPrompt,
  onStartAction,
  choiceActive,
  targetable,
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
  /** In Main, choosing a creature exposes Field below its tile before payment starts. */
  fieldPrompt?: boolean;
  onStartField?: () => void;
  /** In Main or an attack action window, an action-face die can be activated here. */
  actionPrompt?: boolean;
  onStartAction?: () => void;
  /** A pending-choice target: highlight in the die's real board position. */
  choiceActive?: boolean;
  targetable?: boolean;
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
  const inPlay = zone === "FieldZone" || zone === "AttackZone" || zone === "Intimidated";
  const cues = inPlay ? tileCues(die, cardsById, mine ?? true) : undefined;
  const cueRows = inPlay ? explainRows(die, mine ?? true) : [];
  const spinFlash = useSpinFlash(die);
  const card = die.cardId ? cardsById.get(die.cardId) : undefined;
  const name = die.cardId ? (card?.name ?? die.cardId) : "Tardigrade";
  const cls = ["dietile", clickable ? "clickable" : "", picked ? "picked" : "", targetable ? "choice-targetable" : "", choiceActive && !targetable ? "choice-inactive" : ""].filter(Boolean).join(" ");
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
  // During selection, left-click still selects the die. Right-click always
  // opens the same inspector used by the roster without changing selection.
  const canShowInfo = isRolled && !clickable && !choiceActive;
  // Direct feedback (2026-09-10): "when in 'Out of Play' or 'Used Pile'
  // rather than take up space with the word we should just put the
  // character symbol... just the symbol, though, no stats or energy."
  // Unlike Bag/Drawn/Carried, these two piles accumulate the most dice
  // over a game, so they're the ones that actually benefit from
  // dropping the text row.
  const iconOnly = ICON_ONLY_ZONES.has(zone);
  return (
    <div ref={wrapRef} className={`dietile-wrap${showInfo ? " info-open" : ""}${targetable ? " choice-targetable" : ""}`}>
      <button
        type="button"
        className={cls}
        data-fly-id={ROLLED_ZONES.has(zone) || zone === "DiceFromBag" || zone === "DiceFromPrep" ? `die:${die.id}` : undefined}
        onClick={clickable ? onClick : canShowInfo ? () => setShowInfo((v) => !v) : undefined}
        onContextMenu={(e) => {
          e.preventDefault();
          setShowInfo((v) => !v);
        }}
        title={clickable && isRolled ? "Left-click to select; right-click for card details" : "Right-click for card details"}
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
              cues={cues}
            />
            <SpinFlash flash={inPlay ? spinFlash : null} />
            {label && <div className="lbl">{label}</div>}
            {/* Status cues (2026-10-03): the top cue's word, in the slot
                "Must block" used to have. Click the die for why. */}
            {cues?.word && <div className="lbl dk-cue-word">{cues.word}</div>}
            {/* Damage marked on a die in play - see the mobile page's
                identical badge (2026-09-30). */}
            {(die.damage ?? 0) > 0 && (zone === "FieldZone" || zone === "AttackZone") && (
              <span key={die.damage} className="dk-damage-badge" title={`${die.damage} damage marked - clears at the end of the turn`}>
                −{die.damage}
              </span>
            )}
          </>
        )}
      </button>
      {fieldPrompt && onStartField && (
        <button
          type="button"
          className="dk-tile-field-start"
          onClick={(event) => { event.stopPropagation(); onStartField(); }}
        >
          Field
        </button>
      )}
      {actionPrompt && onStartAction && (
        <button
          type="button"
          className="dk-tile-field-start"
          onClick={(event) => { event.stopPropagation(); onStartAction(); }}
        >
          Use
        </button>
      )}
      {showInfo && (
        <CardDetailPopover
          card={card}
          die={die}
          cueRows={cueRows}
          placement={mine === false ? "down" : "up"}
        />
      )}
    </div>
  );
}

// The live board's compact stand-in for the old <details className="how">
// text toggle - same content, an icon+popover instead so it can share the
// ribbon's row (see SettingsMenu in ThemeToggle.tsx, the same pattern,
// same reason).
function HowToPlayMenu({ onOpenLegend }: { onOpenLegend: () => void }) {
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
          <button
            type="button"
            className="btn"
            onClick={() => {
              setOpen(false);
              onOpenLegend();
            }}
          >
            Die frames
          </button>
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
  const flightRootRef = useRef<HTMLDivElement>(null);
  const yourRowRef = useRef<HTMLDivElement>(null);
  const [setupA, setSetupA] = useState<string | null>(null);
  const [setupB, setSetupB] = useState<string | null>(null);
  // Player Two becomes a basic rule-based opponent instead of a second
  // human seat - see bot.ts. Fixed to player two rather than "whichever
  // seat I didn't claim" because vs-computer games never go through the
  // invite-link claim flow at all (both seats' tokens stay in this one
  // browser, same as ordinary pass-and-play - see api.ts's rememberSeats
  // call in startMatch below).
  const [vsComputer, setVsComputer] = useState(false);
  const gameRef = useRef<GameState | null>(null);
  useEffect(() => {
    gameRef.current = game;
  }, [game]);
  const botActingRef = useRef(false);
  // Unpurchased/fieldable dice the bot tried and had rejected this turn
  // (a legality rule bot.ts doesn't model, e.g. a lockout ability) - reset
  // each time a new turn starts so it isn't permanently blacklisted.
  const botSkipIdsRef = useRef<Set<string>>(new Set());
  useEffect(() => {
    botSkipIdsRef.current = new Set();
  }, [game?.activePlayerId]);

  const [selection, setSelection] = useState<Selection>(EMPTY_SELECTION);
  // The server owns legal pending-choice candidates. This state holds only
  // local selections; clicking dice on the board never changes normal game selection.
  const [choicePicked, setChoicePicked] = useState<string[]>([]);
  const choiceKey = game?.pendingChoice
    ? `${game.gameId}|${game.pendingChoice.controllerId}|${game.pendingChoice.intent}|${game.pendingChoice.description}|${game.pendingChoice.candidateIds.join(",")}`
    : null;
  useEffect(() => setChoicePicked([]), [choiceKey]);
  // Desktop fielding starts from the selected creature's own Field button;
  // energy dice are not selectable until the player requests payment.
  const [fieldPaymentDieId, setFieldPaymentDieId] = useState<string | null>(null);
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
  // Bot heartbeat closes over its first render - read live pairings via ref.
  const blockAssignmentsRef = useRef(blockAssignments);
  blockAssignmentsRef.current = blockAssignments;
  const [cardsById, setCardsById] = useState<Map<string, CardDef>>(new Map());
  // The dice-cube roll animation - ported verbatim from ../useDiceRoll.ts.
  // README calls this "the single most important piece to port
  // faithfully" - see animateRolledDice below for how a roll is detected.
  const { spins, offsets, launch: launchRoll, spinTo: spinDie } = useDiceRoll();
  const [bagOpen, setBagOpen] = useState(false);
  const [oppBagOpen, setOppBagOpen] = useState(false);
  // Which pile's inspector popover is open in the collapsed opponent mat
  // (see miniPile below) - a single id rather than per-zone booleans
  // since collapsedMat only ever renders for one board at a time and
  // only one pile's contents are worth showing at once.
  const [collapsedZoneOpen, setCollapsedZoneOpen] = useState<string | null>(null);
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
  // Status cues' first-time legend - see the mobile page's twin.
  const [legendOpen, setLegendOpen] = useState(false);
  const anyCue = !!game?.dice.some((d) => (d.statuses?.length ?? 0) > 0);
  useEffect(() => {
    if (anyCue && !legendSeen()) setLegendOpen(true);
  }, [anyCue]);

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

  // Invite-link join: straight into the game, or - if the host opened it
  // with only their own Champion - a pick first (lobby.tsx).
  const [waiting, setWaiting] = useState<{ gameId: string; hostChampionId: string } | null>(null);
  const [invitePick, setInvitePick] = useState<LobbyStatus | null>(null);
  useEffect(() => {
    resolveInvite()
      .then((r) => {
        if (r?.kind === "game") setGame(r.game);
        else if (r?.kind === "pick") setInvitePick(r.lobby);
      })
      .catch((e) => setError(`Could not join that game: ${e instanceof Error ? e.message : String(e)}`));
  }, []);

  // Poll for the other player's moves - same version-compare shape as v1.
  const gameId = game?.gameId ?? null;

  // Label this game in the browser's saved list (seats.ts), for "Resume a game".
  const savedLabel = game ? `${game.playerOne.champion?.name ?? game.playerOne.name} vs ${game.playerTwo.champion?.name ?? game.playerTwo.name}` : null;
  useEffect(() => {
    if (gameId && savedLabel) describeSavedGame(gameId, savedLabel, vsComputer);
  }, [gameId, savedLabel, vsComputer]);
  const gameVersion = game?.version ?? 0;
  useEffect(() => {
    if (!gameId) return;
    let cancelled = false;
    const timer = window.setInterval(async () => {
      if (busyRef.current) return;
      try {
        const latest = await api.getGame(gameId);
        if (!cancelled && latest.version !== gameVersion) {
          // The other player's move - tumble their roll/reroll rather than
          // just swapping the faces (user request, 2026-09-30).
          const previous = gameRef.current;
          setGame(latest);
          if (previous) requestAnimationFrame(() => animateRolledDice(previous, latest, remoteRolledIds(previous, latest)));
        }
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
    // Goes through apiAs(gameId, owner) - the player REQUIRED to submit
    // this, per decisionOwner - rather than the shared `api` (whichever
    // seat this browser currently "plays as"). Those are the same thing
    // in ordinary two-tab pass-and-play (each tab only ever knows its
    // own token, so apiAs either finds that same token or, on the wrong
    // tab, no token at all - a silent no-op either way, unchanged from
    // before). They're NOT the same in vs-computer mode: this one browser
    // holds both tokens, and whichever of these two steps needs the
    // COMPUTER's token would otherwise always be submitted as the human
    // instead and 403 forever with nothing left to retry it (found via a
    // real playthrough, 2026-09-15 - a human declaring 0 attackers, or an
    // attacking computer whose attack the human leaves entirely
    // unblocked, both landed here and stuck).
    const owner = decisionOwner(game);
    if (!owner) return;
    // The computer's seat answers its own empty steps (bot-decision) -
    // auto-passing its Attack window here would take away its best play,
    // pumping an attacker nobody blocked.
    if (vsComputer && owner === game.playerTwo.id) return;
    const client = apiAs(gameId, owner, true); // automatic moves - marked as such in the game record
    if (game.currentStepId === "assign-blockers" && assignBlockersAttackerCount === 0) {
      runQuiet(() => client.declareBlockers(gameId, []));
    } else if (
      game.currentStepId === "action-global-window" &&
      game.priorityPlayerId === game.activePlayerId &&
      (game.blocks ?? []).length === 0 &&
      // Never while they could still use an action die or pay a Global
      // (user call, 2026-09-28) - e.g. Anger Issues on an unblocked
      // attacker is exactly this window's point.
      !activeCouldAct(game, cardsById)
    ) {
      runQuiet(() => client.assignCombatDamage(gameId, []));
    }
  }, [gameId, game?.version, game?.currentStepId, assignBlockersAttackerCount, blockAssignments]);

  function clearSelection() {
    setSelection(EMPTY_SELECTION);
    setFieldPaymentDieId(null);
  }

  function selectReserveDie(die: Die) {
    const selectedPrimary = game?.dice.find((candidate) => candidate.id === selection.primary) ?? null;
    if (step === "main" && isReservePaymentDie(die, selectedPrimary, fieldPaymentDieId)) {
      // Some character faces show both stats and energy. Once a purchase
      // or Field payment has begun, their energy must take priority over
      // selecting a new creature to field.
      toggleDie(die.id);
      return;
    }
    if ((step === "main" || step === "action-global-window") && die.zone === "ReservePool" && die.isActionFace) {
      // Action-face dice are not creatures. Select them for their Use
      // action without entering the fielding or payment workflows.
      setFieldPaymentDieId(null);
      setSelection((previous) => previous.primary === die.id ? EMPTY_SELECTION : { primary: die.id, secondary: [] });
      return;
    }
    if (step === "main" && die.zone === "ReservePool" && rolled(die) && die.effectiveAttack !== null) {
      // Outside a payment workflow, choosing another creature changes
      // the primary and exposes that creature's own Field button.
      setFieldPaymentDieId(null);
      setSelection((previous) => previous.primary === die.id ? EMPTY_SELECTION : { primary: die.id, secondary: [] });
      return;
    }
    toggleDie(die.id);
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
  // Dice the ACTIVE player just rolled or rerolled, between two states that
  // arrived from elsewhere (a poll, or the computer's own move) - they
  // tumble like your own; see ./DiceKingdomMobilePage.tsx's identical
  // helper, which also holds its tray on screen through a reroll.


  function animateRolledDice(previous: GameState, next: GameState, rolledDieIds?: string[]) {
    const { tumbles, flips } = classifyDieMotion(previous, next, rolledDieIds);
    const byId = new Map(next.dice.map((d) => [d.id, d]));
    const before = new Map(previous.dice.map((d) => [d.id, d]));
    const rolledTargets: RollTarget[] = [];
    const spunTargets: RollTarget[] = [];
    for (const id of [...tumbles, ...flips]) {
      const die = byId.get(id)!;
      const was = before.get(id)!;
      const { index } = facesFor(die, cardsById);
      // What the die showed before the roll stays up until it's in the air.
      const held = {
        face: facesFor(was, cardsById).faces[0],
        energy: was.energySymbolId && was.energyAmount > 0 ? { type: was.energySymbolId, amount: was.energyAmount } : undefined,
      };
      (tumbles.includes(id) ? rolledTargets : spunTargets).push({ dieId: id, faceIndex: index, held });
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
      // Split across two commits/frames (2026-09-16, direct feedback:
      // "the dice rolling seems a bit choppy... both on mobile and on
      // desktop") - see ../DiceKingdomMobilePage.tsx's identical run()
      // for the real, measured cause (a Chrome trace during a roll, not
      // a guess) and why this is two changes, not one: setGame(next)
      // re-renders the WHOLE page (a CDP trace showed a single Layout
      // pass touching 329 of 462 DOM nodes, React's own scheduler
      // blocking the main thread for 40-80ms in one chunk) - calling
      // animateRolledDice in the SAME tick used to bundle starting the
      // tumble's CSS animation into that exact same expensive commit.
      // startTransition lets React chunk its own reconciliation instead
      // of blocking in one piece; the rAF defers the animation start to
      // the next frame, after the data commit has already had a frame
      // to settle. Confirmed with a rAF frame-timing probe across
      // several runs, not just by eye.
      // Tumble first, data second (2026-10-03): the rAF'd animation used
      // to start a frame after the data commit, so a reroll briefly
      // showed its landed face before spinning - see the mobile page's
      // run() for the full remarks.
      if (previous) animateRolledDice(previous, next, rolledDieIds);
      startTransition(() => setGame(next));
      clearSelection();
      setOpenCardId(null); // e.g. a completed Purchase - see openCardId's own remarks
      if (next.currentStepId !== "roll-and-reroll") setRerolledIds([]);
      if (next.currentStepId !== "assign-blockers" && next.currentStepId !== "action-global-window") setBlockAssignments({});
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
      const raw = await fn();
      // In vs-computer mode this may have gone out via apiAs as the
      // computer's own seat (the auto-skip effect above) - the human is
      // always Player One there, so undo the resulting yourPlayerId flip
      // the same way runBot does; see its own remarks for why. A no-op
      // in ordinary pass-and-play, where this always was the human's own
      // token/response already.
      const next = vsComputer ? { ...raw, yourPlayerId: raw.playerOne.id } : raw;
      setGame(next);
    } catch {
      // Expected on whichever browser doesn't hold the seat this
      // particular auto-skip needed.
    } finally {
      busyRef.current = false;
    }
  }

  // Same shape as run(), for the computer opponent - failures here are
  // routine (see performBotAction's own remarks on why a scheduled bot
  // action can find itself stale by the time it actually fires) and
  // shouldn't flash the human-facing error banner or crash as an
  // unhandled rejection the way run()'s own rethrow would; console.warn
  // is enough of a trail if the bot is genuinely stuck. Returns null on
  // failure so callers can tell "nothing changed" apart from a real
  // GameState without needing try/catch of their own.
  async function runBot(fn: () => Promise<GameState>): Promise<GameState | null> {
    if (busyRef.current) return null;
    setBusy(true);
    busyRef.current = true;
    try {
      const previous = gameRef.current;
      const raw = await fn();
      // apiAs(gid, botId) means the response reflects the COMPUTER's own
      // seat (V2GamesController.Result sets yourPlayerId from whichever
      // token the request carried) - the human is always Player One in
      // vs-computer mode (see the vsComputer state var's own remarks), so
      // patch it back rather than let "You"/"Opp" swap on screen for
      // however long until the next poll or human action happens to use
      // the human's own token again and self-correct it.
      const next = { ...raw, yourPlayerId: raw.playerOne.id };
      setGame(next);
      if (previous) animateRolledDice(previous, next, remoteRolledIds(previous, next));
      return next;
    } catch (e) {
      console.warn("[bot] action failed, skipping:", e);
      return null;
    } finally {
      setBusy(false);
      busyRef.current = false;
    }
  }

  // One step of the computer opponent's turn - see bot.ts for the actual
  // decisions. Re-reads gameRef.current (rather than trusting whatever
  // GameState triggered the effect below) because this runs after a
  // deliberate pacing delay, during which a poll or another action may
  // already have moved the game past the step this was scheduled for;
  // re-checking decisionOwner keeps a stale timer from firing a now-
  // illegal action instead of just quietly doing nothing.
  async function performBotAction(botId: string) {
    const g = gameRef.current;
    if (!g || decisionOwner(g) !== botId) return;
    const gid = g.gameId;
    // NOT the shared `api` - this browser only ever holds ONE seat's
    // token as its "current" identity (seats.ts's tokenFor), the human's,
    // same as any other pass-and-play session. Acting as the computer
    // needs Player Two's own token instead, without touching that shared
    // identity out from under the human's next click - see apiAs's own
    // remarks. (Found the hard way, 2026-09-15: every bot action 403'd
    // with "It is not your turn" until this existed - `api` alone can
    // never act as a seat this browser hasn't selected.)
    const botApi = apiAs(gid, botId);
    let decision: BotDecision | null;
    try {
      decision = await botApi.botDecision(gid, botSkipIdsRef.current);
    } catch (e) {
      console.warn("[bot] couldn't fetch a decision, retrying next tick:", e);
      return;
    }
    if (!decision) return;
    const d = decision;
    if (d.kind === "declareBlockers") {
      // Mirrors a human's own block bookkeeping, so the pairing shows on
      // the board while the Action/Global window is open.
      const map: Record<string, string | null> = {};
      for (const a of d.assignments) map[a.attackerDieId] = a.blockerDieId;
      setBlockAssignments(map);
    }
    const result = await runBot(() => botDecisionCall(botApi, gid, d));
    if (result) return;
    // Rejected (a rule the bot doesn't model) or stale by the time it
    // fired: rule a die out for the rest of this turn, or fall back to
    // the empty declaration, instead of retrying the same thing forever.
    if (d.dieId) botSkipIdsRef.current.add(d.dieId);
    else if (d.kind === "declareAttackers") await runBot(() => botApi.declareAttackers(gid, []));
    else if (d.kind === "declareBlockers") {
      setBlockAssignments({});
      await runBot(() => botApi.declareBlockers(gid, []));
    }
  }


  // A heartbeat, not a one-shot scheduled off `game`'s own dependencies -
  // deliberately so. An earlier version rescheduled itself purely by
  // reacting to game?.version changing, which seemed right (every real
  // move changes the version, so each move re-triggers the next) but
  // deadlocked the very first time runBot's own action failed: a no-op
  // failure leaves `game` completely unchanged, so nothing would ever
  // fire the effect again and the computer's turn just stopped forever
  // (caught 2026-09-15 via a real Playwright playthrough - the board
  // froze after exactly one "It is not your turn" console warning).
  // Polling on an interval instead means a failed attempt just gets
  // tried again next tick, same as this file's own poll-for-the-other-
  // player's-moves effect above already does for the opposite direction.
  useEffect(() => {
    if (!vsComputer || !gameId) return;
    const timer = window.setInterval(() => {
      if (botActingRef.current || busyRef.current) return;
      const g = gameRef.current;
      if (!g) return;
      const botId = g.playerTwo.id;
      if (decisionOwner(g) !== botId) return;
      botActingRef.current = true;
      performBotAction(botId).finally(() => {
        botActingRef.current = false;
      });
    }, BOT_MOVE_DELAY_MS);
    return () => window.clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [vsComputer, gameId]);

  async function startMatch() {
    if (!setupA || !setupB) return;
    if (setupB === OPPONENT_PICKS) {
      // Online game with only your own Champion - see lobby.tsx.
      try {
        const opened = await api.openGame(setupA);
        rememberSeats(opened.gameId, opened.seats);
        setWaiting({ gameId: opened.gameId, hostChampionId: opened.hostChampionId });
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
      }
      return;
    }
    await run(async () => {
      const created = await api.createGame(setupA, setupB);
      rememberSeats(created.game.gameId, created.seats);
      return created.game;
    });
  }

  // Reuse mobile’s die-flight logic: only a change of displayed zone/region
  // launches a travelling die, never an unchanged roll or an opponent action.
  useDieFlights(flightRootRef, game, game?.currentStepId ?? "", game?.yourPlayerId ?? game?.playerOne.id ?? "");

  if (!game && (waiting || invitePick)) {
    return (
      <div className="dicekingdom">
        <div className="dk-titlebar-right" style={{ float: "right" }}>
          <ThemeToggle theme={theme} setTheme={setTheme} />
        </div>
        <h1>Dice Kingdom</h1>
        {error && <p className="error">{error}</p>}
        {waiting ? (
          <WaitingForOpponent
            gameId={waiting.gameId}
            hostChampionId={waiting.hostChampionId}
            link={inviteLink(waiting.gameId)}
            onStarted={(g) => {
              setWaiting(null);
              setGame(g);
            }}
            onCancel={() => setWaiting(null)}
          />
        ) : (
          <PickYourChampion
            lobby={invitePick!}
            champions={CHAMPIONS}
            onJoined={(g) => {
              setInvitePick(null);
              setGame(g);
            }}
          />
        )}
      </div>
    );
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
          Pass-and-play, or pick "Opponent picks" for Player 2 to send an invite link and let them choose their own
          Champion. Runs on the real rules
          engine - a small pool, simple abilities, mostly for reacting to how the system feels.
        </p>
        {error && <p className="error">{error}</p>}
        <ResumeGames
          onResume={(r) => {
            setError(null);
            if (r.kind === "game") {
              setVsComputer(r.vsComputer);
              setGame(r.game);
            } else setWaiting({ gameId: r.gameId, hostChampionId: r.hostChampionId });
          }}
        />
        <div className="panel">
          {/* Side by side, not stacked - direct feedback (2026-09-09):
              "can we do the two column approach with the 'select
              Champion' screen as well, so I don't have to scroll?"
              Both players' pickers were identical apart from which
              setup state they wrote to, so this also collapses the
              previous copy-pasted pair into one map over the two. */}
          <label style={{ display: "flex", alignItems: "center", gap: 8, margin: "0 0 14px", fontSize: 14 }}>
            <input
              type="checkbox"
              checked={vsComputer}
              onChange={(e) => {
                setVsComputer(e.target.checked);
                if (e.target.checked && setupB === OPPONENT_PICKS) setSetupB(null);
              }}
            />
            Play vs Computer - a basic rule-based opponent (highest attacker attacks, best affordable
            purchase/block), not a strategic one
          </label>
          <div className="champ-pick-columns">
            {[
              { label: "Player 1", value: setupA, setValue: setSetupA },
              { label: vsComputer ? "Computer" : "Player 2", value: setupB, setValue: setSetupB },
            ].map(({ label, value, setValue }) => (
              <div className="champ-pick-column" key={label}>
                <h3 style={{ margin: "0 0 10px" }}>{label}</h3>
                <ChampionPicker
                  champions={CHAMPIONS}
                  value={value}
                  onPick={setValue}
                  allowOpponentPicks={setValue === setSetupB && !vsComputer}
                />
              </div>
            ))}
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
  // Both Basic Action cards form a shared community pool, regardless of owner.
  const basicActions = basicActionStock(game, cardsById);

  function diceFor(playerId: string, zone?: string) {
    return game!.dice.filter((d) => d.controllerId === playerId && (!zone || d.zone === zone));
  }

  const primaryDie = selection.primary ? game.dice.find((d) => d.id === selection.primary) ?? null : null;
  const myPendingChoice = game.pendingChoice?.controllerId === you ? game.pendingChoice : null;
  const choiceMax = Math.max(1, myPendingChoice?.maxCount ?? 1);
  const choiceCandidates = new Set(myPendingChoice?.candidateIds ?? []);
  const namedCardChoice = myPendingChoice?.intent === "NameCard";
  // Unpurchased dice are represented by roster cards, not loose dice.
  // The other eligible dice remain in their original Field/Reserve/Attack positions.
  const inlineDieChoice = !!myPendingChoice && !namedCardChoice && myPendingChoice.candidateIds.length > 0 &&
    myPendingChoice.candidateIds.every((id) => {
      // Player targets have no die to highlight; offer them by name in
      // the compact prompt alongside directly-selectable creature dice.
      if (id === game.playerOne.id || id === game.playerTwo.id) return true;
      const die = game.dice.find((d) => d.id === id);
      return die && (die.zone === "FieldZone" || die.zone === "ReservePool" || die.zone === "PrepArea" || die.zone === "AttackZone");
    });
  const inlineChoice = inlineDieChoice || namedCardChoice;
  function toggleChoice(id: string) {
    if (!choiceCandidates.has(id)) return;
    setChoicePicked((previous) => previous.includes(id)
      ? previous.filter((x) => x !== id)
      : choiceMax === 1 ? [id] : previous.length < choiceMax ? [...previous, id] : previous);
  }

  // What the current selection actually costs / requires, for the cost
  // line shown next to the contextual action button. Field: any energy
  // type; Purchase: only the card's own type or Wild.
  function costFor(die: Die): { amount: number; matchType: string | null } {
    if (die.zone === "Unpurchased") {
      const card = die.cardId ? cardsById.get(die.cardId) : undefined;
      // Discounts included (V2GameStateDto.PurchaseCosts / V2DieDto.FieldingCost).
      return { amount: (die.cardId ? game?.purchaseCosts?.[die.cardId] : undefined) ?? card?.purchaseCost ?? 0, matchType: card?.energyTypes[0] ?? null };
    }
    if (!die.cardId || die.level === null) return { amount: 0, matchType: null }; // Tardigrade - free
    const card = die.cardId ? cardsById.get(die.cardId) : undefined;
    return { amount: die.fieldingCost ?? card?.levels[die.level - 1]?.fieldingCost ?? 0, matchType: null };
  }

  // Whether a Reserve Pool die can currently be clicked, and what
  // clicking it means, depends only on the step and what's already
  // selected - not on a separate per-feature flag. Mirrors
  // ../ActionTray.tsx's "any die can become primary; once one is
  // primary, others become secondary" permissiveness.
  function reservePoolClickable(d: Die): boolean {
    if (d.controllerId !== you || !isYourTurn) return false;
    if (step === "roll-and-reroll") return rolled(d) && !rerolledIds.includes(d.id);
    if (step === "action-global-window") {
      return game?.priorityPlayerId === you && !game?.pendingChoice &&
        d.zone === "ReservePool" && !!d.isActionFace;
    }
    if (step === "main") {
      if (selection.primary === null) return d.zone === "ReservePool" && rolled(d) &&
        (d.effectiveAttack !== null || !!d.isActionFace); // select creature or action die
      if (d.id === selection.primary) return true; // toggle off
      const primary = game!.dice.find((candidate) => candidate.id === selection.primary);
      if (primary?.zone === "ReservePool" && primary.effectiveAttack !== null) {
        // Another creature can replace the selection at any time; energy
        // only becomes clickable once Field is pressed beneath the die.
        return (d.zone === "ReservePool" && rolled(d) &&
          (d.effectiveAttack !== null || (!!d.isActionFace && fieldPaymentDieId === null))) ||
          isReservePaymentDie(d, primary, fieldPaymentDieId);
      }
      if (primary?.zone === "ReservePool" && primary.isActionFace) {
        // An Action die is a single selected die, not an exclusive mode.
        // Allow the next creature or Action die to become the primary
        // immediately; selectReserveDie already replaces that selection.
        return d.zone === "ReservePool" && rolled(d) &&
          (d.effectiveAttack !== null || !!d.isActionFace);
      }
      return d.energyAmount > 0; // purchase payment
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
    const intimidated = diceFor(playerId, "Intimidated");
    const bag = diceFor(playerId, "Bag");
    const drawn = diceFor(playerId, "DiceFromBag");
    const carried = diceFor(playerId, "DiceFromPrep");
    const unpurchased = diceFor(playerId, "Unpurchased").filter((d) => !d.cardId || !cardsById.get(d.cardId)?.isAction);
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
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "plain"}`} data-region={`${playerId}-${zoneName}`} data-pile={`${playerId === you ? "mine" : "opp"}-${zoneName === "UsedPile" ? "used" : "out"}`}>
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
          <span data-pile={`${playerId === you ? "mine" : "opp"}-bag`}>
            {trayItem("Bag", dice.length, () => setOpen((o) => !o))}
          </span>
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

    // Compact stand-in for a pile in the collapsed opponent mat - same
    // label+count `trayItem` Bag/Drawn/Carried already use, but with its
    // own click-to-inspect popover (direct feedback, 2026-09-11): "we've
    // lost the other zones (used pile, prep area, etc)... they can be
    // small and only have a badge (or die) indicating the number of dice
    // in that area, and if we want to know exactly what is in there we
    // can click on it." Shares `collapsedZoneOpen` rather than its own
    // per-call state so opening one closes any other already open.
    function miniPile(label: string, zoneName: string, dice: Die[]) {
      const open = collapsedZoneOpen === zoneName;
      return (
        <>
          <span data-pile={`${playerId === you ? "mine" : "opp"}-${zoneName === "UsedPile" ? "used" : zoneName === "PrepArea" ? "prep" : "out"}`} data-region={`${playerId}-${zoneName}`}>
            {trayItem(label, dice.length, () => setCollapsedZoneOpen((z) => (z === zoneName ? null : zoneName)))}
          </span>
          {open && (
            <div className="bag-popover down">
              <div className="dierow">
                {dice.length === 0 && <span style={{ opacity: 0.5, fontSize: 12 }}>empty</span>}
                {groupDice(dice, zoneName).map((g) => (
                  <DieTile key={g.key} die={g.sample} zone={zoneName} count={g.count} cardsById={cardsById} accent={accent} />
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
      const isRollingHere = dice.some((d) => spins[d.id]?.kind === "tumble");
      // During Roll & Reroll, the active player's Reserve dice appear in
      // the central Tray instead. Do not draw the same physical dice twice.
      const stagedInTray = zoneName === "ReservePool" && step === "roll-and-reroll" &&
        playerId === game!.activePlayerId &&
        !diceFor(playerId).some((d) => d.zone === "DiceFromBag" || d.zone === "DiceFromPrep");
      const visibleDice = stagedInTray ? [] : dice;
      return (
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "reserve"}${isRollingHere ? " rolling" : ""}${compact ? " compact" : ""}`}
          data-region={`${playerId}-${zoneName}`}
          data-pile={`${playerId === you ? "mine" : "opp"}-${zoneName === "ReservePool" ? "reserve" : "prep"}`}>
          <h4>
            {title} <span className="count">{dice.length}</span>
            {stagedInTray && dice.length > 0 && <span className="dk-in-tray">in Tray</span>}
          </h4>
          <div className="dierow">
            {visibleDice.map((d) => {
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
                  clickable={inlineDieChoice ? choiceCandidates.has(d.id) : reservePoolClickable(d)}
                  picked={inlineDieChoice ? choicePicked.includes(d.id) : picked}
                  choiceActive={inlineDieChoice}
                  targetable={inlineDieChoice && choiceCandidates.has(d.id)}
                  label={already ? "rerolled" : undefined}
                  onClick={() => inlineDieChoice ? toggleChoice(d.id) : selectReserveDie(d)}
                  fieldPrompt={zoneName === "ReservePool" && playerId === you && step === "main" && isYourTurn && game!.priorityPlayerId === you && !game!.pendingChoice && !busy && d.id === selection.primary && d.effectiveAttack !== null && rolled(d) && fieldPaymentDieId !== d.id}
                  onStartField={() => {
                    if (costFor(d).amount === 0) {
                      void run(() => api.field(game!.gameId, d.id, []));
                    } else {
                      setFieldPaymentDieId(d.id);
                    }
                  }}
                  actionPrompt={zoneName === "ReservePool" && playerId === you &&
                    d.id === selection.primary &&
                    !!abilities.actionDice.find((a) => a.die.id === d.id)?.command &&
                    !game!.pendingChoice}
                  onStartAction={() => {
                    const command = abilities.actionDice.find((a) => a.die.id === d.id)?.command;
                    if (command) doAbility(command);
                  }}
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
      <div className="zone zone-field" data-region={`${playerId}-FieldZone`}>
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
                clickable={inlineDieChoice ? choiceCandidates.has(d.id) : clickable}
                picked={inlineDieChoice ? choicePicked.includes(d.id) : picked}
                choiceActive={inlineDieChoice}
                targetable={inlineDieChoice && choiceCandidates.has(d.id)}
                onClick={() => inlineDieChoice ? toggleChoice(d.id) : toggleDie(d.id)}
              />
            );
          })}
        </div>
        {/* Keyword Intimidate - back on the same face at Clean Up. */}
        {intimidated.length > 0 && (
          <div className="dk-intimidated" data-region={`${playerId}-Intimidated`}>
            <span className="zone-note">Intimidated · back at end of turn</span>
            <div className="dierow">
              {intimidated.map((d) => (
                <DieTile key={d.id} die={d} zone="Intimidated" cardsById={cardsById} accent={accent} mine={playerId === you} />
              ))}
            </div>
          </div>
        )}
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
            <span data-pile={`${playerId === you ? "mine" : "opp"}-drawn`}>{trayItem("Drawn This Turn", drawn.length)}</span>
            <span data-pile={`${playerId === you ? "mine" : "opp"}-carried`}>{trayItem("Carried From Prep", carried.length)}</span>
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
    // Used Pile/Out of Play/Prep Area/the Bag tray all shrink to a
    // one-line badge each here rather than dropping out entirely (see
    // miniPile above) - direct feedback (2026-09-11) reversed the
    // original "drop them" call: they're still real zones, just not
    // ones worth full tile rows on a board you're not deciding from.
    // Field Zone sits last (nearest the shared Attack Zone rendered
    // right below this board, opp-then-lane-then-you) - it was first
    // before, which put it furthest from the lane it actually feeds.
    // Reserve Pool rides in the same wrapping row as the pile badges
    // instead of claiming a full-width strip of its own - direct
    // feedback (2026-09-11): "still a lot of empty space we could
    // compact... Reserve doesn't have to span the whole width, it can
    // share with Used and Prep." It's still real die tiles, not a
    // count-only badge (energy there is the one thing worth checking on
    // your own turn, per the 2026-09-08 feedback above) - just sized to
    // its own content instead of stretched, since a flex-row item does
    // that by default.
    //
    // Drawn This Turn/Carried From Prep are dropped here entirely
    // (still shown in the full mat's own tray) - direct feedback
    // (2026-09-11): "most of the time we won't need those sections, so
    // hide them for now." Unlike Used/Prep/Out/Bag, which at least name
    // a real zone worth a tap, these two are turn-scoped bookkeeping
    // that's almost always 0 outside the opponent's own turn (when the
    // full mat renders instead of this one anyway).
    // Used, Reserve Pool, Prep - same left-to-right order as the full
    // mat's own grid ("used reserve prep"/"outofplay reserve prep"),
    // not alphabetical/arbitrary - direct feedback (2026-09-11):
    // "Reserve Pool should be in the middle... matching the active
    // player arrangement."
    const collapsedMat = (
      <div className="mat-collapsed">
        <div className="mini-pile-row">
          {bagTray(bag)}
          {miniPile("Used", "UsedPile", used)}
          {rolledZone("Reserve Pool", "ReservePool", reserve, true)}
          {miniPile("Prep", "PrepArea", prep)}
          {miniPile("Out", "OutOfPlay", outOfPlay)}
        </div>
        {fieldZone}
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
    const regularCards = [...unpurchasedByCard.entries()];
    if (namedCardChoice && isOpponentBoard) {
      // Pangolin names a character CARD, not a die. Include cards even when
      // every copy is already purchased, and offer them on the actual roster.
      for (const id of myPendingChoice!.candidateIds) {
        const die = game!.dice.find((d) => d.id === id);
        if (die?.cardId && !regularCards.some(([cardId]) => cardId === die.cardId)) {
          regularCards.push([die.cardId, []]);
        }
      }
    }
    const sharedActionCards = basicActions.map(({ card, dice }) => [card.id, dice] as [string, Die[]]);
    // Identical roster chip/inspector/purchase workflow for characters and
    // shared Basic Actions. Only the grouping and visual treatment differ.
    function renderRosterChips(entries: Array<[string, Die[]]>, basic = false) {
      return (
        <>
      {entries.map(([cardId, dice]) => {
            const card = cardsById.get(cardId);
            const Avatar = CHARACTER_ICONS[cardId];
            const dieId = dice[0]?.id ?? null;
            const energyType = card?.energyTypes[0] ?? "Wild";
            const canPurchaseNow = isYourTurn && playerId === you && step === "main" && !game!.pendingChoice && game!.priorityPlayerId === you && dieId !== null;
            // Pangolin's lockout on this board's owner (status cues).
            const lockers = (game?.lockedCards ?? []).find((l) => l.playerId === playerId && l.cardId === cardId)?.sources;
            const picked = dieId !== null && selection.primary === dieId;
            const openKey = `${playerId}:${cardId}`;
            const detailOpen = openCardId === openKey;
            const namedChoiceId = namedCardChoice && isOpponentBoard
              ? myPendingChoice!.candidateIds.find((id) => game!.dice.some((d) => d.id === id && d.cardId === cardId))
              : undefined;
            const cardChoicePicked = !!namedChoiceId && choicePicked.includes(namedChoiceId);
            return (
              // Not a <button> disabled outside Purchase's own window -
              // direct feedback (2026-09-07): viewing a card's ability
              // and stats has to work all game, not just when it's your
              // Main step. The actual purchase click moved into a real
              // button inside the popover below, which IS gated on
              // canPurchaseNow.
              <div key={cardId} className={`roster-chip-wrap${basic ? " roster-basic-chip" : ""}`}>
                <button
                  type="button"
                  data-fly-id={playerId === you && dice.length > 0 ? `card:${cardId}` : undefined}
                  className={`roster-chip${detailOpen ? " open" : ""}${picked || cardChoicePicked ? " picked" : ""}${lockers ? " dk-locked" : ""}${namedChoiceId ? " choice-targetable" : ""}`}
                  title={lockers ? `Locked out by ${lockers.join(", ")} - can't be bought or fielded while that's active.` : undefined}
                  style={!basic && accent ? ({ ["--cc" as string]: accent } as const) : undefined}
                  onClick={() => namedChoiceId ? toggleChoice(namedChoiceId) : setOpenCardId((c) => (c === openKey ? null : openKey))}
                  onContextMenu={(e) => { if (namedChoiceId) { e.preventDefault(); setOpenCardId((c) => c === openKey ? null : openKey); } }}
                >
                  {Avatar && <Avatar size={18} />}
                  <span className="rc-name">{card?.name ?? cardId}</span>
                  <span className="rc-cost">
                    {game!.purchaseCosts?.[cardId] ?? card?.purchaseCost} {card?.energyTypes.length ? <CostIcon energyType={energyType} /> : null}
                  </span>
                  <span className="rc-left">×{dice.length} left</span>
                  {lockers && <span className="dk-locked-hatch" aria-hidden="true" />}
                </button>
                {detailOpen && (
                  <CardDetailPopover
                    card={card}
                    fallbackName={cardId}
                    purchaseCost={cardId ? game!.purchaseCosts?.[cardId] : undefined}
                    purchase={canPurchaseNow ? {
                      label: picked ? "Selected" : "Purchase",
                      disabled: busy || !!lockers || (selection.primary !== null && selection.primary !== dieId),
                      onClick: () => { if (dieId) toggleDie(dieId); },
                    } : undefined}
                  />
                )}
              </div>
            );
          })}
        </>
      );
    }
    const rosterRow = (
      <div className="roster-row" data-pile={`${playerId === you ? "mine" : "opp"}-roster`}>
        <div className="roster-regular-cards">
          {regularCards.length === 0 && <span className="roster-empty">No character dice left</span>}
          {renderRosterChips(regularCards)}
        </div>
        {sharedActionCards.length > 0 && (
          <div className="roster-basic-actions" aria-label="Shared Basic Action cards">
            <span className="roster-basic-label">Basic Actions · Shared</span>
            <div className="roster-basic-chips">{renderRosterChips(sharedActionCards, true)}</div>
          </div>
        )}
      </div>
    );
    const roster = isOpponentBoard ? (
      <div className="roster">
        <button
          type="button"
          data-pile="opp-roster"
          className={`roster-collapse-toggle${oppRosterOpen ? " open" : ""}`}
          onClick={() => setOppRosterOpen((o) => !o)}
        >
          <span className="roster-head">Roster</span>
          <span className="roster-collapse-icons">
            {[...unpurchasedByCard.keys(), ...sharedActionCards.map(([id]) => id)].map((cardId) => {
              const Avatar = CHARACTER_ICONS[cardId];
              return Avatar ? <Avatar key={cardId} size={16} /> : <TardigradeIcon key={cardId} size={16} />;
            })}
          </span>
        </button>
        {(oppRosterOpen || (namedCardChoice && isOpponentBoard)) && rosterRow}
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
  // The shared centre row becomes a dice Tray for Roll & Reroll, as on
  // mobile: face-down dice drawn from Bag/Prep before Roll, and their
  // actual faces in Reserve after Roll. It belongs to the active player,
  // including when we're watching an opponent roll. Keep each die in a
  // single visible position so roll and zone-flight animations work.
  function renderRollTray() {
    const playerId = game!.activePlayerId;
    const player = playerId === game!.playerOne.id ? game!.playerOne : game!.playerTwo;
    const accent = player.champion ? `var(--${player.champion.energySymbolId.toLowerCase()})` : undefined;
    const staged = diceFor(playerId).filter((d) => d.zone === "DiceFromBag" || d.zone === "DiceFromPrep");
    const hasRolled = staged.length === 0;
    const dice = hasRolled ? diceFor(playerId, "ReservePool") : staged;
    const mine = playerId === you;
    return (
      <div className="dk-roll-tray" data-region={`${playerId}-roll-tray`}>
        <div className="dk-roll-tray-heading">
          <strong>{mine ? "Tray" : `${player.name}'s Tray`}</strong>
          <span>{dice.length} dice{!hasRolled ? " · ready to roll" : mine ? " · click to select for reroll" : " · rolled"}</span>
        </div>
        <div className="dierow">
          {dice.length === 0 && <span className="dk-roll-tray-empty">No dice in Tray</span>}
          {dice.map((d) => (
            <DieTile
              key={d.id}
              die={d}
              zone={hasRolled ? "ReservePool" : d.zone}
              cardsById={cardsById}
              accent={accent}
              mine={mine}
              clickable={hasRolled && reservePoolClickable(d)}
              picked={selection.primary === d.id || selection.secondary.includes(d.id)}
              label={rerolledIds.includes(d.id) ? "rerolled" : undefined}
              onClick={() => selectReserveDie(d)}
              spin={spins[d.id]}
              turnOffset={offsets[d.id]}
            />
          ))}
        </div>
      </div>
    );
  }

  function renderAttackZone() {
    if (step === "roll-and-reroll") return renderRollTray();
    // Local picks while the defender is choosing, the server's declared
    // blocks after - the attacker's device never has the local ones.
    const assignments: BlockAssignment[] =
      step === "assign-blockers"
        ? Object.entries(blockAssignments)
            .filter((entry): entry is [string, string] => !!entry[1])
            .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId }))
        : (game!.blocks ?? []);
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
        onGroupClick={(ids) => inlineDieChoice ? toggleChoice(ids[0]) : toggleDie(ids[0])}
        targeting={inlineDieChoice ? { candidates: choiceCandidates, picked: new Set(choicePicked) } : undefined}
        spins={spins}
        turnOffsets={offsets}
        canAssignBlockers={!myPendingChoice && step === "assign-blockers" && !isYourTurn}
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
  const own = myLink(game.gameId);

  // The contextual action available for whatever's currently selected -
  // computed once, the same way ../ActionTray.tsx builds its `actions`
  // list from the primary die's zone and the current step, instead of a
  // different bespoke panel per feature.
  function selectionAction(): { label: string; run: () => Promise<GameState>; rolledIds?: string[]; freeField?: boolean } | null {
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
    if (step === "main" && primaryDie.zone === "ReservePool" && rolled(primaryDie) && primaryDie.effectiveAttack !== null && fieldPaymentDieId === primaryDie.id) {
      // Golden Eagle (2026-10-04): with no energy picked, field it free.
      const me = you === game!.playerOne.id ? game!.playerOne : game!.playerTwo; // (yourPlayer is declared further down)
      if (secondaryIds.length === 0 && me.freeFieldAvailable && costFor(primaryDie).amount > 0)
        return { label: "Field free (Golden Eagle)", freeField: true, run: () => api.field(game!.gameId, primaryDie.id, [], true) };
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
  const stepContentIsPanel = !game.pendingChoice && step === "assign-blockers" && !isYourTurn;
  const stepContent =
    game.pendingChoice ? null : game.priorityPlayerId === you && !isYourTurn ? (
      // Priority (Priority.cs): the opponent passed to you. This page has
      // no Globals UI (mobile does), so passing back is the only move.
      <button className="btn" disabled={busy} onClick={() => run(() => api.pass(game.gameId))}>
        Pass
      </button>
    ) : game.priorityPlayerId && game.priorityPlayerId !== you && isYourTurn ? (
      <span className="now-bar-note">{vsComputer ? "Computer is deciding…" : "Your opponent may use a Global…"}</span>
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
      <span className="now-bar-note">
        {vsComputer ? "Computer is assigning blockers…" : "Waiting on the other player to assign blockers…"}
      </span>
    ) : step === "action-global-window" && isYourTurn ? (
      <button
        className="btn"
        disabled={busy}
        onClick={() =>
          run(() =>
            api.assignCombatDamage(game.gameId, game.blocks ?? []),
          )
        }
      >
        {/* Only resolves combat if the other player can't respond - if
            they could use a Global, it hands them priority first. */}
        {game.passGivesPriority ? "Pass Priority" : "Resolve Combat"}
      </button>
    ) : !isYourTurn ? (
      <span className="now-bar-note">{vsComputer ? "Computer is thinking…" : "Waiting on the other player…"}</span>
    ) : (
      <div className="actionrow">
        {step === "start-of-turn" && (
          <button className="btn" disabled={busy} onClick={() => run(() => api.clearAndDraw(game.gameId))}>
            Draw
          </button>
        )}

        {step === "roll-and-reroll" && !diceFor(you).some((d) => (d.zone === "PrepArea" || d.zone === "ReservePool") && rolled(d)) && (
          <button
            className="btn"
            disabled={busy}
            onClick={() => {
              // Real bug, found while verifying the new tumble animation
              // (2026-09-16): ReservePool is empty until AFTER Roll()
              // resolves (TurnEngine.Roll moves dice from DiceFromBag/
              // DiceFromPrep straight into it), so naming no ids here
              // meant animateRolledDice's `explicit` set was always
              // empty and every rolled die fell through to the "spin"
              // (twist) animation instead of the real tumble.
              const drawn = [...diceFor(you, "DiceFromBag"), ...diceFor(you, "DiceFromPrep")];
              run(() => api.roll(game.gameId), drawn.map((d) => d.id));
            }}
          >
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
            {action && (
              <button className="btn" disabled={busy || (!action.freeField && cost !== null && spent < cost.amount)} onClick={() => run(action.run, action.rolledIds)}>
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
            onClick={() => {
              // Desktop still shows one column per attacker (CombatLane.tsx
              // never groups by lane) - which lane each one lands in has no
              // visible effect here, so this just spreads them round-robin
              // across the four lanes the engine now requires a value for.
              const ids = primaryDie ? [primaryDie.id, ...selection.secondary] : [];
              const attackers = ids.map((dieId, i) => ({ dieId, lane: i % 4 }));
              run(() => api.declareAttackers(game.gameId, attackers));
            }}
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
  const abilities = getAbilityOptions(game, cardsById, you, busy);
  function doAbility(command: AbilityCommand) {
    clearSelection();
    run(() => executeAbility(game!.gameId, command));
  }

  return (
    <div ref={flightRootRef} className="dicekingdom">
      {legendOpen && <DieFramesLegend variant="desktop" onClose={() => setLegendOpen(false)} />}
      {myPendingChoice && (
        <div className="dk-floating-choice" role="dialog" aria-label="Choose an ability target">
          <div className="dk-floating-choice-description">{myPendingChoice.description}</div>
          {inlineChoice ? (
            <>
              <p className="dk-choice-instructions">
                {namedCardChoice ? "Select a highlighted card in your opponent’s roster (right-click to inspect)." : "Select highlighted dice on the board (right-click to inspect)."}
                {choiceMax > 1 ? ` ${choicePicked.length}/${choiceMax} selected.` : ""}
              </p>
              {myPendingChoice.candidateIds.filter((id) => id === game.playerOne.id || id === game.playerTwo.id).map((id) => (
                <button key={id} type="button" className={`chip${choicePicked.includes(id) ? " on" : ""}`}
                  onClick={() => toggleChoice(id)}>
                  {id === game.playerOne.id ? game.playerOne.name : game.playerTwo.name}
                </button>
              ))}
              <div className="dk-choice-actions">
                <button type="button" className="btn" disabled={busy || choicePicked.length < myPendingChoice.minCount || choicePicked.length > choiceMax}
                  onClick={() => run(() => api.resolvePendingChoice(game.gameId, choicePicked))}>
                  {choicePicked.length === 0 && myPendingChoice.minCount === 0 ? "Skip" : "Confirm Choice"}
                </button>
              </div>
            </>
          ) : (
            // Choices with player IDs or dice in hidden/grouped piles cannot
            // reliably be selected on the visible board; retain fallback.
            <PendingChoiceChips
              key={choiceKey ?? "no-choice"}
              nameCard={namedCardChoice}
              candidateIds={myPendingChoice.candidateIds}
              max={myPendingChoice.maxCount}
              min={myPendingChoice.minCount}
              players={[game.playerOne, game.playerTwo]}
              dice={game.dice}
              cardsById={cardsById}
              onSubmit={(ids) => run(() => api.resolvePendingChoice(game.gameId, ids))}
            />
          )}
        </div>
      )}
      <GameOverOverlay
        game={game}
        you={vsComputer ? game.playerOne.id : you}
        onNewGame={() => {
          forgetSeats();
          setGame(null);
        }}
      />
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

      <Scoreboard
        opponent={oppPlayer}
        mine={yourPlayer}
        opponentActive={game.activePlayerId === opponentId}
        mineActive={game.activePlayerId === you}
        onUsePower={
          yourPlayer.championPowerUsable && game.priorityPlayerId === you && !game.pendingChoice && !busy
            ? () => run(() => api.championPower(game.gameId))
            : undefined
        }
      />

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
          <HowToPlayMenu onOpenLegend={() => setLegendOpen(true)} />
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
            <span className="sideboard-sub">shared pool · purchase from your roster</span>
            {basicActions.length === 0 && <p className="sideboard-empty">No Basic Action dice remaining.</p>}
            {basicActions.map(({ card, dice }) => {
              const actual = game.purchaseCosts?.[card.id] ?? card.purchaseCost;
              return <div key={card.id} className="dk-basic-action-item">
                <div className="dk-basic-action-head">
                  <strong>{card.name}</strong>
                  <span>Cost {actual} · ×{dice.length} left</span>
                </div>
                <p>{card.actionText ?? card.rawText}</p>
              </div>;
            })}
          </div>
          <div className="sideboard-panel dk-ability-sideboard">
            <SharedAbilityPanel
              variant="desktop"
              abilities={abilities}
              onExecute={doAbility}
              nameOf={(d) => (d.cardId ? cardsById.get(d.cardId)?.name : null) ?? "Tardigrade"}
              actionTextOf={(d) => d.cardId ? cardsById.get(d.cardId)?.actionText : null}
              renderDie={(d) => <DieCube {...facesFor(d, cardsById)} size={34} mine />}
            />
          </div>
        </div>

        <div className="dk-row-opp" ref={oppRowRef}>{renderBoard(opponentId, true)}</div>
        <div className="dk-row-lane">{renderAttackZone()}</div>
        <div className="dk-row-you" ref={yourRowRef}>{renderBoard(you, false)}</div>

        <div className="dk-rail-top">
          {/* "Active: X" and both Champion panels moved into Scoreboard
              (2026-09-09 direct feedback - see that component's own
              remarks); Invite stays here - "Invite and Copy link can
              stay on the bottom for now." */}
          {!vsComputer && (link || own) && <GameLinks invite={link} own={own} />}
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
                  <span className="now-bar-title">{step === "select-attackers" ? "Attack" : STEP_GUIDANCE[step].title}</span>
                </>
              )}
              {!stepContentIsPanel && <div className="now-bar-actions">{stepContent}</div>}
            </div>
            <div className="now-bar-detail" aria-live="polite">
              {myPendingChoice ? (inlineChoice ? "Choose highlighted targets on the board" : "Choose a target in the floating panel")
                : game.pendingChoice ? (vsComputer ? "Computer is choosing…" : "Waiting for the other player's choice…")
                : step === "select-attackers"
                ? "Declare Attackers"
                : step === "main" && primaryDie && primaryDie.zone === "ReservePool" && primaryDie.effectiveAttack !== null && fieldPaymentDieId !== primaryDie.id
                  ? "Select Field beneath the die to begin payment"
                  : step === "main" && primaryDie && action && cost
                    ? `${action.label} ${action.freeField ? "— free" : cost.amount > 0 ? `— cost ${cost.amount}${cost.matchType ? ` ${cost.matchType}` : ""} (${spent}/${cost.amount} selected)` : "— free"}`
                    : ""}
            </div>
            {stepContentIsPanel && <div className="now-panel-scroll">{stepContent}</div>}
          </div>
        </div>

        <div className="dk-rail-bottom">
          {/* Moved off the sideboard and onto your own rail, right under
              your Champion box - direct feedback (2026-09-09): this is
              specifically YOUR energy, sitting right above the log that
              already tracks everything you've done with it. (Champion
              box itself has since moved into Scoreboard - see that
              component's own remarks.)
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
  nameCard = false,
  candidateIds,
  max,
  min,
  players,
  dice,
  cardsById,
  onSubmit,
}: {
  /** The pick names a card (Pangolin's lockout): one chip per card, not per die. */
  nameCard?: boolean;
  candidateIds: string[];
  max: number;
  /** 0 for a "you may" offer (Infiltrate) - confirming with nothing picked declines it. */
  min: number;
  /** For a pick that can name a player (Attune: "the opponent or a target character die"). */
  players: { id: string; name: string }[];
  dice: Die[];
  cardsById: Map<string, { name: string }>;
  onSubmit: (ids: string[]) => void;
}) {
  const [picked, setPicked] = useState<string[]>([]);
  return (
    <>
      <div className="chiprow">
        {(nameCard
          ? [...new Map(candidateIds.map((id) => [dice.find((d) => d.id === id)?.cardId ?? id, id])).values()]
          : candidateIds
        ).map((id) => {
          const die = dice.find((d) => d.id === id);
          const player = die ? undefined : players.find((p) => p.id === id);
          const name = die?.cardId ? (cardsById.get(die.cardId)?.name ?? die.cardId) : (player?.name ?? "Tardigrade");
          if (nameCard)
            return (
              <span key={id} className={`chip${picked.includes(id) ? " on" : ""}`} onClick={() => setPicked(picked.includes(id) ? [] : [id])}>
                {name}
              </span>
            );
          return (
            <span
              key={id}
              className={`chip${picked.includes(id) ? " on" : ""}`}
              onClick={() =>
                setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : p.length < max ? [...p, id] : p))
              }
            >
              {name}
              {/* Paying energy (Breath Weapon) offers Reserve dice - what
                  matters there is the energy, not the body. */}
              {die && (die.zone === "ReservePool" && die.energyAmount > 0
                ? ` · ${die.energyAmount} ${die.energySymbolId}`
                : ` ${die.effectiveAttack}/${die.effectiveDefense}`)}
            </span>
          );
        })}
      </div>
      <button className="btn" disabled={picked.length < Math.max(1, min)} onClick={() => onSubmit(picked)}>
        Confirm Choice
      </button>
      {min === 0 && picked.length === 0 && (
        <button className="btn" onClick={() => onSubmit([])}>
          Decline
        </button>
      )}
    </>
  );
}

