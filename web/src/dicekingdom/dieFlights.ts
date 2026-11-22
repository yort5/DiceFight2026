// Dice that MOVE instead of blipping between places (direct feedback,
// 2026-09-19: "the dice can actually move across the screen instead of just
// blip out of existence in one place and appear in another").
//
// How it works: after every commit, `useDieFlights` measures every element
// tagged `data-fly-id` (dice: "die:<id>", Buy tiles: "card:<cardId>") and
// remembers it. On the NEXT commit it compares against that memory and, for
// a die whose zone / lane / on-screen region changed, launches a "ghost" - a
// fixed-position clone of where it WAS - that flies to where it IS now:
//   - the die still has an element (Tray -> Reserve row, Field -> lane,
//     lane -> Field, ...): fly to that element, which stays hidden until
//     the ghost lands so it never shows twice;
//   - the die has no element any more (KO'd, spent, out of play, bought):
//     fly into its pile (`data-pile="mine-used"` etc.). A KO first shakes
//     and flashes red where it stood, then goes to the Prep pile.
//   - a die with an element but no prior one (a fresh draw): fly out of the
//     pile it came from.
//   - a die that only ever moved between piles (no element either side - a
//     Used Pile refilling the Bag, a draw into Prep): a small token flies
//     pile to pile, and the pile it lands in pulses (2026-11-22, user: a
//     Resurrection that refilled the Bag and drew a die showed nothing but
//     counts changing). A refill goes first, then whatever was drawn out of
//     the refilled Bag flies on from there.
// Ghosts track their destination live every frame, so a layout that is
// still animating (see usePhaseHeight) or a scroll can't make them miss.
//
// Purely cosmetic: it never touches game state, and does nothing under
// prefers-reduced-motion.
import { useLayoutEffect, useRef, type RefObject } from "react";
import { thrownDice } from "./dice3dSetting";
import type { Die, GameState } from "./types";

interface Snap {
  el: HTMLElement;
  rect: DOMRect;
  region: string;
  scrollTop: number;
}

const FLIGHT_MS = 520;
const KO_HOLD_MS = 420;
const SPEND_MS = 380;
const STAGGER_MS = 70;
const MAX_FLIGHTS = 12;
const MIN_TRAVEL_PX = 8;

