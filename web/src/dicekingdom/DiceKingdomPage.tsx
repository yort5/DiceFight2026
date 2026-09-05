import { useEffect, useRef, useState } from "react";
import "./dicekingdom.css";
import { api } from "./api";
import { CHAMPION_ICONS, CHARACTER_ICONS, ENERGY_ICONS } from "./icons";
import { claimSeatFromUrl, inviteLink, nameClaimedSeat, rememberSeats } from "./seats";
import { CombatLane } from "./CombatLane";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor } from "./dieFaces";
import { StepRibbon } from "./StepRibbon";
import { MatchLog } from "./MatchLog";
import { ThemeToggle, useTheme } from "./ThemeToggle";
import { useDiceRoll, type RollTarget } from "./useDiceRoll";
import { characterFaceInfo, dieLabel } from "./dieHelpers";
import type { BlockAssignment, CardDef, Die, GameState, PlayerState } from "./types";

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
  const groups = new Map<string, DieGroup>();
  for (const d of dice) {
    const key = [d.cardId ?? "tardigrade", d.level, d.effectiveAttack, d.effectiveDefense, d.energySymbolId, d.energyAmount].join("|");
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
function LifeBox({ player, you, activePlayerId }: { player: PlayerState; you: string; activePlayerId: string }) {
  const isActive = activePlayerId === player.id;
  const mine = player.id === you;
  const cls = isActive ? (mine ? " turn-mine" : " turn-waiting") : "";
  return (
    <div className={`life-panel${cls}`}>
      <span className="life-label">{mine ? "You" : "Opponent"}</span>
      <span className="life-value">{player.life}</span>
    </div>
  );
}

// README's Column 3 "Champion boxes": a role label, the champion's name
// in the display face, its passive as the note. Lives in the rail, one
// per player - see .dk-rail-top/.dk-rail-bottom in the JSX below for
// *where*: sharing the same grid rows as the opponent/lane/you board
// rows is what puts the opponent's box under the life panels and yours
// with its top edge on the combat divider, matching the reference,
// without measuring anything in JS.
function ChampionBox({ player, isActivePlayer, you }: { player: PlayerState; isActivePlayer: boolean; you: string }) {
  if (!player.champion) return null;
  const Icon = CHAMPION_ICONS[player.champion.id];
  // Direct feedback (2026-09-05): even once every Champion has a real
  // avatar, the energy type still needs to read at a glance - a photo
  // alone doesn't carry that the way a plain color glyph did. Shown
  // alongside the name rather than replacing the avatar.
  const EnergyIcon = ENERGY_ICONS[player.champion.energySymbolId];
  const mine = player.id === you;
  const accent = `var(--${player.champion.energySymbolId.toLowerCase()})`;
  const turnClass = isActivePlayer ? (mine ? " turn-mine" : " turn-waiting") : "";
  return (
    <div className={`championbox${turnClass}`} style={{ ["--cc" as string]: accent }}>
      <div className="championbox-role">{mine ? "Your Champion" : "Opponent Champion"}</div>
      <div className="championbox-body">
        <div className="championbox-text">
          <div className="championbox-name-row">
            <div className="championbox-name">{player.champion.name}</div>
            {EnergyIcon && <EnergyIcon size={15} />}
          </div>
          <div className="championbox-note">{player.champion.passiveText}</div>
        </div>
        {Icon && <Icon size={72} />}
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

function PipBadge({ type, amount }: { type: string; amount: number }) {
  const cssVar = type === "Wild" ? "var(--wild)" : `var(--${type.toLowerCase()})`;
  const Icon = ENERGY_ICONS[type];
  return (
    <span className="pip" style={{ background: cssVar }} title={`${amount} ${type}`}>
      {amount} {Icon ? <Icon size={11} /> : type}
    </span>
  );
}

// A cost as a number + the energy's own icon, instead of spelling the
// type out - direct feedback (2026-09-07): "can we get the icons in
// there instead of the words." Bare span (not a colored pip like
// PipBadge) so it drops into existing text-sized cost labels unchanged.
// Colored to the energy's own accent rather than inheriting the
// surrounding text color - direct feedback (2026-09-05): inheriting
// --text-dim (as roster/popover cost labels do) left Claw's icon a dim
// grey in dark mode, easy to lose against the panel.
function CostIcon({ energyType }: { energyType: string }) {
  const Icon = ENERGY_ICONS[energyType];
  if (!Icon) return <>{energyType}</>;
  const cssVar = energyType === "Wild" ? "var(--wild)" : `var(--${energyType.toLowerCase()})`;
  return (
    <span style={{ color: cssVar, display: "inline-flex" }}>
      <Icon size={11} />
    </span>
  );
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
  /** Overrides the bottom label - used for "already rerolled"/"selected" state during Roll & Reroll. */
  label?: string;
  /** Mid-roll transform and accumulated turn count - see useDiceRoll.ts. */
  spin?: CubeSpin;
  turnOffset?: number;
}) {
  const isRolled = ROLLED_ZONES.has(zone) && rolled(die);
  const Avatar = die.cardId ? CHARACTER_ICONS[die.cardId] : null;
  const name = die.cardId ? (cardsById.get(die.cardId)?.name ?? die.cardId) : "Tardigrade";
  const cls = ["dietile", clickable ? "clickable" : "", picked ? "picked" : ""].filter(Boolean).join(" ");
  const style = accent ? ({ textAlign: "center", ["--cc" as string]: accent, color: accent } as const) : { textAlign: "center" as const };
  return (
    <button type="button" className={cls} onClick={onClick} disabled={!clickable} style={style}>
      {count && count > 1 && <span className="chip-count">×{count}</span>}
      {!isRolled ? (
        <>
          <div className="lbl">{name}</div>
          <div className="stat">—</div>
        </>
      ) : (
        <>
          {/* The same 3D cube /game's board uses (../DieCube.tsx), not a
              flat stat badge - a rolled die is a physical object showing
              a real face, not text about one. */}
          <DieCube {...facesFor(die, cardsById)} size={34} mine={mine ?? true} spin={spin} turnOffset={turnOffset} />
          {die.effectiveAttack !== null && Avatar && <Avatar size={16} />}
          <div className="lbl">
            {labelOverride ?? (die.effectiveAttack === null ? "Surge" : `L${die.level}${die.isTardigrade && !die.energySymbolId ? " · free" : ""}`)}
          </div>
          {die.energySymbolId && die.energyAmount > 0 && (
            <PipBadge type={die.energySymbolId} amount={die.energyAmount} />
          )}
        </>
      )}
    </button>
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
  const { spins, offsets, rolling, launch: launchRoll } = useDiceRoll();
  const [bagOpen, setBagOpen] = useState(false);
  const [oppBagOpen, setOppBagOpen] = useState(false);
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
  function animateRolledDice(previous: GameState, next: GameState, rolledDieIds?: string[]) {
    const before = new Map(previous.dice.map((d) => [d.id, d]));
    const explicit = new Set(rolledDieIds ?? []);
    const targets: RollTarget[] = [];
    for (const die of next.dice) {
      const was = before.get(die.id);
      if (!was) continue;
      if (!rolled(die)) continue;
      const changedFace =
        was.level !== die.level || was.effectiveAttack !== die.effectiveAttack ||
        was.energySymbolId !== die.energySymbolId || was.energyAmount !== die.energyAmount;
      if (!explicit.has(die.id) && !changedFace) continue;
      const { index } = facesFor(die, cardsById);
      targets.push({ dieId: die.id, faceIndex: index });
    }
    launchRoll(targets);
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
    // shape - used for the three grid cells that just show a group of
    // dice (Used Pile, Out of Play, Bag), so the grid markup below reads
    // as "which zone goes where" rather than repeating this each time.
    const ZONE_TINTS: Record<string, string> = {
      UsedPile: "used", OutOfPlay: "outofplay", Bag: "bag",
      PrepArea: "prep", DiceFromBag: "staging", DiceFromPrep: "staging",
    };
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

    // Bag - README: a count that's also a button opening an inspector
    // popover ("contents known, order is not"). Bag contents are public
    // information in this game (same rule the design doc cites), so both
    // players' bags are inspectable; the local player's opens upward,
    // the opponent's downward - see .bag-popover.up/.down.
    function bagZone(dice: Die[]) {
      const mine = playerId === you;
      const open = mine ? bagOpen : oppBagOpen;
      const setOpen = mine ? setBagOpen : setOppBagOpen;
      return (
        <div className="zone zone-bag">
          <button type="button" className="bag-button" onClick={() => setOpen((o) => !o)}>
            <h4>
              Bag <span className="count">{dice.length}</span>
            </h4>
            <span className="bag-hint">{open ? "hide contents" : "click to inspect"}</span>
          </button>
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
        </div>
      );
    }

    // Reserve Pool and Prep Area behave identically during Roll & Reroll -
    // reservePoolClickable is zone-agnostic (it only reads step/selection/
    // rolled state), and these are the tiles a player clicks to build a
    // reroll selection either way. Each die shown individually (not
    // grouped) since a rolled zone is about each die's own face, not a
    // count - see ROLLED_ZONES.
    function rolledZone(title: string, zoneName: string, dice: Die[]) {
      const isRollingHere = rolling && dice.some((d) => spins[d.id]);
      return (
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "reserve"}${isRollingHere ? " rolling" : ""}`}>
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
                  label={already ? "rerolled" : step === "roll-and-reroll" && rolled(d) ? (picked ? "selected" : undefined) : undefined}
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

    const mat = (
      <div className={`mat${mirrored ? " mirrored" : ""}`}>
        <div className="mat-slot mat-field">
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
        </div>
        <div className="mat-slot mat-used">{pileZone("Used Pile", "UsedPile", used)}</div>
        <div className="mat-slot mat-reserve">{rolledZone("Reserve Pool", "ReservePool", reserve)}</div>
        <div className="mat-slot mat-prep">{rolledZone("Prep Area", "PrepArea", prep)}</div>
        <div className="mat-slot mat-outofplay">
          {pileZone("Out of Play", "OutOfPlay", outOfPlay, playerId === you ? "yours only · moves to Used at end of turn" : "theirs · moves to Used at end of turn")}
        </div>
        <div className="mat-slot mat-bag">{bagZone(bag)}</div>
        <div className="mat-slot mat-drawn">{pileZone("Drawn This Turn", "DiceFromBag", drawn)}</div>
        <div className="mat-slot mat-carried">{pileZone("Carried From Prep", "DiceFromPrep", carried)}</div>
      </div>
    );

    // Always visible (README's Column 2 roster strip - a player checks
    // "what's left to buy" constantly, so this isn't hidden behind a
    // <details> the way the compact-chip version had it). Portrait cards
    // per README's chosen variant, one per Character (Dice Kingdom's
    // roster is a Champion + 2 Characters, not Dice Masters' 8, so this
    // reads as a short strip rather than the reference's full row).
    const roster = (
      <div className="roster">
        <div className="roster-head">Roster</div>
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
                  // Opens AWAY from this board's own mat, not toward it -
                  // direct feedback (2026-09-05): the roster sits below
                  // the mat on your own board (mat-then-roster) and above
                  // it on the opponent's (roster-then-mat, see `mirrored`
                  // below the mat/roster JSX), so opening toward the mat
                  // (the previous mirrored-ternary) covered the Reserve
                  // Pool right as a purchase asked you to click into it.
                  <div className={`card-popover ${mirrored ? "up" : "down"}`}>
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
      </div>
    );

    // The roster sits on the OUTER edge of each board, away from the
    // shared Attack Zone between the two mats - real bug, found by
    // direct feedback: rendered as a fixed mat-then-roster sequence
    // regardless of `mirrored`, it landed BELOW the mat every time,
    // which for the mirrored board is the edge next to Field Zone and
    // the Attack Zone (the mirrored mat's rows run the opposite order -
    // see the .mat.mirrored CSS), not away from it.
    return (
      <div key={playerId} className={`playerboard${turnClass}`}>
        {mirrored ? (
          <>
            {roster}
            {mat}
          </>
        ) : (
          <>
            {mat}
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
  // seam ../CombatLane.tsx draws between the two halves. Always rendered,
  // even with nothing declared - CombatLane itself draws three empty
  // "no blocker"/"open slot" placeholder columns in that case, so the
  // lane reads as a permanent part of the table instead of appearing and
  // disappearing as the turn moves through combat.
  function renderAttackZone() {
    const assignments: BlockAssignment[] = Object.entries(blockAssignments)
      .filter((entry): entry is [string, string] => !!entry[1])
      .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId }));
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
        label: `Reroll Selected (${ids.length})`,
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

  return (
    <div className="dicekingdom">
      {error && <p className="error">{error}</p>}

      {/* Once a game is live, /game shows almost no chrome above the
          table at all - title/description/How-to-Play live only on the
          pre-game screen or behind a small toggle, never as a persistent
          block. Direct feedback: the old title+description+How-to-play+
          topbar stack here was costing real vertical space /game never
          spends once a match starts. */}
      <div className="dk-titlebar">
        <StepRibbon game={game} />
        <div className="dk-titlebar-right">
          <ThemeToggle theme={theme} setTheme={setTheme} />
          <details className="how">
            <summary>How to play</summary>
            <ul>
              <li>Draw, then Roll - each die may be rerolled once, together with any others you select, before Continue.</li>
              <li>Field a rolled creature (Tardigrades are free; your Character costs energy, any type) or Purchase another copy of your Character (matching type or Wild only).</li>
              <li>Proceed to Attack, pick attackers; the other seat assigns blockers, then Resolve Combat.</li>
            </ul>
          </details>
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
        </div>

        <div className="dk-row-opp">{renderBoard(opponentId, true)}</div>
        <div className="dk-row-lane">{renderAttackZone()}</div>
        <div className="dk-row-you">{renderBoard(you, false)}</div>

        <div className="dk-rail-top">
          {/* Active + Invite on one line, then life totals side by side -
              ../TurnRail.tsx's own shape. */}
          <div className="active-line">
            <span className={isYourTurn ? "whose-turn mine" : "whose-turn waiting"}>
              <strong>Active:</strong> {game.activePlayerId}
            </span>
            {link && <InviteRow link={link} />}
          </div>
          <div className="life-panels">
            <LifeBox player={opponentId === game.playerOne.id ? game.playerOne : game.playerTwo} you={you} activePlayerId={game.activePlayerId} />
            <LifeBox player={you === game.playerOne.id ? game.playerOne : game.playerTwo} you={you} activePlayerId={game.activePlayerId} />
          </div>
          <ChampionBox
            player={opponentId === game.playerOne.id ? game.playerOne : game.playerTwo}
            isActivePlayer={game.activePlayerId === opponentId}
            you={you}
          />
        </div>

        <div className="dk-rail-mid">
          <div className="controlcenter">
          {/* Ported from ../TurnRail.tsx's Now panel - a step title and
              one-line description, not just a bare button. Shown
              regardless of which contextual panel renders below it
              (pending choice, block assignment, or the plain action
              buttons), same as v1's Now panel sitting above whichever
              of ActionTray/DeclareBlockersPanel/etc. is active. */}
          {STEP_GUIDANCE[step] && (
            <div className="now-header">
              <span className="now-eyebrow">Now</span>
              <h3 className="now-title">{STEP_GUIDANCE[step].title}</h3>
              <p className="now-guidance">{STEP_GUIDANCE[step].text}</p>
            </div>
          )}
          {/* README's rail "Selected die" panel - name, level/stats, and
              whatever contextual action is legal. Shown whenever a die is
              selected, on top of whichever panel/action-row renders
              below (those still drive the actual buttons - this is just
              "what am I looking at"). */}
          {primaryDie && (
            <div className="panel selected-die-panel">
              <span className="now-eyebrow">Selected die</span>
              <h3 className="now-title">{dieLabel(primaryDie, cardsById)}</h3>
              {characterFaceInfo(primaryDie) && (
                <p className="selected-die-stats">
                  L{primaryDie.level} · {characterFaceInfo(primaryDie)!.attack}A/{characterFaceInfo(primaryDie)!.defense}D
                </p>
              )}
            </div>
          )}
          {game.pendingChoice && you === game.pendingChoice.controllerId ? (
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
          <p className="dek">Waiting on the other player's choice…</p>
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
          <p className="dek">Waiting on the other player to assign blockers…</p>
        ) : step === "action-global-window" && isYourTurn ? (
          <div className="panel">
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
          </div>
        ) : !isYourTurn ? (
          <p className="dek">Waiting on the other player…</p>
        ) : (
          <div className="actionrow" style={{ margin: "10px 0", flexDirection: "column", alignItems: "flex-start" }}>
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
              <div className="actionrow">
                {action && (
                  <button className="btn" disabled={busy} onClick={() => run(action.run, action.rolledIds)}>
                    {action.label}
                  </button>
                )}
                <button className="btn ghost" disabled={busy} onClick={() => run(() => api.finishRoll(game.gameId))}>
                  Continue to Main Phase
                </button>
              </div>
            )}

            {step === "main" && (
              <div className="actionrow">
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
                    Proceed to Attack
                  </button>
                )}
                {/* Ported from ../App.tsx's identical "Clean Up (skip
                    attack) ▶" - dropped during the redesign, direct
                    feedback (2026-09-05) asked for it back. The server
                    (TurnEngine.SkipAttackStep) still rejects this with a
                    real error if a forced attacker is outstanding; no
                    client-side gating needed beyond the step check. */}
                {!primaryDie && (
                  <button
                    className="btn ghost"
                    disabled={busy}
                    onClick={() => run(() => api.skipAttackStep(game.gameId))}
                  >
                    Skip Attack Step
                  </button>
                )}
              </div>
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
        )}
          </div>
        </div>

        <div className="dk-rail-bottom">
          <ChampionBox
            player={you === game.playerOne.id ? game.playerOne : game.playerTwo}
            isActivePlayer={game.activePlayerId === you}
            you={you}
          />
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

