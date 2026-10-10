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
import { describeSavedGame, forgetSeats, inviteLink, myLink, rememberSeats } from "./seats";
import { GameOverOverlay } from "./GameOverOverlay";
import { OPPONENT_PICKS, PickYourChampion, ResumeGames, WaitingForOpponent, resolveInvite } from "./lobby";
import { ChampionPicker } from "./ChampionPicker";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor, printedFacesFor } from "./dieFaces";
import { explainRows, tileCues, whereText } from "./statusCues";
import { CueRows } from "./CueRows";
import { DieFramesLegend, legendSeen } from "./DieFramesLegend";
import { SpinFlash, useSpinFlash } from "./SpinFlash";
import { useDieFlights, usePhaseHeight } from "./dieFlights";
import { useDiceRoll, type RollTarget } from "./useDiceRoll";
import { classifyDieMotion, remoteRolledIds } from "./dieMotion";
import { activeCouldAct, botDecisionCall, decisionOwner, pickEnergy, rolled } from "./bot";
import { getAbilityOptions, purchasableDiceFor, executeAbility, type AbilityCommand } from "./sharedAbilities";
import { SharedAbilityPanel } from "./SharedAbilityPanel";
import type { BotDecision, CardDef, Die, GameState, LobbyStatus, PendingChoice, PlayerState, StatModifier } from "./types";

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
//   DiceKingdomConfig grants a Global ability yet and the engine has no
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
const BOT_MOVE_DELAY_MS = 2000; // slow enough to follow the opponent's turn

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

// Every blocker assigned to any attacker in the lane - a lane is one fight
// (CombatEngine.LaneFight), and the UI only ever attaches a lane's blockers
// to its first attacker, so this unions across the lane rather than
// trusting which attacker a blocker was assigned to.
function laneBlockersOf(attackers: Die[], blockersByAttacker: Map<string, Die[]>): Die[] {
  const seen = new Set<string>();
  const out: Die[] = [];
  for (const a of attackers) {
    for (const b of blockersByAttacker.get(a.id) ?? []) {
      if (!seen.has(b.id)) {
        seen.add(b.id);
        out.push(b);
      }
    }
  }
  return out;
}

// How much of a LANE's damage reaches the opponent directly: everything if
// nothing blocks it; otherwise nothing, UNLESS the lane has Overcrush (2+
// attackers in it, or any attacker with the Overcrush keyword) and its
// combined Attack clears its blockers' combined Defense - mirrors
// CombatEngine.AssignCombatDamage's per-lane Overcrush. Shared by the lane
// chip and the breakdown panel so they can never drift apart.
function laneOvercrush(attackers: Die[], cardsById: Map<string, CardDef>): boolean {
  return (
    attackers.length >= 2 ||
    attackers.some((a) => (a.cardId ? (cardsById.get(a.cardId)?.keywords.includes("Overcrush") ?? false) : false))
  );
}
// Damage a reflecting die (Rhinoceros) in this lane is about to take -
// and so send to its controller's opponent. Kept apart from "to face"
// (direct feedback, 2026-09-30: folding Rhino's ability damage into that
// number was confusing): combat damage and reflected damage are
// different things and can even land on different players.
function laneReflect(attackers: Die[], blockers: Die[], preview: Map<string, DiePreview>, cardsById: Map<string, CardDef>) {
  return [...attackers, ...blockers]
    .filter((d) => d.cardId && cardsById.get(d.cardId)?.reflectsDamage)
    .map((d) => ({ die: d, amount: preview.get(d.id)?.incoming ?? 0 }))
    .filter((r) => r.amount > 0);
}

function laneFaceDamage(attackers: Die[], blockers: Die[], cardsById: Map<string, CardDef>): number {
  const totalAtk = attackers.reduce((n, a) => n + (a.effectiveAttack ?? 0), 0);
  if (blockers.length === 0) return totalAtk;
  if (!laneOvercrush(attackers, cardsById)) return 0;
  const blockerDefTotal = blockers.reduce((n, b) => n + (b.effectiveDefense ?? 0), 0);
  return Math.max(0, totalAtk - blockerDefTotal);
}

// What combat damage would do to each die in the lanes if it resolved right
// now - mirrors CombatEngine.AssignCombatDamage: a lane's attackers pool
// their Attack against its blockers, and its blockers pool their Attack
// back across its attackers (each lethal-first in order, remainder on the
// last). A die is KO'd when marked + new damage reaches its Defense.
// Approximation: ignores Fast's two-wave ordering and on-damage abilities.
// Only dice in a blocked lane get an entry.
interface DiePreview {
  defense: number;
  already: number; // damage already marked
  incoming: number; // damage this combat would add
  ko: boolean;
}
function poolDamage(sources: Die[], targets: Die[]): Map<string, number> {
  const lethalLeft = new Map(targets.map((t) => [t.id, Math.max(0, (t.effectiveDefense ?? 0) - (t.damage ?? 0))] as const));
  const dealt = new Map<string, number>(targets.map((t) => [t.id, 0] as const));
  for (const src of sources) {
    let remaining = src.effectiveAttack ?? 0;
    targets.forEach((t, i) => {
      const give = i === targets.length - 1 ? remaining : Math.min(remaining, lethalLeft.get(t.id) ?? 0);
      lethalLeft.set(t.id, Math.max(0, (lethalLeft.get(t.id) ?? 0) - give));
      dealt.set(t.id, (dealt.get(t.id) ?? 0) + give);
      remaining -= give;
    });
  }
  return dealt;
}
function combatPreview(lanes: Die[][], blockersByAttacker: Map<string, Die[]>): Map<string, DiePreview> {
  const out = new Map<string, DiePreview>();
  const entry = (d: Die, incoming: number) => {
    const defense = d.effectiveDefense ?? 0;
    const already = d.damage ?? 0;
    out.set(d.id, { defense, already, incoming, ko: already + incoming >= defense });
  };
  for (const attackers of lanes) {
    const blockers = laneBlockersOf(attackers, blockersByAttacker);
    if (blockers.length === 0 || attackers.length === 0) continue;
    for (const [id, dmg] of poolDamage(attackers, blockers)) entry(blockers.find((b) => b.id === id)!, dmg);
    for (const [id, dmg] of poolDamage(blockers, attackers)) entry(attackers.find((a) => a.id === id)!, dmg);
  }
  return out;
}

// blockAssignments is keyed by attacker id but holds a LIST of blockers
// (gang-blocking) - this is the one place that shape turns into the
// flat {attackerDieId, blockerDieId} pairs the API/bot actually want,
// one row per blocker, shared by every submission call site so they
// can't drift apart on the flattening.
// The server's flat block list back into attacker id -> blockers, in order.
function groupBlocks(blocks: { attackerDieId: string; blockerDieId: string }[]): Record<string, string[]> {
  const out: Record<string, string[]> = {};
  for (const b of blocks) (out[b.attackerDieId] ??= []).push(b.blockerDieId);
  return out;
}

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
// `virtualEnergy`: the payer's deck-out generic energy (bot.ts pickEnergy).
function pickEnergyForCost(reserve: Die[], cost: number, matchType: string | null, virtualEnergy = 0): string[] | null {
  // Shared with the bot - see bot.ts pickEnergy for how the leftover die is chosen.
  return pickEnergy(reserve, cost, matchType, virtualEnergy);
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
// the engine reports, not a fixed lookup table). Infiltrate is real since
// 2026-10-03 (Flying Squirrel); no Dice Kingdom creature has Range yet.
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

type PileZone = "used" | "prep" | "out" | "bag";
const PILE_TITLES: Record<PileZone, string> = { used: "Used", prep: "Prep", out: "Out of play", bag: "Bag" };

// Compact strip of a pile's dice, shown inside the owning player's mat (no
// overlay, so the rest of the game stays visible): real die tiles where a
// face is showing, identity-only tiles otherwise (Bag, anything unrolled).
// Tap the same pile again to close it.
function PileStrip({
  title,
  dice,
  cardsById,
  mine,
  onClose,
  selectedId,
  onInspect,
}: {
  title: string;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  mine: boolean;
  onClose: () => void;
  selectedId: string | null;
  onInspect: (id: string) => void;
}) {
  const nameOf = (d: Die) => (d.cardId ? (cardsById.get(d.cardId)?.name ?? d.cardId) : "Tardigrade");
  const sorted = [...dice].sort((a, b) => nameOf(a).localeCompare(nameOf(b)));
  return (
    <div className="dkm-pile-strip">
      <div className="dkm-pile-strip-head">
        <span className="dkm-pile-label">
          {title} ({dice.length})
        </span>
        <button type="button" className="dkm-text-btn" onClick={onClose}>
          Close
        </button>
      </div>
      {sorted.length === 0 && <span className="dkm-empty-hint">Nothing here.</span>}
      <div className="dkm-tile-row wrap">
        {sorted.map((d) => (
          <button
            key={d.id}
            type="button"
            className={`dkm-pile-sheet-item dkm-pile-inspect-tile${selectedId === d.id ? " picked" : ""}`}
            title={nameOf(d)}
            aria-label={`Inspect ${nameOf(d)}`}
            aria-pressed={selectedId === d.id}
            onClick={() => onInspect(d.id)}
          >
            {rolled(d) ? <DTile die={d} cardsById={cardsById} size={36} mine={mine} flyId={false} /> : <span className="dkm-nofly"><FacedownTile die={d} size={36} /></span>}
          </button>
        ))}
      </div>
    </div>
  );
}

// The one die tile shape reused everywhere a real rolled face is on show
// (tray after rolling, field, reserve creature faces, attack lanes) - a
// thin wrapper around the repo's real DieCube (same 3D cube /game and
// the desktop Dice Kingdom page use), not a re-implementation.
// Picking targets for a pending choice right on the board (direct
// feedback 2026-09-25: "There will be times when placement is important,
// such as when dice are attacking or blocking" - so no separate sheet of
// copies). Legal targets glow where they sit, everything else dims.
interface Targeting {
  candidates: Set<string>;
  picked: Set<string>;
}

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
  flyId = true,
  targetable,
}: {
  die: Die;
  cardsById: Map<string, CardDef>;
  size: number;
  mine: boolean;
  clickable?: boolean;
  picked?: boolean;
  /** A legal target of the pending choice - see Targeting. */
  targetable?: boolean;
  spin?: CubeSpin;
  turnOffset?: number;
  onClick?: () => void;
  /** Tag this tile as the die's on-screen home for flight animations (see dieFlights.ts). */
  flyId?: boolean;
}) {
  const cls = ["dkm-tile", clickable ? "clickable" : "", picked ? "picked" : "", targetable ? "targetable" : ""].filter(Boolean).join(" ");
  // Status cues (Claude Design's "face frame", 2026-10-03) on any die in
  // play or Intimidated - drawn on the die itself; see statusCues.ts.
  const inPlay = die.zone === "FieldZone" || die.zone === "AttackZone" || die.zone === "Intimidated";
  const cues = inPlay ? tileCues(die, cardsById, mine) : undefined;
  const spinFlash = useSpinFlash(die);
  return (
    <button type="button" className={cls} onClick={clickable ? onClick : undefined} disabled={!clickable} data-fly-id={flyId ? `die:${die.id}` : undefined}>
      <DieCube
        {...facesFor(die, cardsById)}
        size={size}
        mine={mine}
        spin={spin}
        turnOffset={turnOffset}
        energyCorner={die.energySymbolId && die.energyAmount > 0 ? { type: die.energySymbolId, amount: die.energyAmount } : undefined}
        cues={cues}
      />
      <SpinFlash flash={inPlay ? spinFlash : null} />
      {/* Damage marked on a die in play (Honey Badger's ping, a survived
          block...) - direct feedback 2026-09-30: nothing showed it. Keyed
          by the amount so a fresh hit pops again; clears at Clean Up. */}
      {(die.damage ?? 0) > 0 && (die.zone === "FieldZone" || die.zone === "AttackZone") && (
        <span key={die.damage} className="dk-damage-badge" title={`${die.damage} damage marked - clears at the end of the turn`}>
          −{die.damage}
        </span>
      )}
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
    <div className="dkm-tile dkm-facedown" style={{ width: size, height: size }} data-fly-id={`die:${die.id}`}>
      <span style={{ opacity: 0.55 }}>{Avatar ? <Avatar size={Math.round(size * 0.5)} /> : <TardigradeIcon size={Math.round(size * 0.5)} />}</span>
    </div>
  );
}