function reducedMotion(): boolean {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

const easeInOut = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

function regionOf(el: HTMLElement): string {
  const named = el.closest<HTMLElement>("[data-region]");
  if (named) return named.dataset.region!;
  const lane = el.closest<HTMLElement>("[data-lane]");
  if (lane) return `lane-${lane.dataset.lane}`;
  if (el.closest(".dkm-phase-stage")) return "stage";
  if (el.closest(".dkm-mat")) return "mat";
  return "other";
}

const PILE_KEYS: Record<string, string> = {
  UsedPile: "used",
  PrepArea: "prep",
  OutOfPlay: "out",
  Bag: "bag",
  ReservePool: "reserve",
  DiceFromBag: "drawn",
  DiceFromPrep: "carried",
  Unpurchased: "roster",
};

// The first VISIBLE match: the mobile page has two copies of some piles
// (one per layout), and the hidden one measures as an empty rect.
function pileEl(root: HTMLElement, die: Die, zone: string, you: string): HTMLElement | null {
  const key = PILE_KEYS[zone];
  if (!key) return null;
  const side = die.ownerId === you ? "mine" : "opp";
  const all = root.querySelectorAll<HTMLElement>(`[data-pile="${side}-${key}"]`);
  for (const el of all) {
    const r = el.getBoundingClientRect();
    if (r.width > 0 && r.height > 0) return el;
  }
  return all[0] ?? null;
}

const TOKEN_PX = 22;
const TOKEN_STAGGER_MS = 55;
const MAX_TOKENS = 10;

// A die with no element of its own, for a pile-to-pile flight: a small
// rounded square in its owner's Champion colour (the face it shows in a
// pile is arbitrary, so its own energy would look random).
function tokenNode(game: GameState, die: Die): HTMLElement {
  const el = document.createElement("div");
  el.className = "dkm-pile-token";
  const owner = die.ownerId === game.playerTwo.id ? game.playerTwo : game.playerOne;
  const energy = owner.champion?.energySymbolId;
  if (energy) el.style.setProperty("--tone", `var(--${energy.toLowerCase()})`);
  return el;
}

function centeredBox(r: DOMRect, size: number): DOMRect {
  return new DOMRect(r.left + r.width / 2 - size / 2, r.top + r.height / 2 - size / 2, size, size);
}

function bump(el: HTMLElement, delay: number) {
  window.setTimeout(() => {
    if (!el.isConnected) return;
    el.classList.remove("dkm-pile-bump");
    void el.offsetWidth; // restart the animation
    el.classList.add("dkm-pile-bump");
    window.setTimeout(() => el.classList.remove("dkm-pile-bump"), 500);
  }, delay);
}

interface Flight {
  from: DOMRect;
  node: HTMLElement;
  dest: () => DOMRect | null;
  hide?: HTMLElement;
  ko?: boolean;
  reason?: string;
  fadeAtEnd?: boolean;
  /** Begin shrunken (a die leaving a small pile) - 1 by default. */
  startScale?: number;
  /** A die spent as energy: quick, shrinks and fades out into the pile. */
  spend?: boolean;
  /** Extra wait before it starts, on top of the stagger. */
  delayMs?: number;
  /** Keep its own size the whole way (a pile token), not the destination's. */
  keepSize?: boolean;
  /** Called once it lands. */
  onLand?: () => void;
}

function launch(root: HTMLElement, f: Flight, index: number) {
  const ghost = document.createElement("div");
  ghost.className = "dkm-ghost";
  const inner = document.createElement("div");
  inner.className = `dkm-ghost-inner${f.ko ? " ko" : ""}`;
  const clone = f.node.cloneNode(true) as HTMLElement;
  // The clone must not be findable as a real die/pile by later queries.
  clone.removeAttribute("data-fly-id");
  clone.querySelectorAll("[data-fly-id],[data-pile]").forEach((n) => {
    n.removeAttribute("data-fly-id");
    n.removeAttribute("data-pile");
  });
  clone.style.visibility = "visible";
  inner.appendChild(clone);
  ghost.appendChild(inner);
  ghost.style.width = `${f.from.width}px`;
  ghost.style.height = `${f.from.height}px`;
  root.appendChild(ghost);

  const hidden = f.hide;
  const priorVisibility = hidden?.style.visibility ?? "";
  if (hidden) hidden.style.visibility = "hidden";

  const startScale = f.startScale ?? 1;
  const duration = f.spend ? SPEND_MS : FLIGHT_MS;
  const delay = (f.delayMs ?? index * STAGGER_MS) + (f.ko ? KO_HOLD_MS : 0);
  const start = performance.now() + delay;
  let last = f.from;
  let landed = false;
  const finish = () => {
    if (!landed) {
      landed = true;
      f.onLand?.();
    }
    ghost.remove();
    if (hidden && hidden.isConnected) hidden.style.visibility = priorVisibility;
  };
  ghost.style.transform = `translate(${f.from.left}px, ${f.from.top}px) scale(${startScale})`;
  // Not shown until it sets off - a delayed flight (a draw waiting on a
  // refill) mustn't sit on its pile in the meantime.
  if (delay > 0) ghost.style.opacity = "0";
  const frame = (now: number) => {
    const raw = (now - start) / duration;
    if (raw < 0) {
      requestAnimationFrame(frame);
      return;
    }
    if (ghost.style.opacity === "0") ghost.style.opacity = "1";
    const t = Math.min(1, raw);
    const e = easeInOut(t);
    const d = f.dest() ?? last;
    last = d;
    // Interpolate centres and scale about the centre, so a die that starts
    // small (or lands in a small pile) stays anchored to the right spot.
    const fcx = f.from.left + f.from.width / 2;
    const fcy = f.from.top + f.from.height / 2;
    const cx = fcx + (d.left + d.width / 2 - fcx) * e;
    const cy = fcy + (d.top + d.height / 2 - fcy) * e;
    const endScale = f.spend ? 0.35 : f.keepSize ? 1 : d.width / Math.max(1, f.from.width);
    const scale = startScale + (endScale - startScale) * e;
    ghost.style.transform = `translate(${cx - f.from.width / 2}px, ${cy - f.from.height / 2}px) scale(${scale})`;
    if (f.spend) ghost.style.opacity = String(1 - e);
    else if (f.fadeAtEnd) ghost.style.opacity = String(1 - 0.75 * e);
    if (t >= 1) finish();
    else requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
  // Safety net: never leave a ghost or a hidden die behind.
  window.setTimeout(finish, delay + duration + 1500);
}

export function useDieFlights(rootRef: RefObject<HTMLElement | null>, game: GameState | null, phase: string, you: string) {
  const snaps = useRef(new Map<string, Snap>());
  const prevDice = useRef(new Map<string, Die>());
  const prevPhase = useRef("");

  // Every commit on purpose (local UI state like a pending lane move also
  // moves dice); the work is a handful of getBoundingClientRect calls.
  useLayoutEffect(() => {
    const root = rootRef.current;
    if (!root || !game) return;
    const scroller = root.querySelector<HTMLElement>(".dkm-scroll");
    const scrollTop = scroller?.scrollTop ?? 0;

    const els = new Map<string, HTMLElement>();
    root.querySelectorAll<HTMLElement>("[data-fly-id]").forEach((el) => {
      const id = el.dataset.flyId!;
      if (!els.has(id)) els.set(id, el);
    });

    const flights: Flight[] = [];
    const prevGameExists = prevDice.current.size > 0;
    const phaseChanged = prevPhase.current !== "" && prevPhase.current !== phase;

    const pileMoves: { die: Die; from: string }[] = [];
    // Owners whose Used Pile refilled their Bag in this update, and how
    // long that wave of tokens takes - a die drawn in the same update came
    // out of the refilled Bag, so it waits and flies from there.
    const refillCount = new Map<string, number>();
    for (const die of game.dice) {
      const prev = prevDice.current.get(die.id);
      if (prev?.zone === "UsedPile" && die.zone === "Bag") refillCount.set(die.ownerId, (refillCount.get(die.ownerId) ?? 0) + 1);
    }
    const refillMs = (owner: string) => {
      const n = refillCount.get(owner) ?? 0;
      return n ? Math.min(n, MAX_TOKENS) * TOKEN_STAGGER_MS + FLIGHT_MS + 120 : 0;
    };
    if (!reducedMotion() && prevGameExists) {
      const adjust = (s: Snap) => {
        const dy = scrollTop - s.scrollTop;
        return new DOMRect(s.rect.left, s.rect.top - dy, s.rect.width, s.rect.height);
      };
      for (const die of game.dice) {
        const prev = prevDice.current.get(die.id);
        if (!prev) continue;
        if (thrownDice.has(die.id)) continue; // a 3D die is carrying it there
        const flyId = `die:${die.id}`;
        const el = els.get(flyId);
        const snap = snaps.current.get(flyId);
        const zoneChanged = prev.zone !== die.zone || prev.lane !== die.lane;

        if (el && snap) {
          const now = el.getBoundingClientRect();
          const from = adjust(snap);
          const moved = Math.hypot(now.left - from.left, now.top - from.top) + Math.abs(now.width - from.width);
          const regionNow = regionOf(el);
          // Only a real change of place counts: a different region (field <->
          // lane <-> lane N), or the phase card swapping under a die in the
          // stage (Tray -> Reserve row). A zone change alone does NOT - e.g.
          // "declare attackers" makes a lane die official without it moving,
          // and a small layout reflow must not replay the flight.
          const relocated = snap.region !== regionNow || (phaseChanged && regionNow === "stage");
          if (relocated && moved > MIN_TRAVEL_PX && snap.el !== el) {
            flights.push({ reason: `move ${die.id} ${snap.region}->${regionNow} ${prev.zone}->${die.zone}`, from, node: snap.el, dest: () => (el.isConnected ? el.getBoundingClientRect() : null), hide: el });
          } else if (relocated && moved > MIN_TRAVEL_PX) {
            // React reused the very same DOM node - it is already at its new
            // spot, so clone it as-is and fly from the remembered rect.
            flights.push({ reason: `move-same-node ${die.id} ${snap.region}->${regionNow} ${prev.zone}->${die.zone}`, from, node: el, dest: () => (el.isConnected ? el.getBoundingClientRect() : null), hide: el });
          }
        } else if (!el && snap && zoneChanged) {
          const target = pileEl(root, die, die.zone, you);
          if (!target) continue;
          const ko = die.zone === "PrepArea" && (prev.zone === "AttackZone" || prev.zone === "FieldZone");
          const spend = prev.zone === "ReservePool" && die.zone === "UsedPile";
          flights.push({
            reason: `vanish ${die.id} ${prev.zone}->${die.zone}${ko ? ' KO' : ''}`,
            spend,
            from: adjust(snap),
            node: snap.el,
            dest: () => (target.isConnected ? target.getBoundingClientRect() : null),
            ko,
            fadeAtEnd: true,
          });
        } else if (el && !snap && zoneChanged) {
          // A die that just appeared: bought (Buy tile -> here) or drawn (out of a pile).
          let from: DOMRect | null = null;
          if (prev.zone === "Unpurchased" && die.cardId) {
            const tile = snaps.current.get(`card:${die.cardId}`);
            if (tile) from = adjust(tile);
          }
          let startScale: number | undefined;
          let delayMs: number | undefined;
          if (!from) {
            const viaBag = prev.zone === "UsedPile" && refillCount.has(die.ownerId);
            if (viaBag) delayMs = refillMs(die.ownerId);
            const src = pileEl(root, die, viaBag ? "Bag" : prev.zone, you);
            if (src) {
              // Full-size die centred on the pile that grows out of it (a
              // pile-sized box would balloon the clone, then snap back).
              const p = src.getBoundingClientRect();
              const size = el.getBoundingClientRect();
              from = new DOMRect(p.left + p.width / 2 - size.width / 2, p.top + p.height / 2 - size.height / 2, size.width, size.height);
              startScale = 0.35;
            }
          }
          if (from) flights.push({ reason: `appear ${die.id} ${prev.zone}->${die.zone}`, from, node: el, dest: () => (el.isConnected ? el.getBoundingClientRect() : null), hide: el, startScale, delayMs });
        } else if (!el && !snap && zoneChanged && prev.zone !== "Unpurchased" && PILE_KEYS[prev.zone] && PILE_KEYS[die.zone]) {
          pileMoves.push({ die, from: prev.zone });
        } else if (!el && !snap && prev.zone === "Unpurchased" && die.zone !== "Unpurchased" && die.cardId) {
          // Bought and it has no element of its own (goes straight to the Used pile).
          const tile = snaps.current.get(`card:${die.cardId}`);
          const target = pileEl(root, die, die.zone, you);
          const roster = pileEl(root, die, "Unpurchased", you); // the opponent's Roster button
          if (tile && target) {
            flights.push({ from: adjust(tile), node: tile.el, dest: () => (target.isConnected ? target.getBoundingClientRect() : null), fadeAtEnd: true });
          } else if (roster && target) {
            const r = roster.getBoundingClientRect();
            flights.push({ from: r, node: roster, dest: () => (target.isConnected ? target.getBoundingClientRect() : null), fadeAtEnd: true, startScale: 0.6 });
          }
        }
      }
    }

    // Pile to pile: the refill wave (Used -> Bag) first, then anything that
    // left the Used Pile for somewhere else this update went through the
    // refilled Bag (drawn), so it flies on from there; plain moves (a draw
    // from a Bag that needed no refill) go with the first wave.
    const tokenFlights: Flight[] = [];
    if (pileMoves.length && root) {
      const refill = pileMoves.filter((m) => m.from === "UsedPile" && m.die.zone === "Bag");
      const lastInto = new Map<HTMLElement, Flight>();
      const queue = (die: Die, fromZone: string, toZone: string, delayMs: number) => {
        const src = pileEl(root, die, fromZone, you);
        const dst = pileEl(root, die, toZone, you);
        if (!src || !dst || src === dst) return;
        const flight: Flight = {
          reason: `pile ${die.id} ${fromZone}->${toZone}`,
          from: centeredBox(src.getBoundingClientRect(), TOKEN_PX),
          node: tokenNode(game, die),
          dest: () => (dst.isConnected ? centeredBox(dst.getBoundingClientRect(), TOKEN_PX) : null),
          delayMs,
          keepSize: true,
          fadeAtEnd: true,
        };
        tokenFlights.push(flight);
        const last = lastInto.get(dst);
        if (!last || (last.delayMs ?? 0) <= delayMs) lastInto.set(dst, flight);
      };
      refill.slice(0, MAX_TOKENS).forEach((m, i) => queue(m.die, "UsedPile", "Bag", i * TOKEN_STAGGER_MS));
      let later = 0;
      for (const m of pileMoves) {
        if (m.from === "UsedPile" && m.die.zone === "Bag") continue;
        if (later >= MAX_TOKENS) break;
        if (m.from === "UsedPile" && refillCount.has(m.die.ownerId)) queue(m.die, "Bag", m.die.zone, refillMs(m.die.ownerId) + later * TOKEN_STAGGER_MS);
        else queue(m.die, m.from, m.die.zone, later * TOKEN_STAGGER_MS);
        later++;
      }
      // Each pile a token lands in pulses once, as the last one arrives.
      lastInto.forEach((f, dst) => (f.onLand = () => bump(dst, 0)));
    }

    // Debug aid: set `window.__dkFlights = []` in the console to log why each flight launched.
    const dbg = (window as unknown as { __dkFlights?: string[] }).__dkFlights;
    if (dbg && flights.length + tokenFlights.length) dbg.push(...flights.concat(tokenFlights).map((f) => f.reason ?? '?'));
    flights.slice(0, MAX_FLIGHTS).forEach((f, i) => launch(root, f, i));
    tokenFlights.forEach((f, i) => launch(root, f, i));

    // Remember this commit for the next one.
    const next = new Map<string, Snap>();
    els.forEach((el, id) => {
      next.set(id, { el, rect: el.getBoundingClientRect(), region: regionOf(el), scrollTop });
    });
    snaps.current = next;
    prevDice.current = new Map(game.dice.map((d) => [d.id, d]));
    prevPhase.current = phase;
  });
}

// The card area under the phase rail grows/shrinks to the next phase's card
// instead of snapping (Tray -> Buy: "expand the Tray out until it becomes the
// Buy area"). Put `ref` on a persistent wrapper around the keyed phase card.
export function usePhaseHeight(ref: RefObject<HTMLElement | null>, phase: string) {
  const last = useRef<{ phase: string; height: number } | null>(null);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const height = el.getBoundingClientRect().height;
    const prev = last.current;
    last.current = { phase, height };
    if (!prev || prev.phase === phase || reducedMotion()) return;
    if (Math.abs(prev.height - height) < 4) return;
    el.style.overflow = "hidden";
    const anim = el.animate([{ height: `${prev.height}px` }, { height: `${height}px` }], {
      duration: 380,
      easing: "cubic-bezier(.22,.68,.36,1)",
    });
    const done = () => {
      el.style.overflow = "";
    };
    anim.onfinish = done;
    anim.oncancel = done;
  }, [phase, ref]);
}
