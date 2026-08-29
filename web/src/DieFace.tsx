import type { CharacterFace } from "./types";

// One level of a character die, laid out the way the die itself is
// printed: fielding cost top left, attack top right, defense bottom
// right, and the burst mark (if any) bottom left.
//
//     +-------+
//     | 1   4 |     fielding cost 1, attack 4,
//     | *   3 |     defense 3, single burst
//     +-------+
//
// This is how the old Teambuilder draws a die face too (its `gendice1`
// builds exactly this 2x2 from the four characters of a level's stat
// line), and it is what PlayerBoard.tsx's die chips already use in the
// game view - there the lower-left corner holds damage taken instead.
// The layout is worth copying because it is the one people already read
// off the physical dice: no order to remember and no separator to
// misparse, which is what the previous "1*4*3" text needed a bullet to
// avoid (a stat can reach double figures, so run-together digits are
// genuinely ambiguous).

export const DIE_FACE_TITLE =
  "Fielding cost (top left), attack (top right), defense (bottom right), " +
  "burst (bottom left). Sorts by attack.";

// The burst mark, drawn rather than imported: the old Teambuilder's
// burst.png is a black glyph on an opaque white background, which would
// show as a white block in dark mode, and at the 8px this corner allows
// a shape built from currentColor stays crisp and takes the theme for
// free. Same reasoning as DieIcon.tsx's SVGs.
function Burst({ stars }: { stars: number }) {
  // A card prints either one burst or a double burst; the sheet records
  // them as "*" and "**", and we draw one or two marks to match.
  return (
    <>
      {Array.from({ length: stars }, (_, i) => (
        <svg key={i} className="card-die-face-burst-icon" viewBox="0 0 12 12" aria-hidden="true">
          <path
            d="M6 0.5 L7.1 4.1 L10.4 2.3 L8.6 5.6 L12 6 L8.6 6.4 L10.4 9.7 L7.1 7.9
               L6 11.5 L4.9 7.9 L1.6 9.7 L3.4 6.4 L0 6 L3.4 5.6 L1.6 2.3 L4.9 4.1 Z"
            fill="currentColor"
          />
        </svg>
      ))}
    </>
  );
}

export function DieFace({ face }: { face: CharacterFace | undefined }) {
  if (!face) return <span className="hint">-</span>;
  return (
    <span className="card-die-face" title={DIE_FACE_TITLE}>
      <span className="card-die-face-cost">{face.fieldingCost}</span>
      <span className="card-die-face-attack">{face.attack}</span>
      <span className="card-die-face-bursts" aria-label={face.burstStars ? `${face.burstStars} burst` : undefined}>
        {face.burstStars ? <Burst stars={face.burstStars} /> : null}
      </span>
      <span className="card-die-face-defense">{face.defense}</span>
    </span>
  );
}
