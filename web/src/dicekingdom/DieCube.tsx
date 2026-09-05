import { ENERGY_ICONS } from "./icons";
import { FACE_ORIENTATIONS, FACE_TRANSFORMS, type CubeFace } from "./dieFaces";

// A die as a real CSS 3D cube rather than a flat badge - ported from
// ../DieCube.tsx verbatim except for `faceIcon`, which resolves an icon
// as one of Dice Kingdom's own SVG components (ClawIcon, ShellIcon, ...)
// instead of v1's `<img src>` GameIcon. See ../DieCube.tsx for the
// geometry commentary (unchanged - it's just 3D placement math).

function faceIcon(face: CubeFace) {
  if (face.kind !== "energy") return null;
  return ENERGY_ICONS[face.icon] ?? null;
}

export interface CubeSpin {
  rx: number;
  ry: number;
  rz: number;
  tx: number;
  ty: number;
  durationMs: number;
  delayMs: number;
  easing: string;
}

const RESTING_EASE = "cubic-bezier(.32,1.42,.46,1)";

export function DieCube(props: {
  faces: CubeFace[];
  /** Which face points at the player. */
  index: number;
  size: number;
  /** True for the local player's dice - only changes the face tint. */
  mine: boolean;
  /** Damage marked on the die, drawn in the face's spare corner. */
  damage?: number;
  /** Mid-roll transform; omitted, the cube sits at rest on `index`. */
  spin?: CubeSpin;
  /** Full turns this die accumulated when it was last rolled, kept so
   *  dropping the spin leaves it exactly where it landed. */
  turnOffset?: number;
}) {
  const { faces, index, size, mine, spin } = props;
  const half = size / 2;
  const hue = mine ? 62 : 250;
  const offset = props.turnOffset ?? 0;
  const resting = FACE_ORIENTATIONS[index] ?? FACE_ORIENTATIONS[0];
  const rx = spin ? spin.rx : resting[0] + offset;
  const ry = spin ? spin.ry : resting[1] + offset;
  const rz = spin ? spin.rz : 0;
  const tx = spin ? spin.tx : 0;
  const ty = spin ? spin.ty : 0;
  const duration = spin ? spin.durationMs : 340;
  const lift = Math.abs(ty);

  return (
    <span
      aria-hidden="true"
      className="die-cube-box"
      style={{ width: size, height: size, perspective: size * 10 }}
    >
      <span
        className="die-cube-shadow"
        style={{
          bottom: -size * 0.14,
          height: size * 0.22,
          background: `radial-gradient(closest-side, oklch(0.08 0.01 155 / ${(0.62 - lift * 0.012).toFixed(2)}), transparent)`,
          transform: `translateX(${tx.toFixed(0)}px) scale(${(1 + lift * 0.02).toFixed(2)})`,
          transition: `transform ${duration}ms ${spin?.easing ?? RESTING_EASE} ${spin?.delayMs ?? 0}ms`,
        }}
      />
      <span
        className="die-cube"
        style={{
          transform: `translate3d(${tx.toFixed(1)}px,${ty.toFixed(1)}px,0) rotateX(${rx.toFixed(1)}deg) rotateY(${ry.toFixed(1)}deg) rotateZ(${rz.toFixed(1)}deg)`,
          transition: `transform ${duration}ms ${spin?.easing ?? RESTING_EASE} ${spin?.delayMs ?? 0}ms`,
        }}
      >
        {faces.map((face, i) => {
          const Icon = faceIcon(face);
          return (
            <span
              key={i}
              className="die-cube-face"
              style={{
                transform: `${FACE_TRANSFORMS[i]} translateZ(${half}px)`,
                borderRadius: Math.max(4, size * 0.15),
                fontSize: size * 0.3,
                background: `linear-gradient(148deg, oklch(0.32 0.02 ${hue}), oklch(0.2 0.02 ${hue}))`,
                borderColor: `oklch(0.5 0.05 ${hue} / 0.7)`,
                boxShadow: `inset 0 0 ${Math.round(size * 0.3)}px oklch(0.1 0.02 250 / 0.8)`,
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
                  {i === index && (props.damage ?? 0) > 0 && (
                    <span className="die-cube-damage">-{props.damage}</span>
                  )}
                </>
              )}
              {face.kind === "energy" && (
                <>
                  {/* Shrunk and moved off the center - direct feedback
                      (2026-09-05): "the energy icons are covering up
                      stuff on the character die faces... maybe the icon
                      needs to be a little smaller." Same top-right slot
                      .die-cube-attack uses on a character face - the two
                      never appear on the same face, so there's no clash
                      reusing the position. */}
                  {Icon && (
                    <span className="die-cube-energy-icon">
                      <Icon size={Math.round(size * 0.3)} />
                    </span>
                  )}
                  {face.amount > 1 && <span className="die-cube-amount">{face.amount}</span>}
                </>
              )}
            </span>
          );
        })}
      </span>
    </span>
  );
}
