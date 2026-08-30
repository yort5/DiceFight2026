import {
  ENERGY_ICONS, GENERIC_ENERGY_ICONS, SIDEKICK_ICON, SPLIT_ENERGY_ICONS, WILD_ENERGY_ICON, type GameIcon,
} from "./gameIcons";
import actionIcon from "./assets/action.png";
import { FACE_ORIENTATIONS, FACE_TRANSFORMS, type CubeFace } from "./dieFaces";

// A die as a real CSS 3D cube rather than a flat badge, so the face
// pointing at the player is a face of an object you could pick up.
//
// The six faces are laid out with `translateZ(size/2)` after a rotation,
// and the cube is rotated to bring face i forward. That is what makes a
// spin up or down free: changing which face is forward is a quarter turn
// of the same element, animated by one CSS transition, with no separate
// keyframes to keep in sync with the level.
//
// Sizes are all derived from `size` so one component covers the 30px
// dice in a gang block and the 50px one in the attack slot.

// The monochrome icons - wild, generic, sidekick, action - are dark ink
// on white and have to flip on these dark faces. The four energy types
// are coloured discs and stay as they are; same split gameIcons.ts
// already draws for the page (see .card-icon-mono).
function faceIcon(face: CubeFace): { icon: GameIcon; mono: boolean } | null {
  if (face.kind === "action") return { icon: { src: actionIcon, label: "Action" }, mono: true };
  if (face.kind !== "energy") return null;
  if (face.icon === "Wild") return { icon: WILD_ENERGY_ICON, mono: true };
  if (face.icon === "Pawn") return { icon: SIDEKICK_ICON, mono: true };
  if (face.icon === "Generic") {
    return { icon: GENERIC_ENERGY_ICONS[String(face.amount)] ?? GENERIC_ENERGY_ICONS["1"], mono: true };
  }
  // A Crossover character's double covers both of its energy types, and
  // prints as the one split symbol rather than two icons.
  if (face.secondIcon) {
    const split = SPLIT_ENERGY_ICONS[`${face.icon.toLowerCase()}/${face.secondIcon.toLowerCase()}`];
    if (split) return { icon: split, mono: false };
  }
  const icon = ENERGY_ICONS[face.icon.toLowerCase()];
  return icon ? { icon, mono: false } : null;
}

export interface CubeSpin {
  /** Cube rotation, in degrees. */
  rx: number;
  ry: number;
  rz: number;
  /** Translation, in px - the lift and scatter of a roll. */
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
  /** A die still on its card shows a face, but no face is really up. */
  unrolled?: boolean;
  /** Damage marked on the die, drawn in the face's spare corner. */
  damage?: number;
  /** Mid-roll transform; omitted, the cube sits at rest on `index`. */
  spin?: CubeSpin;
  /** Full turns this die accumulated when it was last rolled, kept so
   *  that dropping the spin leaves it exactly where it landed - and so a
   *  later spin up or down is a quarter turn from there rather than a
   *  rewind of the whole roll. See useDiceRoll.ts. */
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

  // A cube keeps all six faces in the DOM - the hidden ones are turned
  // away, not removed - so without this the chip's accessible name would
  // read out every face at once. The die's state reaches assistive tech
  // through the chip's own label and title instead.
  return (
    <span
      aria-hidden="true"
      className="die-cube-box"
      style={{ width: size, height: size, perspective: size * 10 }}
    >
      {/* The shadow tracks the cube's lift and scatter on the same
          transition, which is most of what makes a roll read as physical. */}
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
          filter: props.unrolled ? "saturate(0.45) brightness(0.86)" : undefined,
        }}
      >
        {faces.map((face, i) => {
          const icon = faceIcon(face);
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
              {face.kind === "character" && (
                <>
                  {/* Stands in for card art, which we have none of. */}
                  <span className="die-cube-art" />
                  <span className="die-cube-cost">{face.fieldingCost}</span>
                  <span className="die-cube-attack">{face.attack}</span>
                  <span className="die-cube-defense">{face.defense}</span>
                  {i === index && (props.damage ?? 0) > 0 && (
                    <span className="die-cube-damage">-{props.damage}</span>
                  )}
                </>
              )}
              {icon && (
                <img
                  src={icon.icon.src}
                  alt={icon.icon.label}
                  className={`die-cube-icon${icon.mono ? " die-cube-icon-mono" : ""}`}
                  style={{ width: Math.round(size * 0.52) }}
                />
              )}
              {face.kind === "energy" && face.amount > 1 && face.icon !== "Generic" && (
                <span className="die-cube-amount">{face.amount}</span>
              )}
            </span>
          );
        })}
      </span>
    </span>
  );
}
