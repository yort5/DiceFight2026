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
// Ghosts track their destination live every frame, so a layout that is
// still animating (see usePhaseHeight) or a scroll can't make them miss.
//
// Purely cosmetic: it never touches game state, and does nothing under
// prefers-reduced-motion.
import { useLayoutEffect, useRef, type RefObject } from "react";
import type { Die, GameState } from "./types";

interface Snap {
  el: HTMLElement;
  rect: DOMRect;
  region: string;
  scrollTop: number;
}

const FLIGHT_MS = 520;
const KO_HOLD_MS = 420;
const STAGGER_MS = 70;
const MAX_FLIGHTS = 12;
const MIN_TRAVEL_PX = 8;

function reducedMotion(): boolean {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

const easeInOut = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

function regionOf(el: HTMLElement): string {
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
};

function pileEl(root: HTMLElement, die: Die, zone: string, you: string): HTMLElement | null {
  const key = PILE_KEYS[zone];
  if (!key) return null;
  const side = die.ownerId === you ? "mine" : "opp";
  return root.querySelector<HTMLElement>(`[data-pile="${side}-${key}"]`);
}

interface Flight {
  from: DOMRect;
  node: HTMLElement;
  dest: () => DOMRect | null;
  hide?: HTMLElement;
  ko?: boolean;
  fadeAtEnd?: boolean;
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
  ghost.style.transform = `translate(${f.from.left}px, ${f.from.top}px)`;
  root.appendChild(ghost);

  const hidden = f.hide;
  const priorVisibility = hidden?.style.visibility ?? "";
  if (hidden) hidden.style.visibility = "hidden";

  const delay = index * STAGGER_MS + (f.ko ? KO_HOLD_MS : 0);
  const start = performance.now() + delay;
  let last = f.from;
  const finish = () => {
    ghost.remove();
    if (hidden && hidden.isConnected) hidden.style.visibility = priorVisibility;
  };
  const frame = (now: number) => {
    const raw = (now - start) / FLIGHT_MS;
    if (raw < 0) {
      requestAnimationFrame(frame);
      return;
    }
    const t = Math.min(1, raw);
    const e = easeInOut(t);
    const d = f.dest() ?? last;
    last = d;
    const x = f.from.left + (d.left - f.from.left) * e;
    const y = f.from.top + (d.top - f.from.top) * e;
    const sx = 1 + (d.width / Math.max(1, f.from.width) - 1) * e;
    const sy = 1 + (d.height / Math.max(1, f.from.height) - 1) * e;
    ghost.style.transform = `translate(${x}px, ${y}px) scale(${sx}, ${sy})`;
    if (f.fadeAtEnd) ghost.style.opacity = String(1 - 0.75 * e);
    if (t >= 1) finish();
    else requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
  // Safety net: never leave a ghost or a hidden die behind.
  window.setTimeout(finish, delay + FLIGHT_MS + 1500);
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

    if (!reducedMotion() && prevGameExists) {
      const adjust = (s: Snap) => {
        const dy = scrollTop - s.scrollTop;
        return new DOMRect(s.rect.left, s.rect.top - dy, s.rect.width, s.rect.height);
      };
      for (const die of game.dice) {
        const prev = prevDice.current.get(die.id);
        if (!prev) continue;
        const flyId = `die:${die.id}`;
        const el = els.get(flyId);
        const snap = snaps.current.get(flyId);
        const zoneChanged = prev.zone !== die.zone || prev.lane !== die.lane;

        if (el && snap) {
          const now = el.getBoundingClientRect();
          const from = adjust(snap);
          const moved = Math.hypot(now.left - from.left, now.top - from.top) + Math.abs(now.width - from.width);
          const regionChanged = snap.region !== regionOf(el);
          if ((zoneChanged || phaseChanged || regionChanged) && moved > MIN_TRAVEL_PX && snap.el !== el) {
            flights.push({ from, node: snap.el, dest: () => (el.isConnected ? el.getBoundingClientRect() : null), hide: el });
          } else if ((zoneChanged || phaseChanged || regionChanged) && moved > MIN_TRAVEL_PX) {
            // React reused the very same DOM node - it is already at its new
            // spot, so clone it as-is and fly from the remembered rect.
            flights.push({ from, node: el, dest: () => (el.isConnected ? el.getBoundingClientRect() : null), hide: el });
          }
        } else if (!el && snap && zoneChanged) {
          const target = pileEl(root, die, die.zone, you);
          if (!target) continue;
          const ko = die.zone === "PrepArea" && (prev.zone === "AttackZone" || prev.zone === "FieldZone");
          flights.push({
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
          if (!from) {
            const src = pileEl(root, die, prev.zone, you);
            if (src) from = src.getBoundingClientRect();
          }
          if (from) flights.push({ from, node: el, dest: () => (el.isConnected ? el.getBoundingClientRect() : null), hide: el });
        } else if (!el && !snap && prev.zone === "Unpurchased" && die.zone !== "Unpurchased" && die.cardId) {
          // Bought and it has no element of its own (goes straight to the Used pile).
          const tile = snaps.current.get(`card:${die.cardId}`);
          const target = pileEl(root, die, die.zone, you);
          if (tile && target) {
            flights.push({ from: adjust(tile), node: tile.el, dest: () => (target.isConnected ? target.getBoundingClientRect() : null), fadeAtEnd: true });
          }
        }
      }
    }

    flights.slice(0, MAX_FLIGHTS).forEach((f, i) => launch(root, f, i));

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