// `generic`: the player's deck-out Virtual energy (PlayerState.virtualEnergy)
// - shown as plain generic energy rather than as its own marker (user call,
// 2026-09-28: it's spent first and can't spin down anyway, so telling it
// apart would only be clutter).
function EnergyChips({ dice, size = 16, generic = 0 }: { dice: Die[]; size?: number; generic?: number }) {
  const totals = new Map<string, number>();
  for (const d of dice) {
    if (!d.energySymbolId || d.energyAmount <= 0) continue;
    totals.set(d.energySymbolId, (totals.get(d.energySymbolId) ?? 0) + d.energyAmount);
  }
  if (totals.size === 0 && generic <= 0) return <span className="dkm-reserve-empty">reserve empty</span>;
  return (
    <div className="dkm-energy-chips">
      {generic > 0 && (
        <span className="dkm-energy-chip" style={{ borderColor: "var(--generic, #b8ae9c)" }} title="Generic energy - usable for any cost, not a type">
          <b style={{ color: "var(--generic, #b8ae9c)" }}>{generic}</b>
          <small>generic</small>
        </span>
      )}
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
  onOpenLegend,
}: {
  phaseLabel: string;
  steps: ChainStep[];
  index: number;
  onClose: () => void;
  /** Reopens the die-frames legend - mobile has no Help menu of its own. */
  onOpenLegend: () => void;
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
        <button type="button" className="dkm-text-btn" onClick={onOpenLegend}>
          Die frames: what the borders and tabs mean
        </button>
      </div>
    </div>
  );
}

// ---- Mat card (opponent / you) ----

// The Champion power button's label (2026-10-04); the badge's tap text
// explains it in full.
const POWER_LABELS: Record<string, string> = { Wolf: "Pump", Armadillo: "Shield", GreatHornedOwl: "Spin" };

function MatCard({
  mine,
  player,
  dice,
  cardsById,
  isActivePlayer,
  onOpenRoster,
  onOpenPile,
  openPile,
  expandable,
  spins,
  turnOffsets,
  selectedId,
  fieldClickable,
  onTapDie,
  onInspectPileDie,
  targeting,
  onUsePower,
}: {
  /** Your Champion's once-per-turn power is usable now (2026-10-04). */
  onUsePower?: () => void;
  mine: boolean;
  player: PlayerState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  isActivePlayer: boolean;
  onOpenRoster: () => void;
  onOpenPile: (zone: PileZone) => void;
  openPile: PileZone | null;
  expandable: boolean;
  spins: Record<string, CubeSpin>;
  turnOffsets: Record<string, number>;
  selectedId: string | null;
  fieldClickable: (d: Die) => boolean;
  onTapDie: (id: string) => void;
  onInspectPileDie: (id: string) => void;
  targeting?: Targeting | null;
}) {
  const [expanded, setExpanded] = useState(false);
  const [championOpen, setChampionOpen] = useState(false);
  const zone = (name: string) => dice.filter((d) => d.zone === name);
  const field = zone("FieldZone");
  const used = zone("UsedPile");
  const prep = zone("PrepArea");
  const out = zone("OutOfPlay");
  const intimidated = zone("Intimidated");
  const bag = zone("Bag");
  const reserve = zone("ReservePool");
  const rolledReserve = reserve.filter(rolled);

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
        {onUsePower && player.champion && (
          <button type="button" className="dkm-chip-btn dkm-power-btn" onClick={onUsePower} title={player.champion.passiveText}>
            {POWER_LABELS[player.champion.id] ?? "Power"}
          </button>
        )}
        <span className="dkm-mat-life">
          {player.life} <small>life</small>
        </span>
        <span className="dkm-reserve-anchor" data-pile={`${mine ? "mine" : "opp"}-reserve`}>
          <EnergyChips dice={reserve} size={mine ? 16 : 15} generic={player.virtualEnergy ?? 0} />
        </span>
        <span className="dkm-mat-head-actions">
          <button type="button" className="dkm-chip-btn" data-pile={`${mine ? "mine" : "opp"}-roster`} onClick={onOpenRoster}>
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
            <button type="button" className={`dkm-pile-cell dkm-pile-used`} data-pile={`${mine ? "mine" : "opp"}-used`} onClick={() => onOpenPile("used")}>
              <span className="dkm-pile-label">Used</span>
              <PileStack dice={used} cardsById={cardsById} />
            </button>
            <button type="button" className={`dkm-pile-cell dkm-pile-prep`} data-pile={`${mine ? "mine" : "opp"}-prep`} onClick={() => onOpenPile("prep")}>
              <span className="dkm-pile-label">Prep</span>
              <PileStack dice={prep} cardsById={cardsById} />
            </button>
            <button type="button" className={`dkm-pile-cell`} data-pile={`${mine ? "mine" : "opp"}-out`} onClick={() => onOpenPile("out")}>
              <span className="dkm-pile-label">Out</span>
              <PileStack dice={out} cardsById={cardsById} />
            </button>
            <button type="button" className={`dkm-pile-cell`} data-pile={`${mine ? "mine" : "opp"}-bag`} onClick={() => onOpenPile("bag")}>
              <span className="dkm-pile-label">Bag</span>
              <span className="dkm-bag-count">{bag.length}</span>
            </button>
          </div>
        </div>
      ) : (
        <div className="dkm-collapsed-row">
          <button type="button" data-pile={`${mine ? "mine" : "opp"}-used`} onClick={() => onOpenPile("used")}>
            <b>{used.length}</b> used
          </button>
          <button type="button" data-pile={`${mine ? "mine" : "opp"}-prep`} onClick={() => onOpenPile("prep")}>
            <b>{prep.length}</b> prep
          </button>
          <button type="button" data-pile={`${mine ? "mine" : "opp"}-out`} onClick={() => onOpenPile("out")}>
            <b>{out.length}</b> out
          </button>
          <button type="button" data-pile={`${mine ? "mine" : "opp"}-bag`} onClick={() => onOpenPile("bag")}>
            <b>{bag.length}</b> bag
          </button>
        </div>
      )}

      {openPile && (
        <PileStrip
          title={PILE_TITLES[openPile]}
          dice={{ used, prep, out, bag }[openPile]}
          cardsById={cardsById}
          mine={mine}
          onClose={() => onOpenPile(openPile)}
          selectedId={selectedId}
          onInspect={onInspectPileDie}
        />
      )}

      {/* The opponent's rolled Reserve dice, so a creature they field visibly
          comes OUT of somewhere (it used to appear from nowhere - only their
          energy total showed, in the header). Your own reserve lives in the
          Buy card on your turn. */}
      {!mine && rolledReserve.length > 0 && (
        <>
          <span className="dkm-field-label">Their reserve</span>
          <div className="dkm-tile-row wrap" data-region="opp-reserve">
            {rolledReserve.map((d) => (
              <DTile key={d.id} die={d} cardsById={cardsById} size={40} mine={false} />
            ))}
          </div>
        </>
      )}

      <span className="dkm-field-label">{mine ? "Field" : "Their field · active"}</span>
      <div className="dkm-tile-row wrap" data-region={mine ? "field-mine" : "field-opp"}>
        {field.length === 0 && <span className="dkm-empty-hint">Nothing fielded.</span>}
        {field.map((d) => (
          <DTile
            key={d.id}
            die={d}
            cardsById={cardsById}
            size={mine ? 50 : 48}
            mine={mine}
            clickable={targeting ? targeting.candidates.has(d.id) : fieldClickable(d) || tileCues(d, cardsById, mine).any}
            picked={targeting ? targeting.picked.has(d.id) : selectedId === d.id}
            targetable={targeting?.candidates.has(d.id)}
            spin={spins[d.id]}
            turnOffset={turnOffsets[d.id]}
            onClick={() => onTapDie(d.id)}
          />
        ))}
      </div>

      {/* Keyword Intimidate: off the Field (can't block, can't be
          targeted, auras off) until Clean Up puts it back on the same
          face - shown dimmed beside the Field it came from, not hidden
          in a pile, since it's coming straight back. */}
      {intimidated.length > 0 && (
        <>
          <span className="dkm-field-label">Intimidated · back at end of turn</span>
          <div className="dkm-tile-row wrap dkm-intimidated" data-region={mine ? "intimidated-mine" : "intimidated-opp"}>
            {intimidated.map((d) => (
              <DTile key={d.id} die={d} cardsById={cardsById} size={40} mine={mine} clickable={!targeting} picked={selectedId === d.id} onClick={() => onTapDie(d.id)} />
            ))}
          </div>
        </>
      )}
    </div>
  );
}

