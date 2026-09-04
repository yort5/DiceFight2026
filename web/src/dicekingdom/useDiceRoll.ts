import { useCallback, useEffect, useRef, useState } from "react";
import { FACE_ORIENTATIONS } from "./dieFaces";
import type { CubeSpin } from "./DieCube";

// The roll: two CSS transitions per die, no physics engine. Ported
// verbatim from ../useDiceRoll.ts (see there for the flight/settle
// commentary - nothing here is Dice-Kingdom-specific, it's pure
// animation math against the same FACE_ORIENTATIONS geometry DieCube.tsx
// already shares with v1's). The only real difference from v1: this
// module has no `isRoll`/`Die` helper of its own - Dice Kingdom's `rolled()`
// in DiceKingdomPage.tsx already answers "did this die just show a face"
// for v2's Die shape, so callers pass RollTargets straight from that.

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
      setSpins((current) => {
        const next = { ...current };
        for (const target of targets) delete next[target.dieId];
        return next;
      });
    });
  }, []);

  return { spins, offsets, rolling, launch };
}
