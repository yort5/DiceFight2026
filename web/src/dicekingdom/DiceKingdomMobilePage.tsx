import { startTransition, useEffect, useRef, useState } from "react";
import "./dicekingdom.css";
import { api, apiAs } from "./api";
import {
  ArrowRightIcon,
  CHAMPION_ICONS,
  CHARACTER_ICONS,
  ChevronDownIcon,
  EnergyBadge,
  PhaseIcon,
  TardigradeIcon,
  type PhaseKey,
} from "./icons";
import { claimSeatFromUrl, inviteLink, nameClaimedSeat, rememberSeats } from "./seats";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor } from "./dieFaces";
import { useDiceRoll, type RollTarget } from "./useDiceRoll";
import { decideAttackers, decideBlockers, decideMainAction, decidePendingChoice, decisionOwner } from "./bot";
import type { CardDef, Die, GameState, PlayerState, StatModifier } from "./types";

// Dice Kingdom - mobile refresh (2026-09). A GENUINELY SEPARATE front end
// from ../DiceKingdomPage.tsx, not a responsive breakpoint of it - the
// user's own call (design_handoff_dice_kingdom_mobile/README.md, and
// confirmed directly): "I'm actually fine having two completely separate
// UIs for mobile and browser, at least for now." Reuses the desktop
// page's tokens/icons/DieCube/useDiceRoll (all in this same directory)
// and talks to the exact same v2 API and real engine state - only the
// LAYOUT and interaction model are new. This is a visual refresh, not a
// rules override, with one deliberate exception: the Attack Zone is now
// four fixed lanes (see AttackLanesCard's own remarks and
// DieInstance.Lane in the engine) rather than one ad hoc column per
// attacker.
//
// Two things the design handoff explicitly left unresolved, scoped down
// for this pass (agreed with the user before implementing):
// - The global-ability rail is a visual shell only. No card in
//   InstinctClashConfig grants a Global ability yet and the engine has no
//   priority/pass-window state machine, so there is nothing real to wire
//   a pass ping-pong to - see GlobalRail's own remarks.
// - The step chain (StepLine/StepPopout) is derived from REAL engine
//   step ids and REAL card keywords, never hard-coded. Since no creature
//   in the roster carries Range or Infiltrate yet, those two conditional
//   entries are wired up correctly but never actually appear in practice
//   - see deriveAttackChain's own remarks.

const POLL_INTERVAL_MS = 2000;
// The tumble itself (useDiceRoll.ts's TUMBLE_MS) plus a genuine pause to
// actually read the result, before runWithReveal below moves on to
// whatever phase the reroll response really lands on - direct feedback
// (2026-09-16): "pause for a second to see what the results were."
const TUMBLE_REVEAL_HOLD_MS = 1900;
const CHAMPIONS = [
  { id: "Wolf", energy: "Claw" },
  { id: "Armadillo", energy: "Shell" },
  { id: "GoldenEagle", energy: "Wing" },
  { id: "GreatHornedOwl", energy: "Eye" },
];
const LANE_COUNT = 4;
// Same pacing as ../DiceKingdomPage.tsx's identical "Play vs Computer"
// feature (2026-09-17 port - direct feedback: "how do I actually use the
// automated opponent?" - it only ever existed on desktop). bot.ts's
// decision functions and api.ts's apiAs are already shared, page-
// agnostic modules; only the stateful wiring below (whose turn it is,
// the heartbeat timer, the identity patch) needed porting.
const BOT_MOVE_DELAY_MS = 700;

function rolled(d: Die): boolean {
  return d.effectiveAttack !== null || d.energySymbolId !== null;
}

function nameOf(die: Die, cardsById: Map<string, CardDef>): string {
  if (!die.cardId) return "Tardigrade";
  return cardsById.get(die.cardId)?.name ?? die.cardId;
}

// "base [+ label delta]... = total" - the server already sums this into
// effectiveAttack/effectiveDefense; this just un-collapses it back into
// named line items for display. Direct feedback (2026-09-17): "click on
// the '1 v 3' and have it explain where the numbers are coming from."
function statBreakdown(base: number | null, modifiers: StatModifier[] | null, total: number | null): string | null {
  if (base === null || total === null) return null;
  const mods = modifiers ?? [];
  if (mods.length === 0) return `${base}`;
  const modText = mods.map((m) => ` ${m.delta >= 0 ? "+" : "-"}${Math.abs(m.delta)} (${m.label})`).join("");
  return `${base}${modText} = ${total}`;
}

// How much of THIS ONE attacker's damage reaches the opponent directly -
// zero whenever it's blocked, UNLESS it has Overcrush and clears every
// one of its blockers (rule/CombatEngine.AssignCombatDamage's own
// Overcrush handling - never true for a Tardigrade, which has no cardId
// and therefore no keywords at all). Shared by the lane chip and the
// lane breakdown panel so they can never drift apart on this math.
function unblockedFaceDamage(a: Die, myBlockers: Die[], laneAttackerCount: number, cardsById: Map<string, CardDef>): number {
  const atk = a.effectiveAttack ?? 0;
  if (myBlockers.length === 0) return atk;
  // Direct feedback (2026-09-18): a lane with 2+ live attackers grants
  // EVERY attacker in it Overcrush for this combat, mirroring
  // CombatEngine.AssignCombatDamage's own (deliberately isolated,
  // provisional) `sharesACrowdedLane` condition - not a real keyword,
  // not stat pooling, just this one OR'd-in check.
  const hasOvercrush =
    (a.cardId ? (cardsById.get(a.cardId)?.keywords.includes("Overcrush") ?? false) : false) || laneAttackerCount >= 2;
  if (!hasOvercrush) return 0;
  const blockerDefTotal = myBlockers.reduce((n, b) => n + (b.effectiveDefense ?? 0), 0);
  return Math.max(0, atk - blockerDefTotal);
}

// blockAssignments is keyed by attacker id but holds a LIST of blockers
// (gang-blocking) - this is the one place that shape turns into the
// flat {attackerDieId, blockerDieId} pairs the API/bot actually want,
// one row per blocker, shared by every submission call site so they
// can't drift apart on the flattening.
function blockAssignmentsToApi(assignments: Record<string, string[]>): { attackerDieId: string; blockerDieId: string }[] {
  return Object.entries(assignments).flatMap(([attackerDieId, blockerIds]) =>
    blockerIds.map((blockerDieId) => ({ attackerDieId, blockerDieId })),
  );
}

// Cost is paid from whichever Reserve energy dice cover it - the mobile
// design drops individual Reserve die tiles entirely (the divider rail's
// own remarks: "all we need once we are past the Main step is the energy
// that is left"), showing only the aggregate chip row. The real engine
// still wants specific die ids though, so this picks a legal set behind
// the scenes rather than asking the player to hunt for exact-type dice
// one at a time - there is no meaningful choice being taken away (every
// die of a matching type is fungible for paying a cost).
// Real bug, direct feedback (2026-09-16): "I still can't purchase the
// non-champion energy characters, even though I have a Wild." This used
// to require EVERY spent pip to match the card's own type (or be Wild),
// which silently made an off-type purchase need its ENTIRE cost in
// Wild alone - confirmed wrong against the real rule (TurnEngine.
// SpendEnergy): only ONE offered pip has to satisfy the required type
// (a printed match or a Wild pip standing in for it); every other pip
// spent just counts toward the total amount, of ANY type at all. A
// Claw-heavy reserve can absolutely buy a Wing card, one Wild pip plus
// spare Claim for the rest, same as the physical game.
function pickEnergyForCost(reserve: Die[], cost: number, matchType: string | null): string[] | null {
  if (cost <= 0) return [];
  let pool = reserve.filter((d) => d.energyAmount > 0).sort((a, b) => a.energyAmount - b.energyAmount);
  const picked: string[] = [];
  let total = 0;
  if (matchType) {
    const matchIdx = pool.findIndex((d) => d.energySymbolId === matchType || d.energySymbolId === "Wild");
    if (matchIdx === -1) return null; // nothing at all satisfies the type requirement
    const matchDie = pool[matchIdx];
    picked.push(matchDie.id);
    total += matchDie.energyAmount;
    pool = pool.filter((_, i) => i !== matchIdx);
  }
  for (const d of pool) {
    if (total >= cost) break;
    picked.push(d.id);
    total += d.energyAmount;
  }
  return total >= cost ? picked : null;
}

interface ChainStep {
  label: string;
  conditional?: boolean;
}

// Only these five currentStepId values are ever actually observed as a
// paused, actionable step - every other StepIds entry (attack-effects,
// block-effects, fast-damage, normal-damage, damage-ko-effects, main-end,
// clear-and-draw, cleanup) is walked through server-side inside a single
// action call (CombatEngine/TurnEngine's own EnterStep remarks: "non-
// input steps are walked THROUGH by the method that precedes them").
// Matches ../DiceKingdomPage.tsx's identical STEP_GUIDANCE map.
function phaseForStep(stepId: string): PhaseKey {
  switch (stepId) {
    case "start-of-turn":
      return "clear";
    case "roll-and-reroll":
      return "roll";
    case "main":
      return "main";
    case "select-attackers":
    case "assign-blockers":
    case "action-global-window":
      return "attack";
    default:
      return "cleanup"; // return-to-field
  }
}

const PHASES: { key: PhaseKey; label: string }[] = [
  { key: "clear", label: "Clear & Draw" },
  { key: "roll", label: "Roll & Reroll" },
  { key: "main", label: "Main" },
  { key: "attack", label: "Attack" },
  { key: "cleanup", label: "Clean Up" },
];

// The step chain is derived fresh every render from real board state,
// never hard-coded - README's "the important bit". Two entries (Range
// damage/Infiltrate) only appear when a DECLARED ATTACKER's own card
// actually carries that keyword (read off the real V2CardDefDto.keywords
// the engine reports, not a fixed lookup table). No creature in
// InstinctClashConfig carries either keyword yet (V2_PLAN.md/CardCatalog.cs's
// own "not implemented" notes), so in today's game these two never
// appear - this derivation is correct and ready, not inert set dressing,
// it simply has nothing to react to yet.
//
// Order corrected from the design handoff's own illustrative chain
// ("Declare attackers - Action & globals - [Range] - Assign blockers -
// [Infiltrate] - Damage"): the REAL engine always resolves Assign
// Blockers before the Action/Global window (CombatEngine.DeclareBlockers
// enters ActionGlobalWindow, not the reverse - Rule 2.7's own sequence).
// The design session wasn't necessarily aware of that ordering; this
// follows the real rule, not the mockup.
function deriveAttackChain(dice: Die[], activePlayerId: string, cardsById: Map<string, CardDef>): ChainStep[] {
  const attackers = dice.filter((d) => d.zone === "AttackZone" && d.controllerId === activePlayerId);
  const keywordsOf = (d: Die) => (d.cardId ? cardsById.get(d.cardId)?.keywords ?? [] : []);
  const hasRange = attackers.some((d) => keywordsOf(d).includes("Range"));
  const hasInfiltrate = attackers.some((d) => keywordsOf(d).includes("Infiltrate"));
  const chain: ChainStep[] = [{ label: "Declare attackers" }];
  if (hasRange) chain.push({ label: "Range damage", conditional: true });
  chain.push({ label: "Assign blockers" });
  if (hasInfiltrate) chain.push({ label: "Infiltrate", conditional: true });
  chain.push({ label: "Action & globals" });
  return chain;
}