// A tap still toggles reroll selection. Holding a die opens its details instead.
function RerollDieTile({ die, cardsById, picked, spin, turnOffset, onToggle, onInspect, interactive }: {
  die: Die;
  cardsById: Map<string, CardDef>;
  picked: boolean;
  spin?: CubeSpin;
  turnOffset?: number;
  onToggle: () => void;
  onInspect: () => void;
  interactive: boolean;
}) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const start = useRef<{ x: number; y: number } | null>(null);
  const longPressed = useRef(false);
  const clearPress = () => {
    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = null;
    start.current = null;
  };
  useEffect(() => () => {
    if (timer.current !== null) clearTimeout(timer.current);
  }, []);
  return (
    <button
      type="button"
      data-fly-id={interactive ? `die:${die.id}` : undefined}
      className={`dkm-tile${interactive ? " clickable" : ""}${picked ? " picked" : ""}`}
      onPointerDown={(e) => {
        if (!interactive || e.button !== 0) return;
        clearPress();
        longPressed.current = false;
        start.current = { x: e.clientX, y: e.clientY };
        timer.current = setTimeout(() => {
          longPressed.current = true;
          timer.current = null;
          onInspect();
        }, 500);
      }}
      onPointerMove={(e) => {
        if (start.current && Math.hypot(e.clientX - start.current.x, e.clientY - start.current.y) > 12) clearPress();
      }}
      onPointerUp={clearPress}
      onPointerCancel={clearPress}
      onPointerLeave={clearPress}
      onContextMenu={(e) => {
        e.preventDefault();
        if (!interactive) return;
        clearPress();
        longPressed.current = true;
        onInspect();
      }}
      onClick={() => {
        if (longPressed.current) {
          longPressed.current = false;
          return;
        }
        if (interactive) onToggle();
      }}
      disabled={!interactive}
    >
      <DieCube
        {...facesFor(die, cardsById)}
        size={58}
        mine={interactive}
        spin={spin}
        turnOffset={turnOffset}
        energyCorner={die.energySymbolId && die.energyAmount > 0 ? { type: die.energySymbolId, amount: die.energyAmount } : undefined}
      />
    </button>
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
  onInspectDie,
  spins,
  turnOffsets,
  interactive = true,
}: {
  title: string;
  hint: string;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  rolledYet: boolean;
  rerollPicked: string[];
  onToggleReroll: (id: string) => void;
  onInspectDie: (id: string) => void;
  spins: Record<string, CubeSpin>;
  turnOffsets: Record<string, number>;
  /** False when this is the OPPONENT's tray: shown face-up once rolled, but not tappable for reroll. */
  interactive?: boolean;
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
            <RerollDieTile
              key={d.id}
              die={d}
              cardsById={cardsById}
              picked={rerollPicked.includes(d.id)}
              spin={spins[d.id]}
              turnOffset={turnOffsets[d.id]}
              interactive={interactive}
              onToggle={() => onToggleReroll(d.id)}
              onInspect={() => onInspectDie(d.id)}
            />
          ) : (
            <FacedownTile key={d.id} die={d} size={58} />
          ),
        )}
      </div>
    </div>
  );
}

