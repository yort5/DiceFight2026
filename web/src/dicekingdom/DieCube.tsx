import type { CSSProperties } from "react";
import { EnergyBadge } from "./icons";
import { FACE_TRANSFORMS, type CubeFace } from "./dieFaces";

// A die as a real CSS 3D cube. Motion refresh (2026-09-16) ported from
// design_handoff_dice_kingdom_mobile/ANIMATIONS.md - a spec Claude Design
// produced specifically for this component's roll/re-roll tumble.
// Structure/geometry otherwise unchanged from the original (ported from
// ../DieCube.tsx verbatim, before this pass): DieCube.tsx's own history.
//
// The one structural change the spec drove: rotation used to encode
// VALUE (the cube rested at a per-`index` orientation so the right
// physical face pointed at the camera - see the removed FACE_ORIENTATIONS,
// dieFaces.ts's own remarks). Now rotation encodes MOTION only - the cube
// always rests flat, and slot 0 (`.front`, below) always shows the die's
// current value directly as a content prop, never a rotation target. See
// dieFaces.ts for why that's safe (short version: slot 0 is rotated away
// from the camera for nearly all of a tumble anyway, so its content can
// update the instant new data arrives without ever being seen out of
// sync with the spin).
//
// No more per-face energy icon/amount (2026-09-09) - a hybrid face's
// energy has no home in the CubeFace model at all (only stat-face
// fields), so that indicator always had to come from the live die state
// anyway. `energyCorner` draws it once, as a sibling of the rotating
// cube INSIDE die-cube-box (not a separate wrapper positioned around
// this whole component) - box-relative percentages then land exactly on
// the die's own visible edges, not stretched by the box's own trailing
// margin the way an external wrapper's would be.

// The four tumble tracks (ANIMATIONS.md §3) - keyframes, not transitions,
// on purpose: a transition from rest to rest+360n resolves to the same
// matrix and interpolates nothing, so the die would visibly not move.
// Every track starts and ends at a whole multiple of 360° (see dieFaces.ts),
// so a dropped frame or a competing state update can never strand a die
// mid-air - there is no cleanup pass a track ever needs.
export type TumbleTrack = 0 | 1 | 2 | 3;
const TUMBLE_KEYFRAMES = ["dkTumbleA", "dkTumbleB", "dkTumbleC", "dkTumbleD"] as const;
const TUMBLE_EASE = "cubic-bezier(.26,.62,.32,1)";
const SHADOW_EASE = "cubic-bezier(.3,.08,.5,1)";
// A short, hop-free single spin for prefers-reduced-motion - ANIMATIONS.md
// §7: "collapses the tumble to a single 320ms track with no stagger...
// drop the hop amplitude to 0." A fixed literal track (not the real
// tracks with their translateY zeroed via a CSS variable) since none of
// the real tracks are parameterized that way - simpler to give reduced
// motion its own minimal keyframe than to rewrite the tuned ones.
const REDUCED_TUMBLE_KEYFRAME = "dkTumbleReduced";

// A quick single-axis turn for the "spin to a new face" case (partial
// energy spend spinning a double face down to single - see useDiceRoll.
// ts's spinTo) - ANIMATIONS.md doesn't cover this one (it's scoped to
// roll/re-roll and zone moves), so this keeps the pre-existing "one clean
// turn, no tumble" distinction from a full roll, adapted to the new
// content-is-a-prop model: always a full 360° turn (never a bare 180),
// so it also always lands back at a net-identical rotation - the same
// "terminal, no cleanup needed" property the tumble tracks have, just via
// a transition instead of keyframes (safe here specifically because the
// accumulated angle - CubeSpin's `toDeg`/DieCube's `turnOffset` - is a
// monotonically increasing real number, never the same value twice).
const SPIN_EASE = "cubic-bezier(.32,1.42,.46,1)";

export type CubeSpin =
  | { kind: "tumble"; track: TumbleTrack; durationMs: number; delayMs: number; reduced?: boolean; generation: number }
  | { kind: "flip"; toDeg: number; durationMs: number };