function chainFor(
  phase: PhaseKey,
  stepId: string,
  hasRolledThisStep: boolean,
  dice: Die[],
  activePlayerId: string,
  cardsById: Map<string, CardDef>,
): { steps: ChainStep[]; index: number } {
  switch (phase) {
    case "clear":
      return { steps: [{ label: "Clear & draw" }], index: 0 };
    case "roll":
      // Real engine only ever exposes one step id here (roll-and-reroll);
      // "Roll" vs "Reroll & place in Reserve" is genuinely all the
      // granularity there is to derive (see TurnEngine.Roll/FinishRoll -
      // there's no separate "committed to Reserve" step to point at).
      return { steps: [{ label: "Roll" }, { label: "Reroll & place in Reserve" }], index: hasRolledThisStep ? 1 : 0 };
    case "main":
      return { steps: [{ label: "Buy & field" }], index: 0 };
    case "attack": {
      const steps = deriveAttackChain(dice, activePlayerId, cardsById);
      const order = ["select-attackers", "assign-blockers", "action-global-window"];
      const realIndex = order.indexOf(stepId);
      // Map the real step onto whichever chain entry it corresponds to -
      // conditional entries never change which REAL step we're on, only
      // how many entries sit between them.
      let index = 0;
      if (stepId === "assign-blockers") index = steps.findIndex((s) => s.label === "Assign blockers");
      else if (stepId === "action-global-window") index = steps.findIndex((s) => s.label === "Action & globals");
      void realIndex;
      return { steps, index: Math.max(0, index) };
    }
    default:
      return { steps: [{ label: "End of turn" }], index: 0 };
  }
}

// The energy-badge glyphs' white-icon-with-a-hard-edged-outline look
// (icons.tsx's EnergyBadge, styled by .dicekingdom .energy-badge svg)
// depends on this filter existing SOMEWHERE in the document under this
// exact id - normally rendered once by ../DiceKingdomPage.tsx, which
// this page never mounts alongside (mutually exclusive routes), so it
// has to render its own copy. Real bug, direct feedback (2026-09-13):
// without it, energy symbols read as "barely visible" - present, just
// with no color/outline separating the glyph from its own badge fill.
function EnergyBadgeOutlineDefs() {
  return (
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
  );
}

// ---- Small shared bits ----

function AvatarGlyph({ die, size = 15, color }: { die: Die; cardsById?: never; size?: number; color: string }) {
  const Avatar = die.cardId ? CHARACTER_ICONS[die.cardId] : null;
  return (
    <span className="dkm-avatar-disc" style={{ width: size, height: size, background: color, color: "#fff8ec" }}>
      {Avatar ? <Avatar size={Math.round(size * 0.62)} /> : <TardigradeIcon size={Math.round(size * 0.62)} />}
    </span>
  );
}

function typeColorOf(die: Die, cardsById: Map<string, CardDef>): string {
  const card = die.cardId ? cardsById.get(die.cardId) : undefined;
  const type = card?.energyTypes[0] ?? die.energySymbolId ?? "Wild";
  return `var(--${type.toLowerCase()})`;
}

