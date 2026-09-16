import { useCallback, useEffect, useRef, useState } from "react";
import type { CubeSpin, TumbleTrack } from "./DieCube";

// The roll: real CSS keyframe tumbles, not transitions between computed
// poses. Motion refresh (2026-09-16), ported from design_handoff_dice_
// kingdom_mobile/ANIMATIONS.md - a spec Claude Design produced for this
// exact hook's roll/re-roll animation. Replaces the previous flight/
// settle two-transition fake (a real physics-free hack that worked but
// never looked like a genuine tumble) with the spec's four named CSS
// keyframe tracks (dicekingdom.css's dkTumbleA-D) - DieCube.tsx just
// picks one per die and lets the browser run it; this hook's whole job
// is choosing which track, how long, and how staggered, then clearing
// the spin once it's done (see DieCube.tsx/dieFaces.ts for why every
// track can be cleared without a "let it finish landing" dance: they all
// start and end at a whole multiple of 360°, so a die that's mid-flight
// when its `spin` entry disappears was already back at a flat, correct-
// looking rest pose at that exact moment).

const STAGGER_MS = 70;
const TUMBLE_MS = 900;
const TUMBLE_MS_REDUCED = 320;
const TUMBLE_TRACK_COUNT = 4;

// Direct feedback (2026-09-05): a die spinning down to a lower energy
// face after a partial spend "shouldn't look the same as an actual
// randomized roll" - no toss-up, no multi-360 tumble, no random tilt,
// just a single direct turn from its current face to the new one.
// ANIMATIONS.md doesn't cover this case (scoped to roll/re-roll and zone
// moves) - kept as its own, simpler mechanism; see DieCube.tsx's own
// remarks on why a transition (not a keyframe track) is safe here.
const SPIN_MS = 380;

function reducedMotionPreferred(): boolean {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

export interface RollTarget {
  dieId: string;
  /** Kept for caller compatibility (../DiceKingdomPage.tsx/DiceKingdomMobilePage.tsx
   *  both already compute this from facesFor() to detect a changed face) -
   *  unused by this hook itself now. The tumble tracks spin and land flat
   *  regardless of which face is landed on; see dieFaces.ts's remarks on
   *  why rotation no longer needs to target a specific face at all. */
  faceIndex: number;
}

export function useDiceRoll() {
  const [spins, setSpins] = useState<Record<string, CubeSpin>>({});
  const [offsets, setOffsets] = useState<Record<string, number>>({});
  // Mirrors `offsets` synchronously so spinTo can read the CURRENT
  // accumulated angle without depending on React's functional-updater
  // timing (spinTo's own closure is stale - useCallback(..., []) - so it
  // can't just read the `offsets` state variable directly either).
  const offsetsRef = useRef<Record<string, number>>({});
  const [rolling, setRolling] = useState(false);
  const timers = useRef<number[]>([]);
  // A monotonic counter, not Date.now() - guarantees every generation is
  // unique even if two rolls somehow land in the same millisecond, which
  // wall-clock time can't promise.
  const generationRef = useRef(0);

  const clearTimers = () => {
    timers.current.forEach(clearTimeout);
    timers.current = [];
  };
  useEffect(() => clearTimers, []);

  const launch = useCallback((targets: RollTarget[]) => {
    if (targets.length === 0) return;
    clearTimers();

    const reduced = reducedMotionPreferred();
    const duration = reduced ? TUMBLE_MS_REDUCED : TUMBLE_MS;

    const next: Record<string, CubeSpin> = {};
    targets.forEach((target, i) => {
      generationRef.current += 1;
      next[target.dieId] = {
        kind: "tumble",
        track: Math.floor(Math.random() * TUMBLE_TRACK_COUNT) as TumbleTrack,
        durationMs: duration,
        delayMs: reduced ? 0 : i * STAGGER_MS,
        reduced,
        // Forces DieCube's .die-cube span to remount (React `key`) so
        // the CSS animation restarts cleanly even on a die that's rolled
        // again before its previous tumble finished - ANIMATIONS.md §3's
        // own retriggering note.
        generation: generationRef.current,
      };
    });

    setSpins((current) => ({ ...current, ...next }));
    setRolling(true);

    const maxDelay = reduced ? 0 : (targets.length - 1) * STAGGER_MS;
    const after = (ms: number, fn: () => void) => {
      timers.current.push(setTimeout(fn, ms) as unknown as number);
    };
    after(duration + maxDelay, () => {
      setRolling(false);
      setSpins((current) => {
        const cleared = { ...current };
        for (const target of targets) delete cleared[target.dieId];
        return cleared;
      });
    });
  }, []);

  const spinTo = useCallback((targets: RollTarget[]) => {
    if (targets.length === 0) return;
    clearTimers();

    // Unlike launch(), this doesn't fall back to a shorter real spin in
    // reduced-motion mode - ANIMATIONS.md doesn't specify one for this
    // (spec-uncovered) case, and the twist is subtle enough already that
    // skipping it entirely (same as before this pass) is a reasonable
    // read of "reduce motion".
    if (reducedMotionPreferred()) return;

    const nextSpins: Record<string, CubeSpin> = {};
    const patch: Record<string, number> = {};
    for (const target of targets) {
      // Always a full +360 turn (never a bare 180) so it also always
      // lands back at a net-identical rotation, same "terminal" property
      // the tumble tracks have - see DieCube.tsx's own remarks.
      const toDeg = (offsetsRef.current[target.dieId] ?? 0) + 360;
      patch[target.dieId] = toDeg;
      nextSpins[target.dieId] = { kind: "flip", toDeg, durationMs: SPIN_MS };
    }
    offsetsRef.current = { ...offsetsRef.current, ...patch };
    setOffsets(offsetsRef.current);
    setSpins((current) => ({ ...current, ...nextSpins }));

    const after = (ms: number, fn: () => void) => {
      timers.current.push(setTimeout(fn, ms) as unknown as number);
    };
    after(SPIN_MS, () => {
      setSpins((current) => {
        const cleared = { ...current };
        for (const target of targets) delete cleared[target.dieId];
        return cleared;
      });
    });
  }, []);

  return { spins, offsets, rolling, launch, spinTo };
}