export function DieCube(props: {
  faces: CubeFace[];
  /** Which face is the die's current value. */
  index: number;
  size: number;
  /** True for the local player's dice - only changes the face tint. */
  mine: boolean;
  /** Damage marked on the die, drawn in the face's spare corner. */
  damage?: number;
  /** Transient roll/flip motion; omitted, the cube sits at rest. */
  spin?: CubeSpin;
  /** Accumulated rotateY (a whole multiple of 360, so always visually
   *  flat) left over from the die's last "flip" - kept so resting there
   *  doesn't snap. Unused during a tumble (see dieFaces.ts's remarks). */
  turnOffset?: number;
  /** The die's own live energy, read straight off the DTO rather than
   *  the (rotating, face-specific) cube model - see the file header. */
  energyCorner?: { type: string; amount: number };
}) {
  const { faces, index, size, mine, spin } = props;
  const half = size / 2;
  const hue = mine ? 62 : 250;

  let cubeStyle: CSSProperties;
  let cubeKey: string | number;
  // The shadow's hop-synced squash/stretch only makes sense alongside an
  // actual hop (ANIMATIONS.md §3: "shrinks and fades as the die lifts, so
  // the arc reads as height rather than scale") - flip has no vertical
  // motion at all, and reduced motion drops the hop amplitude to zero
  // (§7), so both leave the shadow static (computed alongside cubeStyle,
  // in the same branches, so TS narrows `spin` without a cast).
  let shadowStyle: CSSProperties = {};
  if (spin?.kind === "tumble") {
    cubeKey = spin.generation;
    cubeStyle = {
      animationName: spin.reduced ? REDUCED_TUMBLE_KEYFRAME : TUMBLE_KEYFRAMES[spin.track],
      animationDuration: `${spin.durationMs}ms`,
      animationDelay: `${spin.delayMs}ms`,
      animationTimingFunction: TUMBLE_EASE,
      animationFillMode: "both",
    };
    if (!spin.reduced) {
      shadowStyle = {
        animationName: spin.track % 2 === 0 ? "dkShA" : "dkShB",
        animationDuration: `${spin.durationMs}ms`,
        animationDelay: `${spin.delayMs}ms`,
        animationTimingFunction: SHADOW_EASE,
        animationFillMode: "both",
      };
    }
  } else if (spin?.kind === "flip") {
    cubeKey = "flip";
    cubeStyle = {
      transform: `rotateY(${spin.toDeg}deg)`,
      transition: `transform ${spin.durationMs}ms ${SPIN_EASE}`,
    };
  } else {
    cubeKey = "rest";
    cubeStyle = { transform: `rotateY(${props.turnOffset ?? 0}deg)` };
  }

  return (
    <span
      aria-hidden="true"
      className="die-cube-box"
      style={{ width: size, height: size, perspective: size * 10 }}
    >
      <span
        key={`shadow-${cubeKey}`}
        className="die-cube-shadow"
        style={{
          bottom: -size * 0.14,
          height: size * 0.22,
          background: "radial-gradient(closest-side, oklch(0.08 0.01 155 / 0.62), transparent)",
          ...shadowStyle,
        }}
      />
      <span key={`cube-${cubeKey}`} className="die-cube" style={cubeStyle}>
        {faces.map((face, i) => {
          const isFront = i === index;
          return (
            <span
              key={i}
              className={`die-cube-face${isFront ? " front" : " hidden"}`}
              style={{
                transform: `${FACE_TRANSFORMS[i]} translateZ(${half}px)`,
                borderRadius: Math.max(4, size * 0.15),
                fontSize: size * 0.3,
                ...(isFront
                  ? {
                      background: `linear-gradient(158deg, oklch(0.38 0.03 ${hue}), oklch(0.28 0.03 ${hue}) 58%, oklch(0.22 0.02 ${hue}))`,
                      borderColor: `oklch(0.5 0.05 ${hue} / 0.7)`,
                      boxShadow: `inset 0 1.5px 0 rgba(255,255,255,.18), inset 0 -3px 5px rgba(0,0,0,.45)`,
                    }
                  : {
                      // Muted "other faces" material, glimpsed only mid-
                      // tumble - ANIMATIONS.md §2's literal values (not
                      // hue-shifted by `mine`; barely-seen filler doesn't
                      // carry the mine/opponent cue the way the front
                      // face's tint does).
                      background: "linear-gradient(150deg,#3a4258,#232936)",
                      borderColor: "rgba(92,100,132,.8)",
                      boxShadow: "inset 0 0 16px rgba(10,12,20,.78)",
                      opacity: 0.72,
                    }),
              }}
            >
              {/* The card's own identity, centered - direct feedback
                  (2026-09-05): "I don't really know which dice are
                  Tardigrades and which one is a Pangolin... Character
                  stat faces should definitely have that character's
                  symbol in the center." Same spot on every face kind
                  (replaces the old generic diagonal-stripe texture on
                  character faces, which carried no identity at all) -
                  the corner-positioned stats/energy-type icon sit on
                  top of it, never over it, so it never competes with
                  the numbers that actually have to be read precisely. */}
              {face.avatar && <face.avatar size={Math.round(size * 0.48)} />}
              {face.kind === "character" && (
                <>
                  {/* Always shown, including 0 - direct feedback
                      (2026-09-07): a Tardigrade's free faces should still
                      print "0" rather than leave the corner blank, so a
                      free die reads as "costs 0" and not "cost unknown". */}
                  <span className="die-cube-cost">{face.fieldingCost}</span>
                  <span className="die-cube-attack">{face.attack}</span>
                  <span className="die-cube-defense">{face.defense}</span>
                  {isFront && (props.damage ?? 0) > 0 && (
                    <span className="die-cube-damage">-{props.damage}</span>
                  )}
                </>
              )}
            </span>
          );
        })}
      </span>
      {props.energyCorner && (
        <span className="pip-stack on-die">
          {Array.from({ length: Math.max(1, props.energyCorner.amount) }, (_, i) => (
            <EnergyBadge key={i} type={props.energyCorner!.type} size={Math.round(size * 0.34)} />
          ))}
        </span>
      )}
    </span>
  );
}