// A compact pile stack tile (Used/Prep/Out/opponent-expanded zones) -
// identity only, no stats, matching the repo's existing ICON_ONLY_ZONES
// convention (../DiceKingdomPage.tsx) generalized into a small colored
// "Variant A" badge instead of a plain glyph, per the handoff's own
// stack-tile spec.
function PileStack({ dice, cardsById }: { dice: Die[]; cardsById: Map<string, CardDef> }) {
  const groups = new Map<string, { sample: Die; count: number }>();
  for (const d of dice) {
    const key = d.cardId ?? "tardigrade";
    const g = groups.get(key);
    if (g) g.count += 1;
    else groups.set(key, { sample: d, count: 1 });
  }
  if (groups.size === 0) return <span className="dkm-empty-dash">—</span>;
  return (
    <div className="dkm-tile-row">
      {[...groups.values()].map(({ sample, count }) => {
        const color = typeColorOf(sample, cardsById);
        return (
          <div key={sample.cardId ?? "tardigrade"} className="dkm-stack-tile" style={{ borderColor: color }}>
            <AvatarGlyph die={sample} size={15} color={color} />
            {count > 1 && (
              <span className="dkm-badge" style={{ background: color }}>
                ×{count}
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
}

// The one die tile shape reused everywhere a real rolled face is on show
// (tray after rolling, field, reserve creature faces, attack lanes) - a
// thin wrapper around the repo's real DieCube (same 3D cube /game and
// the desktop Dice Kingdom page use), not a re-implementation.
function DTile({
  die,
  cardsById,
  size,
  mine,
  clickable,
  picked,
  spin,
  turnOffset,
  onClick,
}: {
  die: Die;
  cardsById: Map<string, CardDef>;
  size: number;
  mine: boolean;
  clickable?: boolean;
  picked?: boolean;
  spin?: CubeSpin;
  turnOffset?: number;
  onClick?: () => void;
}) {
  const cls = ["dkm-tile", clickable ? "clickable" : "", picked ? "picked" : ""].filter(Boolean).join(" ");
  return (
    <button type="button" className={cls} onClick={clickable ? onClick : undefined} disabled={!clickable}>
      <DieCube
        {...facesFor(die, cardsById)}
        size={size}
        mine={mine}
        spin={spin}
        turnOffset={turnOffset}
        energyCorner={die.energySymbolId && die.energyAmount > 0 ? { type: die.energySymbolId, amount: die.energyAmount } : undefined}
      />
    </button>
  );
}

// A drawn-but-not-yet-rolled die (DiceFromBag/DiceFromPrep) has no real
// face to show - rather than fake stats on it, this shows identity only,
// same "no real face yet" honesty the desktop page already applies (it
// shows these zones as a bare count, never fake tiles at all).
function FacedownTile({ die, size }: { die: Die; size: number }) {
  const Avatar = die.cardId ? CHARACTER_ICONS[die.cardId] : null;
  return (
    <div className="dkm-tile dkm-facedown" style={{ width: size, height: size }}>
      <span style={{ opacity: 0.55 }}>{Avatar ? <Avatar size={Math.round(size * 0.5)} /> : <TardigradeIcon size={Math.round(size * 0.5)} />}</span>
    </div>
  );
}

function EnergyChips({ dice, size = 16 }: { dice: Die[]; size?: number }) {
  const totals = new Map<string, number>();
  for (const d of dice) {
    if (!d.energySymbolId || d.energyAmount <= 0) continue;
    totals.set(d.energySymbolId, (totals.get(d.energySymbolId) ?? 0) + d.energyAmount);
  }
  if (totals.size === 0) return <span className="dkm-reserve-empty">reserve empty</span>;
  return (
    <div className="dkm-energy-chips">
      {[...totals.entries()].map(([type, amount]) => (
        <span key={type} className="dkm-energy-chip" style={{ borderColor: `var(--${type.toLowerCase()})` }}>
          <EnergyBadge type={type} size={size} />
          <b style={{ color: `var(--${type.toLowerCase()})` }}>{amount}</b>
        </span>
      ))}
    </div>
  );
}

// ---- Header: phase rail + step line ----

function PhaseRail({ current, onTap }: { current: PhaseKey; onTap: (p: PhaseKey) => void }) {
  const currentIndex = PHASES.findIndex((p) => p.key === current);
  return (
    <div className="dkm-phase-rail">
      {PHASES.map((p, i) => {
        const state = i === currentIndex ? "active" : i < currentIndex ? "done" : "upcoming";
        return (
          <button key={p.key} type="button" className={`dkm-phase-pill ${state}`} onClick={() => onTap(p.key)}>
            <PhaseIcon phase={p.key} size={17} />
            {state === "active" && <span className="dkm-phase-label">{p.label}</span>}
          </button>
        );
      })}
    </div>
  );
}

function StepLine({
  title,
  index,
  total,
  onToggle,
}: {
  title: string;
  index: number;
  total: number;
  onToggle: () => void;
}) {
  return (
    <button type="button" className="dkm-step-line" onClick={onToggle}>
      <span className="dkm-step-title">{title}</span>
      <span className="dkm-step-chip">
        step {index + 1}/{total}
        <ChevronDownIcon size={10} />
      </span>
    </button>
  );
}

function StepPopout({
  phaseLabel,
  steps,
  index,
  onClose,
}: {
  phaseLabel: string;
  steps: ChainStep[];
  index: number;
  onClose: () => void;
}) {
  return (
    <div className="dkm-overlay-backdrop" onClick={onClose}>
      <div className="dkm-popout" onClick={(e) => e.stopPropagation()}>
        <div className="dkm-popout-head">
          <span className="dkm-popout-title">{phaseLabel} · Steps</span>
          <button type="button" className="dkm-text-btn" onClick={onClose}>
            Close
          </button>
        </div>
        {steps.map((s, i) => (
          <div key={s.label} className={`dkm-popout-row${i === index ? " current" : ""}`}>
            <span className={`dkm-popout-dot${i < index ? " done" : i === index ? " current" : ""}`} />
            <span className="dkm-popout-label">{s.label}</span>
            {s.conditional && <span className="dkm-dashed-badge">if declared</span>}
          </div>
        ))}
        <p className="dkm-popout-note">This chain is derived from board state each render - it can grow or shrink turn to turn.</p>
      </div>
    </div>
  );
}

// ---- Global ability rail (visual shell only - see file header) ----

function GlobalRail() {
  return (
    <div className="dkm-global-rail">
      <div className="dkm-global-caption">
        <span className="dkm-global-title">Global</span>
        <span className="dkm-global-note">either player</span>
      </div>
      {/* No card in the current roster grants a Global ability yet, so
          there is nothing real to list here - kept as its own component
          (not folded into the mats) so it can move or gain content later
          without touching them, per the handoff's own "provisional"
          callout on this rail's placement. */}
      <div className="dkm-global-empty">No Global abilities available yet.</div>
    </div>
  );
}

// ---- Mat card (opponent / you) ----

function MatCard({
  mine,
  player,
  dice,
  cardsById,
  isActivePlayer,
  onOpenRoster,
  expandable,
  spins,
  turnOffsets,
  selectedId,
  fieldClickable,
  onTapDie,
}: {
  mine: boolean;
  player: PlayerState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  isActivePlayer: boolean;
  onOpenRoster: () => void;
  expandable: boolean;
  spins: Record<string, CubeSpin>;
  turnOffsets: Record<string, number>;
  selectedId: string | null;
  fieldClickable: (d: Die) => boolean;
  onTapDie: (id: string) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [championOpen, setChampionOpen] = useState(false);
  const zone = (name: string) => dice.filter((d) => d.zone === name);
  const field = zone("FieldZone");
  const used = zone("UsedPile");
  const prep = zone("PrepArea");
  const out = zone("OutOfPlay");
  const bag = zone("Bag");
  const reserve = zone("ReservePool");

  // Fixed you=Claw-orange/opp=Eye-purple, independent of either
  // player's actual Champion type - the handoff's own deliberate
  // wayfinding scheme (every "YOU"/"OPP" label, the priority strip, the
  // targeted-lane border all key off this pair, never the champion's
  // energy color). A die's own type still shows through its energy-
  // corner glyph and pile-tile border color, so that information isn't
  // lost, just not doubled up on the mat chrome too.
  const accent = mine ? "var(--claw)" : "var(--eye)";
  const turnClass = isActivePlayer ? " active" : "";
  const ChampIcon = player.champion ? CHAMPION_ICONS[player.champion.id] : null;

  return (
    <div className={`dkm-mat${mine ? " mine" : ""}${turnClass}`} style={{ ["--cc" as string]: accent }}>
      <div className="dkm-mat-head">
        <span className="dkm-mat-label">{mine ? "You" : "Opp"}</span>
        {player.champion && (
          <button type="button" className="dkm-champion-badge" onClick={() => setChampionOpen((v) => !v)}>
            {ChampIcon && <ChampIcon size={16} />}
            <span>{player.champion.name}</span>
          </button>
        )}
        <span className="dkm-mat-life">
          {player.life} <small>life</small>
        </span>
        <EnergyChips dice={reserve} size={mine ? 16 : 15} />
        <span className="dkm-mat-head-actions">
          <button type="button" className="dkm-chip-btn" onClick={onOpenRoster}>
            Roster
          </button>
          {expandable && (
            <button type="button" className="dkm-text-btn" onClick={() => setExpanded((v) => !v)}>
              {expanded ? "Hide piles" : "Show piles"}
            </button>
          )}
        </span>
      </div>

      {/* Real gap, direct feedback (2026-09-17): "once the game is in
          session, I can't tell what either myself or my opponent picked
          as champion" - the champion badge above names WHO, this names
          WHAT IT DOES, since a stat that looks off is often just a
          passive you can't see applying (the same feedback's own
          example: a die reading a little high/low on attack or defense
          because of the OTHER player's champion, not yours). */}
      {championOpen && player.champion && <p className="dkm-champion-passive">{player.champion.passiveText}</p>}

      {!expandable || expanded ? (
        <div className={expandable ? "dkm-mat-expanded" : undefined}>
          <div className="dkm-pile-grid">
            <div className="dkm-pile-cell">
              <span className="dkm-pile-label">Used</span>
              <PileStack dice={used} cardsById={cardsById} />
            </div>
            <div className="dkm-pile-cell">
              <span className="dkm-pile-label">Prep</span>
              <PileStack dice={prep} cardsById={cardsById} />
            </div>
            <div className="dkm-pile-cell">
              <span className="dkm-pile-label">Out</span>
              <PileStack dice={out} cardsById={cardsById} />
            </div>
            <div className="dkm-pile-cell">
              <span className="dkm-pile-label">Bag</span>
              <span className="dkm-bag-count">{bag.length}</span>
            </div>
          </div>
        </div>
      ) : (
        <div className="dkm-collapsed-row">
          <span>
            <b>{used.length}</b> used
          </span>
          <span>
            <b>{prep.length}</b> prep
          </span>
          <span>
            <b>{out.length}</b> out
          </span>
          <span>
            <b>{bag.length}</b> bag
          </span>
        </div>
      )}

      <span className="dkm-field-label">{mine ? "Field" : "Their field · active"}</span>
      <div className="dkm-tile-row wrap">
        {field.length === 0 && <span className="dkm-empty-hint">Nothing fielded.</span>}
        {field.map((d) => (
          <DTile
            key={d.id}
            die={d}
            cardsById={cardsById}
            size={mine ? 50 : 48}
            mine={mine}
            clickable={fieldClickable(d)}
            picked={selectedId === d.id}
            spin={spins[d.id]}
            turnOffset={turnOffsets[d.id]}
            onClick={() => onTapDie(d.id)}
          />
        ))}
      </div>
    </div>
  );
}

// ---- Phase stages ----

function TrayCard({
  title,
  hint,
  dice,
  cardsById,
  rolledYet,
  rerollPicked,
  onToggleReroll,
  spins,
  turnOffsets,
}: {
  title: string;
  hint: string;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  rolledYet: boolean;
  rerollPicked: string[];
  onToggleReroll: (id: string) => void;
  spins: Record<string, CubeSpin>;
  turnOffsets: Record<string, number>;
}) {
  return (
    <div className="dkm-card">
      <div className="dkm-card-head">
        <span className="dkm-card-title accent">{title}</span>
        <span className="dkm-card-hint">{hint}</span>
      </div>
      <div className="dkm-tile-row wrap">
        {dice.length === 0 && <span className="dkm-empty-hint">Tray is empty — draw to fill it.</span>}
        {dice.map((d) =>
          rolledYet ? (
            <button
              key={d.id}
              type="button"
              className={`dkm-tile clickable${rerollPicked.includes(d.id) ? " picked" : ""}`}
              onClick={() => onToggleReroll(d.id)}
            >
              <DieCube
                {...facesFor(d, cardsById)}
                size={58}
                mine
                spin={spins[d.id]}
                turnOffset={turnOffsets[d.id]}
                energyCorner={d.energySymbolId && d.energyAmount > 0 ? { type: d.energySymbolId, amount: d.energyAmount } : undefined}
              />
            </button>
          ) : (
            <FacedownTile key={d.id} die={d} size={58} />
          ),
        )}
      </div>
    </div>
  );
}

function BuyCard({
  unpurchasedByCard,
  cardsById,
  reserve,
  you,
  selectedId,
  onSelect,
  onOpenRoster,
}: {
  unpurchasedByCard: Map<string, Die[]>;
  cardsById: Map<string, CardDef>;
  reserve: Die[];
  you: string;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onOpenRoster: () => void;
}) {
  // ALL rolled reserve dice, not just the character-face ones you could
  // field - direct feedback (2026-09-15): folding the energy-only dice
  // into just the mat header's total left "confusing... I think we need
  // to continue to show all of them" (they're shown as individual tiles
  // everywhere else, Roll & Reroll included). Energy dice render here
  // too now, just not clickable - there's nothing to select them FOR,
  // Purchase/Field auto-picks payment via pickEnergyForCost below.
  const rolledReserve = reserve.filter((d) => rolled(d));
  // Three at a time, same as the handoff's own spec - "FULL ROSTER"
  // opens the sheet for the rest, this card is a quick-buy strip, not
  // the whole roster.
  const visible = [...unpurchasedByCard.entries()].slice(0, 3);
  return (
    <div className="dkm-card">
      <div className="dkm-card-head">
        <span className="dkm-card-title accent">Buy</span>
        <button type="button" className="dkm-text-btn" onClick={onOpenRoster}>
          Full Roster
        </button>
      </div>
      <div className="dkm-buy-row">
        {visible.map(([cardId, dice]) => {
          const card = cardsById.get(cardId);
          const Avatar = CHARACTER_ICONS[cardId];
          const dieId = dice[0].id;
          const affordable = pickEnergyForCost(reserve, card?.purchaseCost ?? 0, card?.energyTypes[0] ?? null) !== null;
          return (
            <button
              key={cardId}
              type="button"
              className={`dkm-buy-tile${selectedId === dieId ? " picked" : ""}${affordable ? "" : " unaffordable"}`}
              onClick={() => onSelect(dieId)}
            >
              <span className="dkm-buy-avatar">{Avatar ? <Avatar size={22} /> : <TardigradeIcon size={22} />}</span>
              <span className="dkm-buy-name">{card?.name ?? cardId}</span>
              <span className="dkm-buy-cost-row">
                {(card?.energyTypes ?? []).map((t) => (
                  <EnergyBadge key={t} type={t} size={11} />
                ))}
                <b className="dkm-buy-cost" style={{ color: `var(--${(card?.energyTypes[0] ?? "claw").toLowerCase()})` }}>
                  {card?.purchaseCost}
                </b>
              </span>
            </button>
          );
        })}
        {unpurchasedByCard.size === 0 && <span className="dkm-empty-hint">Nothing left to buy.</span>}
      </div>
      <span className="dkm-field-label">Reserve</span>
      <div className="dkm-tile-row wrap">
        {rolledReserve.length === 0 && <span className="dkm-empty-hint">Nothing rolled yet.</span>}
        {rolledReserve.map((d) => {
          const fieldable = d.effectiveAttack !== null;
          return (
            <DTile
              key={d.id}
              die={d}
              cardsById={cardsById}
              size={50}
              mine
              picked={selectedId === d.id}
              clickable={fieldable}
              onClick={fieldable ? () => onSelect(d.id) : undefined}
            />
          );
        })}
      </div>
      {void you}
    </div>
  );
}

// The Attack Zone's four fixed lanes - the one deliberate rules/backend
// change in this refresh (see the file header and DieInstance.Lane).
// Several attackers may share a lane now; each still keeps its own
// independent blocker underneath (CombatAssignment is untouched, still
// strictly per-attacker) - a lane is a display grouping, not a pooled-
// damage mechanic, so blocking a SPECIFIC attacker still means tapping
// that specific attacker's own tile, not just "the lane".
// A die sitting in a lane, wrapped so a tap both selects it AND stops the
// tap from also bubbling up to the lane's own onTapLane (DTile's own
// button doesn't take an event, only a plain callback, so this wrapper
// is what actually owns stopPropagation - see its own onClick).
function LaneDie({
  die,
  cardsById,
  you,
  size,
  picked,
  onTap,
}: {
  die: Die;
  cardsById: Map<string, CardDef>;
  you: string;
  size: number;
  picked: boolean;
  onTap: () => void;
}) {
  return (
    <div
      className="dkm-lane-die-wrap"
      onClick={(e) => {
        e.stopPropagation();
        onTap();
      }}
    >
      <DTile die={die} cardsById={cardsById} size={size} mine={die.controllerId === you} clickable picked={picked} />
    </div>
  );
}

// Real bug, direct feedback (2026-09-17): "Dice don't show up properly
// in Attack Zone" - these tiles were a bespoke text-only placeholder
// (a bare number plus a name string), never the same DieCube every
// other zone on this page actually renders dice with. Now uses the
// shared DTile (size shrinks with LaneDie's own tileSize, same as
// before) so an attacker/blocker in a lane looks like the same physical
// die it is everywhere else - full stats, avatar, type-colored border.
function AttackLanesCard({
  isYourTurn,
  step,
  attackersByLane,
  blockersByAttacker,
  cardsById,
  you,
  laneSel,
  onTapLane,
  onTapAttacker,
  onTapBlocker,
  onTapChip,
  selectedId,
}: {
  isYourTurn: boolean;
  step: string;
  attackersByLane: Die[][];
  blockersByAttacker: Map<string, Die[]>;
  cardsById: Map<string, CardDef>;
  you: string;
  laneSel: number;
  onTapLane: (lane: number) => void;
  onTapAttacker: (id: string) => void;
  onTapBlocker: (attackerId: string, blockerId: string) => void;
  onTapChip: (lane: number) => void;
  selectedId: string | null;
}) {
  const totalDeclared = attackersByLane.reduce((n, l) => n + l.length, 0);
  return (
    <div className="dkm-card">
      <div className="dkm-card-head">
        <span className="dkm-card-title accent">Attack Zone</span>
        <span className="dkm-card-hint">
          {totalDeclared > 0 ? `lane ${laneSel + 1} targeted` : "tap a lane to target it"}
        </span>
      </div>
      <div className="dkm-lanes">
        {Array.from({ length: LANE_COUNT }, (_, lane) => {
          const attackers = attackersByLane[lane] ?? [];
          const targeted = lane === laneSel;
          const blockers = attackers.flatMap((a) => blockersByAttacker.get(a.id) ?? []);
          const tileSize = attackers.length <= 1 ? 52 : attackers.length === 2 ? 42 : 34;
          // Direct feedback (2026-09-17): "get rid of '1 v 1'... it's not
          // really helpful. Knowing what will go through 'to face' is
          // still good info." A blocked attacker's damage doesn't reach
          // the opponent at all UNLESS it has Overcrush and clears every
          // one of its blockers (rule/CombatEngine.AssignCombatDamage's
          // own Overcrush handling) - an unblocked attacker always hits
          // face for its full Attack. Full mutual-KO math (both
          // directions) lives in the tap-to-open breakdown now; the chip
          // itself only ever answers this one question.
          const faceDamage = attackers.reduce(
            (n, a) => n + unblockedFaceDamage(a, blockersByAttacker.get(a.id) ?? [], attackers.length, cardsById),
            0,
          );
          const chipText = attackers.length === 0 ? null : `${faceDamage} to face`;
          // Direct feedback (2026-09-17): "making me click on the attacker
          // to block doesn't make sense... tapping anywhere in the lane
          // should do it." Any live attacker in the lane works as the
          // add-blocker target now (addBlocker only ever ADDS, never
          // replaces - direct feedback 2026-09-18 - so which specific
          // attacker id a blocker is nominally paired with no longer
          // matters for a multi-attacker lane the way it used to).
          const tapLane = () => {
            if (step === "assign-blockers" && !isYourTurn && attackers.length >= 1) {
              onTapAttacker(attackers[0].id);
              return;
            }
            onTapLane(lane);
          };
          return (
            <div
              key={lane}
              role="button"
              tabIndex={0}
              className={`dkm-lane${targeted ? " targeted" : ""}`}
              onClick={tapLane}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") tapLane();
              }}
            >
              {/* Real bug, direct feedback (2026-09-17): the opponent's mat
                  renders above the lanes and yours renders below, so a
                  lane's own dice need to sit on the side matching who
                  attacked - otherwise it reads as "the dice on top belong
                  to the mat on top" when it's actually the opposite. The
                  attacking side's dice are always the active player's, so
                  when you're defending (isYourTurn false, active player is
                  the opponent) the attackers belong on top near their mat
                  and your blockers belong on the bottom near yours. */}
              {isYourTurn ? (
                <>
                  <div className="dkm-lane-blockers">
                    {/* Direct feedback (2026-09-17): "there is no visual
                        [cue] to highlight who is the aggressor and who is
                        defending" - a plain text role caption per section,
                        not a color/border cue, so it reads the same
                        regardless of position/orientation or color vision. */}
                    {attackers.length > 0 && <span className="dkm-lane-role def">Blocking</span>}
                    {blockers.map((b) => (
                      <LaneDie key={b.id} die={b} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === b.id} onTap={() => onTapBlocker(attackers[0]?.id ?? "", b.id)} />
                    ))}
                  </div>
                  {chipText && (
                    <span
                      className="dkm-lane-chip tappable"
                      onClick={(e) => {
                        e.stopPropagation();
                        onTapChip(lane);
                      }}
                    >
                      {chipText}
                    </span>
                  )}
                  <div className="dkm-lane-attackers">
                    {attackers.length > 0 && <span className="dkm-lane-role atk">Attacking</span>}
                    {attackers.map((a) => (
                      <LaneDie key={a.id} die={a} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === a.id} onTap={() => onTapAttacker(a.id)} />
                    ))}
                    {attackers.length === 0 && <div className="dkm-lane-tile empty attacker-empty" />}
                  </div>
                </>
              ) : (
                <>
                  <div className="dkm-lane-attackers">
                    {attackers.length > 0 && <span className="dkm-lane-role atk">Attacking</span>}
                    {attackers.map((a) => (
                      <LaneDie key={a.id} die={a} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === a.id} onTap={() => onTapAttacker(a.id)} />
                    ))}
                    {attackers.length === 0 && <div className="dkm-lane-tile empty attacker-empty" />}
                  </div>
                  {chipText && (
                    <span
                      className="dkm-lane-chip tappable"
                      onClick={(e) => {
                        e.stopPropagation();
                        onTapChip(lane);
                      }}
                    >
                      {chipText}
                    </span>
                  )}
                  <div className="dkm-lane-blockers">
                    {attackers.length > 0 && <span className="dkm-lane-role def">Blocking</span>}
                    {blockers.map((b) => (
                      <LaneDie key={b.id} die={b} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === b.id} onTap={() => onTapBlocker(attackers[0]?.id ?? "", b.id)} />
                    ))}
                    {step === "assign-blockers" && !isYourTurn && attackers.length > 0 && blockers.length === 0 && (
                      <div className="dkm-lane-tile empty blocker-empty">no blocker</div>
                    )}
                  </div>
                </>
              )}
              <span className="dkm-lane-number">{String(lane + 1).padStart(2, "0")}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function CleanUpCard({ reserve, cardsById, you }: { reserve: Die[]; cardsById: Map<string, CardDef>; you: string }) {
  void cardsById;
  void you;
  return (
    <div className="dkm-card">
      <div className="dkm-card-head">
        <span className="dkm-card-title accent">End of turn</span>
      </div>
      {reserve.length === 0 ? (
        <span className="dkm-empty-hint">Nothing left in Reserve.</span>
      ) : (
        <div className="dkm-ledger-row">
          <span className="dkm-ledger-count">{reserve.length}</span>
          <span>Reserve</span>
          <ArrowRightIcon size={14} />
          <span>Used</span>
        </div>
      )}
    </div>
  );
}

// Fielding/attack/defense per level, slash-separated - direct feedback
// (2026-09-16): "we still need to put stats in the roster somehow - even
// if it's just with slashes." Neither this sheet nor the Buy strip
// showed a card's actual battlefield stats anywhere, only its purchase
// cost - the one thing you can't tell from a name and an avatar.
function levelStatsLine(card: CardDef | undefined): string {
  if (!card) return "";
  return card.levels.map((l) => `${l.fieldingCost}/${l.attack}/${l.defense}`).join("  ·  ");
}

function RosterSheet({
  title,
  cards,
  canBuy,
  reserve,
  onBuy,
  onClose,
}: {
  title: string;
  cards: { card: CardDef | undefined; cardId: string; dieId: string; remaining: number }[];
  /** Only true for your OWN roster, during Main, on your turn - see the
   *  real bug this fixed (2026-09-16): every "Roster" button on the page
   *  used to open this same sheet always showing YOUR cards, so tapping
   *  the OPPONENT's button silently showed (and, once buying was added
   *  here, would have let you buy from) your own roster instead of
   *  theirs. `cards`/`canBuy` are now both keyed off which button was
   *  actually tapped (DiceKingdomMobilePage's `rosterViewFor`). */
  canBuy: boolean;
  reserve: Die[];
  onBuy: (dieId: string) => void;
  onClose: () => void;
}) {
  return (
    <div className="dkm-overlay-backdrop" onClick={onClose}>
      <div className="dkm-sheet" onClick={(e) => e.stopPropagation()}>
        <div className="dkm-sheet-handle" />
        <div className="dkm-popout-head">
          <span className="dkm-popout-title">{title}</span>
          <button type="button" className="dkm-text-btn" onClick={onClose}>
            tap to close
          </button>
        </div>
        {/* Neither number column said what it was - direct feedback
            (2026-09-15). A one-time header instead of repeating a label
            on all eight rows; "left" gets the fuller "dice left" since
            "cost" alone is self-evident next to a purchase price but
            "left" alone reads ambiguous out of context. */}
        <div className="dkm-roster-row dkm-roster-head">
          <span className="dkm-roster-avatar" />
          <div className="dkm-roster-mid" />
          <span className="dkm-roster-col-label">Cost</span>
          <span className="dkm-roster-col-label">Dice left</span>
        </div>
        {cards.length === 0 && <p className="dkm-empty-hint">Nothing left to buy.</p>}
        {cards.map(({ card, cardId, dieId, remaining }) => {
          const Avatar = CHARACTER_ICONS[cardId];
          const types = card?.energyTypes ?? [];
          const affordable = canBuy && pickEnergyForCost(reserve, card?.purchaseCost ?? 0, types[0] ?? null) !== null;
          const row = (
            <>
              <span className="dkm-roster-avatar">{Avatar ? <Avatar size={20} /> : <TardigradeIcon size={20} />}</span>
              <div className="dkm-roster-mid">
                <span className="dkm-roster-name">{card?.name ?? cardId}</span>
                {card && card.keywords.length > 0 && <span className="dkm-dashed-badge">{card.keywords.join(", ")}</span>}
                <span className="dkm-roster-stats">{levelStatsLine(card)}</span>
              </div>
              {/* One EnergyBadge per required type - usually one, two for
                  a crossover/splash card - so the cost number is never
                  shown without saying what it's a cost OF. */}
              <span className="dkm-roster-cost-wrap">
                {types.map((t) => (
                  <EnergyBadge key={t} type={t} size={12} />
                ))}
                <b className="dkm-roster-cost" style={{ color: `var(--${(types[0] ?? "claw").toLowerCase()})` }}>
                  {card?.purchaseCost}
                </b>
              </span>
              <span className="dkm-roster-left">{remaining}</span>
            </>
          );
          return canBuy ? (
            <button
              key={cardId}
              type="button"
              className={`dkm-roster-row dkm-roster-row-btn${affordable ? "" : " unaffordable"}`}
              disabled={!affordable}
              onClick={() => {
                onBuy(dieId);
                onClose();
              }}
            >
              {row}
            </button>
          ) : (
            <div key={cardId} className="dkm-roster-row">
              {row}
            </div>
          );
        })}
      </div>
    </div>
  );
}

// ---- Main page ----

export function DiceKingdomMobilePage() {
  const [game, setGame] = useState<GameState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [setupA, setSetupA] = useState<string | null>(null);
  const [setupB, setSetupB] = useState<string | null>(null);
  // Player Two becomes a basic rule-based opponent instead of a second
  // human seat - see bot.ts, and ../DiceKingdomPage.tsx's identical
  // vsComputer for the fuller remarks (fixed to player two rather than
  // "whichever seat I didn't claim" since vs-computer games never go
  // through the invite-link claim flow at all - both tokens stay in this
  // one browser, same as ordinary pass-and-play).
  const [vsComputer, setVsComputer] = useState(false);
  const gameRef = useRef<GameState | null>(null);
  useEffect(() => {
    gameRef.current = game;
  }, [game]);
  const botActingRef = useRef(false);
  // Unpurchased/fieldable dice the bot tried and had rejected this turn
  // (a legality rule bot.ts doesn't model) - reset each new turn so it
  // isn't permanently blacklisted.
  const botSkipIdsRef = useRef<Set<string>>(new Set());
  useEffect(() => {
    botSkipIdsRef.current = new Set();
  }, [game?.activePlayerId]);
  const [cardsById, setCardsById] = useState<Map<string, CardDef>>(new Map());

  const [selectedId, setSelectedId] = useState<string | null>(null);
  // Which lane's Attack Zone chip is showing its stat breakdown, or null -
  // direct feedback (2026-09-17): "click on the '1 v 3' and have it
  // explain where the numbers are coming from." Mutually exclusive with
  // selectedId (both use the same bottom-bar panel slot).
  const [laneBreakdown, setLaneBreakdown] = useState<number | null>(null);
  const [rerollPicked, setRerollPicked] = useState<string[]>([]);
  const [rerollUsedThisStep, setRerollUsedThisStep] = useState(false);
  const [pendingAttackers, setPendingAttackers] = useState<Record<string, number>>({});
  const [laneSel, setLaneSel] = useState(0);
  // Real bug, direct feedback (2026-09-18): a single blocker id per
  // attacker meant assigning a SECOND blocker to a lane (gang-blocking -
  // the backend's own CombatAssignment already supports it, a blocker
  // is just never limited to one per attacker there) silently REPLACED
  // the first one instead of adding to it - tapping an existing blocker
  // "swapped" it back to the field. Now a list per attacker key.
  const [blockAssignments, setBlockAssignments] = useState<Record<string, string[]>>({});
  const [stepsOpen, setStepsOpen] = useState(false);
  // Which player's roster the sheet is showing, or null when closed -
  // NOT a bare boolean (real bug, direct feedback 2026-09-16): both
  // mats' Roster buttons used to open the exact same sheet, which always
  // showed YOUR OWN unpurchased cards regardless of which one was
  // tapped, so the opponent's button silently showed your roster.
  const [rosterViewFor, setRosterViewFor] = useState<string | null>(null);
  // A brief confirmation that startMatch's auto-copy (below) actually
  // landed - clipboard writes can silently fail (permissions, an
  // unsupported browser), so this only shows on the real success
  // callback, not just "we tried." Self-clears; the persistent Invite
  // row above the Log is still there afterward for a second copy.
  const [inviteCopiedBanner, setInviteCopiedBanner] = useState(false);
  useEffect(() => {
    if (!inviteCopiedBanner) return;
    const timer = window.setTimeout(() => setInviteCopiedBanner(false), 4000);
    return () => window.clearTimeout(timer);
  }, [inviteCopiedBanner]);

  const { spins, offsets, launch: launchRoll, spinTo: spinDie } = useDiceRoll();

  useEffect(() => {
    api.getCards().then((cards) => setCardsById(new Map(cards.map((c) => [c.id, c]))));
  }, []);

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
        // quiet - next poll either works or doesn't matter yet
      }
    }, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [gameId, gameVersion]);

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
      // Split across two commits/frames (2026-09-16, direct feedback:
      // "the dice rolling seems a bit choppy") - real, measured cause (a
      // Chrome trace during a roll, not a guess): setGame(next) re-
      // renders the WHOLE page (a CDP trace showed a single Layout pass
      // touching 329 of 462 DOM nodes, React's own scheduler blocking
      // the main thread for 40-80ms in one chunk) - calling
      // animateRolledDice in the SAME tick used to bundle starting the
      // tumble's CSS animation into that exact same expensive commit, so
      // its first frame was competing with the heaviest possible layout
      // pass for the same paint budget. startTransition lets React chunk
      // its own reconciliation instead of blocking in one piece; the
      // rAF defers the animation start to the NEXT frame, after the
      // data commit has already had a frame to settle, rather than
      // stacking both into one. Confirmed with a rAF frame-timing probe
      // across several runs, not just by eye.
      startTransition(() => setGame(next));
      if (previous) requestAnimationFrame(() => animateRolledDice(previous, next, rolledDieIds));
      setSelectedId(null);
      setLaneBreakdown(null);
      if (next.currentStepId !== "roll-and-reroll") {
        setRerollPicked([]);
        setRerollUsedThisStep(false);
      }
      if (next.currentStepId !== "select-attackers") setPendingAttackers({});
      // Pairings must survive into action-global-window: the server doesn't
      // persist them, and assignCombatDamage([]) treats every attacker as
      // unblocked (so nothing is ever KO'd).
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

  // A reroll ends the WHOLE Roll & Reroll step server-side, in one shot -
  // the real tabletop rule ("you get one reroll decision, and taking it
  // ends the step"), confirmed against the live engine (2026-09-16): the
  // /reroll response already carries currentStepId "main". Calling the
  // ordinary run() above for it used to swap this page's phase-stage
  // card straight from the tray to Buy & Field the instant that response
  // landed - maybe 150ms into the tumble's 900ms - direct feedback: "the
  // dice disappear right away and you can't see it." This holds the OLD
  // phase on screen (so the tray stays mounted and the tumble has
  // somewhere to land that's actually visible) while adopting the NEW
  // dice data (so it tumbles to the real result), then waits out the
  // tumble plus a genuine pause to actually read it before revealing
  // the real state underneath. The poll in the effect above already
  // skips itself while `busy` is true, so it can't race in with the
  // real state early and cut the hold short.
  async function runWithReveal(fn: () => Promise<GameState>, revealedDieIds: string[]) {
    setBusy(true);
    busyRef.current = true;
    setError(null);
    try {
      const previous = game;
      const next = await fn();
      if (!previous) {
        setGame(next);
        return next;
      }
      startTransition(() => setGame({ ...next, currentStep: previous.currentStep, currentStepId: previous.currentStepId }));
      // Deferred a frame - see run()'s identical remarks on why.
      requestAnimationFrame(() => animateRolledDice(previous, next, revealedDieIds));
      setSelectedId(null);
      setLaneBreakdown(null);
      setRerollPicked([]);
      setRerollUsedThisStep(true);
      await new Promise((resolve) => setTimeout(resolve, TUMBLE_REVEAL_HOLD_MS));
      startTransition(() => setGame(next));
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
  // hold the seat for - see the auto-skip effect below. In ordinary
  // pass-and-play that's just "the other tab doesn't have the token, a
  // 403 is expected." In vs-computer mode it can ALSO go out via apiAs
  // as the computer's own seat (the auto-skip effect uses decisionOwner,
  // which can name either player) - the human is always Player One
  // there (see vsComputer's own remarks), so undo the resulting
  // yourPlayerId flip the same way runBot does, or "You"/"Opp" swap on
  // screen until the next human action self-corrects it.
  async function runQuiet(fn: () => Promise<GameState>) {
    if (busyRef.current) return;
    busyRef.current = true;
    try {
      const raw = await fn();
      const next = vsComputer ? { ...raw, yourPlayerId: raw.playerOne.id } : raw;
      setGame(next);
    } catch {
      // expected on whichever browser doesn't hold the seat this needed
    } finally {
      busyRef.current = false;
    }
  }

  // Same auto-skip-through-an-empty-window behavior as the desktop page
  // and /game before it - see ../DiceKingdomPage.tsx's identical effect,
  // including why this goes through apiAs(gameId, owner) rather than the
  // shared `api`: in vs-computer mode this one browser holds both
  // tokens, and whichever of these two steps needs the COMPUTER's token
  // would otherwise always be submitted as the human instead and 403
  // forever with nothing left to retry it.
  const assignBlockersAttackerCount = game
    ? game.dice.filter((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId).length
    : 0;
  useEffect(() => {
    if (!gameId || !game) return;
    const owner = decisionOwner(game);
    if (!owner) return;
    const client = apiAs(gameId, owner);
    if (game.currentStepId === "assign-blockers" && assignBlockersAttackerCount === 0) {
      runQuiet(() => client.declareBlockers(gameId, []));
    } else if (game.currentStepId === "action-global-window" && blockAssignmentsToApi(blockAssignments).length === 0) {
      runQuiet(() => client.assignCombatDamage(gameId, []));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gameId, game?.version, game?.currentStepId, assignBlockersAttackerCount]);

  // Same shape as run(), for the computer opponent - see
  // ../DiceKingdomPage.tsx's identical runBot for why failures here are
  // routine (console.warn, not the human-facing error banner) and why
  // yourPlayerId gets patched back to Player One.
  async function runBot(fn: () => Promise<GameState>): Promise<GameState | null> {
    if (busyRef.current) return null;
    setBusy(true);
    busyRef.current = true;
    try {
      const previous = game;
      const raw = await fn();
      const next = { ...raw, yourPlayerId: raw.playerOne.id };
      startTransition(() => setGame(next));
      if (previous) requestAnimationFrame(() => animateRolledDice(previous, next));
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
  // decisions, and ../DiceKingdomPage.tsx's identical performBotAction
  // for the fuller remarks (re-reading gameRef.current rather than
  // trusting whatever triggered the scheduling effect, since a deliberate
  // pacing delay means the game may have already moved past this step by
  // the time it actually fires).
  async function performBotAction(botId: string) {
    const g = gameRef.current;
    if (!g || decisionOwner(g) !== botId) return;
    const gid = g.gameId;
    const step = g.currentStepId;
    const botApi = apiAs(gid, botId);

    if (g.pendingChoice) {
      await runBot(() => botApi.resolvePendingChoice(gid, decidePendingChoice(g)));
      return;
    }
    if (step === "start-of-turn") {
      await runBot(() => botApi.clearAndDraw(gid));
      return;
    }
    if (step === "roll-and-reroll") {
      const hasRolled = g.dice.some(
        (d) => d.controllerId === botId && (d.zone === "PrepArea" || d.zone === "ReservePool") && rolled(d),
      );
      await runBot(() => (hasRolled ? botApi.finishRoll(gid) : botApi.roll(gid)));
      return;
    }
    if (step === "main") {
      const decision = decideMainAction(g, botId, cardsById, botSkipIdsRef.current);
      if (decision.kind === "enterAttackStep") {
        await runBot(() => botApi.enterAttackStep(gid));
        return;
      }
      const result =
        decision.kind === "field"
          ? await runBot(() => botApi.field(gid, decision.dieId, decision.energyDieIds))
          : await runBot(() => botApi.purchase(gid, decision.dieId, decision.energyDieIds));
      if (!result) botSkipIdsRef.current.add(decision.dieId);
      return;
    }
    if (step === "select-attackers") {
      const result = await runBot(() => botApi.declareAttackers(gid, decideAttackers(g, botId)));
      if (!result) await runBot(() => botApi.declareAttackers(gid, []));
      return;
    }
    if (step === "assign-blockers") {
      const assignments = decideBlockers(g, botId);
      const map: Record<string, string[]> = {};
      for (const a of assignments) (map[a.attackerDieId] ??= []).push(a.blockerDieId);
      setBlockAssignments(map);
      const result = await runBot(() => botApi.declareBlockers(gid, assignments));
      if (!result) {
        setBlockAssignments({});
        await runBot(() => botApi.declareBlockers(gid, []));
      }
      return;
    }
    if (step === "action-global-window") {
      const assignments = blockAssignmentsToApi(blockAssignments);
      // The empty case is handled generically by the auto-skip effect
      // above - only step in here for a real pairing.
      if (assignments.length === 0) return;
      await runBot(() => botApi.assignCombatDamage(gid, assignments));
      return;
    }
    if (step === "return-to-field") {
      await runBot(() => botApi.cleanUp(gid));
    }
  }

  // A heartbeat, not a one-shot scheduled off `game`'s own dependencies -
  // see ../DiceKingdomPage.tsx's identical effect for why (a failed
  // no-op action would otherwise leave `game` unchanged and the
  // scheduling effect would never fire again, freezing the computer's
  // turn permanently).
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
    const next = await run(async () => {
      const created = await api.createGame(setupA, setupB);
      rememberSeats(created.game.gameId, created.seats);
      return created.game;
    });
    // Copies the invite link the moment the match exists, right off the
    // Start Match tap - direct feedback (2026-09-17), asked for alongside
    // moving the persistent Invite row down to above the Log: "maybe we
    // could also include it on the 'Start Match' screen." There's no
    // link before a game exists to build one from, so this is the
    // closest real equivalent - the very first thing that happens after
    // starting is the link already being in your clipboard, ready to
    // send, rather than needing to scroll down to find the button.
    if (!vsComputer) {
      const link = inviteLink(next.gameId, "/dice-kingdom/mobile");
      if (link) {
        navigator.clipboard?.writeText(link).then(
          () => setInviteCopiedBanner(true),
          () => {}, // clipboard write blocked - the manual button above the log still works
        );
      }
    }
  }

  if (!game) {
    return (
      <div className="dicekingdom dk-mobile dkm-root">
        <EnergyBadgeOutlineDefs />
        <p className="dkm-eyebrow">DiceFight v3 · mobile</p>
        <h1 className="dkm-title">Dice Kingdom</h1>
        <p className="dkm-dek">Pick a Champion for each seat, then send the invite link from inside the match.</p>
        {error && <p className="dkm-error">{error}</p>}
        {/* Same two-column layout as ../DiceKingdomPage.tsx's own picker
            (direct feedback, 2026-09-14: "makes more sense to have them
            in two columns") - reuses that page's own .champ-pick-columns/
            .champ-opt classes verbatim rather than a mobile-specific
            reimplementation, now that this page carries the .dicekingdom
            class those are scoped under (see the energy-badge fix's own
            remarks on why that class was added here). */}
        <div className="panel">
          <label style={{ display: "flex", alignItems: "center", gap: 8, margin: "0 0 14px", fontSize: 14 }}>
            <input type="checkbox" checked={vsComputer} onChange={(e) => setVsComputer(e.target.checked)} />
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
                <div className="champ-pick">
                  {CHAMPIONS.map((c) => {
                    const Icon = CHAMPION_ICONS[c.id];
                    return (
                      <button
                        key={c.id}
                        type="button"
                        className={`champ-opt${value === c.id ? " selected" : ""}`}
                        style={{ ["--sel" as string]: `var(--${c.energy.toLowerCase()})`, color: `var(--${c.energy.toLowerCase()})` }}
                        onClick={() => setValue(c.id)}
                      >
                        <Icon />
                        <div className="cname" style={{ color: "var(--text-h)" }}>
                          {c.id.replace(/([A-Z])/g, " $1").trim()}
                        </div>
                      </button>
                    );
                  })}
                </div>
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
  const phase = phaseForStep(step);
  const phaseLabel = PHASES.find((p) => p.key === phase)!.label;

  const youPlayer = you === game.playerOne.id ? game.playerOne : game.playerTwo;
  const oppPlayer = opponentId === game.playerOne.id ? game.playerOne : game.playerTwo;
  const yourDice = game.dice.filter((d) => d.controllerId === you);
  const oppDice = game.dice.filter((d) => d.controllerId === opponentId);
  const yourReserve = yourDice.filter((d) => d.zone === "ReservePool");

  // Direct feedback (2026-09-18): tapping a die to declare/block should
  // visually move it out of the Field row into the Attack Zone, not
  // leave it sitting in both places - the real die doesn't actually
  // change zone server-side until the whole declare/block batch is
  // submitted (AttackLanesCard's own remarks), so this is purely a
  // local-preview filter on top of it. Covers blockers (what was
  // reported) and attacker declarations (the identical gap - a
  // declared-but-not-yet-submitted attacker had the same problem).
  const pendingLocallyMovedIds = new Set<string>(
    step === "select-attackers"
      ? Object.keys(pendingAttackers)
      : step === "assign-blockers"
        ? Object.values(blockAssignments).flat()
        : [],
  );
  const yourFieldVisibleDice = yourDice.filter((d) => !pendingLocallyMovedIds.has(d.id));

  const drawnZone = yourDice.filter((d) => d.zone === "DiceFromBag" || d.zone === "DiceFromPrep");
  const hasRolledThisStep = step === "roll-and-reroll" && drawnZone.length === 0;
  const { steps: chainSteps, index: chainIndex } = chainFor(phase, step, hasRolledThisStep || rerollUsedThisStep, game.dice, game.activePlayerId, cardsById);

  function unpurchasedFor(dice: Die[]): Map<string, Die[]> {
    const map = new Map<string, Die[]>();
    for (const d of dice.filter((d) => d.zone === "Unpurchased")) {
      if (!d.cardId) continue;
      map.set(d.cardId, [...(map.get(d.cardId) ?? []), d]);
    }
    return map;
  }
  const unpurchasedByCard = unpurchasedFor(yourDice);
  const oppUnpurchasedByCard = unpurchasedFor(oppDice);

  // Attackers grouped by lane for the real (post-declare) steps; during
  // Declare Attackers itself the lanes preview the LOCAL pending picks
  // instead, since the real engine only moves dice into the Attack Zone
  // once the whole batch is submitted (see AttackLanesCard's own remarks
  // and pendingAttackers above) - one atomic DeclareAttackers call, not
  // one attacker at a time.
  const attackersByLane: Die[][] = Array.from({ length: LANE_COUNT }, () => []);
  if (step === "select-attackers") {
    for (const [dieId, lane] of Object.entries(pendingAttackers)) {
      const die = yourDice.find((d) => d.id === dieId);
      if (die) attackersByLane[lane]?.push(die);
    }
  } else {
    for (const d of game.dice) {
      if (d.zone === "AttackZone" && d.lane !== null) attackersByLane[d.lane]?.push(d);
    }
  }
  // A blocker's own attacker id isn't on the DTO directly - built here
  // from blockAssignments instead, still held locally through Action &
  // Globals since CombatAssignment isn't persisted server-side (same
  // resend-it-every-call pattern ../DiceKingdomPage.tsx already uses).
  // Nothing to show yet during Declare Attackers itself.
  const blockersByAttacker = new Map<string, Die[]>();
  if (step !== "select-attackers") {
    for (const [attackerId, blockerIds] of Object.entries(blockAssignments)) {
      const blockers = blockerIds.map((id) => game.dice.find((d) => d.id === id)).filter((d): d is Die => !!d);
      if (blockers.length > 0) blockersByAttacker.set(attackerId, blockers);
    }
  }
  const laneAttackersForBreakdown = laneBreakdown !== null ? (attackersByLane[laneBreakdown] ?? []) : [];

  function costFor(die: Die): { amount: number; matchType: string | null } {
    if (die.zone === "Unpurchased") {
      const card = die.cardId ? cardsById.get(die.cardId) : undefined;
      return { amount: card?.purchaseCost ?? 0, matchType: card?.energyTypes[0] ?? null };
    }
    if (!die.cardId || die.level === null) return { amount: 0, matchType: null };
    const card = die.cardId ? cardsById.get(die.cardId) : undefined;
    return { amount: card?.levels[die.level - 1]?.fieldingCost ?? 0, matchType: null };
  }

  function toggleReroll(id: string) {
    setRerollPicked((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function toggleSelect(id: string) {
    setLaneBreakdown(null);
    setSelectedId((cur) => (cur === id ? null : id));
  }

  function toggleLaneBreakdown(lane: number) {
    setSelectedId(null);
    setLaneBreakdown((cur) => (cur === lane ? null : lane));
  }

  function toggleAttacker(dieId: string) {
    setPendingAttackers((prev) => {
      const next = { ...prev };
      if (dieId in next) delete next[dieId];
      else next[dieId] = laneSel;
      return next;
    });
  }

  // Direct feedback (2026-09-17): "I would like to be able to simply
  // click on the fielded die and then click on the desired lane to
  // declare that die attacking in that lane... Right now I have to
  // click the die, click the lane, then scroll down to click the action
  // button." Tapping a lane while a selectable FieldZone die of yours is
  // selected now declares it directly (or re-lanes it, if it was already
  // pending elsewhere) - the "Declare into lane N"/"Pull back" inspect
  // action still exists for pulling an attacker back out.
  function declareIntoLane(lane: number) {
    setLaneSel(lane);
    if (selectedDie && selectedDie.zone === "FieldZone" && selectedDie.controllerId === you) {
      setPendingAttackers((prev) => ({ ...prev, [selectedDie.id]: lane }));
      setSelectedId(null);
    }
  }

  // Direct feedback (2026-09-18): "tapping for the second blocker
  // doesn't appear to work... it just swaps the die in the field zone
  // with the die already blocking." Gang-blocking (several of your own
  // dice on one lane) is a real, already-supported thing server-side -
  // this needs to ADD to whichever blockers are already there, never
  // replace them. A blocker can still only ever be in ONE lane at a
  // time, so it's pulled out of any other attacker's list first.
  function addBlocker(attackerId: string, blockerId: string) {
    setBlockAssignments((prev) => {
      const next: Record<string, string[]> = {};
      for (const [aid, bids] of Object.entries(prev)) {
        const filtered = bids.filter((id) => id !== blockerId);
        if (filtered.length > 0) next[aid] = filtered;
      }
      next[attackerId] = [...(next[attackerId] ?? []), blockerId];
      return next;
    });
    setSelectedId(null);
  }

  // Removes a blocker from wherever it's currently assigned (its own
  // lane pairing doesn't matter for this - it's only ever in one place).
  function removeBlocker(blockerId: string) {
    setBlockAssignments((prev) => {
      const next: Record<string, string[]> = {};
      for (const [aid, bids] of Object.entries(prev)) {
        const filtered = bids.filter((id) => id !== blockerId);
        if (filtered.length > 0) next[aid] = filtered;
      }
      return next;
    });
  }

  const selectedDie = selectedId ? game.dice.find((d) => d.id === selectedId) ?? null : null;

  // The inspect panel's action list - README's own table, mapped onto
  // the actions this page can actually take right now.
  type InspectAction = { label: string; run: () => void; primary?: boolean };
  const inspectActions: InspectAction[] = [];
  if (selectedDie) {
    if (selectedDie.zone === "Unpurchased" && step === "main" && isYourTurn) {
      const { amount, matchType } = costFor(selectedDie);
      const ids = pickEnergyForCost(yourReserve, amount, matchType);
      inspectActions.push({
        label: ids === null ? "Can't afford" : `Purchase (${amount})`,
        run: () => {
          if (ids !== null) run(() => api.purchase(game.gameId, selectedDie.id, ids));
        },
      });
    }
    if (selectedDie.zone === "ReservePool" && rolled(selectedDie) && selectedDie.effectiveAttack !== null && step === "main" && isYourTurn) {
      const { amount, matchType } = costFor(selectedDie);
      const ids = pickEnergyForCost(yourReserve, amount, matchType);
      inspectActions.push({
        label: ids === null ? "Can't afford" : "Field this creature",
        run: () => {
          if (ids !== null) run(() => api.field(game.gameId, selectedDie.id, ids));
        },
      });
    }
    if (selectedDie.zone === "FieldZone" && selectedDie.controllerId === you && step === "select-attackers" && isYourTurn) {
      const already = selectedDie.id in pendingAttackers;
      inspectActions.push({
        label: already ? "Pull back" : `Declare into lane ${laneSel + 1}`,
        run: () => toggleAttacker(selectedDie.id),
      });
    }
    if (selectedDie.zone === "FieldZone" && selectedDie.controllerId === you && step === "assign-blockers" && !isYourTurn) {
      inspectActions.push({
        label: `Tap an attacker to block into lane ${laneSel + 1}`,
        run: () => {},
      });
    }
  }

  const onTapMatDie = (id: string) => {
    if (step === "assign-blockers" && !isYourTurn) {
      const die = game.dice.find((d) => d.id === id);
      if (die && die.controllerId === you && die.zone === "FieldZone") {
        toggleSelect(id);
        return;
      }
    }
    toggleSelect(id);
  };

  const onTapAttacker = (attackerId: string) => {
    if (step === "assign-blockers" && !isYourTurn && selectedId) {
      const blockerDie = game.dice.find((d) => d.id === selectedId);
      if (blockerDie && blockerDie.controllerId === you && blockerDie.zone === "FieldZone") {
        addBlocker(attackerId, selectedId);
        return;
      }
    }
    // A lane that already has an attacker fills most of its own tap
    // target with that die's tile, so tapping the lane again to add a
    // SECOND attacker there (declareIntoLane's own remarks) actually
    // lands on this existing tile. Read that the same way when a
    // DIFFERENT fielded die of ours is the one currently selected -
    // "add it to this lane" - rather than just reselecting the die
    // that's already here.
    if (
      step === "select-attackers" && isYourTurn && selectedId && selectedId !== attackerId &&
      attackerId in pendingAttackers
    ) {
      const selected = game.dice.find((d) => d.id === selectedId);
      if (selected && selected.zone === "FieldZone" && selected.controllerId === you) {
        setPendingAttackers((prev) => ({ ...prev, [selectedId]: pendingAttackers[attackerId] }));
        setSelectedId(null);
        return;
      }
    }
    toggleSelect(attackerId);
  };

  // Direct feedback (2026-09-18): "if I tap the die in the field zone
  // and then tap an existing blocker, it should add that die to that
  // lane" - previously this bare-toggled the tapped blocker's own
  // selection instead, abandoning whatever field die was selected.
  // Tapping an already-SELECTED blocker again removes it (matches
  // onTapAttacker's own "declared already -> pull it back" pattern for
  // attackers, now expressed per-blocker instead of clearing the whole
  // lane at once).
  const onTapBlocker = (attackerId: string, blockerId: string) => {
    if (step === "assign-blockers" && !isYourTurn) {
      if (selectedId && selectedId !== blockerId) {
        const selected = game.dice.find((d) => d.id === selectedId);
        if (selected && selected.zone === "FieldZone" && selected.controllerId === you) {
          addBlocker(attackerId, selectedId);
          return;
        }
      }
      if (selectedId === blockerId) {
        removeBlocker(blockerId);
        setSelectedId(null);
        return;
      }
    }
    toggleSelect(blockerId);
  };

  // ---- Primary button ----
  let primaryLabel = "";
  let primaryNote: string | undefined;
  let primaryDisabled = busy;
  let primaryRun: (() => void) | null = null;
  // Main's own secondary action - real bug, direct feedback (2026-09-16):
  // "we lost the ability to skip the attack step." ../DiceKingdomPage.tsx
  // has always paired "Attack"/"Skip Attack" as two buttons right on
  // Main (its own comment: brought back after 2026-09-05 feedback asked
  // for it once already) - this page only ever built the "Attack" half
  // (as the primary button) and never added the second. The server side
  // (TurnEngine.SkipAttackStep) still rejects it with a real error if a
  // forced attacker is outstanding, same as desktop - no client-side
  // gating needed beyond the step check already gating the primary.
  let secondaryLabel: string | undefined;
  let secondaryRun: (() => void) | null = null;

  if (!isYourTurn && step !== "assign-blockers") {
    primaryLabel = "Waiting…";
    primaryNote = `${oppPlayer.name} is acting`;
    primaryDisabled = true;
  } else if (step === "start-of-turn") {
    primaryLabel = "Draw four";
    primaryRun = () => run(() => api.clearAndDraw(game.gameId));
  } else if (step === "roll-and-reroll") {
    if (!hasRolledThisStep) {
      primaryLabel = "Roll";
      // Real bug, found while verifying the tumble animation (2026-09-16):
      // `yourReserve` is empty until AFTER this call resolves (Roll()
      // moves dice from DiceFromBag/DiceFromPrep straight into
      // ReservePool - see TurnEngine.Roll's own remarks), so passing it
      // here always named zero dice, meaning animateRolledDice's
      // `explicit` set was always empty and every rolled die fell
      // through to the "spin" (twist) animation instead of the real
      // tumble - unnoticed until there was a real tumble to compare
      // against. `drawnZone` is exactly the set about to be rolled.
      primaryRun = () => run(() => api.roll(game.gameId), drawnZone.map((d) => d.id));
    } else if (rerollPicked.length > 0) {
      primaryLabel = `Reroll (${rerollPicked.length})`;
      primaryRun = () => runWithReveal(() => api.reroll(game.gameId, rerollPicked), rerollPicked);
    } else {
      primaryLabel = "To Reserve";
      primaryRun = () => run(() => api.finishRoll(game.gameId));
    }
  } else if (step === "main") {
    primaryLabel = "Done buying";
    primaryNote = "enter the Attack Step";
    primaryRun = () => run(() => api.enterAttackStep(game.gameId));
    secondaryLabel = "Skip Attack";
    secondaryRun = () => run(() => api.skipAttackStep(game.gameId));
  } else if (step === "select-attackers") {
    const n = Object.keys(pendingAttackers).length;
    primaryLabel = `Declare Attackers (${n})`;
    primaryRun = () =>
      run(() => api.declareAttackers(game.gameId, Object.entries(pendingAttackers).map(([dieId, lane]) => ({ dieId, lane }))));
  } else if (step === "assign-blockers") {
    if (isYourTurn) {
      primaryLabel = "Waiting…";
      primaryNote = `${oppPlayer.name} is assigning blockers`;
      primaryDisabled = true;
    } else {
      primaryLabel = "Blockers set";
      primaryRun = () => run(() => api.declareBlockers(game.gameId, blockAssignmentsToApi(blockAssignments)));
    }
  } else if (step === "action-global-window") {
    primaryLabel = "Resolve Damage";
    primaryRun = () => run(() => api.assignCombatDamage(game.gameId, blockAssignmentsToApi(blockAssignments)));
  } else {
    // return-to-field
    primaryLabel = "Pass Turn";
    primaryRun = () => run(() => api.cleanUp(game.gameId));
  }

  const link = inviteLink(game.gameId, "/dice-kingdom/mobile");
  const logEntries = game.log.slice(-4);
  function rosterRowsFor(map: Map<string, Die[]>) {
    return [...map.entries()].map(([cardId, dice]) => ({
      card: cardsById.get(cardId),
      cardId,
      dieId: dice[0].id,
      remaining: dice.length,
    }));
  }
  const rosterCards = rosterRowsFor(unpurchasedByCard);
  const oppRosterCards = rosterRowsFor(oppUnpurchasedByCard);

  return (
    <div className="dicekingdom dk-mobile dkm-root">
      <EnergyBadgeOutlineDefs />
      <div className="dkm-header">
        <PhaseRail current={phase} onTap={() => {}} />
        <StepLine title={chainSteps[chainIndex]?.label ?? phaseLabel} index={chainIndex} total={chainSteps.length} onToggle={() => setStepsOpen((v) => !v)} />
      </div>

      <div className="dkm-scroll">
        {error && <p className="dkm-error">{error}</p>}
        {inviteCopiedBanner && <p className="dkm-invite-toast">Invite link copied — send it to your opponent.</p>}

        <MatCard
          mine={false}
          player={oppPlayer}
          dice={oppDice}
          cardsById={cardsById}
          isActivePlayer={opponentId === game.activePlayerId}
          onOpenRoster={() => setRosterViewFor(opponentId)}
          expandable
          spins={spins}
          turnOffsets={offsets}
          selectedId={selectedId}
          fieldClickable={() => false}
          onTapDie={onTapAttacker}
        />

        <GlobalRail />

        {/* Keyed by phase so React remounts this on every phase change,
            replaying dkPhaseIn (dicekingdom.css) - a plain settle-in
            rather than a hard cut when the view swaps to a new phase's
            card, direct feedback (2026-09-16): "...then shrink them into
            their destination." (runWithReveal above is what makes sure
            this swap only happens once a reroll's tumble has actually
            been seen, not the instant the server responds.) */}
        <div key={phase} className="dkm-phase-stage">
          {phase === "clear" && (
            <TrayCard
              title="Draw"
              hint={`${yourDice.filter((d) => d.zone === "Bag").length} in bag`}
              dice={[]}
              cardsById={cardsById}
              rolledYet={false}
              rerollPicked={[]}
              onToggleReroll={() => {}}
              spins={spins}
              turnOffsets={offsets}
            />
          )}
          {phase === "roll" && (
            <TrayCard
              title={hasRolledThisStep ? "Tray · tap to select for reroll" : "Tray"}
              hint={`${drawnZone.length || yourReserve.length} dice`}
              dice={hasRolledThisStep ? yourReserve : drawnZone}
              cardsById={cardsById}
              rolledYet={hasRolledThisStep}
              rerollPicked={rerollPicked}
              onToggleReroll={toggleReroll}
              spins={spins}
              turnOffsets={offsets}
            />
          )}
          {phase === "main" && (
            <BuyCard
              unpurchasedByCard={unpurchasedByCard}
              cardsById={cardsById}
              reserve={yourReserve}
              you={you}
              selectedId={selectedId}
              onSelect={toggleSelect}
              onOpenRoster={() => setRosterViewFor(you)}
            />
          )}
          {phase === "attack" && (
            <AttackLanesCard
              isYourTurn={isYourTurn}
              step={step}
              attackersByLane={attackersByLane}
              blockersByAttacker={blockersByAttacker}
              cardsById={cardsById}
              you={you}
              laneSel={laneSel}
              onTapLane={declareIntoLane}
              onTapAttacker={onTapAttacker}
              onTapBlocker={onTapBlocker}
              onTapChip={toggleLaneBreakdown}
              selectedId={selectedId}
            />
          )}
          {phase === "cleanup" && <CleanUpCard reserve={yourReserve} cardsById={cardsById} you={you} />}
        </div>

        <MatCard
          mine
          player={youPlayer}
          dice={yourFieldVisibleDice}
          cardsById={cardsById}
          isActivePlayer={you === game.activePlayerId}
          onOpenRoster={() => setRosterViewFor(you)}
          expandable={false}
          spins={spins}
          turnOffsets={offsets}
          selectedId={selectedId}
          fieldClickable={(d) =>
            (isYourTurn && step === "select-attackers" && d.controllerId === you) ||
            (!isYourTurn && step === "assign-blockers" && d.controllerId === you)
          }
          onTapDie={onTapMatDie}
        />

        {/* Moved down from the top of the scroll region (direct feedback,
            2026-09-17): "it takes up a lot of room up there for
            something that will only be clicked once." Hidden entirely
            in vs-computer mode - there's no second seat to invite, both
            tokens already live in this one browser. */}
        {!vsComputer && link && (
          <div className="dkm-invite">
            <span>Invite</span>
            <button type="button" className="dkm-text-btn" onClick={() => navigator.clipboard?.writeText(link)}>
              Copy link
            </button>
          </div>
        )}

        <div className="dkm-log">
          <span className="dkm-log-label">Log</span>
          {logEntries.length === 0 ? (
            <p className="dkm-empty-hint">Nothing has happened yet.</p>
          ) : (
            logEntries.map((entry, i) => (
              <p key={entry.seq} className={i === logEntries.length - 1 ? "dkm-log-line newest" : "dkm-log-line"}>
                {entry.text}
              </p>
            ))
          )}
        </div>
      </div>

      <div className="dkm-bottom-bar">
        {selectedDie && (
          <div className="dkm-inspect" style={{ ["--cc" as string]: typeColorOf(selectedDie, cardsById) }}>
            <AvatarGlyph die={selectedDie} size={42} color={typeColorOf(selectedDie, cardsById)} />
            <div className="dkm-inspect-mid">
              <span className="dkm-inspect-name">{nameOf(selectedDie, cardsById)}</span>
              <span className="dkm-inspect-sub">
                {/* A face can carry BOTH a character body and an energy
                    symbol at once (v3/DESIGN_NOTES.md's hybrid-face
                    rule) - character always wins the label here since
                    that's the stat that actually matters for this
                    step's actions. */}
                {selectedDie.level !== null
                  ? "character face"
                  : selectedDie.energySymbolId
                    ? `${selectedDie.energySymbolId} energy`
                    : "unrolled"}{" "}
                · {selectedDie.zone}
              </span>
              {selectedDie.attackModifiers && (
                <span className="dkm-inspect-stats">
                  ATK {statBreakdown(selectedDie.baseAttack, selectedDie.attackModifiers, selectedDie.effectiveAttack)} · DEF{" "}
                  {statBreakdown(selectedDie.baseDefense, selectedDie.defenseModifiers, selectedDie.effectiveDefense)}
                </span>
              )}
            </div>
            <button type="button" className="dkm-inspect-close" onClick={() => setSelectedId(null)}>
              ×
            </button>
            {inspectActions.map((a) => (
              <button
                key={a.label}
                type="button"
                className="dkm-inspect-action"
                disabled={a.label === "Can't afford"}
                onClick={a.run}
              >
                {a.label}
              </button>
            ))}
          </div>
        )}
        {laneBreakdown !== null && (
          <div className="dkm-inspect dkm-lane-inspect">
            <div className="dkm-inspect-mid">
              <span className="dkm-inspect-name">Lane {laneBreakdown + 1} breakdown</span>
              {/* Direct feedback (2026-09-17): "it's not just the
                  attacker's attack value and the defender's defense -
                  if the defending die's attack is >= the attacking
                  die's defense, the attacking die is KO'd. Both
                  directions matter." Combat is mutual (CombatEngine.
                  ResolveFastOrSlowDamage - every blocker deals its own
                  full Attack back regardless of the damage split), so
                  this shows both checks: attacker ATK vs blocker DEF,
                  AND blocker ATK vs attacker DEF.
                  Follow-up (same date): "the math isn't mathing - Lane 2
                  has 3A total attacking and 2D total defending, a
                  difference of 1, not 2" / "TO FACE seems to assume
                  every die has Overcrush." Neither - a lane can hold
                  MULTIPLE attackers that are each blocked (or not)
                  completely independently (a lane is a display grouping,
                  not a pooled fight - CombatEngine has no such thing as
                  shared/pooled blocking). The confusing "2 to face" was
                  really ONE attacker fully blocked (0 to face, no
                  Overcrush) plus a SEPARATE, genuinely unblocked attacker
                  hitting face for its own full Attack - spelled out
                  per-attacker below instead of left to add up silently. */}
              {laneAttackersForBreakdown.map((a) => {
                const myBlockers = blockersByAttacker.get(a.id) ?? [];
                const blockerAtkTotal = myBlockers.reduce((n, b) => n + (b.effectiveAttack ?? 0), 0);
                const blockerDefTotal = myBlockers.reduce((n, b) => n + (b.effectiveDefense ?? 0), 0);
                const attackerKOd = myBlockers.length > 0 && blockerAtkTotal >= (a.effectiveDefense ?? 0);
                const blockerKOd = myBlockers.length > 0 && (a.effectiveAttack ?? 0) >= blockerDefTotal;
                const faceDamage = unblockedFaceDamage(a, myBlockers, laneAttackersForBreakdown.length, cardsById);
                return (
                  <div key={a.id} className="dkm-inspect-engagement">
                    <span className="dkm-inspect-stats">
                      {nameOf(a, cardsById)} (attacking) — ATK {statBreakdown(a.baseAttack, a.attackModifiers, a.effectiveAttack) ?? "-"}
                      {myBlockers.length > 0 && <>, DEF {statBreakdown(a.baseDefense, a.defenseModifiers, a.effectiveDefense) ?? "-"}</>}
                      {attackerKOd && <b className="dkm-ko-tag"> → KO'd</b>}
                      {myBlockers.length === 0 ? (
                        <b className="dkm-ko-tag"> — unblocked, {faceDamage} to face</b>
                      ) : (
                        faceDamage > 0 && <b className="dkm-ko-tag"> — Overcrush, {faceDamage} to face</b>
                      )}
                    </span>
                    {myBlockers.map((b) => (
                      <span key={b.id} className="dkm-inspect-stats">
                        {nameOf(b, cardsById)} (blocking) — DEF {statBreakdown(b.baseDefense, b.defenseModifiers, b.effectiveDefense) ?? "-"}, ATK{" "}
                        {statBreakdown(b.baseAttack, b.attackModifiers, b.effectiveAttack) ?? "-"}
                        {blockerKOd && <b className="dkm-ko-tag"> → KO'd</b>}
                      </span>
                    ))}
                  </div>
                );
              })}
            </div>
            <button type="button" className="dkm-inspect-close" onClick={() => setLaneBreakdown(null)}>
              ×
            </button>
          </div>
        )}
        <div className="dkm-primary-row">
          {secondaryLabel && (
            <button type="button" className="dkm-secondary-btn" disabled={primaryDisabled} onClick={() => secondaryRun?.()}>
              {secondaryLabel}
            </button>
          )}
          <button type="button" className="dkm-primary-btn" disabled={primaryDisabled} onClick={() => primaryRun?.()}>
            <span>{primaryLabel}</span>
            {primaryNote && <small>{primaryNote}</small>}
          </button>
        </div>
      </div>

      {stepsOpen && <StepPopout phaseLabel={phaseLabel} steps={chainSteps} index={chainIndex} onClose={() => setStepsOpen(false)} />}
      {rosterViewFor && (
        <RosterSheet
          title={rosterViewFor === you ? "Your Roster" : "Their Roster"}
          cards={rosterViewFor === you ? rosterCards : oppRosterCards}
          canBuy={rosterViewFor === you && isYourTurn && step === "main"}
          reserve={yourReserve}
          onBuy={toggleSelect}
          onClose={() => setRosterViewFor(null)}
        />
      )}
    </div>
  );
}