// A cost that a discount (a Champion passive) has changed shows the printed
// number struck through beside the real one (direct feedback, 2026-09-27:
// a discount that "was not visually clear").
function CostNumber({ printed, actual }: { printed: number; actual: number }) {
  if (actual === printed) return <>{actual}</>;
  return (
    <>
      <s className="dkm-cost-printed">{printed}</s> {actual}
    </>
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
  purchaseCostOf,
  foresightReady = false,
  virtualEnergy = 0,
  lockedBy,
}: {
  /** Cards you can't buy or field right now (Pangolin), and what's locking them. */
  lockedBy?: Map<string, string[]>;
  purchaseCostOf: (cardId: string) => number;
  /** Deck-out generic energy, spent automatically before any die. */
  virtualEnergy?: number;
  /** Foresight can be used right now - any Reserve die can be tapped for it. */
  foresightReady?: boolean;
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
  // the whole roster. Ordered most-expensive-affordable first (direct
  // feedback 2026-09-24: with 6 energy you want the 5s and 6s suggested,
  // not the 2s and 3s); if fewer than three are affordable, the rest of
  // the strip fills with the cheapest unaffordable ones - the closest to
  // being buyable.
  const costOf = purchaseCostOf;
  const canAfford = (cardId: string) => {
    const card = cardsById.get(cardId);
    return pickEnergyForCost(reserve, costOf(cardId), card?.energyTypes[0] ?? null, virtualEnergy) !== null;
  };
  const entries = [...unpurchasedByCard.entries()];
  const affordableEntries = entries.filter(([id]) => canAfford(id)).sort(([a], [b]) => costOf(b) - costOf(a));
  const unaffordableEntries = entries.filter(([id]) => !canAfford(id)).sort(([a], [b]) => costOf(a) - costOf(b));
  const visible = [...affordableEntries, ...unaffordableEntries].slice(0, 3);
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
          const affordable = canAfford(cardId);
          const lockers = lockedBy?.get(cardId);
          return (
            <button
              key={cardId}
              type="button"
              data-fly-id={`card:${cardId}`}
              className={`dkm-buy-tile${selectedId === dieId ? " picked" : ""}${affordable ? "" : " unaffordable"}${lockers ? " dk-locked" : ""}`}
              title={lockers ? `Locked out by ${lockers.join(", ")} - you can't buy or field it while that's active.` : undefined}
              onClick={() => onSelect(dieId)}
            >
              {lockers && <span className="dk-locked-hatch" aria-hidden="true" />}
              <span className="dkm-buy-avatar">{Avatar ? <Avatar size={22} /> : <TardigradeIcon size={22} />}</span>
              <span className="dkm-buy-name">{card?.name ?? cardId}</span>
              <span className="dkm-buy-cost-row">
                {(card?.energyTypes ?? []).map((t) => (
                  <EnergyBadge key={t} type={t} size={11} />
                ))}
                <b className="dkm-buy-cost" style={{ color: `var(--${(card?.energyTypes[0] ?? "claw").toLowerCase()})` }}>
                  <CostNumber printed={card?.purchaseCost ?? 0} actual={costOf(cardId)} />
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
          // A creature face to field, or an action face to use.
          const fieldable = d.effectiveAttack !== null || !!d.isActionFace || foresightReady;
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
  preview,
  targeting,
}: {
  die: Die;
  cardsById: Map<string, CardDef>;
  you: string;
  size: number;
  picked: boolean;
  onTap: () => void;
  preview?: DiePreview;
  targeting?: Targeting | null;
}) {
  return (
    <div
      className="dkm-lane-die-wrap"
      onClick={(e) => {
        e.stopPropagation();
        onTap();
      }}
    >
      <DTile
        die={die}
        cardsById={cardsById}
        size={size}
        mine={die.controllerId === you}
        clickable={targeting ? targeting.candidates.has(die.id) : true}
        picked={targeting ? targeting.picked.has(die.id) : picked}
        targetable={targeting?.candidates.has(die.id)}
      />
      {preview && <DefenceMeter p={preview} width={size} />}
    </div>
  );
}

// Defence meter under a lane die (design option 6a): the bar is the die's own
// Defense, dark = damage already marked, red = what this combat would add;
// the label reads "-N" (incoming) and "KO" when that would be lethal.
function DefenceMeter({ p, width }: { p: DiePreview; width: number }) {
  const def = Math.max(1, p.defense);
  const pct = (n: number) => `${Math.min(100, (n / def) * 100)}%`;
  return (
    <div className={`dkm-meter${p.ko ? " ko" : ""}`} style={{ width }}>
      <div className="dkm-meter-bar">
        <span className="dkm-meter-marked" style={{ width: pct(p.already) }} />
        <span className="dkm-meter-incoming" style={{ width: pct(p.incoming), left: pct(p.already) }} />
      </div>
      <div className="dkm-meter-label">
        <span>-{p.incoming}</span>
        {p.ko && <b>KO</b>}
      </div>
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
  targeting,
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
  targeting?: Targeting | null;
}) {
  const totalDeclared = attackersByLane.reduce((n, l) => n + l.length, 0);
  const preview = combatPreview(attackersByLane, blockersByAttacker);
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
          const blockers = laneBlockersOf(attackers, blockersByAttacker);
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
          const faceDamage = laneFaceDamage(attackers, blockers, cardsById);
          const chipText = attackers.length === 0 ? null : `${faceDamage} to face`;
          const reflected = laneReflect(attackers, blockers, preview, cardsById).reduce((n, r) => n + r.amount, 0);
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
          // Status cues: an unblockable attacker makes its whole lane
          // unblockable (CombatEngine.ValidateUnblockable).
          const laneUnblockable = attackers.some((a) => a.statuses?.some((st) => st.kind === "unblockable"));
          return (
            <div
              key={lane}
              role="button"
              tabIndex={0}
              data-lane={lane}
              className={`dkm-lane${targeted ? " targeted" : ""}${laneUnblockable ? " unblockable" : ""}`}
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
              {/* Whichever side sits on top renders reversed, so each
                  side's FIRST die (the one combat damage hits first -
                  CombatEngine.AssignCombatDamage goes in lane order) sits
                  next to the middle where the two teams meet, and damage
                  reads as spreading outward from there. Direct feedback
                  (2026-09-25): top-down damage on the upper side looked
                  backwards. */}
              {isYourTurn ? (
                <>
                  <div className="dkm-lane-blockers">
                    {/* Direct feedback (2026-09-17): "there is no visual
                        [cue] to highlight who is the aggressor and who is
                        defending" - a plain text role caption per section,
                        not a color/border cue, so it reads the same
                        regardless of position/orientation or color vision. */}
                    {attackers.length > 0 && <span className="dkm-lane-role def">Blocking</span>}
                    {laneUnblockable && blockers.length === 0 && <span className="dkm-lane-cant">can't be blocked</span>}
                    {[...blockers].reverse().map((b) => (
                      <LaneDie key={b.id} die={b} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === b.id} onTap={() => onTapBlocker(attackers[0]?.id ?? "", b.id)} preview={preview.get(b.id)} targeting={targeting} />
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
                  {reflected > 0 && (
                    <span className="dkm-lane-chip reflect" title="Damage a reflecting creature (Rhinoceros) takes is dealt to its opponent">
                      ↩ {reflected} reflect
                    </span>
                  )}
                  <div className="dkm-lane-attackers">
                    {attackers.length > 0 && <span className="dkm-lane-role atk">Attacking</span>}
                    {attackers.map((a) => (
                      <LaneDie key={a.id} die={a} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === a.id} onTap={() => onTapAttacker(a.id)} preview={preview.get(a.id)} targeting={targeting} />
                    ))}
                    {attackers.length === 0 && <div className="dkm-lane-tile empty attacker-empty" />}
                  </div>
                </>
              ) : (
                <>
                  <div className="dkm-lane-attackers">
                    {attackers.length > 0 && <span className="dkm-lane-role atk">Attacking</span>}
                    {[...attackers].reverse().map((a) => (
                      <LaneDie key={a.id} die={a} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === a.id} onTap={() => onTapAttacker(a.id)} preview={preview.get(a.id)} targeting={targeting} />
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
                  {reflected > 0 && (
                    <span className="dkm-lane-chip reflect" title="Damage a reflecting creature (Rhinoceros) takes is dealt to its opponent">
                      ↩ {reflected} reflect
                    </span>
                  )}
                  <div className="dkm-lane-blockers">
                    {attackers.length > 0 && <span className="dkm-lane-role def">Blocking</span>}
                    {laneUnblockable && blockers.length === 0 && <span className="dkm-lane-cant">can't be blocked</span>}
                    {blockers.map((b) => (
                      <LaneDie key={b.id} die={b} cardsById={cardsById} you={you} size={tileSize} picked={selectedId === b.id} onTap={() => onTapBlocker(attackers[0]?.id ?? "", b.id)} preview={preview.get(b.id)} targeting={targeting} />
                    ))}
                    {step === "assign-blockers" && !isYourTurn && attackers.length > 0 && blockers.length === 0 && (
                      <div className="dkm-lane-tile empty blocker-empty">no blocker</div>
                    )}
                  </div>
                </>
              )}
              <span className="dkm-lane-number">
                {laneUnblockable && <span className="dkm-lane-unblock-tag">»</span>}
                {String(lane + 1).padStart(2, "0")}
              </span>
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
  purchaseCostOf,
  virtualEnergy = 0,
}: {
  title: string;
  purchaseCostOf: (cardId: string) => number;
  /** Deck-out generic energy, spent automatically before any die. */
  virtualEnergy?: number;
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
          const affordable = canBuy && pickEnergyForCost(reserve, purchaseCostOf(cardId), types[0] ?? null, virtualEnergy) !== null;
          const row = (
            <>
              <span className="dkm-roster-avatar">{Avatar ? <Avatar size={20} /> : <TardigradeIcon size={20} />}</span>
              <div className="dkm-roster-mid">
                <span className="dkm-roster-name">{card?.name ?? cardId}</span>
                <span className="dkm-roster-stats">{levelStatsLine(card)}</span>
                {/* The ability is the primary information (direct feedback,
                    2026-09-21) - always shown in full, never behind a tap.
                    rawText already leads with its keywords ("Fast. On
                    Attack: ..."), so no separate keyword badge. */}
                {card && <span className="dkm-roster-text">{card.rawText}</span>}
              </div>
              {/* One EnergyBadge per required type - usually one, two for
                  a crossover/splash card - so the cost number is never
                  shown without saying what it's a cost OF. */}
              <span className="dkm-roster-cost-wrap">
                {types.map((t) => (
                  <EnergyBadge key={t} type={t} size={12} />
                ))}
                <b className="dkm-roster-cost" style={{ color: `var(--${(types[0] ?? "claw").toLowerCase()})` }}>
                  <CostNumber printed={card?.purchaseCost ?? 0} actual={purchaseCostOf(cardId)} />
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

// Pick which energy dice pay a fielding cost (direct feedback, 2026-09-21:
// "there's a bit more strategy there, we shouldn't assume"). Dice are spent
// in the order tapped, exactly like TurnEngine.SpendEnergy: it stops as
// soon as the cost is met, and only the LAST die can be partly spent.
function PaymentSheet({
  title,
  cost,
  energyDice,
  cardsById,
  onConfirm,
  onClose,
}: {
  title: string;
  cost: number;
  energyDice: Die[];
  cardsById: Map<string, CardDef>;
  onConfirm: (ids: string[]) => void;
  onClose: () => void;
}) {
  const [picked, setPicked] = useState<string[]>([]);
  const total = picked.reduce((n, id) => n + (energyDice.find((d) => d.id === id)?.energyAmount ?? 0), 0);
  const met = total >= cost;
  const toggle = (id: string) =>
    setPicked((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : met ? prev : [...prev, id]));
  const last = picked.length > 0 ? energyDice.find((d) => d.id === picked[picked.length - 1]) : undefined;
  const wasted = met && last ? total - cost : 0;
  return (
    <div className="dkm-overlay-backdrop" onClick={onClose}>
      <div className="dkm-sheet dkm-pay-sheet" onClick={(e) => e.stopPropagation()}>
        <div className="dkm-sheet-handle" />
        <div className="dkm-popout-head">
          <span className="dkm-popout-title">{title}</span>
          <button type="button" className="dkm-text-btn" onClick={onClose}>
            cancel
          </button>
        </div>
        <p className="dkm-pay-status">
          Choose energy to spend: <b>{Math.min(total, cost)}</b> / {cost}
          {wasted > 0 && <span className="dkm-pay-warn"> · {wasted} extra on your last pick is lost</span>}
        </p>
        <div className="dkm-tile-row wrap">
          {energyDice.map((d) => (
            <DTile
              key={d.id}
              die={d}
              cardsById={cardsById}
              size={54}
              mine
              flyId={false}
              clickable={picked.includes(d.id) || !met}
              picked={picked.includes(d.id)}
              onClick={() => toggle(d.id)}
            />
          ))}
        </div>
        <button type="button" className="dkm-primary-btn dkm-pay-confirm" disabled={!met} onClick={() => onConfirm(picked)}>
          <span>Pay {cost} &amp; field</span>
        </button>
      </div>
    </div>
  );
}

// A card ability waiting on its controller to pick targets (e.g. Honey
// Badger's "On Field: deal 1 damage to a target creature"). Real bug,
// direct feedback 2026-09-25: mobile had no UI for this at all - only the
// bot could answer one - so the server sat waiting on a choice the human
// couldn't see, and every other action bounced with "Resolve the pending
// choice before taking another action". No cancel: the ability has
// already triggered and has to resolve.
function ChoiceSheet({
  choice,
  dice,
  players,
  you,
  cardsById,
  busy,
  onConfirm,
}: {
  choice: PendingChoice;
  dice: Die[];
  players: PlayerState[];
  you: string;
  cardsById: Map<string, CardDef>;
  busy: boolean;
  onConfirm: (ids: string[]) => void;
}) {
  const [picked, setPicked] = useState<string[]>([]);
  const [minimized, setMinimized] = useState(false);
  const swipeStartY = useRef<number | null>(null);
  const ignoreNextClick = useRef(false);
  const startSheetSwipe = (event: React.PointerEvent<HTMLElement>) => {
    if (event.button !== 0) return;
    ignoreNextClick.current = false;
    swipeStartY.current = event.clientY;
    event.currentTarget.setPointerCapture(event.pointerId);
  };
  const finishSheetSwipe = (event: React.PointerEvent<HTMLElement>) => {
    if (swipeStartY.current === null) return;
    const distance = event.clientY - swipeStartY.current;
    swipeStartY.current = null;
    if ((!minimized && distance > 35) || (minimized && distance < -35)) {
      ignoreNextClick.current = true;
      setMinimized(!minimized);
    }
  };
  const handleHeaderClick = () => {
    if (ignoreNextClick.current) {
      ignoreNextClick.current = false;
      return;
    }
    if (minimized) setMinimized(false);
  };
  const max = Math.max(1, choice.maxCount);
  const toggle = (id: string) =>
    setPicked((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : max === 1 ? [id] : prev.length < max ? [...prev, id] : prev,
    );
  const ready = picked.length >= choice.minCount && picked.length <= max;
  const candidateDice = choice.candidateIds.map((id) => dice.find((d) => d.id === id)).filter((d): d is Die => !!d);
  const otherCandidates = choice.candidateIds.filter((id) => !dice.some((d) => d.id === id));
  const group = (mine: boolean) => candidateDice.filter((d) => (d.controllerId === you) === mine);
  // Every option one of your own Reserve dice: paying a cost (Breath
  // Weapon), not picking a target.
  const paying = candidateDice.length > 0 && candidateDice.every((d) => d.zone === "ReservePool" && d.controllerId === you);
  const renderGroup = (label: string, list: Die[], mine: boolean) =>
    list.length > 0 && (
      <>
        <span className="dkm-field-label">{label}</span>
        <div className="dkm-tile-row wrap">
          {list.map((d) => (
            <DTile
              key={d.id}
              die={d}
              cardsById={cardsById}
              size={54}
              mine={mine}
              flyId={false}
              clickable
              picked={picked.includes(d.id)}
              onClick={() => toggle(d.id)}
            />
          ))}
        </div>
      </>
    );
  return (
    <div className={`dkm-overlay-backdrop${minimized ? " dkm-choice-minimized" : ""}`}>
      <div className="dkm-sheet dkm-pay-sheet dkm-choice-sheet">
        <div
          className="dkm-choice-header"
          role="button"
          tabIndex={0}
          aria-expanded={!minimized}
          aria-label={minimized ? "Expand pending choice" : "Swipe down to minimize pending choice"}
          onClick={handleHeaderClick}
          onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setMinimized(!minimized); } }}
          onPointerDown={startSheetSwipe}
          onPointerUp={finishSheetSwipe}
          onPointerCancel={() => { swipeStartY.current = null; }}
        >
          <div className="dkm-choice-collapse"><span className="dkm-sheet-handle" /></div>
          <div className="dkm-popout-head">
            <span className="dkm-popout-title">{paying ? "Pay energy" : "Choose a target"}</span>
          </div>
          <p className="dkm-pay-status">
            <b>{choice.description}</b>
            {max > 1 && (
              <span>{" "}· {picked.length} / {max}</span>
            )}
          </p>
        </div>
        {choice.intent === "NameCard" ? (
          // Naming a card, not a die (Pangolin's lockout): one option per
          // card, by name - every die of it is an equivalent answer, and 32
          // unlabeled stat tiles made "which one is Silverback" a guess.
          <div className="dkm-name-card-list">
            {[...new Map(candidateDice.filter((d) => d.cardId).map((d) => [d.cardId!, d])).values()]
              .sort((a, b) => (cardsById.get(b.cardId!)?.purchaseCost ?? 0) - (cardsById.get(a.cardId!)?.purchaseCost ?? 0))
              .map((d) => {
                const card = cardsById.get(d.cardId!);
                const copies = candidateDice.filter((x) => x.cardId === d.cardId);
                const unbought = copies.filter((x) => x.zone === "Unpurchased").length;
                const isPicked = copies.some((x) => picked.includes(x.id));
                return (
                  <button
                    key={d.cardId}
                    type="button"
                    className={`dkm-secondary-btn dkm-name-card${isPicked ? " picked" : ""}`}
                    onClick={() => setPicked(isPicked ? [] : [d.id])}
                  >
                    <span className="dkm-name-card-main">
                      <span className="dkm-name-card-header">
                        <b>{card?.name ?? d.cardId}</b>
                        <small>cost {card?.purchaseCost} · {copies.length - unbought} owned, {unbought} unbought</small>
                      </span>
                      <span className="dkm-name-card-ability">
                        {card?.rawText?.trim() || card?.actionText?.trim() || "No character ability."}
                      </span>
                    </span>
                    <span className="dkm-name-card-faces" aria-label="Non-energy die faces">
                      {printedFacesFor(d, cardsById)
                        .filter((face) => face.kind !== "energy")
                        .map((face, index) => (
                          <span key={index} title={face.kind === "character" ? `Level ${face.level}: field ${face.fieldingCost}, attack ${face.attack}, defense ${face.defense}` : "Action face"}>
                            <DieCube faces={[face, face, face, face, face, face]} index={0} size={28} mine={d.controllerId === you} />
                          </span>
                        ))}
                    </span>
                  </button>
                );
              })}
          </div>
        ) : (
          <>
            {renderGroup("Theirs", group(false), false)}
            {renderGroup("Yours", group(true), true)}
          </>
        )}
        {otherCandidates.length > 0 && (
          <div className="dkm-tile-row wrap">
            {otherCandidates.map((id) => (
              <button
                key={id}
                type="button"
                className={`dkm-secondary-btn${picked.includes(id) ? " picked" : ""}`}
                onClick={() => toggle(id)}
              >
                {players.find((p) => p.id === id)?.name ?? id}
              </button>
            ))}
          </div>
        )}
        {choice.candidateIds.length === 0 && <span className="dkm-empty-hint">No legal targets.</span>}
        <button
          type="button"
          className="dkm-primary-btn dkm-pay-confirm"
          disabled={busy || !ready}
          onClick={() => onConfirm(picked)}
        >
          <span>{picked.length === 0 && choice.minCount === 0 ? "Skip" : "Confirm"}</span>
        </button>
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
  // Targets picked so far for the pending choice - see Targeting.
  const [choicePicked, setChoicePicked] = useState<string[]>([]);
  const choiceKey = game?.pendingChoice
    ? `${game.pendingChoice.controllerId}|${game.pendingChoice.description}|${game.pendingChoice.candidateIds.join(",")}`
    : "";
  useEffect(() => {
    setChoicePicked([]);
    // An open inspect sheet would sit on top of the board being targeted.
    if (choiceKey) setSelectedId(null);
  }, [choiceKey]);
  // Which lane's Attack Zone chip is showing its stat breakdown, or null -
  // direct feedback (2026-09-17): "click on the '1 v 3' and have it
  // explain where the numbers are coming from." Mutually exclusive with
  // selectedId (both use the same bottom-bar panel slot).
  const [laneBreakdown, setLaneBreakdown] = useState<number | null>(null);
  // Whether the log is expanded to the whole game (default: last few lines).
  const [logOpen, setLogOpen] = useState(false);
  const logScrollRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = logScrollRef.current;
    if (logOpen && el) el.scrollTop = el.scrollHeight;
  }, [logOpen, game?.log.length]);
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
  // The bot heartbeat effect below closes over its first render, so it must
  // read the live pairings through a ref (a stale {} made it skip Resolve
  // Damage forever and left the human stuck on "Waiting…").
  const blockAssignmentsRef = useRef(blockAssignments);
  blockAssignmentsRef.current = blockAssignments;
  const [stepsOpen, setStepsOpen] = useState(false);
  // Status cues' first-time legend: opens the first time any die shows a
  // cue (once per browser), and from the step pop-out after that.
  const [legendOpen, setLegendOpen] = useState(false);
  const anyCue = !!game?.dice.some((d) => (d.statuses?.length ?? 0) > 0);
  useEffect(() => {
    if (anyCue && !legendSeen()) setLegendOpen(true);
  }, [anyCue]);
  // Which player's roster the sheet is showing, or null when closed -
  // NOT a bare boolean (real bug, direct feedback 2026-09-16): both
  // mats' Roster buttons used to open the exact same sheet, which always
  // showed YOUR OWN unpurchased cards regardless of which one was
  // tapped, so the opponent's button silently showed your roster.
  const [rosterViewFor, setRosterViewFor] = useState<string | null>(null);
  // A fielding payment in progress: which die is being fielded (the sheet
  // asks which energy dice pay for it).
  const [payingFieldId, setPayingFieldId] = useState<string | null>(null);
  const [pileView, setPileView] = useState<{ mine: boolean; zone: PileZone } | null>(null);
  const [pileInspectId, setPileInspectId] = useState<string | null>(null);
  function onInspectPileDie(id: string) {
    setLaneBreakdown(null);
    setPileInspectId((previous) => {
      const next = previous === id ? null : id;
      setSelectedId(next);
      return next;
    });
  }
  function togglePileView(mine: boolean, zone: PileZone) {
    setPileView((previous) => previous?.mine === mine && previous.zone === zone ? null : { mine, zone });
    setSelectedId(null);
    setPileInspectId(null);
  }
  // A brief confirmation that startMatch's auto-copy (below) actually
  // landed - clipboard writes can silently fail (permissions, an
  // unsupported browser), so this only shows on the real success
  // callback, not just "we tried." Self-clears; the persistent Invite
  // row above the Log is still there afterward for a second copy.
  const [inviteCopiedBanner, setInviteCopiedBanner] = useState(false);
  const [linksOpen, setLinksOpen] = useState(false);
  const [linkCopied, setLinkCopied] = useState<"invite" | "own" | "failed-invite" | "failed-own" | null>(null);
  function copyLink(url: string, which: "invite" | "own") {
    navigator.clipboard?.writeText(url).then(
      () => setLinkCopied(which),
      () => setLinkCopied(which === "invite" ? "failed-invite" : "failed-own"),
    );
  }
  useEffect(() => {
    if (!linkCopied) return;
    const timer = window.setTimeout(() => setLinkCopied(null), 2000);
    return () => window.clearTimeout(timer);
  }, [linkCopied]);
  useEffect(() => {
    if (!inviteCopiedBanner) return;
    const timer = window.setTimeout(() => setInviteCopiedBanner(false), 4000);
    return () => window.clearTimeout(timer);
  }, [inviteCopiedBanner]);

  const { spins, offsets, launch: launchRoll, spinTo: spinDie } = useDiceRoll();

  useEffect(() => {
    api.getCards().then((cards) => setCardsById(new Map(cards.map((c) => [c.id, c]))));
  }, []);

  // An invite link in the URL: straight into the game, or - if the host
  // opened it with only their own Champion - a pick first (lobby.tsx).
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

  const gameId = game?.gameId ?? null;
  const gameVersion = game?.version ?? 0;

  // Label this game in the browser's saved list (seats.ts), for "Resume a game".
  const savedLabel = game ? `${game.playerOne.champion?.name ?? game.playerOne.name} vs ${game.playerTwo.champion?.name ?? game.playerTwo.name}` : null;
  useEffect(() => {
    if (gameId && savedLabel) describeSavedGame(gameId, savedLabel, vsComputer);
  }, [gameId, savedLabel, vsComputer]);
  useEffect(() => {
    if (!gameId) return;
    let cancelled = false;
    const timer = window.setInterval(async () => {
      if (busyRef.current) return;
      try {
        const latest = await api.getGame(gameId);
        if (cancelled || latest.version === gameVersion) return;
        // The other player's move: animate their roll/reroll (adoptRemote),
        // busy while a reroll's tumble is held on screen so this poll
        // can't cut the hold short.
        busyRef.current = true;
        setBusy(true);
        try {
          await adoptRemote(gameRef.current, latest);
        } finally {
          busyRef.current = false;
          setBusy(false);
        }
      } catch {
        // quiet - next poll either works or doesn't matter yet
      }
    }, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [gameId, gameVersion]);

  // Dice the ACTIVE player just rolled or rerolled, between two states that
  // arrived from elsewhere (a poll, or the computer's own move): newly
  // rolled into their Reserve Pool, or a face change there during Roll &
  // Reroll. Those tumble like your own; anything else just spins.

  // Adopts a state the OTHER player produced (user request, 2026-09-30:
  // "when watching your opponent roll and re-roll, it'd be nice to see the
  // animations instead of the dice just changing"). A reroll also ends
  // their Roll & Reroll step, which would unmount the tray mid-tumble - so,
  // exactly like runWithReveal does for your own reroll, the old step is
  // held on screen (with the new dice) until the tumble has been seen.
  async function adoptRemote(previous: GameState | null, next: GameState) {
    const rolledIds = previous ? remoteRolledIds(previous, next) : [];
    const leavesRoll = !!previous && previous.currentStepId === "roll-and-reroll" && next.currentStepId !== "roll-and-reroll";
    if (!previous || rolledIds.length === 0 || !leavesRoll) {
      startTransition(() => setGame(next));
      if (previous) requestAnimationFrame(() => animateRolledDice(previous, next, rolledIds));
      return;
    }
    startTransition(() => setGame({ ...next, currentStep: previous.currentStep, currentStepId: previous.currentStepId }));
    requestAnimationFrame(() => animateRolledDice(previous, next, rolledIds));
    await new Promise((resolve) => setTimeout(resolve, TUMBLE_REVEAL_HOLD_MS));
    setGame(next);
  }

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
      // ONE transition for the game AND the local UI state that depends on it.
      // Split across urgent + transition updates, React committed the cleared
      // local state (pending attackers, selection...) a frame BEFORE the new
      // game - e.g. declared attackers briefly snapped back to Field - and the
      // dice-flight layer animated that bounce. Same-batch = one clean commit.
      // The tumble starts FIRST (2026-10-03, direct feedback: "you can
      // sometimes see the new die face on a reroll before the animation
      // kicks in"). It used to start a frame AFTER the data commit (the
      // rAF below the choppiness note), so the landed face showed, still,
      // for a frame or more before spinning. Now the cheap spin update
      // commits first, on the die's OLD face, and the heavy data commit
      // lands while it's already tumbling - the new face only ever
      // appears in motion. The choppiness fix still holds: the animation
      // still doesn't start inside the expensive commit's frame, and a
      // running CSS transform animation isn't stalled by it.
      if (previous) animateRolledDice(previous, next, rolledDieIds);
      startTransition(() => {
        setGame(next);
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
      });
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
      // Tumble first, then the data - see run()'s remarks on why.
      animateRolledDice(previous, next, revealedDieIds);
      startTransition(() => setGame({ ...next, currentStep: previous.currentStep, currentStepId: previous.currentStepId }));
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
      !activeCouldAct(game, cardsById)
    ) {
      // Unblocked damage used to land the instant blockers were set - pause
      // so the player can see the blocks (or lack of them) first.
      const t = window.setTimeout(() => runQuiet(() => client.assignCombatDamage(gameId, [])), BOT_MOVE_DELAY_MS);
      return () => window.clearTimeout(t);
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
      const previous = gameRef.current;
      const raw = await fn();
      const next = { ...raw, yourPlayerId: raw.playerOne.id };
      await adoptRemote(previous, next); // the computer's roll tumbles like a human opponent's
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
      const map: Record<string, string[]> = {};
      for (const a of d.assignments) (map[a.attackerDieId] ??= []).push(a.blockerDieId);
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
    if (setupB === OPPONENT_PICKS) {
      // Online game with only your own Champion: open it, keep both seats
      // (yours plays, the other becomes the invite link), and wait.
      try {
        const opened = await api.openGame(setupA);
        rememberSeats(opened.gameId, opened.seats);
        setWaiting({ gameId: opened.gameId, hostChampionId: opened.hostChampionId });
        const link = inviteLink(opened.gameId, "/dice-kingdom/mobile");
        if (link) navigator.clipboard?.writeText(link).catch(() => {});
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
      }
      return;
    }
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

  // Hooks (must sit above the early return): dice flights + phase-card resize.
  const stageShellRef = useRef<HTMLDivElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const flightPhase = game ? phaseForStep(game.currentStepId) : "";
  usePhaseHeight(stageShellRef, flightPhase);
  useDieFlights(rootRef, game, flightPhase, game ? (game.yourPlayerId ?? game.playerOne.id) : "");

  if (!game && (waiting || invitePick)) {
    return (
      <div ref={rootRef} className="dicekingdom dk-mobile dkm-root">
        <EnergyBadgeOutlineDefs />
        <p className="dkm-eyebrow">DiceFight v3 · mobile</p>
        <h1 className="dkm-title">Dice Kingdom</h1>
        {error && <p className="dkm-error">{error}</p>}
        {waiting ? (
          <WaitingForOpponent
            gameId={waiting.gameId}
            hostChampionId={waiting.hostChampionId}
            link={inviteLink(waiting.gameId, "/dice-kingdom/mobile")}
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
      <div ref={rootRef} className="dicekingdom dk-mobile dkm-root">
        <EnergyBadgeOutlineDefs />
        <p className="dkm-eyebrow">DiceFight v3 · mobile</p>
        <h1 className="dkm-title">Dice Kingdom</h1>
        <p className="dkm-dek">
          Pick your Champion. For an online game, choose "Opponent picks" for Player 2 and send the link - they choose
          their own.
        </p>
        {error && <p className="dkm-error">{error}</p>}
        <ResumeGames
          onResume={(r) => {
            setError(null);
            if (r.kind === "game") {
              setVsComputer(r.vsComputer);
              setGame(r.game);
            } else setWaiting({ gameId: r.gameId, hostChampionId: r.hostChampionId });
          }}
        />
        {/* Same two-column layout as ../DiceKingdomPage.tsx's own picker
            (direct feedback, 2026-09-14: "makes more sense to have them
            in two columns") - reuses that page's own .champ-pick-columns/
            .champ-opt classes verbatim rather than a mobile-specific
            reimplementation, now that this page carries the .dicekingdom
            class those are scoped under (see the energy-badge fix's own
            remarks on why that class was added here). */}
        <div className="panel">
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
  // The tray belongs to whoever's turn it is (direct feedback, 2026-09-21:
  // on the opponent's turn it showed YOUR leftover dice, never the ones they
  // were rolling). The opponent's version is read-only.
  const trayOwnerDice = isYourTurn ? yourDice : oppDice;
  const trayDrawn = trayOwnerDice.filter((d) => d.zone === "DiceFromBag" || d.zone === "DiceFromPrep");
  const trayReserve = trayOwnerDice.filter((d) => d.zone === "ReservePool");
  const trayRolled = step === "roll-and-reroll" && trayDrawn.length === 0;
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
  // Basic Action dice are community property - the opponent's Champion's
  // action is yours to buy too.
  const unpurchasedByCard = unpurchasedFor(purchasableDiceFor(game, cardsById, you));
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
    // Declaration order, same as while declaring and as the engine deals
    // damage - game.dice is internal die order.
    for (const lane of attackersByLane) lane.sort((a, b) => (a.attackOrder ?? Infinity) - (b.attackOrder ?? Infinity));
  }
  // While the defender is still choosing, their local picks; once
  // declared, the server's copy (game.blocks) - the only one the
  // attacker's device ever has in two-device play (real bug, 2026-09-25:
  // the attacker saw no blockers at all). Nothing to show yet during
  // Declare Attackers itself.
  const blockersByAttacker = new Map<string, Die[]>();
  const shownBlocks: Record<string, string[]> =
    step === "assign-blockers" ? blockAssignments : groupBlocks(game.blocks ?? []);
  if (step !== "select-attackers") {
    for (const [attackerId, blockerIds] of Object.entries(shownBlocks)) {
      const blockers = blockerIds.map((id) => game.dice.find((d) => d.id === id)).filter((d): d is Die => !!d);
      if (blockers.length > 0) blockersByAttacker.set(attackerId, blockers);
    }
  }
  const laneAttackersForBreakdown = laneBreakdown !== null ? (attackersByLane[laneBreakdown] ?? []) : [];

  // What a card costs YOU to buy, discounts included (V2GameStateDto.PurchaseCosts).
  function purchaseCostOf(cardId: string): number {
    return game!.purchaseCosts?.[cardId] ?? cardsById.get(cardId)?.purchaseCost ?? 0;
  }

  function costFor(die: Die): { amount: number; matchType: string | null } {
    if (die.zone === "Unpurchased") {
      const card = die.cardId ? cardsById.get(die.cardId) : undefined;
      return { amount: purchaseCostOf(die.cardId ?? ""), matchType: card?.energyTypes[0] ?? null };
    }
    if (!die.cardId || die.level === null) return { amount: 0, matchType: null };
    const card = die.cardId ? cardsById.get(die.cardId) : undefined;
    // The server's cost, discounts included (V2DieDto.FieldingCost).
    return { amount: die.fieldingCost ?? card?.levels[die.level - 1]?.fieldingCost ?? 0, matchType: null };
  }

  function toggleReroll(id: string) {
    setRerollPicked((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function toggleSelect(id: string) {
    if (targeting) return; // the board is picking targets - see tapTarget
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
  const inspectingPileDie = selectedDie !== null && pileInspectId === selectedDie.id && pileView !== null;
  const selectedPurchaseCard = selectedDie?.zone === "Unpurchased" && selectedDie.cardId
    ? cardsById.get(selectedDie.cardId) : undefined;
  const purchaseCopies = selectedPurchaseCard && selectedDie
    ? game.dice.filter((d) => d.cardId === selectedPurchaseCard.id && d.ownerId === selectedDie.ownerId)
    : [];
  const purchaseUnowned = purchaseCopies.filter((d) => d.zone === "Unpurchased").length;
  // Bought by whom: a creature only by its owner, but a Basic Action by
  // either player - count the copies the viewer controls.
  const purchaseOwned = selectedPurchaseCard?.isAction
    ? purchaseCopies.filter((d) => d.zone !== "Unpurchased" && d.controllerId === you).length
    : purchaseCopies.length - purchaseUnowned;

  // Every ability's eligibility and payment comes from the same selector as desktop.
  const havePriority = game.priorityPlayerId === you;
  const abilities = getAbilityOptions(game, cardsById, you, busy);
  const { foresightReady } = abilities;
  const yourVirtual = youPlayer.virtualEnergy ?? 0;
  function doAbility(command: AbilityCommand) {
    setSelectedId(null);
    run(() => executeAbility(game!.gameId, command));
  }

  // The inspect panel's action list - README's own table, mapped onto
  // the actions this page can actually take right now.
  type InspectAction = { label: string; run: () => void; primary?: boolean };
  const inspectActions: InspectAction[] = [];
  if (selectedDie) {
    if (selectedDie.zone === "Unpurchased" && step === "main" && isYourTurn) {
      const { amount, matchType } = costFor(selectedDie);
      const ids = pickEnergyForCost(yourReserve, amount, matchType, yourVirtual);
      inspectActions.push({
        label: ids === null ? "Can't afford" : `Purchase (${amount})`,
        run: () => {
          if (ids !== null) run(() => api.purchase(game.gameId, selectedDie.id, ids));
        },
      });
    }
    if (selectedDie.zone === "ReservePool" && rolled(selectedDie) && selectedDie.effectiveAttack !== null && step === "main" && isYourTurn) {
      const { amount, matchType } = costFor(selectedDie);
      const ids = pickEnergyForCost(yourReserve, amount, matchType, yourVirtual);
      // Free (Tardigrade), covered by Virtual energy, or no real choice
      // (every energy die is needed) pays itself; anything else asks which
      // dice to spend.
      const spendable = yourReserve.filter((d) => d.energyAmount > 0 && d.id !== selectedDie.id);
      const noChoice = spendable.reduce((n, d) => n + d.energyAmount, 0) === amount;
      // Golden Eagle (2026-10-04): once per turn, field one creature free.
      if (youPlayer.freeFieldAvailable && amount > 0) {
        inspectActions.push({
          label: "Field free (Golden Eagle)",
          run: () => run(() => api.field(game.gameId, selectedDie.id, [], true)),
        });
      }
      inspectActions.push({
        label: ids === null ? "Can't afford" : "Field this creature",
        run: () => {
          if (ids === null) return;
          if (amount === 0 || ids.length === 0) run(() => api.field(game.gameId, selectedDie.id, []));
          else if (noChoice) run(() => api.field(game.gameId, selectedDie.id, spendable.map((d) => d.id)));
          else setPayingFieldId(selectedDie.id);
        },
      });
    }
    const foresightCommand = abilities.foresightDice.find((a) => a.die.id === selectedDie.id)?.command;
    if (foresightCommand) {
      inspectActions.push({
        label: "Foresight: reroll this die",
        run: () => doAbility(foresightCommand),
      });
    }
    const actionCommand = abilities.actionDice.find((a) => a.die.id === selectedDie.id)?.command;
    if (actionCommand) {
      inspectActions.push({
        label: `Use ${nameOf(selectedDie, cardsById)}`,
        run: () => doAbility(actionCommand),
      });
    }
    if (selectedDie.zone === "FieldZone" && selectedDie.controllerId === you && step === "select-attackers" && isYourTurn) {
      const already = selectedDie.id in pendingAttackers;
      inspectActions.push({
        label: already ? "Pull back" : `Declare into lane ${laneSel + 1}`,
        // Deselect after, same as tapping the lane - leaving it selected
        // flipped this same button to "Pull back" under your finger, so
        // it read as not having worked (direct feedback, 2026-09-30).
        run: () => {
          toggleAttacker(selectedDie.id);
          setSelectedId(null);
        },
      });
    }
    if (selectedDie.zone === "FieldZone" && selectedDie.controllerId === you && step === "assign-blockers" && !isYourTurn) {
      // Real buttons (direct feedback, 2026-09-30 - this used to be a
      // do-nothing "tap an attacker" label, with no way to pull a
      // blocker back from here): pull back if it's blocking, otherwise
      // one "Block lane N" per lane with an attacker in it.
      const blocking = Object.values(blockAssignments).some((ids) => ids.includes(selectedDie.id));
      if (blocking) {
        inspectActions.push({
          label: "Pull back",
          run: () => {
            removeBlocker(selectedDie.id);
            setSelectedId(null);
          },
        });
      } else {
        const lanes = new Map<number, string>();
        for (const a of game.dice.filter((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId && d.lane !== null))
          if (!lanes.has(a.lane!)) lanes.set(a.lane!, a.id);
        for (const [lane, attackerId] of [...lanes.entries()].sort(([x], [y]) => x - y))
          inspectActions.push({ label: `Block lane ${lane + 1}`, run: () => addBlocker(attackerId, selectedDie.id) });
      }
    }
  }

  // A pending choice that's yours to answer and whose candidates are all
  // dice on the board: pick them in place (see Targeting). Anything else
  // (a player, say) still falls back to ChoiceSheet.
  const myChoice = game.pendingChoice && game.pendingChoice.controllerId === you ? game.pendingChoice : null;
  const choiceOnBoard =
    !!myChoice &&
    myChoice.candidateIds.length > 0 &&
    myChoice.candidateIds.every((id) => game.dice.some((d) => d.id === id && (d.zone === "FieldZone" || d.zone === "AttackZone")));
  const targeting: Targeting | null = choiceOnBoard
    ? { candidates: new Set(myChoice!.candidateIds), picked: new Set(choicePicked) }
    : null;
  const choiceMax = myChoice ? Math.max(1, myChoice.maxCount) : 1;
  // Returns true when the tap was consumed by targeting (legal or not -
  // nothing else should happen to the board while a choice is open).
  const tapTarget = (id: string): boolean => {
    if (!targeting) return false;
    if (targeting.candidates.has(id)) {
      setChoicePicked((prev) =>
        prev.includes(id) ? prev.filter((x) => x !== id) : choiceMax === 1 ? [id] : prev.length < choiceMax ? [...prev, id] : prev,
      );
    }
    return true;
  };

  const onTapMatDie = (id: string) => {
    if (tapTarget(id)) return;
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
    if (tapTarget(attackerId)) return;
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
    if (tapTarget(blockerId)) return;
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

  if (game.gameOver) {
    primaryLabel = "Game over";
    primaryDisabled = true;
  } else if (myChoice) {
    const ready = choicePicked.length >= myChoice.minCount && choicePicked.length <= choiceMax;
    primaryLabel =
      choicePicked.length === 0 && myChoice.minCount === 0
        ? "Skip"
        : choiceMax > 1
          ? `Confirm targets (${choicePicked.length}/${choiceMax})`
          : "Confirm target";
    primaryNote = choiceOnBoard ? `${myChoice.description} Tap a highlighted die.` : myChoice.description;
    primaryDisabled = busy || !ready;
    primaryRun = () => run(() => api.resolvePendingChoice(game.gameId, choicePicked));
  } else if (game.pendingChoice) {
    primaryLabel = "Waiting…";
    primaryNote = `${oppPlayer.name} is choosing`;
    primaryDisabled = true;
  } else if (havePriority && !isYourTurn) {
    // They passed priority to you: one Global (from the rail), or pass.
    primaryLabel = "Pass";
    primaryNote = `${oppPlayer.name} passed - use one Global, or pass`;
    primaryRun = () => run(() => api.pass(game.gameId));
  } else if (isYourTurn && game.priorityPlayerId && !havePriority) {
    primaryLabel = "Waiting…";
    primaryNote = `${oppPlayer.name} may use a Global`;
    primaryDisabled = true;
  } else if (!isYourTurn && step !== "assign-blockers") {
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
      // A forced blocker (Hermit Crab) has to be assigned first - say which,
      // instead of letting the server reject the whole declaration.
      const assigned = new Set(Object.values(blockAssignments).flat());
      const unassignedForced = game.dice.filter(
        (d) => d.controllerId === you && d.zone === "FieldZone" && d.mustBlock && !assigned.has(d.id),
      );
      const anyAttacker = game.dice.some((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId);
      if (anyAttacker && unassignedForced.length > 0) {
        primaryDisabled = true;
        primaryNote = `${unassignedForced.map((d) => nameOf(d, cardsById)).join(" and ")} must block - assign ${unassignedForced.length === 1 ? "it" : "them"} to a lane`;
      }
    }
  } else if (step === "action-global-window") {
    // Passing only resolves damage if the other player can't respond; if
    // they could use a Global it hands them priority first (Priority.cs) -
    // "Resolve Damage" was the label either way (direct feedback 2026-09-30).
    primaryLabel = game.passGivesPriority ? "Pass Priority" : "Resolve Damage";
    if (game.passGivesPriority) primaryNote = `${oppPlayer.name} may use a Global, then damage resolves`;
    primaryRun = () => run(() => api.assignCombatDamage(game.gameId, game.blocks ?? []));
  } else {
    // return-to-field
    primaryLabel = "Pass Turn";
    primaryRun = () => run(() => api.cleanUp(game.gameId));
  }

  const link = inviteLink(game.gameId, "/dice-kingdom/mobile");
  const own = myLink(game.gameId, "/dice-kingdom/mobile");
  const logEntries = logOpen ? game.log : game.log.slice(-4);
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
    <div
      ref={rootRef}
      className={`dicekingdom dk-mobile dkm-root${targeting ? " dkm-targeting" : ""}`}
      onClickCapture={(event) => {
        if (!selectedId) return;
        const target = event.target;
        if (!(target instanceof Element)) return;
        // Keep the panel open for interactions inside it. Die clicks use their
        // own selection handlers, including tap-again-to-deselect.
        if (target.closest(
          ".dkm-inspect, .dkm-tile.clickable, .dkm-buy-tile, " +
          ".dkm-pile-inspect-tile, .dkm-lane-die-wrap"
        )) return;
        setSelectedId(null);
        setPileInspectId(null);
      }}
    >
      <EnergyBadgeOutlineDefs />
      <GameOverOverlay
        game={game}
        you={vsComputer ? game.playerOne.id : you}
        onNewGame={() => {
          forgetSeats();
          setGame(null);
        }}
      />
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
          onOpenPile={(zone) => togglePileView(false, zone)}
          openPile={pileView?.mine === false ? pileView.zone : null}
          expandable
          spins={spins}
          turnOffsets={offsets}
          selectedId={selectedId}
          fieldClickable={() => false}
          onTapDie={onTapAttacker}
          onInspectPileDie={onInspectPileDie}
          targeting={targeting}
        />

        <SharedAbilityPanel
          variant="mobile"
          abilities={abilities}
          onExecute={doAbility}
          nameOf={(d) => nameOf(d, cardsById)}
          actionTextOf={(d) => d.cardId ? cardsById.get(d.cardId)?.actionText : null}
          renderDie={(d) => <DTile die={d} cardsById={cardsById} size={34} mine flyId={false} />}
        />

        {/* Keyed by phase so React remounts this on every phase change,
            replaying dkPhaseIn (dicekingdom.css) - a plain settle-in
            rather than a hard cut when the view swaps to a new phase's
            card, direct feedback (2026-09-16): "...then shrink them into
            their destination." (runWithReveal above is what makes sure
            this swap only happens once a reroll's tumble has actually
            been seen, not the instant the server responds.) */}
        <div ref={stageShellRef} className="dkm-phase-shell">
        <div key={phase} className="dkm-phase-stage">
          {phase === "clear" && (
            <TrayCard
              title={isYourTurn ? "Draw" : `${oppPlayer.name} draws`}
              hint={`${trayOwnerDice.filter((d) => d.zone === "Bag").length} in bag`}
              dice={[]}
              cardsById={cardsById}
              rolledYet={false}
              rerollPicked={[]}
              onToggleReroll={() => {}}
              onInspectDie={() => {}}
              spins={spins}
              turnOffsets={offsets}
            />
          )}
          {phase === "roll" && (
            <TrayCard
              title={
                !isYourTurn ? `${oppPlayer.name}'s tray` : hasRolledThisStep ? "Tray · tap to select for reroll · long press to view die info" : "Tray"
              }
              hint={`${trayDrawn.length || trayReserve.length} dice`}
              dice={trayRolled ? trayReserve : trayDrawn}
              cardsById={cardsById}
              rolledYet={trayRolled}
              rerollPicked={isYourTurn ? rerollPicked : []}
              onToggleReroll={toggleReroll}
              onInspectDie={(id) => { setLaneBreakdown(null); setSelectedId(id); }}
              spins={spins}
              turnOffsets={offsets}
              interactive={isYourTurn}
            />
          )}
          {/* Only on your own turn - on the opponent's Main phase your
              Buy strip was just noise (direct feedback 2026-09-25: "Not
              necessary"); their Reserve row already shows what they
              have to spend. */}
          {phase === "main" && isYourTurn && (
            <BuyCard
              unpurchasedByCard={unpurchasedByCard}
              cardsById={cardsById}
              reserve={yourReserve}
              you={you}
              selectedId={selectedId}
              onSelect={toggleSelect}
              onOpenRoster={() => setRosterViewFor(you)}
              purchaseCostOf={purchaseCostOf}
              foresightReady={foresightReady}
              virtualEnergy={yourVirtual}
              lockedBy={new Map((game.lockedCards ?? []).filter((l) => l.playerId === you).map((l) => [l.cardId, l.sources]))}
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
              targeting={targeting}
            />
          )}
          {phase === "cleanup" && <CleanUpCard reserve={yourReserve} cardsById={cardsById} you={you} />}
        </div>
        </div>

        <MatCard
          mine
          onUsePower={
            youPlayer.championPowerUsable && game.priorityPlayerId === you && !game.pendingChoice && !busy
              ? () => run(() => api.championPower(game.gameId))
              : undefined
          }
          player={youPlayer}
          dice={yourFieldVisibleDice}
          cardsById={cardsById}
          isActivePlayer={you === game.activePlayerId}
          onOpenPile={(zone) => togglePileView(true, zone)}
          openPile={pileView?.mine === true ? pileView.zone : null}
          onOpenRoster={() => setRosterViewFor(you)}
          expandable={false}
          spins={spins}
          turnOffsets={offsets}
          selectedId={selectedId}
          fieldClickable={(d) =>
            (isYourTurn && phase === "main" && d.controllerId === you) ||
            (isYourTurn && step === "select-attackers" && d.controllerId === you) ||
            (!isYourTurn && step === "assign-blockers" && d.controllerId === you)
          }
          onTapDie={onTapMatDie}
          onInspectPileDie={onInspectPileDie}
          targeting={targeting}
        />

        {/* Game links, folded away behind one small toggle (user,
            2026-10-08: "it's unlikely to be used very often, so it could
            require a click to get to"). Invite = the other seat's link;
            Your seat = a link back into your own side, to keep or to
            carry on from another device. Says so when a copy worked
            (2026-09-27: it copied, but "felt like it did nothing"). Sits
            above the Log, out of the way; hidden in vs-computer mode,
            where there's no second player to send a link to. */}
        {!vsComputer && (link || own) && (
          <div className="dkm-invite">
            <button type="button" className="dkm-text-btn dkm-links-toggle" onClick={() => setLinksOpen((v) => !v)}>
              Game links {linksOpen ? "▾" : "▸"}
            </button>
            {linksOpen && link && (
              <button type="button" className="dkm-text-btn" onClick={() => copyLink(link, "invite")}>
                {linkCopied === "invite" ? "Invite copied ✓" : linkCopied === "failed-invite" ? "Couldn't copy" : "Copy invite"}
              </button>
            )}
            {linksOpen && own && (
              <button type="button" className="dkm-text-btn" onClick={() => copyLink(own, "own")}>
                {linkCopied === "own" ? "Your link copied ✓" : linkCopied === "failed-own" ? "Couldn't copy" : "Copy your seat's link"}
              </button>
            )}
          </div>
        )}

        <div className="dkm-log">
          {/* Tap "Log" for the whole game so far (direct feedback,
              2026-09-21) - collapsed it shows just the latest few lines. */}
          <button type="button" className="dkm-log-label dkm-log-toggle" onClick={() => setLogOpen((v) => !v)}>
            Log {logOpen ? "▾ full history" : `▸ ${game.log.length > 4 ? "tap for all " + game.log.length : ""}`}
          </button>
          {logEntries.length === 0 ? (
            <p className="dkm-empty-hint">Nothing has happened yet.</p>
          ) : (
            <div className={logOpen ? "dkm-log-scroll" : undefined} ref={logScrollRef}>
              {logEntries.map((entry, i) =>
                entry.isTurnStart ? (
                  <p key={entry.seq} className="dkm-log-turn">
                    <span>— — —</span> {entry.text} <span>— — —</span>
                  </p>
                ) : (
                  <p key={entry.seq} className={i === logEntries.length - 1 ? "dkm-log-line newest" : "dkm-log-line"}>
                    {entry.text}
                  </p>
                ),
              )}
            </div>
          )}
        </div>
      </div>

      <div className="dkm-bottom-bar">
        {selectedDie && (
          <div className="dkm-inspect" style={{ ["--cc" as string]: typeColorOf(selectedDie, cardsById) }}>
            <AvatarGlyph die={selectedDie} size={42} color={typeColorOf(selectedDie, cardsById)} />
            <div className="dkm-inspect-mid">
              <div className="dkm-inspect-title-row">
                <span className="dkm-inspect-name">{nameOf(selectedDie, cardsById)}</span>
                {(inspectingPileDie || selectedPurchaseCard || selectedDie.zone === "FieldZone" || (phase === "roll" && selectedDie.zone === "ReservePool")) && (
                  <div className="dkm-purchase-character-faces" aria-label="Non-energy die faces">
                    {printedFacesFor(selectedDie, cardsById)
                      .filter((face) => face.kind !== "energy")
                      .map((face, index) => (
                        <span key={index} title={face.kind === "character" ? `Level ${face.level}: field ${face.fieldingCost}, attack ${face.attack}, defense ${face.defense}` : "Action face"}>
                          <DieCube faces={[face, face, face, face, face, face]} index={0} size={28} mine />
                        </span>
                      ))}
                  </div>
                )}
              </div>
              <span className="dkm-inspect-sub">
                {/* A face can carry BOTH a character body and an energy
                    symbol at once (v3/DESIGN_NOTES.md's hybrid-face
                    rule) - character always wins the label here since
                    that's the stat that actually matters for this
                    step's actions. */}
                {selectedDie.zone === "FieldZone" || selectedDie.zone === "AttackZone" || selectedDie.zone === "Intimidated"
                  ? whereText(selectedDie, selectedDie.controllerId === you)
                  : `${selectedDie.level !== null
                      ? "character face"
                      : selectedDie.energySymbolId
                        ? `${selectedDie.energySymbolId} energy`
                        : "unrolled"} · ${selectedDie.zone}`}
              </span>
              {selectedDie.attackModifiers && (
                <span className="dkm-inspect-stats">
                  ATK {statBreakdown(selectedDie.baseAttack, selectedDie.attackModifiers, selectedDie.effectiveAttack)} · DEF{" "}
                  {statBreakdown(selectedDie.baseDefense, selectedDie.defenseModifiers, selectedDie.effectiveDefense)}
                </span>
              )}
              {/* Status cues (2026-10-03): what's going on with this die,
                  where it came from, and how long it lasts. */}
              <CueRows rows={explainRows(selectedDie, selectedDie.controllerId === you)} />
            </div>
            <button type="button" className="dkm-inspect-close" onClick={() => { setSelectedId(null); setPileInspectId(null); }}>
              ×
            </button>
            {inspectingPileDie && selectedDie.cardId && (
              <div className="dkm-purchase-ability">
                {cardsById.get(selectedDie.cardId)?.rawText?.trim() || cardsById.get(selectedDie.cardId)?.actionText?.trim() || "No character ability."}
              </div>
            )}
            {selectedPurchaseCard && (
              <div className="dkm-purchase-ability">
                {selectedPurchaseCard.rawText?.trim() || selectedPurchaseCard.actionText?.trim() || "No character ability."}
              </div>
            )}
{phase === "roll" && selectedDie.zone === "ReservePool" && selectedDie.cardId && (
  <div className="dkm-purchase-ability">
    {cardsById.get(selectedDie.cardId)?.rawText?.trim() || cardsById.get(selectedDie.cardId)?.actionText?.trim() || "No character ability."}
  </div>
)}

{selectedDie.zone === "FieldZone" && selectedDie.cardId && (
  <div className="dkm-purchase-ability">
    {cardsById.get(selectedDie.cardId)?.rawText?.trim() || cardsById.get(selectedDie.cardId)?.actionText?.trim() || "No character ability."}
  </div>
)}

{/* Show creature abilities in the Reserve Pool even if fielding is unaffordable. */}
{phase !== "roll" && selectedDie.zone === "ReservePool" && selectedDie.cardId && selectedDie.effectiveAttack !== null && (
  <div className="dkm-purchase-ability">
    {cardsById.get(selectedDie.cardId)?.rawText?.trim() || cardsById.get(selectedDie.cardId)?.actionText?.trim() || "No character ability."}
  </div>
)}
            {selectedPurchaseCard ? (
              <div className="dkm-purchase-row">
                <div className="dkm-purchase-count" aria-label={`${purchaseOwned} purchased`}>
                  <b>{purchaseOwned}</b><small>Purchased</small>
                </div>
                <div className="dkm-purchase-actions">
                  {inspectActions.map((a) => (
                    <button key={a.label} type="button" className="dkm-inspect-action"
                      disabled={a.label === "Can't afford"} onClick={a.run}>
                      {a.label}
                    </button>
                  ))}
                </div>
                <div className="dkm-purchase-count" aria-label={`${purchaseUnowned} unpurchased`}>
                  <b>{purchaseUnowned}</b><small>Unpurchased</small>
                </div>

              </div>
            ) : inspectActions.map((a) => (
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
              {/* A lane is ONE fight (2026-09-21): its attackers' Attack adds
                  together against its blockers' Defense, and its blockers'
                  Attack adds together against its attackers' Defense.
                  Overcrush (2+ attackers, or the keyword) sends the excess
                  to the player once every blocker is down. */}
              {(() => {
                const laneBlockers = laneBlockersOf(laneAttackersForBreakdown, blockersByAttacker);
                const atkTotal = laneAttackersForBreakdown.reduce((n, a) => n + (a.effectiveAttack ?? 0), 0);
                const defTotal = laneAttackersForBreakdown.reduce((n, a) => n + (a.effectiveDefense ?? 0), 0);
                const bAtkTotal = laneBlockers.reduce((n, b) => n + (b.effectiveAttack ?? 0), 0);
                const bDefTotal = laneBlockers.reduce((n, b) => n + (b.effectiveDefense ?? 0), 0);
                const faceDamage = laneFaceDamage(laneAttackersForBreakdown, laneBlockers, cardsById);
                const overcrush = laneOvercrush(laneAttackersForBreakdown, cardsById);
                return (
                  <div className="dkm-inspect-engagement">
                    {laneAttackersForBreakdown.map((a) => (
                      <span key={a.id} className="dkm-inspect-stats">
                        {nameOf(a, cardsById)} (attacking) — ATK {statBreakdown(a.baseAttack, a.attackModifiers, a.effectiveAttack) ?? "-"}
                        {laneBlockers.length > 0 && <>, DEF {statBreakdown(a.baseDefense, a.defenseModifiers, a.effectiveDefense) ?? "-"}</>}
                      </span>
                    ))}
                    {laneBlockers.map((b) => (
                      <span key={b.id} className="dkm-inspect-stats">
                        {nameOf(b, cardsById)} (blocking) — DEF {statBreakdown(b.baseDefense, b.defenseModifiers, b.effectiveDefense) ?? "-"}, ATK{" "}
                        {statBreakdown(b.baseAttack, b.attackModifiers, b.effectiveAttack) ?? "-"}
                      </span>
                    ))}
                    {laneBlockers.length === 0 ? (
                      <span className="dkm-inspect-stats">
                        <b className="dkm-ko-tag">Unblocked — {faceDamage} to face</b>
                      </span>
                    ) : (
                      <span className="dkm-inspect-stats">
                        Lane ATK {atkTotal} vs blockers' DEF {bDefTotal}
                        {atkTotal >= bDefTotal ? <b className="dkm-ko-tag"> → blockers KO'd</b> : " → blockers survive"}
                        {" · "}Blockers' ATK {bAtkTotal} vs lane DEF {defTotal}
                        {bAtkTotal >= defTotal && <b className="dkm-ko-tag"> → attackers KO'd</b>}
                        {overcrush && faceDamage > 0 && <b className="dkm-ko-tag"> · Overcrush: {faceDamage} to face</b>}
                      </span>
                    )}
                    {laneReflect(laneAttackersForBreakdown, laneBlockers, combatPreview(attackersByLane, blockersByAttacker), cardsById).map((r) => (
                      <span key={r.die.id} className="dkm-inspect-stats">
                        <b className="dkm-reflect-tag">
                          ↩ {nameOf(r.die, cardsById)} takes {r.amount} and reflects it to{" "}
                          {(r.die.controllerId === game.playerOne.id ? game.playerTwo : game.playerOne).name} (its ability, not combat damage)
                        </b>
                      </span>
                    ))}
                  </div>
                );
              })()}
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

      {payingFieldId && (() => {
        const die = game.dice.find((d) => d.id === payingFieldId);
        if (!die) return null;
        const { amount } = costFor(die);
        return (
          <PaymentSheet
            title={`Field ${nameOf(die, cardsById)}`}
            // Virtual energy pays first, server-side (fielding has no type).
            cost={Math.max(0, amount - yourVirtual)}
            energyDice={yourReserve.filter((d) => d.energyAmount > 0 && d.id !== die.id)}
            cardsById={cardsById}
            onConfirm={(ids) => {
              setPayingFieldId(null);
              setSelectedId(null);
              run(() => api.field(game.gameId, die.id, ids));
            }}
            onClose={() => setPayingFieldId(null)}
          />
        );
      })()}
      {myChoice && !choiceOnBoard && (
        <ChoiceSheet
          key={`${game.version}:${myChoice.description}`}
          choice={myChoice}
          dice={game.dice}
          players={[game.playerOne, game.playerTwo]}
          you={you}
          cardsById={cardsById}
          busy={busy}
          onConfirm={(ids) => run(() => api.resolvePendingChoice(game.gameId, ids))}
        />
      )}
      {stepsOpen && (
        <StepPopout
          phaseLabel={phaseLabel}
          steps={chainSteps}
          index={chainIndex}
          onClose={() => setStepsOpen(false)}
          onOpenLegend={() => {
            setStepsOpen(false);
            setLegendOpen(true);
          }}
        />
      )}
      {legendOpen && <DieFramesLegend variant="mobile" onClose={() => setLegendOpen(false)} />}
      {rosterViewFor && (
        <RosterSheet
          title={rosterViewFor === you ? "Your Roster" : "Their Roster"}
          cards={rosterViewFor === you ? rosterCards : oppRosterCards}
          canBuy={rosterViewFor === you && isYourTurn && step === "main"}
          reserve={yourReserve}
          onBuy={toggleSelect}
          onClose={() => setRosterViewFor(null)}
          purchaseCostOf={purchaseCostOf}
          virtualEnergy={yourVirtual}
        />
      )}
    </div>
  );
}
