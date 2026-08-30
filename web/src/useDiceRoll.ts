import { useCallback, useEffect, useRef, useState } from "react";
import { FACE_ORIENTATIONS } from "./dieFaces";
import type { CubeSpin } from "./DieCube";
import type { Die } from "./types";

// The roll: two CSS transitions per die, no physics engine.
//
// 1. FLIGHT - the die is thrown at its target face plus a couple of extra
//    full turns, overshooting on every axis, lifted off the mat and
//    scattered sideways.
// 2. SETTLE - it transitions to the exact resting orientation for that
//    face, back down on the mat, with a slight final scatter. The bounce
//    is the overshoot in the settle curve, not a third stage.
//
// The rolled result is the SERVER's, decided before any of this starts;
// the animation only chooses how the die gets there. Once a spin exists
// for a die, DieCube drives the cube from it rather than from the die's
// face index, so a re-render mid-flight cannot snap the cube to its
// resting pose.
//
// The extra turns are kept after the animation ends, as `offsets`. The
// cube's resting transform is then FACE_ORIENTATIONS[face] + offset -
// exactly where the settle left it - so clearing the transient spin does
// not unwind the die, and a later spin up or down is a true quarter turn
// from wherever it came to rest rather than a 720-degree rewind.

const FLIGHT_MIN_MS = 520;
const FLIGHT_SPREAD_MS = 120;
const STAGGER_MS = 70;
const SETTLE_AT_MS = 560;
const SETTLE_MS = 430;
const DONE_AT_MS = 1080;
const FLIGHT_EASE = "cubic-bezier(.22,.62,.4,.98)";
const SETTLE_EASE = "cubic-bezier(.28,1.5,.42,.96)";

export interface RollTarget {
  dieId: string;
  /** Which face the server says the die landed on. */
  faceIndex: number;
}

/**
 * True when a change to a die is a ROLL rather than a spin. A roll turns
 * the die over - the status changes, or an energy face becomes a
 * different energy face. Changing level while still showing a character
 * face is a spin (rule 2.6.1.6 also spins a double down to its single),
 * and that is a quarter turn the cube's ordinary transition already
 * gives us for free.
 */
export function isRoll(before: Die, after: Die): boolean {
  if (before.status !== after.status) return true;
  if (after.status !== "Energy") return false;
  return before.energyKind !== after.energyKind || before.providedEnergyType !== after.providedEnergyType;
}

export function useDiceRoll() {
  const [spins, setSpins] = useState<Record<string, CubeSpin>>({});
  const [offsets, setOffsets] = useState<Record<string, number>>({});
  const [rolling, setRolling] = useState(false);
  const timers = useRef<number[]>([]);

  const clearTimers = () => {
    timers.current.forEach(clearTimeout);
    timers.current = [];
  };
  useEffect(() => clearTimers, []);

  const launch = useCallback((targets: RollTarget[]) => {
    if (targets.length === 0) return;
    clearTimers();

    // Someone who has asked for less motion still gets the result - the
    // die is simply already on its face, with no tumble and no extra
    // turns to keep track of.
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) {
      setSpins({});
      setOffsets((current) => {
        const next = { ...current };
        for (const target of targets) delete next[target.dieId];
        return next;
      });
      return;
    }

    const flight: Record<string, CubeSpin> = {};
    const settle: Record<string, CubeSpin> = {};
    const turnOffsets: Record<string, number> = {};

    targets.forEach((target, i) => {
      const [restX, restY] = FACE_ORIENTATIONS[target.faceIndex] ?? FACE_ORIENTATIONS[0];
      const offset = -360 * (2 + Math.floor(Math.random() * 3));
      turnOffsets[target.dieId] = offset;
      const delay = i * STAGGER_MS;

      flight[target.dieId] = {
        // Overshoot past the target on both axes, so the settle has
        // somewhere to come back from.
        rx: restX + offset - 140,
        ry: restY + offset + 90,
        rz: (Math.random() - 0.5) * 220,
        tx: (Math.random() - 0.5) * 120,
        ty: -26 - Math.random() * 16,
        durationMs: FLIGHT_MIN_MS + Math.random() * FLIGHT_SPREAD_MS,
        delayMs: delay,
        easing: FLIGHT_EASE,
      };
      settle[target.dieId] = {
        rx: restX + offset,
        ry: restY + offset,
        rz: 0,
        tx: (Math.random() - 0.5) * 34,
        ty: 0,
        durationMs: SETTLE_MS,
        delayMs: 0,
        easing: SETTLE_EASE,
      };
    });

    setSpins((current) => ({ ...current, ...flight }));
    setOffsets((current) => ({ ...current, ...turnOffsets }));
    setRolling(true);

    const maxDelay = (targets.length - 1) * STAGGER_MS;
    const after = (ms: number, fn: () => void) => {
      timers.current.push(setTimeout(fn, ms) as unknown as number);
    };
    after(SETTLE_AT_MS + maxDelay, () => setSpins((current) => ({ ...current, ...settle })));
    after(DONE_AT_MS + maxDelay, () => {
      setRolling(false);
      // Drop the transient transforms but keep the turn offsets, so the
      // dice stay exactly where they landed.
      setSpins((current) => {
        const next = { ...current };
        for (const target of targets) delete next[target.dieId];
        return next;
      });
    });
  }, []);

  return { spins, offsets, rolling, launch };
}
