import { useEffect, useRef, useState } from "react";
import "./dicekingdom.css";
import { api } from "./api";
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
import type { CardDef, Die, GameState, PlayerState } from "./types";

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
const CHAMPIONS = [
  { id: "Wolf", energy: "Claw" },
  { id: "Armadillo", energy: "Shell" },
  { id: "GoldenEagle", energy: "Wing" },
  { id: "GreatHornedOwl", energy: "Eye" },
];
const LANE_COUNT = 4;

function rolled(d: Die): boolean {
  return d.effectiveAttack !== null || d.energySymbolId !== null;
}

function nameOf(die: Die, cardsById: Map<string, CardDef>): string {
  if (!die.cardId) return "Tardigrade";
  return cardsById.get(die.cardId)?.name ?? die.cardId;
}

// Cost is paid from whichever Reserve energy dice cover it - the mobile
// design drops individual Reserve die tiles entirely (the divider rail's
// own remarks: "all we need once we are past the Main step is the energy
// that is left"), showing only the aggregate chip row. The real engine
// still wants specific die ids though, so this picks a legal set behind
// the scenes rather than asking the player to hunt for exact-type dice
// one at a time - there is no meaningful choice being taken away (every
// die of a matching type is fungible for paying a cost).
function pickEnergyForCost(reserve: Die[], cost: number, matchType: string | null): string[] | null {
  if (cost <= 0) return [];
  const eligible = reserve
    .filter((d) => d.energyAmount > 0 && (!matchType || d.energySymbolId === matchType || d.energySymbolId === "Wild"))
    .sort((a, b) => a.energyAmount - b.energyAmount);
  const picked: string[] = [];
  let total = 0;
  for (const d of eligible) {
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

  return (
    <div className={`dkm-mat${mine ? " mine" : ""}${turnClass}`} style={{ ["--cc" as string]: accent }}>
      <div className="dkm-mat-head">
        <span className="dkm-mat-label">{mine ? "You" : "Opp"}</span>
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
  const fieldable = reserve.filter((d) => rolled(d) && d.effectiveAttack !== null);
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
              <span className="dkm-buy-cost">{card?.purchaseCost}</span>
            </button>
          );
        })}
        {unpurchasedByCard.size === 0 && <span className="dkm-empty-hint">Nothing left to buy.</span>}
      </div>
      <span className="dkm-field-label">Reserve · creature faces</span>
      <div className="dkm-tile-row wrap">
        {fieldable.length === 0 && <span className="dkm-empty-hint">Nothing rolled to field yet.</span>}
        {fieldable.map((d) => (
          <DTile key={d.id} die={d} cardsById={cardsById} size={50} mine picked={selectedId === d.id} clickable onClick={() => onSelect(d.id)} />
        ))}
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
function AttackLanesCard({
  isYourTurn,
  step,
  attackersByLane,
  blockersByAttacker,
  cardsById,
  laneSel,
  onTapLane,
  onTapAttacker,
  onTapBlockerOnAttacker,
  selectedId,
}: {
  isYourTurn: boolean;
  step: string;
  attackersByLane: Die[][];
  blockersByAttacker: Map<string, Die[]>;
  cardsById: Map<string, CardDef>;
  laneSel: number;
  onTapLane: (lane: number) => void;
  onTapAttacker: (id: string) => void;
  onTapBlockerOnAttacker: (attackerId: string) => void;
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
          const totalAtk = attackers.reduce((n, d) => n + (d.effectiveAttack ?? 0), 0);
          const blockers = attackers.flatMap((a) => blockersByAttacker.get(a.id) ?? []);
          const totalDef = blockers.reduce((n, d) => n + (d.effectiveDefense ?? 0), 0);
          const tileSize = attackers.length <= 1 ? 52 : attackers.length === 2 ? 42 : 34;
          const chipText = attackers.length === 0 ? null : blockers.length === 0 ? `${totalAtk} to face` : `${totalAtk} v ${totalDef}`;
          return (
            <button key={lane} type="button" className={`dkm-lane${targeted ? " targeted" : ""}`} onClick={() => onTapLane(lane)}>
              <div className="dkm-lane-blockers">
                {attackers.flatMap((a) => blockersByAttacker.get(a.id) ?? []).map((b) => (
                  <div
                    key={b.id}
                    className="dkm-lane-tile blocker"
                    style={{ height: tileSize }}
                    onClick={(e) => {
                      e.stopPropagation();
                      onTapBlockerOnAttacker(b.id);
                    }}
                  >
                    <span className="dkm-lane-def">{b.effectiveDefense}</span>
                    <span className="dkm-lane-name">{nameOf(b, cardsById)}</span>
                  </div>
                ))}
                {step === "assign-blockers" && !isYourTurn && attackers.length > 0 && blockers.length === 0 && (
                  <div className="dkm-lane-tile empty blocker-empty">no blocker</div>
                )}
              </div>
              {chipText && <span className="dkm-lane-chip">{chipText}</span>}
              <div className="dkm-lane-attackers">
                {attackers.map((a) => (
                  <div
                    key={a.id}
                    className={`dkm-lane-tile attacker${selectedId === a.id ? " picked" : ""}`}
                    style={{ height: tileSize }}
                    onClick={(e) => {
                      e.stopPropagation();
                      onTapAttacker(a.id);
                    }}
                  >
                    <span className="dkm-lane-atk">{a.effectiveAttack}</span>
                    <span className="dkm-lane-name">{nameOf(a, cardsById)}</span>
                  </div>
                ))}
                {attackers.length === 0 && <div className="dkm-lane-tile empty attacker-empty" />}
              </div>
              <span className="dkm-lane-number">{String(lane + 1).padStart(2, "0")}</span>
            </button>
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

function RosterSheet({
  cards,
  onClose,
}: {
  cards: { card: CardDef | undefined; cardId: string; remaining: number }[];
  onClose: () => void;
}) {
  return (
    <div className="dkm-overlay-backdrop" onClick={onClose}>
      <div className="dkm-sheet" onClick={(e) => e.stopPropagation()}>
        <div className="dkm-sheet-handle" />
        <div className="dkm-popout-head">
          <span className="dkm-popout-title">Roster</span>
          <button type="button" className="dkm-text-btn" onClick={onClose}>
            tap to close
          </button>
        </div>
        {cards.map(({ card, cardId, remaining }) => {
          const Avatar = CHARACTER_ICONS[cardId];
          return (
            <div key={cardId} className="dkm-roster-row">
              <span className="dkm-roster-avatar">{Avatar ? <Avatar size={20} /> : <TardigradeIcon size={20} />}</span>
              <div className="dkm-roster-mid">
                <span className="dkm-roster-name">{card?.name ?? cardId}</span>
                {card && card.keywords.length > 0 && <span className="dkm-dashed-badge">{card.keywords.join(", ")}</span>}
              </div>
              <span className="dkm-roster-cost">{card?.purchaseCost}</span>
              <span className="dkm-roster-left">{remaining}</span>
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
  const [cardsById, setCardsById] = useState<Map<string, CardDef>>(new Map());

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [rerollPicked, setRerollPicked] = useState<string[]>([]);
  const [rerollUsedThisStep, setRerollUsedThisStep] = useState(false);
  const [pendingAttackers, setPendingAttackers] = useState<Record<string, number>>({});
  const [laneSel, setLaneSel] = useState(0);
  const [blockAssignments, setBlockAssignments] = useState<Record<string, string | null>>({});
  const [stepsOpen, setStepsOpen] = useState(false);
  const [rosterOpen, setRosterOpen] = useState(false);

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
      setGame(next);
      if (previous) animateRolledDice(previous, next, rolledDieIds);
      setSelectedId(null);
      if (next.currentStepId !== "roll-and-reroll") {
        setRerollPicked([]);
        setRerollUsedThisStep(false);
      }
      if (next.currentStepId !== "select-attackers") setPendingAttackers({});
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

  async function runQuiet(fn: () => Promise<GameState>) {
    if (busyRef.current) return;
    busyRef.current = true;
    try {
      const next = await fn();
      setGame(next);
    } catch {
      // expected on whichever browser doesn't hold the seat this needed
    } finally {
      busyRef.current = false;
    }
  }

  // Same auto-skip-through-an-empty-window behavior as the desktop page
  // and /game before it - see ../DiceKingdomPage.tsx's identical effect.
  const assignBlockersAttackerCount = game
    ? game.dice.filter((d) => d.zone === "AttackZone" && d.controllerId === game.activePlayerId).length
    : 0;
  useEffect(() => {
    if (!gameId || !game) return;
    if (game.currentStepId === "assign-blockers" && assignBlockersAttackerCount === 0) {
      runQuiet(() => api.declareBlockers(gameId, []));
    } else if (game.currentStepId === "action-global-window" && Object.values(blockAssignments).filter(Boolean).length === 0) {
      runQuiet(() => api.assignCombatDamage(gameId, []));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gameId, game?.version, game?.currentStepId, assignBlockersAttackerCount]);

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
      <div className="dk-mobile dkm-root">
        <p className="dkm-eyebrow">DiceFight v3 · mobile</p>
        <h1 className="dkm-title">Dice Kingdom</h1>
        <p className="dkm-dek">Pick a Champion for each seat, then send the invite link from inside the match.</p>
        {error && <p className="dkm-error">{error}</p>}
        {[
          { label: "Player 1", value: setupA, setValue: setSetupA },
          { label: "Player 2", value: setupB, setValue: setSetupB },
        ].map(({ label, value, setValue }) => (
          <div className="dkm-champ-pick" key={label}>
            <h3>{label}</h3>
            <div className="dkm-champ-grid">
              {CHAMPIONS.map((c) => {
                const Icon = CHAMPION_ICONS[c.id];
                return (
                  <button
                    key={c.id}
                    type="button"
                    className={`dkm-champ-opt${value === c.id ? " selected" : ""}`}
                    style={{ ["--cc" as string]: `var(--${c.energy.toLowerCase()})` }}
                    onClick={() => setValue(c.id)}
                  >
                    <Icon size={26} />
                    <span>{c.id.replace(/([A-Z])/g, " $1").trim()}</span>
                  </button>
                );
              })}
            </div>
          </div>
        ))}
        <button className="dkm-primary-btn" disabled={!setupA || !setupB || busy} onClick={startMatch}>
          <span>Start Match</span>
        </button>
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

  const drawnZone = yourDice.filter((d) => d.zone === "DiceFromBag" || d.zone === "DiceFromPrep");
  const hasRolledThisStep = step === "roll-and-reroll" && drawnZone.length === 0;
  const { steps: chainSteps, index: chainIndex } = chainFor(phase, step, hasRolledThisStep || rerollUsedThisStep, game.dice, game.activePlayerId, cardsById);

  const unpurchasedByCard = new Map<string, Die[]>();
  for (const d of yourDice.filter((d) => d.zone === "Unpurchased")) {
    if (!d.cardId) continue;
    unpurchasedByCard.set(d.cardId, [...(unpurchasedByCard.get(d.cardId) ?? []), d]);
  }

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
    for (const [attackerId, blockerId] of Object.entries(blockAssignments)) {
      if (!blockerId) continue;
      const blocker = game.dice.find((d) => d.id === blockerId);
      if (blocker) blockersByAttacker.set(attackerId, [blocker]);
    }
  }

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
    setSelectedId((cur) => (cur === id ? null : id));
  }

  function toggleAttacker(dieId: string) {
    setPendingAttackers((prev) => {
      const next = { ...prev };
      if (dieId in next) delete next[dieId];
      else next[dieId] = laneSel;
      return next;
    });
  }

  function assignBlocker(attackerId: string, blockerId: string) {
    setBlockAssignments((prev) => {
      const next: Record<string, string | null> = {};
      for (const [aid, bid] of Object.entries(prev)) next[aid] = bid === blockerId ? null : bid;
      next[attackerId] = blockerId;
      return next;
    });
    setSelectedId(null);
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
        assignBlocker(attackerId, selectedId);
        return;
      }
    }
    if (blockAssignments[attackerId] && step === "assign-blockers" && !isYourTurn) {
      setBlockAssignments((prev) => ({ ...prev, [attackerId]: null }));
      return;
    }
    toggleSelect(attackerId);
  };

  // ---- Primary button ----
  let primaryLabel = "";
  let primaryNote: string | undefined;
  let primaryDisabled = busy;
  let primaryRun: (() => void) | null = null;

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
      primaryRun = () => run(() => api.roll(game.gameId), yourReserve.map((d) => d.id));
    } else if (rerollPicked.length > 0) {
      primaryLabel = `Reroll (${rerollPicked.length})`;
      primaryRun = () =>
        run(() => api.reroll(game.gameId, rerollPicked), rerollPicked).then(() => setRerollUsedThisStep(true));
    } else {
      primaryLabel = "To Reserve";
      primaryRun = () => run(() => api.finishRoll(game.gameId));
    }
  } else if (step === "main") {
    primaryLabel = "Done buying";
    primaryNote = "enter the Attack Step";
    primaryRun = () => run(() => api.enterAttackStep(game.gameId));
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
      primaryRun = () =>
        run(() =>
          api.declareBlockers(
            game.gameId,
            Object.entries(blockAssignments)
              .filter((e): e is [string, string] => !!e[1])
              .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId })),
          ),
        );
    }
  } else if (step === "action-global-window") {
    primaryLabel = "Resolve Damage";
    primaryRun = () =>
      run(() =>
        api.assignCombatDamage(
          game.gameId,
          Object.entries(blockAssignments)
            .filter((e): e is [string, string] => !!e[1])
            .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId })),
        ),
      );
  } else {
    // return-to-field
    primaryLabel = "Pass Turn";
    primaryRun = () => run(() => api.cleanUp(game.gameId));
  }

  const link = inviteLink(game.gameId, "/dice-kingdom/mobile");
  const logEntries = game.log.slice(-4);
  const rosterCards = [...unpurchasedByCard.keys()].map((cardId) => ({
    card: cardsById.get(cardId),
    cardId,
    remaining: unpurchasedByCard.get(cardId)?.length ?? 0,
  }));

  return (
    <div className="dk-mobile dkm-root">
      <div className="dkm-header">
        <PhaseRail current={phase} onTap={() => {}} />
        <StepLine title={chainSteps[chainIndex]?.label ?? phaseLabel} index={chainIndex} total={chainSteps.length} onToggle={() => setStepsOpen((v) => !v)} />
      </div>

      <div className="dkm-scroll">
        {error && <p className="dkm-error">{error}</p>}
        {link && (
          <div className="dkm-invite">
            <span>Invite</span>
            <button type="button" className="dkm-text-btn" onClick={() => navigator.clipboard?.writeText(link)}>
              Copy link
            </button>
          </div>
        )}

        <MatCard
          mine={false}
          player={oppPlayer}
          dice={oppDice}
          cardsById={cardsById}
          isActivePlayer={opponentId === game.activePlayerId}
          onOpenRoster={() => setRosterOpen(true)}
          expandable
          spins={spins}
          turnOffsets={offsets}
          selectedId={selectedId}
          fieldClickable={() => false}
          onTapDie={onTapAttacker}
        />

        <GlobalRail />

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
            onOpenRoster={() => setRosterOpen(true)}
          />
        )}
        {phase === "attack" && (
          <AttackLanesCard
            isYourTurn={isYourTurn}
            step={step}
            attackersByLane={attackersByLane}
            blockersByAttacker={blockersByAttacker}
            cardsById={cardsById}
            laneSel={laneSel}
            onTapLane={setLaneSel}
            onTapAttacker={onTapAttacker}
            onTapBlockerOnAttacker={(id) => toggleSelect(id)}
            selectedId={selectedId}
          />
        )}
        {phase === "cleanup" && <CleanUpCard reserve={yourReserve} cardsById={cardsById} you={you} />}

        <MatCard
          mine
          player={youPlayer}
          dice={yourDice}
          cardsById={cardsById}
          isActivePlayer={you === game.activePlayerId}
          onOpenRoster={() => setRosterOpen(true)}
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
        <button type="button" className="dkm-primary-btn" disabled={primaryDisabled} onClick={() => primaryRun?.()}>
          <span>{primaryLabel}</span>
          {primaryNote && <small>{primaryNote}</small>}
        </button>
      </div>

      {stepsOpen && <StepPopout phaseLabel={phaseLabel} steps={chainSteps} index={chainIndex} onClose={() => setStepsOpen(false)} />}
      {rosterOpen && <RosterSheet cards={rosterCards} onClose={() => setRosterOpen(false)} />}
    </div>
  );
}
