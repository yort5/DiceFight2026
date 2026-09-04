import { useEffect, useRef, useState } from "react";
import "./dicekingdom.css";
import { api } from "./api";
import { CHAMPION_ICONS, CHARACTER_ICONS } from "./icons";
import { claimSeatFromUrl, inviteLink, nameClaimedSeat, rememberSeats } from "./seats";
import { CombatLane } from "./CombatLane";
import { DieCube } from "./DieCube";
import { facesFor } from "./dieFaces";
import { StepRibbon } from "./StepRibbon";
import { MatchLog } from "./MatchLog";
import { ThemeToggle, useTheme } from "./ThemeToggle";
import type { BlockAssignment, CardDef, Die, GameState, PlayerState } from "./types";

const POLL_INTERVAL_MS = 2000;
const CHAMPIONS = [
  { id: "Lion", energy: "Claw" },
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

// Was the first thing rendered inside each board, ahead of Field Zone -
// direct feedback: it can't sit between Field Zone and the Attack Zone
// (those need to line up directly across from each other), and more
// generally the middle of the page is where the actual game happens, so
// anything that can move to the side should. Now lives in the rail.
function ChampBanner({ player, isActivePlayer, you }: { player: PlayerState; isActivePlayer: boolean; you: string }) {
  if (!player.champion) return null;
  const Icon = CHAMPION_ICONS[player.champion.id];
  if (!Icon) return null;
  const mine = player.id === you;
  const accent = `var(--${player.champion.energySymbolId.toLowerCase()})`;
  const turnClass = isActivePlayer ? (mine ? " turn-mine" : " turn-waiting") : "";
  return (
    <div className={`champbanner${turnClass}`} style={{ color: accent, ["--cc" as string]: accent }}>
      <Icon />
      <div>
        <div className="cbname" style={{ color: "var(--text-h)" }}>
          {mine ? "You" : "Opponent"} — {player.champion.name}
        </div>
        <div className="cbpassive">{player.champion.passiveText}</div>
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
  return (
    <span className="pip" style={{ background: cssVar }}>
      {amount} {type}
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
          <DieCube {...facesFor(die, cardsById)} size={40} mine={mine ?? true} />
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

  async function run(fn: () => Promise<GameState>) {
    setBusy(true);
    busyRef.current = true;
    setError(null);
    try {
      const next = await fn();
      setGame(next);
      clearSelection();
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
    const turnClass = isActivePlayer ? (playerId === you ? " turn-mine" : " turn-waiting") : "";

    // A single die-count zone, matching v1's PlayerBoard.tsx's mat-slot
    // shape - used for the three grid cells that just show a group of
    // dice (Used Pile, Out of Play, Bag), so the grid markup below reads
    // as "which zone goes where" rather than repeating this each time.
    const ZONE_TINTS: Record<string, string> = {
      UsedPile: "used", OutOfPlay: "outofplay", Bag: "bag",
      PrepArea: "prep", DiceFromBag: "staging", DiceFromPrep: "staging",
    };
    function pileZone(title: string, zoneName: string, dice: Die[]) {
      return (
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "plain"}`}>
          <h4>
            {title} <span className="count">{dice.length}</span>
          </h4>
          <div className="dierow">
            {dice.length === 0 && <span style={{ opacity: 0.5, fontSize: 12 }}>empty</span>}
            {groupDice(dice, zoneName).map((g) => (
              <DieTile key={g.key} die={g.sample} zone={zoneName} count={g.count} cardsById={cardsById} accent={accent} />
            ))}
          </div>
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
      return (
        <div className={`zone zone-${ZONE_TINTS[zoneName] ?? "reserve"}`}>
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
                const clickable = d.controllerId === you && isYourTurn && step === "select-attackers";
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
        <div className="mat-slot mat-outofplay">{pileZone("Out of Play", "OutOfPlay", outOfPlay)}</div>
        <div className="mat-slot mat-bag">{pileZone("Bag", "Bag", bag)}</div>
        <div className="mat-slot mat-drawn">{pileZone("Drawn This Turn", "DiceFromBag", drawn)}</div>
        <div className="mat-slot mat-carried">{pileZone("Carried From Prep", "DiceFromPrep", carried)}</div>
      </div>
    );

    // Always visible, matching ../PlayerBoard.tsx's own roster - a
    // player checks "what's left to buy" constantly during a game, so
    // it stays open by default rather than only appearing for the
    // active player mid-Main. Purchasing only actually enables when
    // it's legal - own board, your turn, Main step - everything else
    // here still shows so both rosters stay checkable at a glance.
    // Compact chips, not full dietile cards - direct feedback: these
    // were the same big card size as an in-play die, when they're
    // reference-only most of the game (../CommunityCards.tsx's own
    // compact-row precedent for "a lot of these, glanced at often").
    const roster = (
      <details className="roster" open>
        <summary>Unpurchased roster ({[...unpurchasedByCard.values()].reduce((n, d) => n + d.length, 0)})</summary>
        <div className="roster-row">
          {unpurchasedByCard.size === 0 && <span style={{ opacity: 0.5, fontSize: 12 }}>nothing left to buy</span>}
          {[...unpurchasedByCard.entries()].map(([cardId, dice]) => {
            const card = cardsById.get(cardId);
            const Avatar = CHARACTER_ICONS[cardId];
            const dieId = dice[0].id;
            const canPurchaseNow = isYourTurn && playerId === you && step === "main";
            const clickable = canPurchaseNow && (selection.primary === null || selection.primary === dieId);
            const picked = selection.primary === dieId;
            return (
              <button
                key={cardId}
                type="button"
                className={`roster-chip${clickable ? " clickable" : ""}${picked ? " picked" : ""}`}
                disabled={!clickable}
                style={accent ? ({ ["--cc" as string]: accent } as const) : undefined}
                onClick={() => toggleDie(dieId)}
              >
                {Avatar && <Avatar size={20} />}
                <span className="rc-name">{card?.name ?? cardId}</span>
                <span className="rc-cost">
                  {card?.purchaseCost} {card?.energyTypes[0]}
                </span>
                <span className="rc-left">×{dice.length}</span>
              </button>
            );
          })}
        </div>
      </details>
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
      />
    );
  }

  const link = inviteLink(game.gameId);

  // The contextual action available for whatever's currently selected -
  // computed once, the same way ../ActionTray.tsx builds its `actions`
  // list from the primary die's zone and the current step, instead of a
  // different bespoke panel per feature.
  function selectionAction(): { label: string; run: () => Promise<GameState> } | null {
    if (!primaryDie) return null;
    const secondaryIds = selection.secondary;
    if (step === "roll-and-reroll" && (primaryDie.zone === "PrepArea" || primaryDie.zone === "ReservePool")) {
      const ids = [primaryDie.id, ...secondaryIds];
      return {
        label: `Reroll Selected (${ids.length})`,
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

      {/* Two columns, same shape as /game's main-column + side-column
          (App.css's .app-layout.game-layout): the boards (and the
          Attack Zone each carries inline) are the main column, and
          everything that isn't a die zone - Champion, whose-turn
          controls, the contextual action for whatever's selected - is
          the rail. Direct feedback: Champion used to sit between Field
          Zone and the Attack Zone (they need to line up directly across
          from each other), and the turn controls sat between the two
          boards, right where the actual game is happening. Neither
          belongs in the middle. */}
      <div className="dk-layout">
        <div className="dk-main">
          {renderBoard(opponentId, true)}
          {renderAttackZone()}
          {renderBoard(you, false)}
        </div>

        <div className="dk-rail">
          {/* Active + Invite on one line, then life totals side by side -
              ../TurnRail.tsx's own shape, moved off the table into the
              rail entirely (real feedback: life boxes flanking a phase
              pill above the board were still costing vertical space,
              just differently). */}
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

          {/* Opponent first, then you - same order as the boards below,
              so the rail reads top-to-bottom the same way the table does. */}
          <ChampBanner
            player={opponentId === game.playerOne.id ? game.playerOne : game.playerTwo}
            isActivePlayer={game.activePlayerId === opponentId}
            you={you}
          />
          <ChampBanner
            player={you === game.playerOne.id ? game.playerOne : game.playerTwo}
            isActivePlayer={game.activePlayerId === you}
            you={you}
          />

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
          <BlockPanel
            game={game}
            you={you}
            blocks={blockAssignments}
            setBlocks={setBlockAssignments}
            selection={selection}
            onPickAttacker={(id) => setSelection({ primary: id, secondary: [] })}
            run={run}
          />
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
                  <button className="btn" disabled={busy} onClick={() => run(action.run)}>
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
                  <button className="btn" disabled={busy || (cost !== null && spent < cost.amount)} onClick={() => run(action.run)}>
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

function BlockPanel({
  game,
  you,
  blocks,
  setBlocks,
  selection,
  onPickAttacker,
  run,
}: {
  game: GameState;
  you: string;
  blocks: Record<string, string | null>;
  setBlocks: (u: (b: Record<string, string | null>) => Record<string, string | null>) => void;
  selection: Selection;
  onPickAttacker: (id: string) => void;
  run: (fn: () => Promise<GameState>) => Promise<GameState>;
}) {
  const attackerDice = game.dice.filter((d) => d.zone === "AttackZone");
  const blockers = game.dice.filter((d) => d.controllerId === you && d.zone === "FieldZone");
  const used = new Set(Object.values(blocks).filter((v): v is string => !!v));
  const blockPick = selection.primary;
  return (
    <div className="panel">
      <p>
        <b>Assign blockers.</b> Click an attacker, then a defender to block it (or leave unblocked).
      </p>
      <div className="chiprow">
        {attackerDice.map((a) => (
          <span key={a.id} className={`chip${blockPick === a.id ? " on" : ""}`} onClick={() => onPickAttacker(a.id)}>
            {a.effectiveAttack}/{a.effectiveDefense} ← {blocks[a.id] ? "blocked" : "unblocked"}
          </span>
        ))}
      </div>
      {blockPick && (
        <div className="chiprow">
          <span className="chip" onClick={() => setBlocks((b) => ({ ...b, [blockPick]: null }))}>
            no block
          </span>
          {blockers
            .filter((b) => !used.has(b.id) || blocks[blockPick] === b.id)
            .map((b) => (
              <span
                key={b.id}
                className={`chip${blocks[blockPick] === b.id ? " on" : ""}`}
                onClick={() => setBlocks((prev) => ({ ...prev, [blockPick]: b.id }))}
              >
                {b.effectiveAttack}/{b.effectiveDefense}
              </span>
            ))}
        </div>
      )}
      <button
        className="btn"
        onClick={async () => {
          const assignments = Object.entries(blocks)
            .filter(([, v]) => v)
            .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId: blockerDieId! }));
          await run(() => api.declareBlockers(game.gameId, assignments));
        }}
      >
        Confirm Blocks
      </button>
    </div>
  );
}
