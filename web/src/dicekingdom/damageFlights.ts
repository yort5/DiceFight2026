// Damage you can see land (user, 2026-11-27: "might be nice to have some
// kind of animation showing the damage to each player"). The life badges
// already flash and float a "-N" (MatCard), but in combat you're usually
// scrolled to the Attack Zone with one or both badges off screen. So:
//   - when a player's life drops, each lane chip that read "N to face" for
//     them a moment ago sends a "-N" flying from the lane to their life
//     badge - or to the screen's edge toward it, if it's out of view;
//   - when their badge is out of view, a callout at that edge says who
//     lost (or gained) how much.
// Mobile only. Purely cosmetic - it reads the game, never changes it - and
// does nothing under prefers-reduced-motion.
//
// Markup it reads: lane chips carry data-face-damage (the number) and
// data-face-side ("mine" / "opp", whose life it comes off); life badges
// carry data-life-side.
import { useLayoutEffect, useRef, type RefObject } from "react";
import type { GameState } from "./types";

type Side = "mine" | "opp";
interface Chip {
  side: Side;
  n: number;
  rect: DOMRect;
}

const FLY_MS = 650;
const STAGGER_MS = 140;
const CALLOUT_MS = 1600;

function reducedMotion(): boolean {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

// The visible band of the screen: under the header, above the bottom bar.
function band(root: HTMLElement): { top: number; bottom: number } {
  const header = root.querySelector<HTMLElement>(".dkm-header")?.getBoundingClientRect();
  const bar = root.querySelector<HTMLElement>(".dkm-bottom-bar")?.getBoundingClientRect();
  return { top: header ? header.bottom : 0, bottom: bar ? bar.top : window.innerHeight };
}

// The player's life badge, if one is fully in view.
function visibleBadge(root: HTMLElement, side: Side): DOMRect | null {
  const { top, bottom } = band(root);
  for (const el of root.querySelectorAll<HTMLElement>(`[data-life-side="${side}"]`)) {
    const r = el.getBoundingClientRect();
    // Wholly in view - one half under the bottom bar is easy to miss.
    if (r.width > 0 && r.top >= top && r.bottom <= bottom) return r;
  }
  return null;
}

// Where a hit heads for: the badge's middle, else the edge toward it.
function targetPoint(root: HTMLElement, side: Side): { x: number; y: number; onScreen: boolean } {
  const badge = visibleBadge(root, side);
  if (badge) return { x: badge.left + badge.width / 2, y: badge.top + badge.height / 2, onScreen: true };
  const { top, bottom } = band(root);
  return { x: window.innerWidth / 2, y: side === "opp" ? top + 28 : bottom - 28, onScreen: false };
}

function fly(root: HTMLElement, chip: Chip, to: { x: number; y: number }, delay: number) {
  const el = document.createElement("div");
  el.className = "dk-dmg-fly";
  el.textContent = `−${chip.n}`;
  root.appendChild(el);
  const x0 = chip.rect.left + chip.rect.width / 2;
  const y0 = chip.rect.top + chip.rect.height / 2;
  const at = (x: number, y: number, s: number) => `translate(${x}px, ${y}px) translate(-50%, -50%) scale(${s})`;
  const anim = el.animate(
    [
      { transform: at(x0, y0, 0.6), opacity: 0 },
      { transform: at(x0, y0 - 18, 1.35), opacity: 1, offset: 0.2 },
      { transform: at(to.x, to.y, 0.9), opacity: 1, offset: 0.9 },
      { transform: at(to.x, to.y, 0.4), opacity: 0 },
    ],
    { duration: FLY_MS, delay, easing: "cubic-bezier(.5,0,.6,1)", fill: "both" },
  );
  const done = () => el.remove();
  anim.onfinish = done;
  anim.oncancel = done;
  window.setTimeout(done, delay + FLY_MS + 500);
}

function callout(root: HTMLElement, side: Side, text: string, loss: boolean, delay: number) {
  const { top, bottom } = band(root);
  const el = document.createElement("div");
  el.className = `dk-dmg-callout ${loss ? "loss" : "gain"}`;
  el.textContent = text;
  el.style.top = side === "opp" ? `${top + 10}px` : "";
  el.style.bottom = side === "mine" ? `${window.innerHeight - bottom + 10}px` : "";
  root.appendChild(el);
  const anim = el.animate(
    [
      { transform: "translateX(-50%) scale(.6)", opacity: 0 },
      { transform: "translateX(-50%) scale(1.12)", opacity: 1, offset: 0.12 },
      { transform: "translateX(-50%) scale(1)", opacity: 1, offset: 0.2 },
      { transform: "translateX(-50%) scale(1)", opacity: 1, offset: 0.8 },
      { transform: "translateX(-50%) scale(.95)", opacity: 0 },
    ],
    { duration: CALLOUT_MS, delay, easing: "ease-out", fill: "both" },
  );
  const done = () => el.remove();
  anim.onfinish = done;
  anim.oncancel = done;
  window.setTimeout(done, delay + CALLOUT_MS + 500);
}

export function useDamageFlights(rootRef: RefObject<HTMLElement | null>, game: GameState | null, you: string) {
  const prev = useRef<{ gameId: string; life: Record<Side, number>; chips: Chip[] } | null>(null);

  useLayoutEffect(() => {
    const root = rootRef.current;
    if (!root || !game) return;
    const me = game.playerOne.id === you ? game.playerOne : game.playerTwo;
    const opp = game.playerOne.id === you ? game.playerTwo : game.playerOne;
    const life: Record<Side, number> = { mine: me.life, opp: opp.life };
    const chips: Chip[] = [...root.querySelectorAll<HTMLElement>("[data-face-damage]")]
      .map((el) => ({ side: el.dataset.faceSide as Side, n: Number(el.dataset.faceDamage), rect: el.getBoundingClientRect() }))
      .filter((c) => (c.side === "mine" || c.side === "opp") && c.n > 0 && c.rect.width > 0);

    const before = prev.current;
    if (before && before.gameId === game.gameId && !reducedMotion()) {
      for (const side of ["opp", "mine"] as Side[]) {
        const change = life[side] - before.life[side];
        if (change === 0) continue;
        const to = targetPoint(root, side);
        const hits = change < 0 ? before.chips.filter((c) => c.side === side) : [];
        hits.forEach((chip, i) => fly(root, chip, to, i * STAGGER_MS));
        if (!to.onScreen) {
          const who = side === "mine" ? "You" : opp.name;
          const landed = hits.length ? (hits.length - 1) * STAGGER_MS + FLY_MS * 0.85 : 0;
          callout(root, side, `${who} ${change < 0 ? "−" : "+"}${Math.abs(change)}`, change < 0, landed);
        }
      }
    }
    // Only the last commit's chips: damage lands on the update right after
    // the lanes last showed it, and older chips (damage that was then
    // prevented) mustn't fly at some later, unrelated life change.
    prev.current = { gameId: game.gameId, life, chips };
  });
}
