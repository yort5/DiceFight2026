// 3D dice (2026-10-28): the switch, and the on-demand load of the 3D code
// (dice3d/roll3d.ts - three.js and the physics, kept out of the first page
// load). On by default; a "3D dice" switch in the ☰ menu turns it off per
// device, like Sound. Off anyway under prefers-reduced-motion or without
// WebGL, where rolls keep the CSS tumble (useDiceRoll.ts).
import { useSyncExternalStore } from "react";

const KEY = "dk-dice3d";
const listeners = new Set<() => void>();

function readOn(): boolean {
  try {
    return localStorage.getItem(KEY) !== "off";
  } catch {
    return true;
  }
}
let on = readOn();

export function setDice3dOn(next: boolean) {
  on = next;
  try {
    localStorage.setItem(KEY, next ? "on" : "off");
  } catch {
    // Still applies for this visit.
  }
  listeners.forEach((l) => l());
  if (next) preloadDice3d();
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useDice3dOn(): boolean {
  return useSyncExternalStore(subscribe, () => on, () => false);
}

let webgl: boolean | null = null;
function hasWebgl(): boolean {
  if (webgl === null) {
    try {
      const c = document.createElement("canvas");
      webgl = !!(c.getContext("webgl2") ?? c.getContext("webgl"));
    } catch {
      webgl = false;
    }
  }
  return webgl;
}

function reducedMotion(): boolean {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

type Roll3d = typeof import("./dice3d/roll3d");
let loaded: Roll3d | null = null;
let loading: Promise<Roll3d | null> | null = null;

/** Start fetching the 3D code (a board calls this on mount). */
export function preloadDice3d() {
  if (loading || !on || reducedMotion() || !hasWebgl()) return;
  loading = import("./dice3d/roll3d").then(
    (m) => (loaded = m),
    () => null,
  );
}

/** The 3D roll, if it's switched on and ready right now; otherwise null. */
export function dice3d(): Roll3d | null {
  return on && !reducedMotion() && hasWebgl() ? loaded : null;
}

/** Dice being thrown in 3D: the zone-move flights (dieFlights.ts) leave them alone. */
export const thrownDice = new Set<string>();
