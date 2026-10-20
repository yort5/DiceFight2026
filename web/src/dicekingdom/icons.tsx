import { useId, type ReactElement, type ReactNode } from "react";
import wolfChampionArt from "./assets/wolf-champion.jpg";
import tardigradeSketch from "./assets/tardigrade-sketch.jpg";
import barnSwallowDuck from "./assets/barn-swallow-duck.jpg";

// Hand-drawn icon set for Dice Kingdom - the same SVG concepts already
// designed and reviewed as Claude artifacts this session (the energy-pip
// icon sheet and the 45-animal avatar sheet), ported into the real app.
// Every icon is a single <path>-based glyph using currentColor, so a
// wrapping element's `color` sets the badge's hue - see dicekingdom.css's
// `.pip`/`.avatar` classes for the accent tokens.

type IconProps = { size?: number };

function Svg({ size = 24, children }: IconProps & { children: ReactNode }) {
  return (
    <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true">
      {children}
    </svg>
  );
}

// ---- Energy glyphs (Claw / Shell / Wing / Eye / Wild) ----
// currentColor for the main shape, a fixed warm cutout tone for the inner
// detail (seam/pupil) - matches the energy-pip-icons artifact exactly.
const CUTOUT = "#241B12";

export function ClawIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g transform="translate(32,33) rotate(35)" fill="currentColor">
        <path d="M-17,-19 C-14,-13 -14,13 -17,19 C-20,13 -20,-13 -17,-19 Z" />
        <path d="M0,-22 C3,-15 3,15 0,22 C-3,15 -3,-15 0,-22 Z" />
        <path d="M17,-19 C20,-13 20,13 17,19 C14,13 14,-13 17,-19 Z" />
      </g>
    </Svg>
  );
}

export function ShellIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M8,42 C7,24 17,9 32,9 C47,9 57,24 56,42 C53,48 48,40 44,47 C40,40 36,48 32,41 C28,48 24,40 20,47 C16,40 11,48 8,42 Z"
      />
      <path fill="none" stroke={CUTOUT} strokeWidth={3} strokeLinecap="round" d="M32,14 C30,22 30,32 32,39" />
    </Svg>
  );
}

export function WingIcon(props: IconProps) {
  return (
    <Svg {...props}>
      {/* Scaled to 75% about center (2026-09-10) - direct feedback: "the
          circle border for Wing energy" looked "a little off" (only
          Wing, not Claw). The un-scaled path's own corners reached
          ~34 units from center, past the ~32-unit inscribed circle
          every other energy glyph stays within (Claw/Shell/Eye all
          land around 25-26) - just enough for the outline filter's
          dilate to poke a sliver past the badge's circular edge. */}
      <g transform="translate(32,32) scale(0.75) translate(-32,-32)">
        <path
          fill="currentColor"
          d="M8,54 C10,40 16,24 30,14 C38,9 46,7 56,8 C50,12 45,15 41,21 C46,20 51,20 55,23 C48,26 42,28 38,33 C44,33 49,35 52,38 C44,41 37,42 33,47 C29,51 23,53 18,55 C14,56 10,56 8,54 Z"
        />
      </g>
    </Svg>
  );
}

export function EyeIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M4,32 Q32,6 60,32 Q32,58 4,32 Z" />
      <ellipse cx={32} cy={32} rx={4.4} ry={15} fill={CUTOUT} />
      <circle cx={27} cy={21} r={3} fill="currentColor" opacity={0.55} />
    </Svg>
  );
}

// A Tardigrade's two Wild faces provide Wild energy - any of
// the four types. No printed animal for "any", so this is a plain glyph
// rather than a fifth creature, same role as v1's WILD_ENERGY_ICON "?".
export function WildIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <text x={32} y={44} fontSize={34} fontWeight={700} textAnchor="middle" fill="currentColor">
        ?
      </text>
    </Svg>
  );
}

export const ENERGY_ICONS: Record<string, (p: IconProps) => ReactElement> = {
  Claw: ClawIcon,
  Shell: ShellIcon,
  Wing: WingIcon,
  Eye: EyeIcon,
  Wild: WildIcon,
};

// ---- Small UI glyphs (How to Play / Settings) - plain chrome, not game
// content, so these stay simple rather than earning a full hand-drawn
// treatment like the energy/animal icons above. ----
export function HelpIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx={32} cy={32} r={28} fill="none" stroke="currentColor" strokeWidth={5} />
      <text x={32} y={44} fontSize={32} fontWeight={700} textAnchor="middle" fill="currentColor">
        ?
      </text>
    </Svg>
  );
}

// A "sliders" glyph, not a literal gear - direct feedback asked for "a
// gear for settings," but a hand-drawn gear tooth shape is fussy at
// this size; this reads as "settings" just as clearly and is far
// simpler to keep crisp small.
export function SettingsIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g stroke="currentColor" strokeWidth={5} strokeLinecap="round">
        <line x1={10} y1={18} x2={54} y2={18} />
        <line x1={10} y1={32} x2={54} y2={32} />
        <line x1={10} y1={46} x2={54} y2={46} />
      </g>
      <circle cx={22} cy={18} r={5.5} fill="currentColor" />
      <circle cx={42} cy={32} r={5.5} fill="currentColor" />
      <circle cx={26} cy={46} r={5.5} fill="currentColor" />
    </Svg>
  );
}

// "Variant A" from the corner-badge mockup (2026-09-08, compared
// alongside a neutral-circle/colored-symbol alternative): the energy
// type's own color as a circle's FILL, its icon in white/cream on top -
// direct feedback picked this one specifically because it "reads faster
// and stays legible even shrunk small," unlike a bare colored icon with
// no backing shape. One shared badge for every place that shows an
// energy type - a die's own rolled amount, a printed cost, a popover
// detail - each just asking for whatever size that spot needs, so the
// look stays the same everywhere instead of drifting per call site.
export function EnergyBadge({ type, size = 16 }: { type: string; size?: number }) {
  const Icon = ENERGY_ICONS[type];
  if (!Icon) return <span style={{ fontSize: size * 0.6 }}>{type}</span>;
  const cssVar = type === "Wild" ? "var(--wild)" : `var(--${type.toLowerCase()})`;
  return (
    <span className="energy-badge" style={{ width: size, height: size, background: cssVar }}>
      <Icon size={Math.round(size * 0.62)} />
    </span>
  );
}

// ---- Champion avatars (no energy tie-in - see v3/DESIGN_NOTES.md on why
// avatars are deliberately orthogonal to energy color) ----

// The first real piece of fan art in the set (2026-09-06, drawn by the
// user's kid) - a raster image rather than a currentColor SVG glyph like
// every other icon here, so it can't pick up an accent tint the way
// those do. Same IconProps signature as the rest (just `size`) so it
// drops into CHAMPION_ICONS/callers without any special-casing. More of
// these are expected to replace the placeholder SVG glyphs over time.
export function WolfIcon({ size = 24 }: IconProps) {
  return (
    <img
      src={wolfChampionArt}
      alt="Wolf"
      width={size}
      height={size}
      style={{ objectFit: "contain", borderRadius: "20%", background: "#fff" }}
    />
  );
}

// A plain currentColor glyph in the same minimal style as the other
// three Champions - kept alongside WolfIcon (the real photo) for
// whatever small/tinted context a flat raster image doesn't suit (the
// photo can't pick up an accent color the way this can, and doesn't
// shrink to a tiny badge as cleanly as a few paths do).
export function WolfGlyphIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M14,14 L26,24 L32,18 L38,24 L50,14 L46,32 L52,40 L40,42 L32,56 L24,42 L12,40 L18,32 Z"
      />
    </Svg>
  );
}

export function ArmadilloIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        d="M10,48 C10,32 20,20 32,20 C44,20 54,32 54,48 Z"
        fill="none"
        stroke="currentColor"
        strokeWidth={4}
      />
      <line x1={10} y1={48} x2={54} y2={48} stroke="currentColor" strokeWidth={4} />
    </Svg>
  );
}

export function GoldenEagleIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M14,26 C22,20 34,20 40,26 C34,26 26,30 20,38 C16,34 14,30 14,26 Z" />
    </Svg>
  );
}

export function GreatHornedOwlIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <polygon points="18,26 24,10 30,26" fill="currentColor" />
      <polygon points="34,26 40,10 46,26" fill="currentColor" />
      <path d="M20,28 C20,20 44,20 44,28 C44,36 20,36 20,28 Z" fill="currentColor" />
    </Svg>
  );
}

export const CHAMPION_ICONS: Record<string, (p: IconProps) => ReactElement> = {
  Wolf: WolfIcon,
  Armadillo: ArmadilloIcon,
  GoldenEagle: GoldenEagleIcon,
  GreatHornedOwl: GreatHornedOwlIcon,
};

// A generic glyph for the basic pool creature - direct feedback
// (2026-09-05): "I don't really know which dice are Tardigrades and
// which one is a Pangolin" - every die's cube face now shows SOME
// identity glyph regardless of which face it's resting on (see
// dieFaces.ts's `avatar` field), and a Tardigrade has no CardId/card
// avatar of its own to use for that. Not meant to be a scientifically
// accurate tardigrade, just a simple, at-a-glance "this is the basic
// creature, not someone's Character" silhouette in the same plain-path
// style as the rest of this file.
// Side profile, not the original front-on blob-plus-four-dots - direct
// feedback (2026-09-08): "I think that's more recognizable" at the tiny
// sizes this actually renders at. A rounded snout at the front, a
// plump tapering body, four stubby legs trailing along the bottom -
// the classic "water bear" silhouette, simplified down to what still
// reads at 15-20px.
export function TardigradeIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M14,30 C14,20 22,14 34,14 C46,14 54,21 54,30 C54,39 46,44 34,44 C20,44 14,40 14,30 Z"
      />
      <circle cx={13} cy={28} r={6} fill="currentColor" />
      <g fill="currentColor">
        <rect x={18} y={42} width={5} height={10} rx={2.5} />
        <rect x={28} y={43} width={5} height={10} rx={2.5} />
        <rect x={38} y={43} width={5} height={10} rx={2.5} />
        <rect x={47} y={41} width={5} height={10} rx={2.5} />
      </g>
    </Svg>
  );
}

// A real sketch, kept alongside the vector TardigradeIcon rather than
// replacing it - direct feedback pattern (2026-09-08): the vector glyph
// is what actually renders at die-cube sizes (15-20px, where a scanned
// pencil drawing would just look like noise), but a bigger context - a
// Tardigrade's own info popover (DieTile's own "click for full
// information", added the same day) - has the room for the real thing,
// same "bigger image" rule the user gave for a Champion portrait.
export function TardigradePhotoIcon({ size = 24 }: IconProps) {
  return (
    <img
      src={tardigradeSketch}
      alt="Tardigrade"
      width={size}
      height={size}
      style={{ objectFit: "contain", borderRadius: "20%", background: "#fff" }}
    />
  );
}

// ---- Character avatars (the 8-card v3 pool) ----

const CREAM = "#FFF8EC";

function HoneyBadgerIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <polygon points="24,17 32,21.5 32,30.5 24,35 16,30.5 16,21.5" />
        <polygon points="40,17 48,21.5 48,30.5 40,35 32,30.5 32,21.5" />
        <polygon points="32,32 40,36.5 40,45.5 32,50 24,45.5 24,36.5" />
      </g>
    </Svg>
  );
}

function WolverineIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <polygon points="12,50 32,14 52,50" fill="currentColor" />
      <polygon points="26,26 32,14 38,26 34,23 32,25 30,23" fill={CREAM} />
    </Svg>
  );
}

function HippopotamusIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M10,26 C10,20 54,20 54,26 C54,40 10,40 10,26 Z" fill="currentColor" />
      <polygon points="16,26 21,26 18,34" fill={CREAM} />
      <polygon points="48,26 43,26 46,34" fill={CREAM} />
    </Svg>
  );
}

function MuskOxIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx={32} cy={32} r={7} fill="none" stroke="currentColor" strokeWidth={2.4} />
      <g stroke="currentColor" strokeWidth={3} strokeLinecap="round">
        <path d="M32,18 L32,10" />
        <path d="M32,46 L32,54" />
        <path d="M18,32 L10,32" />
        <path d="M46,32 L54,32" />
        <path d="M22,22 L16,16" />
        <path d="M42,22 L48,16" />
        <path d="M22,42 L16,48" />
        <path d="M42,42 L48,48" />
      </g>
    </Svg>
  );
}

function OspreyIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M18,30 C22,24 32,22 40,26 C34,28 26,30 18,30 Z" fill="currentColor" />
      <path d="M18,30 L12,26 L13,33 Z" fill="currentColor" />
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={3.4}
        strokeLinecap="round"
        d="M22,30 C22,38 26,42 22,48 M30,29 C31,37 35,41 32,48"
      />
    </Svg>
  );
}

// A real drawing, not the original nest-glyph - direct feedback
// (2026-09-08): the user's own example of what a house-drawn animal
// picture could replace ("if I upload a duck, you could swap out the
// barn swallow"), then an actual duck drawing arrived a few messages
// later. Same img-based pattern as WolfIcon.
function BarnSwallowIcon({ size = 24 }: IconProps) {
  return (
    <img
      src={barnSwallowDuck}
      alt="Barn Swallow"
      width={size}
      height={size}
      style={{ objectFit: "contain", borderRadius: "20%", background: "#fff" }}
    />
  );
}

function BarnOwlIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M32,48 C14,34 14,18 32,20 C50,18 50,34 32,48 Z" fill="currentColor" />
    </Svg>
  );
}

function HyenaIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <polygon points="14,32 40,26 40,38" fill="currentColor" />
      <g fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round">
        <path d="M44,26 C48,24 50,20 50,16" />
        <path d="M46,32 C50,31 53,28 54,24" />
        <path d="M44,38 C48,39 51,42 52,46" />
      </g>
    </Svg>
  );
}

// The remaining 24 of the 32-card roster (2026-09-05) - ported from the
// "Forty-five personal marks" avatar-sheet artifact (the same one the
// first 8 above already came from), picking out the ones matching this
// roster's actual names; the sheet's other marks (Rhino, Wolf, Snow
// Leopard, etc.) were earlier brainstorm alternates for animals that
// aren't in the final 32 and are left unported. Box Turtle isn't on
// that sheet at all - drawn fresh below, in the same plain-path style,
// since Snapping Turtle already claimed the sheet's one turtle concept
// (a snapped twig, for its aggressive bite).

function GrizzlyBearIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12,26 C16,18 28,15 38,20 C30,23 20,26 12,26 Z" fill="currentColor" />
      <path d="M12,26 L5,20 L7,28 Z" fill="currentColor" />
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={4}
        strokeLinecap="round"
        strokeLinejoin="round"
        d="M12,42 L19,35 L26,42 L33,35 L40,42 L47,35 L54,42"
      />
    </Svg>
  );
}

function OrcaIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M18,20 C30,16 46,22 48,34 C42,40 30,42 22,36 C26,34 28,28 24,24 C22,26 20,24 18,20 Z"
      />
    </Svg>
  );
}

function PeregrineFalconIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M14,50 C22,34 34,20 50,12 C44,22 38,32 34,40 C30,46 22,50 14,50 Z"
      />
    </Svg>
  );
}

function TigerIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x={12} y={20} width={40} height={24} rx={10} fill="currentColor" />
      <g stroke={CREAM} strokeWidth={3.4} strokeLinecap="round">
        <path d="M20,22 C24,28 24,36 20,42" />
        <path d="M32,20 C36,28 36,36 32,44" />
        <path d="M44,22 C48,28 48,36 44,42" />
      </g>
    </Svg>
  );
}

function StoatIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M14,18 C26,14 38,22 34,34 C31,42 22,44 16,40 C22,40 28,36 28,30 C28,22 20,20 14,22 Z"
      />
      <circle cx={16} cy={40} r={5} fill="currentColor" />
    </Svg>
  );
}

function CapeBuffaloIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M32,26 C26,26 22,20 14,20 C20,26 24,30 24,34 C24,38 20,44 16,48 C24,46 30,38 32,32 C34,38 40,46 48,48 C44,44 40,38 40,34 C40,30 44,26 50,20 C42,20 38,26 32,26 Z"
      />
    </Svg>
  );
}

function PangolinIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M42,14 C24,14 14,26 16,38 C17,46 24,52 34,50 C28,48 22,42 24,34 C26,26 34,22 42,24 C36,20 38,16 42,14 Z"
      />
      <g fill="none" stroke={CREAM} strokeWidth={2}>
        <path d="M22,32 C26,30 30,30 33,32" />
        <path d="M20,38 C25,36 30,36 33,39" />
      </g>
    </Svg>
  );
}

function HermitCrabIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={3}
        d="M20,44 C14,34 18,18 34,16 C48,15 52,28 46,38 C40,47 26,48 20,44 Z"
      />
      <path d="M16,46 L8,42 M16,48 L8,52" stroke="currentColor" strokeWidth={3} strokeLinecap="round" />
    </Svg>
  );
}

function OpossumIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={32} cy={34} rx={18} ry={9} fill="currentColor" />
      <g stroke="currentColor" strokeWidth={3} strokeLinecap="round">
        <path d="M18,27 L14,16" />
        <path d="M26,25 L24,14" />
        <path d="M38,25 L40,14" />
        <path d="M46,27 L50,16" />
      </g>
      <g stroke="currentColor" strokeWidth={2} strokeLinecap="round">
        <path d="M27,32 L31,36 M31,32 L27,36" />
        <path d="M33,32 L37,36 M37,32 L33,36" />
      </g>
    </Svg>
  );
}

function QueenTermiteIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M32,10 C36,24 42,38 46,52 L18,52 C22,38 28,24 32,10 Z" />
      <g stroke={CREAM} strokeWidth={2}>
        <path d="M23,32 L41,32" />
        <path d="M21,42 L43,42" />
      </g>
    </Svg>
  );
}

function SnappingTurtleIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={4.5}
        strokeLinecap="round"
        d="M12,44 L26,30 L22,24 L38,20 L52,10"
      />
    </Svg>
  );
}

// Not on the source avatar sheet - Snapping Turtle already claimed its
// one turtle concept (a snapped twig). A domed, hinge-lined shell
// stands in for Box Turtle's own defining trait instead: a hinged
// plastron that lets it seal shut completely, unlike a snapper's.
function BoxTurtleIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M12,38 C12,24 20,16 32,16 C44,16 52,24 52,38 C52,42 44,44 32,44 C20,44 12,42 12,38 Z"
      />
      <line x1={32} y1={20} x2={32} y2={42} stroke={CREAM} strokeWidth={1.6} />
      <line x1={18} y1={34} x2={46} y2={34} stroke={CREAM} strokeWidth={1.6} />
    </Svg>
  );
}

function HummingbirdIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M32,50 C24,36 24,22 32,12 C40,22 40,36 32,50 Z" />
      <g fill="currentColor">
        <polygon points="32,12 26,4 32,7 38,4" />
        <polygon points="20,18 12,14 18,20 12,24" />
        <polygon points="44,18 52,14 46,20 52,24" />
      </g>
    </Svg>
  );
}

function MountainGoatIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M10,50 L10,38 L22,38 L22,28 L34,28 L34,18 L46,18 L46,10 L54,10 L54,50 Z" />
    </Svg>
  );
}

function MonarchButterflyIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M32,10 C42,14 46,26 40,40 C36,50 28,50 24,40 C18,26 22,14 32,10 Z" />
      <line x1={32} y1={14} x2={32} y2={46} stroke={CREAM} strokeWidth={1.6} />
    </Svg>
  );
}

function HomingPigeonIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={32} cy={32} rx={16} ry={8} fill="none" stroke="currentColor" strokeWidth={4} />
      <rect x={27} y={40} width={10} height={8} rx={1.5} fill="currentColor" />
    </Svg>
  );
}

function GreyhoundIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <line x1={16} y1={12} x2={16} y2={52} stroke="currentColor" strokeWidth={3} />
      <path fill="currentColor" d="M16,16 L44,16 L36,26 L44,36 L16,36 Z" />
      <g fill={CREAM}>
        <rect x={19} y={18} width={6} height={6} />
        <rect x={31} y={18} width={6} height={6} />
        <rect x={25} y={24} width={6} height={6} />
        <rect x={19} y={30} width={6} height={4} />
      </g>
    </Svg>
  );
}

function AlbatrossIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M22,26 C26,22 38,22 42,26 C38,24 26,24 22,26 Z" />
      <g fill="none" stroke="currentColor" strokeWidth={2.6} strokeLinecap="round">
        <path d="M10,40 C18,36 26,44 34,40" />
        <path d="M28,46 C36,42 44,50 52,46" />
      </g>
    </Svg>
  );
}

function AnglerfishIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="none" stroke="currentColor" strokeWidth={2.6} strokeLinecap="round" d="M18,46 C18,30 24,20 30,14" />
      <circle cx={30} cy={12} r={5} fill="currentColor" />
      <g stroke="currentColor" strokeWidth={1.6} strokeLinecap="round">
        <path d="M30,4 L30,1" />
        <path d="M22,8 L19,6" />
        <path d="M38,8 L41,6" />
      </g>
    </Svg>
  );
}

function CowbirdIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <ellipse cx={18} cy={34} rx={4} ry={6} transform="rotate(-15 18 34)" />
        <polygon points="14,20 18,12 22,20" />
        <polygon points="6,24 10,16 14,24" />
        <polygon points="22,24 26,16 30,24" />
      </g>
      <g fill="currentColor">
        <ellipse cx={44} cy={30} rx={6} ry={9} transform="rotate(10 44 30)" />
        <ellipse cx={52} cy={32} rx={6} ry={9} transform="rotate(10 52 32)" />
      </g>
    </Svg>
  );
}

function MagpieIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M32,10 C40,20 42,36 34,54 C30,36 30,20 32,10 Z" />
      <line x1={32} y1={14} x2={32} y2={50} stroke={CREAM} strokeWidth={1.4} />
      <g stroke={CREAM} strokeWidth={1.2}>
        <path d="M32,22 L26,26" />
        <path d="M32,22 L38,26" />
        <path d="M32,32 L25,36" />
        <path d="M32,32 L39,36" />
      </g>
    </Svg>
  );
}

function RavenIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <polygon points="32,16 42,32 32,48 22,32" fill="currentColor" />
      <g stroke="currentColor" strokeWidth={2} strokeLinecap="round">
        <path d="M46,14 L50,10" />
        <path d="M50,14 L46,10" />
      </g>
    </Svg>
  );
}

function ElephantIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={5}
        strokeLinecap="round"
        d="M24,14 C40,14 44,24 38,32 C32,40 40,42 34,48"
      />
      <circle cx={34} cy={54} r={3} fill="currentColor" />
    </Svg>
  );
}

function FoxIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M14,20 C30,18 46,28 46,42 C36,44 24,40 20,30 C18,26 16,22 14,20 Z"
      />
      <path fill={CREAM} d="M46,42 C40,44 34,42 30,38 C36,36 42,38 46,42 Z" />
    </Svg>
  );
}

// ---- Mobile refresh (2026-09) chrome glyphs ----
// A second, unrelated icon language from the animal/energy set above -
// stroke-based on a 24-unit viewBox, straight from the design handoff's
// literal paths (design_handoff_dice_kingdom_mobile/README.md), not
// reproduced by eye. Kept separate from Svg/ENERGY_ICONS/CHARACTER_ICONS
// rather than forced into that 64-unit fill-based style, since these
// really are a different visual language (phase-rail pills, not game
// pieces) - the handoff itself draws this distinction.
function StrokeSvg({ size = 17, strokeWidth = 1.9, children }: IconProps & { strokeWidth?: number; children: ReactNode }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={strokeWidth} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  );
}

export type PhaseKey = "clear" | "roll" | "main" | "attack" | "cleanup";

const PHASE_PATHS: Record<PhaseKey, string> = {
  clear: "M12 3v10M8 9l4 4 4-4M4 15v5h16v-5",
  roll: "M4 4h16v16H4zM9 9h0.01M15 15h0.01",
  main: "M12 5v14M5 12h14",
  attack: "M5 4l5 16M11 4l5 16M17 5l4 12",
  cleanup: "M20 12a8 8 0 1 1-2.4-5.7M20 4v4h-4",
};

export function PhaseIcon({ phase, size = 17 }: { phase: PhaseKey; size?: number }) {
  return (
    <StrokeSvg size={size}>
      <path d={PHASE_PATHS[phase]} />
    </StrokeSvg>
  );
}

export function ChevronDownIcon({ size = 10 }: IconProps) {
  return (
    <StrokeSvg size={size} strokeWidth={2.6}>
      <path d="M6 9l6 6 6-6" />
    </StrokeSvg>
  );
}

export function ArrowRightIcon({ size = 14 }: IconProps) {
  return (
    <StrokeSvg size={size} strokeWidth={2}>
      <path d="M5 12h14M14 7l5 5-5 5" />
    </StrokeSvg>
  );
}

// Three cards added to the roster after the original 32-card sheet
// (DiceKingdomConfig.cs's own Mongoose/Swift/Cuttlefish remarks -
// extra cost-curve entries per type) with no avatar drawn for them yet -
// real bug, direct feedback (2026-09-13): all three silently fell back
// to TardigradeIcon (CHARACTER_ICONS had no "DK-CLAW-09"/"DK-WING-09"/
// "DK-EYE-09" entry at all), most visibly "Mongoose looks like a
// Tardigrade" in the roster/buy strip.

function MongooseIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M8,36 C8,28 16,24 26,24 C36,24 46,28 52,32 C46,32 38,30 30,30 C34,34 34,40 28,44 C24,47 16,47 10,44 C15,43 19,40 19,36 C15,38 11,38 8,36 Z"
      />
      <polygon points="8,36 2,32 4,40" fill="currentColor" />
    </Svg>
  );
}

function SwiftIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="currentColor"
        d="M32,30 C24,18 12,14 4,16 C12,20 18,26 20,32 C12,32 4,36 0,42 C10,42 18,40 24,34 C26,40 30,46 38,50 C36,42 34,36 34,30 C40,34 48,36 56,34 C48,30 40,28 32,30 Z"
      />
    </Svg>
  );
}

// Head-on with the frill fanned out - the threat display Intimidate is.
function FrilledLizardIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M32,42 L5,30 C6,14 20,6 32,6 C44,6 58,14 59,30 Z" />
      <g stroke={CREAM} strokeWidth={1.8} strokeLinecap="round" opacity={0.75}>
        <path d="M32,40 L11,24" />
        <path d="M32,40 L20,13" />
        <path d="M32,40 L32,10" />
        <path d="M32,40 L44,13" />
        <path d="M32,40 L53,24" />
      </g>
      <ellipse cx={32} cy={44} rx={9} ry={8} fill="currentColor" />
      <path d="M26,49 Q32,54 38,49" stroke={CREAM} strokeWidth={2} fill="none" strokeLinecap="round" />
      <circle cx={28} cy={41} r={1.8} fill={CREAM} />
      <circle cx={36} cy={41} r={1.8} fill={CREAM} />
    </Svg>
  );
}

// Gliding, membrane spread, seen from below.
function FlyingSquirrelIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M10,22 C20,26 44,26 54,22 L50,42 C42,38 22,38 14,42 Z" />
      <circle cx={32} cy={20} r={7} fill="currentColor" />
      <path fill="currentColor" d="M26,15 L25,8 L30,13 Z M38,15 L39,8 L34,13 Z" />
      <ellipse cx={32} cy={50} rx={5} ry={10} fill="currentColor" />
      <circle cx={29.5} cy={19} r={1.6} fill={CREAM} />
      <circle cx={34.5} cy={19} r={1.6} fill={CREAM} />
    </Svg>
  );
}

// Side-on, a jolt arcing off its back - every action used zaps something.
function ElectricEelIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={7}
        strokeLinecap="round"
        d="M8,44 C16,34 24,50 32,40 C40,30 48,46 56,36"
      />
      <circle cx={10} cy={42} r={1.4} fill={CREAM} />
      <path fill="currentColor" d="M34,8 L26,22 L32,22 L28,32 L40,17 L34,17 L38,8 Z" />
    </Svg>
  );
}

// Curled tail, crest and a swivelling eye - blends in, slips past.
function ChameleonIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M8,34 C8,22 20,16 34,18 C44,19 52,24 54,32 C48,38 36,40 26,38 C18,37 12,38 8,34 Z" />
      <path fill="currentColor" d="M8,34 L4,26 L14,24 Z" />
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={4}
        strokeLinecap="round"
        d="M52,32 C60,38 58,52 48,52 C41,52 40,44 46,43 C50,42 51,47 48,48"
      />
      <g stroke="currentColor" strokeWidth={3} strokeLinecap="round">
        <path d="M22,38 L20,48" />
        <path d="M38,39 L40,48" />
      </g>
      <circle cx={16} cy={27} r={3.6} fill={CREAM} />
      <circle cx={16} cy={27} r={1.6} fill="currentColor" />
    </Svg>
  );
}

// Glowing tail end and a pair of wings - flashes on a double energy roll.
function FireflyIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={32} cy={24} rx={6} ry={8} fill="currentColor" />
      <ellipse cx={32} cy={42} rx={8} ry={10} fill="currentColor" />
      <circle cx={32} cy={44} r={5} fill={CREAM} />
      <path fill="currentColor" opacity={0.7} d="M26,28 C14,20 8,30 14,36 C18,38 24,34 26,32 Z M38,28 C50,20 56,30 50,36 C46,38 40,34 38,32 Z" />
      <g stroke="currentColor" strokeWidth={2} strokeLinecap="round">
        <path d="M30,17 L26,9" />
        <path d="M34,17 L38,9" />
      </g>
    </Svg>
  );
}

// A looping leech with its sucker down - drains whatever it touches.
function LeechIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path
        fill="none"
        stroke="currentColor"
        strokeWidth={9}
        strokeLinecap="round"
        d="M12,46 C12,30 26,22 32,32 C38,42 52,34 52,20"
      />
      <circle cx={12} cy={48} r={6} fill="currentColor" />
      <circle cx={12} cy={48} r={2.5} fill={CREAM} />
    </Svg>
  );
}

// Upright on sentry duty.
function MeerkatIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={32} cy={14} rx={7} ry={8} fill="currentColor" />
      <path fill="currentColor" d="M24,22 C22,34 22,46 24,56 L40,56 C42,46 42,34 40,22 Z" />
      <g stroke="currentColor" strokeWidth={3} strokeLinecap="round">
        <path d="M25,30 L21,36" />
        <path d="M39,30 L43,36" />
      </g>
      <circle cx={29} cy={13} r={1.5} fill={CREAM} />
      <circle cx={35} cy={13} r={1.5} fill={CREAM} />
    </Svg>
  );
}

// Three-part body, mandibles open.
function ArmyAntIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx={16} cy={30} r={7} fill="currentColor" />
      <ellipse cx={30} cy={32} rx={6} ry={5} fill="currentColor" />
      <ellipse cx={46} cy={34} rx={11} ry={8} fill="currentColor" />
      <g stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" fill="none">
        <path d="M10,26 L4,20 M10,33 L3,36" />
        <path d="M27,36 L22,48 M30,37 L30,50 M33,36 L38,48" />
      </g>
    </Svg>
  );
}

// Spitting a jet up and over.
function ArcherfishIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M10,44 C18,34 34,32 44,40 L54,32 L52,46 L54,58 L44,50 C34,56 18,54 10,44 Z" />
      <circle cx={18} cy={42} r={2} fill={CREAM} />
      <g fill="currentColor">
        <circle cx={14} cy={30} r={2.2} />
        <circle cx={20} cy={20} r={2.2} />
        <circle cx={30} cy={13} r={2.2} />
        <circle cx={42} cy={10} r={2.2} />
      </g>
    </Svg>
  );
}

// Beetle with its spray going off behind it.
function BombardierBeetleIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={26} cy={34} rx={14} ry={11} fill="currentColor" />
      <circle cx={10} cy={34} r={5} fill="currentColor" />
      <path d="M26,23 L26,45" stroke={CREAM} strokeWidth={1.6} />
      <g stroke="currentColor" strokeWidth={2.4} strokeLinecap="round">
        <path d="M44,30 L56,24" />
        <path d="M44,34 L58,34" />
        <path d="M44,38 L56,44" />
      </g>
    </Svg>
  );
}

// Long lizard, forked tongue out.
function KomodoDragonIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path fill="currentColor" d="M6,32 C10,26 18,26 24,28 L46,28 C52,28 58,32 62,38 C54,36 50,36 46,38 L24,38 C18,40 10,38 6,32 Z" />
      <g stroke="currentColor" strokeWidth={3} strokeLinecap="round">
        <path d="M26,38 L22,48 M40,38 L44,48 M28,28 L24,20 M40,28 L44,20" />
      </g>
      <path d="M6,32 L0,30 M6,32 L0,34" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" />
      <circle cx={12} cy={30} r={1.6} fill={CREAM} />
    </Svg>
  );
}

function CuttlefishIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={30} cy={26} rx={14} ry={10} fill="currentColor" />
      <g stroke="currentColor" strokeWidth={2.4} strokeLinecap="round" fill="none">
        <path d="M20,34 C18,42 16,48 14,52" />
        <path d="M26,36 C25,43 24,48 23,52" />
        <path d="M34,36 C35,43 36,48 37,52" />
        <path d="M40,34 C42,42 44,48 46,52" />
      </g>
      <circle cx={35} cy={24} r={2.2} fill={CREAM} />
    </Svg>
  );
}

// --- Basic Actions (2026-09-26): plants, not animals - "to differentiate
// these as actions with no stats, let's give them a plant or nature
// theme." Same flat 64x64 silhouette style as the creatures. ---

// Anger Issues - a thistle: prickly, and it bites back.
function ThistleIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <rect x={30} y={34} width={4} height={24} rx={2} />
        <path d="M32,50 C24,48 18,42 16,36 C23,38 28,42 32,46 Z" />
        <path d="M32,44 C40,42 46,36 48,30 C41,32 36,36 32,40 Z" />
        <ellipse cx={32} cy={30} rx={9} ry={7} />
        <path d="M22,22 L27,26 L25,14 L30,24 L32,8 L34,24 L39,14 L37,26 L42,22 L38,28 L26,28 Z" />
      </g>
      <path d="M26,30 L38,30 M27,34 L37,34" stroke={CREAM} strokeWidth={1.6} strokeLinecap="round" />
    </Svg>
  );
}

// Archnemesis - a Venus flytrap: two jaws closing on each other, the
// "deal damage to each other" of the card.
function VenusFlytrapIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <rect x={30} y={36} width={4} height={22} rx={2} />
        <path d="M32,50 C25,48 20,44 18,39 C24,40 29,43 32,47 Z" />
        <path d="M31,34 C24,34 16,28 14,18 C22,18 29,24 31,34 Z" />
        <path d="M33,34 C40,34 48,28 50,18 C42,18 35,24 33,34 Z" />
      </g>
      <g stroke={CREAM} strokeWidth={1.6} strokeLinecap="round">
        <path d="M16,19 L13,15 M20,19 L18,14 M24,21 L23,16" />
        <path d="M48,19 L51,15 M44,19 L46,14 M40,21 L41,16" />
      </g>
    </Svg>
  );
}

// Distraction - a bright flower, the lure you can't look away from.
function FlowerIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <rect x={30} y={34} width={4} height={24} rx={2} />
        <path d="M32,52 C26,50 22,46 21,41 C26,42 30,45 32,49 Z" />
        {[0, 72, 144, 216, 288].map((deg) => (
          <ellipse key={deg} cx={32} cy={13} rx={7} ry={10} transform={`rotate(${deg} 32 24)`} />
        ))}
      </g>
      <circle cx={32} cy={24} r={6} fill={CREAM} />
    </Svg>
  );
}

// Resurrection - a seedling pushing back up out of the soil.
function SeedlingIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <path d="M10,54 C16,46 48,46 54,54 Z" />
        <rect x={30} y={26} width={4} height={24} rx={2} />
        <path d="M32,30 C30,20 22,14 10,16 C12,27 21,32 32,30 Z" />
        <path d="M32,26 C34,15 43,9 55,11 C53,22 44,28 32,26 Z" />
      </g>
      <path d="M31,29 C25,25 19,21 14,19 M33,25 C40,20 46,15 51,13" stroke={CREAM} strokeWidth={1.4} fill="none" strokeLinecap="round" />
    </Svg>
  );
}

// Mutation - a mushroom: the strange thing that grows overnight.
function MushroomIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <g fill="currentColor">
        <path d="M8,34 C8,18 20,10 32,10 C44,10 56,18 56,34 Z" />
        <path d="M25,34 L39,34 L41,54 C41,57 23,57 23,54 Z" />
      </g>
      <g fill={CREAM}>
        <circle cx={22} cy={24} r={4} />
        <circle cx={36} cy={18} r={3} />
        <circle cx={44} cy={28} r={3.5} />
        <circle cx={30} cy={29} r={2.2} />
      </g>
    </Svg>
  );
}

// Flat, transparent silhouette marks for the individually registered new cards.
// Even-odd cutouts keep their eyes and gaps transparent in both site themes.
// `holes`, when given, is cut out through a mask instead - for a shape
// whose own parts overlap, which even-odd would punch holes through.
function silhouette(path: string, holes?: string): (props: IconProps) => ReactElement {
  return function CardSilhouette(props: IconProps) {
    const maskId = "sil" + useId().replace(/[^A-Za-z0-9_-]/g, "");
    if (!holes) return <Svg {...props}><path d={path} fill="currentColor" fillRule="evenodd" /></Svg>;
    return (
      <Svg {...props}>
        <mask id={maskId}><rect width="64" height="64" fill="#fff" /><path d={holes} fill="#000" /></mask>
        <path d={path} fill="currentColor" mask={`url(#${maskId})`} />
      </Svg>
    );
  };
}

const AxolotlIcon = silhouette("M21 27Q20 17 29 17H37Q44 17 43 28L53 20L49 31L58 34L47 38L53 47L42 41Q42 51 34 55Q24 51 23 42L11 48L17 38L6 34L17 30L11 20ZM28 27a2 2 0 1 0 4 0a2 2 0 1 0-4 0M37 27a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const PuffinIcon = silhouette("M25 12Q35 7 42 15L54 22L42 25Q42 43 50 52H17L23 43Q15 28 22 19ZM28 17a3 3 0 1 0 6 0a3 3 0 1 0-6 0M25 29Q23 38 30 44Q36 38 34 29Z");
const TrapdoorSpiderIcon = silhouette("M25 22Q32 14 39 22L42 32L52 23L57 25L45 37L55 46L52 50L40 40L39 52L34 53L34 40H30L30 53L25 52L24 40L12 50L9 46L19 37L7 25L12 23L22 32ZM28 26a3 3 0 1 0 6 0a3 3 0 1 0-6 0M4 58Q32 46 60 58L60 62H4Z");
const StagBeetleIcon = silhouette("M24 27L17 19L15 5L21 8L23 18L28 21L28 9H32L32 22H36L39 18L41 8L47 5L45 19L38 27Q45 35 43 46L49 54L45 57L38 50Q32 56 26 50L19 57L15 54L21 46Q19 35 24 27ZM30 30V47H34V30Z");
const WeaverbirdIcon = silhouette("M28 10Q35 10 38 17L48 19L38 24L34 39L43 43L37 47L25 43L19 48L15 44L21 37L23 23L8 29L15 15ZM30 16a2 2 0 1 0 4 0a2 2 0 1 0-4 0M12 50Q32 42 52 50L48 59H16Z");
const SugarGliderIcon = silhouette("M25 14L20 7L15 12L17 24L5 39L20 48L25 38L32 55L39 38L44 48L59 39L47 24L49 12L44 7L39 14L32 10ZM25 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0M35 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const PrairieDogIcon = silhouette("M22 25Q17 19 20 13Q24 9 29 13Q40 9 45 19L54 24L45 29L41 37Q45 44 48 54H17Q14 44 20 38L24 30ZM32 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0M19 46L6 40L4 45L19 53Z");
const SpittingCobraIcon = silhouette("M26 31Q13 26 15 14Q17 4 27 8L32 14L37 8Q47 4 49 14Q51 26 38 31V42Q48 45 48 52Q46 61 31 59L31 54Q43 55 42 51Q39 48 31 48Q26 48 26 41ZM22 16L26 19L26 24L20 21ZM42 16L38 19L38 24L44 21ZM32 8L30 1H34Z");
const PufferfishIcon = silhouette("M32 7L36 14L43 10L45 19L54 18L51 27L60 32L51 37L54 46L45 45L43 54L36 50L32 57L28 50L21 54L19 45L10 46L13 37L4 32L13 27L10 18L19 19L21 10L28 14ZM38 26a4 4 0 1 0 8 0a4 4 0 1 0-8 0M21 26Q17 33 23 38Q28 31 21 26Z");
const SeasonalRenewalIcon = silhouette("M30 8V33Q19 13 7 18Q8 34 27 39V56H33V42Q55 40 57 19Q41 13 34 32V8ZM13 49L8 39L2 49ZM49 52L55 42L61 52Z");
const DeathstalkerScorpionIcon = silhouette("M24 33Q19 24 24 19L21 12L11 10L6 17L12 27L18 23L21 28L13 33L7 29L4 33L14 40L22 35L24 41L16 48L19 52L29 44Q36 49 42 41L48 44L52 39L47 35Q59 28 56 13Q53 3 44 6L42 15L48 12Q56 26 42 30L38 22L41 17L37 13L30 19L32 28Z");
const GeladaIcon = silhouette("M18 24L16 11L25 16L32 9L39 16L48 11L46 24L52 35L45 49L32 57L19 49L12 35ZM20 28L28 30L26 35L19 33ZM44 28L36 30L38 35L45 33ZM27 43L32 38L37 43L34 49H30Z");
const LionfishIcon = silhouette("M12 32L3 22L7 12L17 23L20 7L25 19L32 3L37 19L45 7L45 23L56 14L53 28L62 33L51 39L54 53L43 44L40 58L34 45L25 58L24 43L10 49L15 38ZM37 29a3 3 0 1 0 6 0a3 3 0 1 0-6 0");
const GiantTortoiseIcon = silhouette("M12 40Q10 16 31 12Q53 16 52 40L59 40L60 47L50 48L48 57H41L40 49H24L23 57H16L15 48L5 47L4 40ZM19 35Q19 21 30 18V36ZM35 18Q45 21 45 35H35Z");
const MandrillIcon = silhouette("M14 19L20 9L27 13L32 8L37 13L44 9L50 19L47 40L37 56H27L17 40ZM22 21V39H26V21ZM38 21V39H42V21ZM29 23H35V43L39 47L32 52L25 47L29 43Z");
const NutcrackerIcon = silhouette("M18 25L10 14L24 16Q29 7 38 11L48 16L59 20L45 24L39 39L48 52L29 48L17 54L20 39L9 38ZM30 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0M31 32L24 40L33 38Z");
const AfricanWildDogIcon = silhouette("M17 20L9 7L20 5L26 16H38L44 5L55 7L47 20L49 37L39 53H25L15 37ZM20 26L28 29L24 33L19 30ZM44 26L36 29L40 33L45 30ZM28 40L32 36L36 40L32 45Z");
const HorseshoeCrabIcon = silhouette("M8 33Q6 9 32 6Q58 9 56 33L47 44L35 42L34 61H30L29 42L17 44ZM15 29Q17 15 32 13Q47 15 49 29L43 35L21 35Z");
const KangarooRatIcon = silhouette("M20 26L18 8Q25 7 29 19L34 13Q44 11 48 22L56 29L46 34L41 42L51 54H38L30 40L22 46L15 43L23 33ZM35 23a2 2 0 1 0 4 0a2 2 0 1 0-4 0M23 44Q10 48 5 35L2 39Q8 57 24 50Z");
const OrbWeaverIcon = silhouette("M31 4L33 4L34 14L45 8L48 11L38 21L55 22L57 27L42 30L58 41L56 45L41 38L45 56L41 59L35 43L33 61H29L27 43L21 59L17 56L21 38L6 45L4 41L20 30L5 27L7 22L24 21L14 11L17 8L28 14ZM25 26Q31 18 37 26Q42 40 31 45Q20 40 25 26Z");
const GrowthSpurtIcon = silhouette("M28 58V29L10 19L10 10L28 17V5H34V25L53 12L56 21L34 35V58ZM7 39L17 42L14 32ZM45 41L50 31L55 40Z");
const VampireSquidIcon = silhouette("M25 11Q32 5 39 11L45 27L58 45L47 52L39 46L36 58L32 48L28 58L25 46L17 52L6 45L19 27ZM26 22a3 3 0 1 0 6 0a3 3 0 1 0-6 0M34 22a3 3 0 1 0 6 0a3 3 0 1 0-6 0");
const GibbonIcon = silhouette("M22 18Q13 11 7 6L3 12L17 29L21 43L15 57H24L30 42L34 42L40 57H49L43 43L47 29L61 12L57 6L42 18Q32 9 22 18ZM23 22Q32 17 41 22L38 32H26Z");
const LionessIcon = silhouette("M12 23L8 12L20 8L24 16Q32 12 40 16L44 8L56 12L52 23L48 44L35 55H29L16 44ZM18 27L28 29L25 34L19 31ZM46 27L36 29L39 34L45 31ZM26 40L32 36L38 40L32 47Z");
const WildfireIcon = silhouette("M15 55Q0 43 16 31L18 45Q26 32 23 19L34 3Q34 27 45 22L44 37L54 30Q65 45 49 55ZM28 53Q17 47 31 34Q31 44 38 40Q48 50 37 53Z");
const MimicOctopusIcon = silhouette("M22 10Q32 5 42 10L45 28L39 38Q46 47 56 37L60 42Q47 60 36 44Q34 59 23 58Q13 57 13 48Q13 42 20 41L22 45Q15 49 22 52Q29 54 28 40Q18 49 9 42L4 47L1 43L9 36Q16 40 23 35L19 28ZM26 22a3 3 0 1 0 6 0a3 3 0 1 0-6 0M34 22a3 3 0 1 0 6 0a3 3 0 1 0-6 0");
const RedSquirrelIcon = silhouette("M26 40Q3 48 6 22Q8 5 21 8Q32 11 22 26L17 22Q22 13 17 14Q11 13 12 24Q12 37 27 33L30 21L28 9L34 15Q43 10 48 21L54 25L45 30L43 42L50 54H24L21 48ZM37 22a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const WombatIcon = silhouette("M9 34Q9 19 24 20L28 12L35 16L38 10L44 15L47 23L57 28L56 36L44 41L42 52H35L34 43H22L21 52H14L13 42ZM40 25a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const AssassinBugIcon = silhouette("M28 15L21 5L24 3L32 12L40 3L43 5L36 15L39 25L55 16L57 20L42 32L58 40L56 44L40 36L38 47L47 55L43 58L32 50L21 58L17 55L26 47L24 36L8 44L6 40L22 32L7 20L9 16L25 25ZM30 21V43H34V21Z");
const FrigatebirdIcon = silhouette("M29 26L13 6L3 9L16 31L25 34L15 53L30 44L32 56L35 43L49 54L41 34L53 25L61 6L51 7L37 25L37 17L44 14L37 11L31 13Z");
const CoyoteIcon = silhouette("M17 20L17 4L27 15L39 14L48 6L47 23L54 31L49 41L35 55L21 44L12 31ZM19 28L28 29L25 34L19 32ZM43 26L37 29L40 33L46 31ZM30 39L35 36L40 39L35 45Z");
const TasmanianDevilIcon = silhouette("M16 23L8 13L13 7L24 17H40L51 7L56 13L48 23L52 36L41 52H23L12 36ZM18 29L28 30L24 35L18 33ZM46 29L36 30L40 35L46 33ZM22 40L32 44L42 40L36 49H28Z");
const AyeAyeIcon = silhouette("M18 24Q6 23 7 12Q9 4 18 8L25 18H39L46 8Q55 4 57 12Q58 23 46 24L46 38L37 47H27L18 38ZM23 25a5 5 0 1 0 10 0a5 5 0 1 0-10 0M35 25a5 5 0 1 0 10 0a5 5 0 1 0-10 0M28 46L22 54H9V58H27L34 48L46 52L59 37L56 34L43 46L38 43Z");
const CaracalIcon = silhouette("M15 24L12 7L18 2L22 19L28 15H38L44 18L49 2L54 7L50 27L47 43L35 55H29L17 43ZM19 28L28 30L25 34L20 32ZM45 28L36 30L39 34L44 32ZM27 40L32 37L37 40L32 45Z");
const WoodpeckerIcon = silhouette("M27 18L21 5L33 9L38 17L57 23L40 26L35 39L44 53L29 50L18 59L19 43L8 40L22 29ZM30 19a2 2 0 1 0 4 0a2 2 0 1 0-4 0M22 33L15 37L22 39Z");
const AntlionIcon = silhouette("M29 22L15 8L8 13L23 28L18 37L8 32L5 37L17 45L10 54L14 58L25 49L32 57L39 49L50 58L54 54L47 45L59 37L56 32L46 37L41 28L56 13L49 8L35 22ZM26 31Q32 25 38 31L35 43H29Z");
const SecretaryBirdIcon = silhouette("M24 17L19 4L25 8L28 3L31 12L37 10L44 17L54 20L42 24L39 34L29 41L27 53L33 56V59H20L24 38L17 29ZM33 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0M37 36L44 46L52 46V50H40L31 39Z");
const MockingbirdIcon = silhouette("M12 33L7 19L20 25L24 17Q27 7 37 13L40 17L54 20L40 24L36 37L51 43L44 47L31 42L21 54L14 53L19 39ZM29 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0M23 29L27 36L32 31Z");
const PorcupineIcon = silhouette("M7 38L3 30L11 31L8 18L16 23L17 8L25 20L33 5L36 20L47 8L45 26L56 17L52 34L61 40L50 44L48 53H41L40 45H22L21 53H14L14 44ZM42 35a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const LeafTailedGeckoIcon = silhouette("M27 26L18 20L13 11L17 6L21 17L30 19L34 8L39 9L38 22L49 25L57 21L61 26L51 32L40 30L39 40L49 43L55 51L52 56L46 47L35 46L23 56L10 49L9 40L18 35L27 39ZM28 28L27 36L34 38L36 29Z");
const SableAntelopeIcon = silhouette("M23 25Q5 10 15 3L19 7Q13 13 27 20H37Q51 13 45 7L49 3Q59 10 41 25L45 39L36 55H28L19 39ZM23 29L28 30L27 35L22 33ZM41 29L36 30L37 35L42 33ZM27 42L32 38L37 42L32 48Z");
const WildBoarIcon = silhouette("M10 32L16 22L18 11L26 20Q42 15 49 29L58 32L56 43L46 45L42 54H35L33 46H22L20 54H13L14 42ZM46 33L44 43L39 45L42 50L51 43L52 33ZM30 25a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const BonoboIcon = silhouette("M17 22L20 12L27 17L32 11L37 17L44 12L47 22L48 38L39 55H25L16 38ZM20 24Q26 17 32 25Q38 17 44 24L40 34H24ZM26 42L32 37L38 42L34 49H30Z");
const KingCobraIcon = silhouette("M26 29Q9 27 12 14Q15 4 25 10L32 3L39 10Q49 4 52 14Q55 27 38 29V41Q51 42 53 51Q53 60 34 60V55Q47 55 47 50Q46 47 31 47Q26 46 26 39ZM20 15L26 18L23 23ZM44 15L38 18L41 23Z");
const SeaCucumberIcon = silhouette("M10 34Q12 20 27 19L43 22L50 28L58 25L55 32L62 36L54 39L58 47L49 43Q34 51 18 46L11 43L5 39ZM18 30a2 2 0 1 0 4 0a2 2 0 1 0-4 0M29 32a2 2 0 1 0 4 0a2 2 0 1 0-4 0M38 39a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const FruitBatIcon = silhouette("M26 23L23 11L30 18H34L41 11L38 23L54 7L61 13L53 37L44 30L40 44L33 36L32 55L31 36L24 44L20 30L11 37L3 13L10 7ZM28 24V29H31V24ZM33 24V29H36V24Z");
const CuckooIcon = silhouette("M19 23L11 8L22 14L27 20L29 13Q34 7 40 14L43 17L57 20L42 24L37 39L48 55L32 49L21 58L19 48L27 37L8 36ZM32 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const GiantCentipedeIcon = silhouette("M13 4L18 8L26 5L32 10L39 7L45 12L51 7L54 12L47 21L55 24L54 28L46 26L43 32L54 36L52 40L42 37L38 43L49 48L46 52L36 47L29 55L18 59L17 54L25 50L27 43L14 47L12 43L29 38L30 32L15 34L14 30L29 26L28 21L17 24L14 20L22 16L13 12ZM30 14L32 18L37 16L36 12Z");
const HarpyEagleIcon = silhouette("M17 19L15 5L25 13L32 7L39 13L49 5L47 19L52 30L45 42L37 45L34 55H30L27 45L19 42L12 30ZM20 24L29 27L26 32L20 29ZM44 24L35 27L38 32L44 29ZM29 35L32 31L36 35L32 43Z");
const PorbeagleIcon = silhouette("M9 33L21 24L29 7L37 24L45 29L59 19L54 34L61 46L44 39L36 42L29 53L27 41L18 40ZM19 29a2 2 0 1 0 4 0a2 2 0 1 0-4 0M10 34L18 36L16 38Z");
const BisonIcon = silhouette("M7 23L10 10L20 20L26 11L33 20L48 21L57 31L52 43L48 52H40L39 44H27L25 54H17L15 40L8 35ZM16 26a2 2 0 1 0 4 0a2 2 0 1 0-4 0M9 33L15 33L13 37Z");
const MosquitoIcon = silhouette("M29 24L24 6L29 5L33 20L38 5L42 7L36 25L42 28L56 16L59 20L45 33L60 44L57 48L40 37L36 53L32 60L28 53L24 37L7 48L4 44L19 33L5 20L8 16L22 28ZM29 28L31 47H33L35 28Z");
const StarfishIcon = silhouette("M32 4L39 23L59 23L44 36L49 57L32 45L15 57L20 36L5 23H25ZM30 24L30 39L34 39V24Z");
const AdaptationIcon = silhouette("M12 19L24 8L32 14L39 7L54 18L46 25L52 37L43 47L48 55L34 58L27 48L15 53L9 42L20 36L14 29ZM26 23Q20 32 28 40L38 37L40 26L33 20Z");
const PackInstinctIcon = silhouette("M6 26L10 10L24 19L32 15L40 19L54 10L58 26L49 43L32 57L15 43ZM14 26L27 29L24 35L16 32ZM50 26L37 29L40 35L48 32ZM24 43L32 38L40 43L32 50Z");
const ProtectiveBarkIcon = silhouette("M10 10Q32 3 54 10V31Q54 48 32 60Q10 48 10 31ZM20 16V34L26 36V48H31V26L39 19L36 15L31 20V13H26V29L24 29V16Z");
const StampedeIcon = silhouette("M8 44L17 30L27 34L30 22L45 26L57 14L61 19L48 33L44 44L34 41L30 55H23L26 40L20 39L15 50H8ZM4 26H17V31H4ZM3 36H12V41H3Z");
const CleanerWrasseIcon = silhouette("M5 31L13 26Q33 11 47 25L59 16L57 30L61 43L47 36Q30 49 13 36ZM19 25L16 30Q30 32 44 28L41 23Q30 28 19 25ZM11 31a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const DungBeetleIcon = silhouette("M25 27L19 19L13 18L10 23L17 28L16 39L8 44L10 48L19 45L22 51L18 58H24L30 52H35L41 58H47L42 50L45 45L54 49L57 44L48 39L47 29L53 25L54 18L48 17L42 24L38 26ZM30 29V47H34V29ZM29 5Q40 0 44 11Q48 23 35 23Q24 24 24 14Q24 8 29 5Z");
const PeacockMantisShrimpIcon = silhouette("M15 18L6 8L10 4L22 13L26 7L31 14L37 8L41 16L52 13L56 17L44 23L44 30L56 33L55 39L43 36L40 43L50 48L47 53L38 48L32 57L24 53L19 42L10 47L7 43L17 36L15 29L5 32L3 28L13 23ZM23 21L29 23L27 27L21 25ZM34 20L39 22L38 27L32 25Z");
const PistolShrimpIcon = silhouette("M23 17L20 8L24 6L30 15L35 6L39 9L34 18L39 26L50 20L56 23L60 34L56 41L46 39L43 34L38 36L35 46L26 54L13 51L5 45L9 40L19 46L27 40L24 31L14 36L10 32L20 24ZM49 28L49 33L54 34L53 29Z");
const SaigaIcon = silhouette("M20 24L16 5L20 3L25 20H38L44 3L48 5L43 26L43 38L39 44L44 49L41 56H30L25 48L18 44L16 34ZM24 28L28 29L28 34L23 32ZM37 28L41 29L42 33L38 34ZM27 40L34 39L39 49L34 52L29 48Z");
const ArcticTernIcon = silhouette("M29 27L11 10L2 12L21 35L27 34L21 53L32 45L43 53L37 34L43 35L62 12L53 10L35 27L34 19L43 17L35 13L29 16Z");
const QueenBeeIcon = silhouette("M25 21L19 13L22 10L30 17L32 8L35 17L43 10L46 13L39 21L41 28Q55 15 59 26Q61 37 42 37L40 46L32 57L24 46L22 37Q3 37 5 26Q9 15 23 28ZM26 34H38V39H26ZM28 45H36L32 50Z");
const WaspIcon = silhouette("M28 20L22 9L26 7L32 15L38 7L42 9L36 20L36 26Q54 15 58 23Q62 35 40 35L36 44L33 59L30 44L24 35Q2 35 6 23Q10 15 28 26ZM28 32H36V37H28Z");


// Animal and nature silhouettes for the 2026-10-13 imports.
const PlanarianIcon = silhouette("M32 59C25 51 23 45 25 35L26 24L15 16Q19 7 27 12L32 7L37 12Q45 7 49 16L38 24L39 35C41 45 39 51 32 59ZM23 15a2 2 0 1 0 4 0a2 2 0 1 0-4 0M37 15a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const AbundantWatersIcon = silhouette("M7 12Q18 3 32 12Q46 21 57 12V20Q46 29 32 20Q18 11 7 20ZM7 29Q18 20 32 29Q46 38 57 29V37Q46 46 32 37Q18 28 7 37ZM7 46Q18 37 32 46Q46 55 57 46V54Q46 63 32 54Q18 45 7 54Z");
const HerculesBeetleIcon = silhouette("M30 25V12L22 7L24 3L33 9L40 3L43 7L35 15V25L41 23L45 15L51 11L54 14L48 20L45 29L42 32L45 37L57 32L59 37L47 42L46 48L56 53L53 57L43 52Q32 63 21 52L11 57L8 53L18 48L17 42L5 37L7 32L19 37L22 32L19 29L16 20L10 14L13 11L19 15L23 23ZM29 35V53H34V35Z");
const MarmosetIcon = silhouette("M32 18Q31 7 21 11L17 6L14 15L8 13L11 23L17 25Q14 32 21 37L21 45L16 51L20 55L30 48L32 40Q38 37 40 47Q42 60 53 54Q63 48 56 39L52 42Q56 49 52 50Q45 54 44 45Q43 33 34 34L34 27L42 23L47 16L40 15L39 8ZM20 21a2 2 0 1 0 4 0a2 2 0 1 0-4 0M29 21a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const FireAntIcon = silhouette("M28 15L20 7L22 4L32 11L42 4L44 7L36 15Q44 19 40 27L36 31L38 36L50 25L54 28L42 40L56 45L54 49L41 45L44 57L39 59L36 48Q32 52 28 48L25 59L20 57L23 45L10 49L8 45L22 40L10 28L14 25L26 36L28 31L24 27Q20 19 28 15ZM27 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0M34 20a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const ElectricRayIcon = silhouette("M32 9Q36 17 42 20L59 33L49 40L38 39L34 45V52Q34 57 39 56L42 51L46 53Q44 62 36 61Q28 61 28 52V45L25 39L15 40L5 33L22 20Q28 17 32 9ZM24 25a2 2 0 1 0 4 0a2 2 0 1 0-4 0M36 25a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const VelvetWormIcon = silhouette("M47 13L45 5L49 4L52 13L57 8L60 11L55 18Q59 24 53 28L48 33L52 38L49 42L44 38L39 41L42 47L38 50L34 44L28 43L26 50L22 48L24 41L19 37L13 41L10 37L16 32Q10 26 14 19L8 15L11 11L17 15Q21 10 27 14L25 21Q20 20 21 26Q24 34 33 34Q42 33 44 24Q38 19 41 15ZM47 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const LeafInsectIcon = silhouette("M29 16L23 5L27 3L32 12L37 3L41 5L35 16L43 23L55 15L58 19L46 28L48 36L59 35L59 40L47 41L43 48L53 55L50 59L39 51L32 59L25 51L14 59L11 55L21 48L17 41L5 40L5 35L16 36L18 28L6 19L9 15L21 23ZM30 23V48H34V23ZM22 29L27 33L27 38L21 34ZM42 29L37 33L37 38L43 34Z");
const OrchidMantisIcon = silhouette("M24 8L32 14L40 8L43 13L35 21L35 29L45 21L44 12L48 11L54 27L41 34L52 43L48 49L38 39L37 50L44 57L40 61L32 54L24 61L20 57L27 50L26 39L16 49L12 43L23 34L10 27L16 11L20 12L19 21L29 29L29 21L21 13ZM26 14a2 2 0 1 0 4 0a2 2 0 1 0-4 0M34 14a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const BowerbirdIcon = silhouette("M6 55V28Q6 9 23 6L25 11Q12 14 12 29V55ZM58 55V28Q58 9 41 6L39 11Q52 14 52 29V55ZM25 25Q29 17 37 22L40 27L49 29L39 34Q41 42 36 46L42 54H36L32 47L28 54H22L25 44L17 45L20 32L14 27ZM31 26a2 2 0 1 0 4 0a2 2 0 1 0-4 0M24 33L28 40L34 35Z");
const FireSalamanderIcon = silhouette("M28 19L18 14L10 18L8 14L17 8L28 12L29 5L34 3L39 10L37 21L44 28L53 23L58 28L56 32L50 29L43 35L38 42L43 48L50 49L49 54L40 54L32 47L23 49L17 46L13 38L17 36L22 43L27 42L30 31L23 27Q17 31 17 36L12 35Q12 28 22 22ZM30 14a2 2 0 1 0 4 0a2 2 0 1 0-4 0M31 25a2 2 0 1 0 4 0a2 2 0 1 0-4 0M9 40Q6 56 22 57L25 52Q11 51 13 41Z");
const MimicFilefishIcon = silhouette("M7 34L17 25L18 9L22 7L25 22Q37 17 46 28L59 22L57 34L60 46L46 39Q35 49 23 43L18 52L15 49L18 41L8 38ZM17 30a2 2 0 1 0 4 0a2 2 0 1 0-4 0M29 27L35 24L38 29L31 32ZM34 35L40 32L43 37L37 41Z");
const HarrisHawkIcon = silhouette("M30 20L19 6L10 4L19 22L4 14L3 20L22 35L11 34L8 39L25 42L24 54L32 49L40 54L39 42L56 39L53 34L42 35L61 20L60 14L45 22L54 4L45 6L34 20L33 12L39 14L43 19L38 23L37 31L31 36L27 30ZM32 17a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0");
const SpringEmergenceIcon = silhouette("M29 44V32Q14 35 9 16Q27 14 32 26Q37 14 55 16Q50 35 35 32V44Q45 42 49 50Q48 59 32 60Q16 59 15 50Q19 42 29 44ZM18 21L27 28L24 30L16 24ZM46 21L37 28L40 30L48 24ZM23 48Q18 52 24 55L26 51Z");
const ThornShotIcon = silhouette("M10 54L47 17L49 4L56 8L60 15L47 20L10 57ZM20 43L18 27L28 35L35 29L35 15L44 21L40 32L53 31L44 42L35 39L27 46L28 59L18 55ZM43 14L49 14L51 10L47 8Z");
const ForagingIcon = silhouette("M30 34L26 20Q12 24 8 9Q22 5 29 14L32 23Q35 12 49 10Q55 24 38 29L36 36L42 37Q49 30 55 37Q63 46 52 52Q45 55 40 49Q37 61 27 57Q20 54 22 46Q11 48 7 39Q4 30 14 27Q22 25 26 34ZM14 32a2 2 0 1 0 4 0a2 2 0 1 0-4 0M46 39a2 2 0 1 0 4 0a2 2 0 1 0-4 0M28 47a2 2 0 1 0 4 0a2 2 0 1 0-4 0");

// KingdomImports.cs (2026-10-20).
const SnowLeopardIcon = silhouette("M45 23L47 16L51 21Q57 20 59 26Q60 32 54 34L51 35L49 43L51 52H46L44 42H29L26 52H21L21 41Q14 38 12 31Q11 25 7 27Q3 30 7 36Q10 41 7 45Q1 42 1 35Q1 21 13 22Q17 24 18 29Q25 26 37 27ZM53 25a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M24 32a2 2 0 1 0 4 0a2 2 0 1 0-4 0M32 34a2 2 0 1 0 4 0a2 2 0 1 0-4 0M38 30a2 2 0 1 0 4 0a2 2 0 1 0-4 0M30 30a1.4 1.4 0 1 0 2.8 0a1.4 1.4 0 1 0-2.8 0");
const RedPandaIcon = silhouette("M22 12L20 4L27 8Q32 6 37 8L44 4L42 12Q46 18 42 23L50 13L55 16L45 32L44 44L48 58H38L34 48H30L26 58H16L20 44L19 32L9 16L14 13L22 23Q18 18 22 12ZM24 15Q27 13 29 17Q26 19 24 15M35 17Q37 13 40 15Q38 19 35 17M30 20L34 20L32 22ZM45 42Q62 38 62 50Q62 62 50 60Q58 54 54 50Q50 46 44 50ZM52 42L54 48L57 46L55 41ZM58 50L62 52L61 55L57 53Z");
const KoalaIcon = silhouette("M15 10Q4 12 5 23Q6 32 15 31Q14 44 22 52Q32 58 42 52Q50 44 49 31Q58 32 59 23Q60 12 49 10Q41 9 39 17Q32 14 25 17Q23 9 15 10ZM22 30a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0M37 30a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0M28 35Q32 32 36 35L37 43Q32 47 27 43ZM11 17Q14 14 18 17Q15 22 11 21ZM46 17Q50 14 53 17Q53 21 49 22Z");
const ManateeIcon = silhouette("M4 33Q4 24 14 22Q28 18 42 24Q48 27 50 31Q56 26 61 30Q64 37 58 41Q53 44 49 39Q44 44 34 45Q30 52 24 52Q26 48 26 45Q14 44 8 40Q4 37 4 33ZM11 29a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M4 33Q7 36 11 35");
const PlatypusIcon = silhouette("M2 32Q2 26 8 26L16 28Q20 20 30 20Q40 20 44 26L58 26Q63 26 63 32Q63 38 58 38L44 38Q40 44 30 44Q20 44 16 36L8 38Q2 38 2 32ZM18 27a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0M18 37a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0M22 22L16 14L22 13L26 20ZM22 42L16 50L22 51L26 44ZM38 21L42 13L47 16L42 23ZM38 43L42 51L47 48L42 41Z");
const BeardedDragonIcon = silhouette("M4 28L14 22L22 23L28 28L38 27L48 29L58 24L63 26L52 36L40 38L38 44L43 48L39 50L33 44L24 44L20 50L16 48L18 42L14 40L12 44L9 40L10 36L7 35L9 32L4 31ZM13 26a1.4 1.4 0 1 0 2.8 0a1.4 1.4 0 1 0-2.8 0M26 27L25 24L29 26L31 22L33 26L36 22L38 26L41 23L42 27Z");
const BoxJellyfishIcon = silhouette("M14 30Q14 10 32 10Q50 10 50 30L50 32H14ZM16 32V58L19 54V32M25 32V60L28 56V32M36 32V60L39 56V32M45 32V58L48 54V32M22 22a2 2 0 1 0 4 0a2 2 0 1 0-4 0");
const RaccoonIcon = silhouette("M8 8Q16 8 20 16Q26 14 32 14Q38 14 44 16Q48 8 56 8Q58 18 54 24Q62 32 56 42Q48 54 32 56Q16 54 8 42Q2 32 10 24Q6 18 8 8ZM11 30Q20 22 28 30Q30 35 26 37Q18 38 11 34ZM53 30Q44 22 36 30Q34 35 38 37Q46 38 53 34ZM18 31a2 2 0 1 0 4 0a2 2 0 1 0-4 0M42 31a2 2 0 1 0 4 0a2 2 0 1 0-4 0M28 44Q32 41 36 44L32 48Z");
const PrayingMantisIcon = silhouette("M4 47Q12 36 33 37L36 44Q16 51 4 47ZM32 39L42 13L46 14L37 42ZM39 7L55 7L46 16ZM42 19L55 22L53 35L49 34L50 26L41 24ZM29 42L23 57L20 56L25 42ZM34 43L39 58L36 59L31 44ZM18 44L10 56L7 55L15 44Z", "M44 9a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0");
const PolarBearIcon = silhouette("M50 24L52 20L56 22Q62 25 62 30L58 32L52 32L46 36L46 50H40L38 40L26 42L26 52H20L18 42Q10 40 8 32Q8 22 22 20Q36 18 46 22ZM55 25a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0");
const CassowaryIcon = silhouette("M27 13Q25 2 31 1Q35 1 36 6L38 12L46 15L38 17L38 26Q52 28 54 40Q54 50 44 54L40 54L42 62H37L35 54H29L28 62H23L25 54Q12 50 12 40Q14 28 28 26L28 16Z", "M32 12a1.4 1.4 0 1 0 2.8 0a1.4 1.4 0 1 0-2.8 0M27 10L35 9L35 10L27 11Z");
const SkunkIcon = silhouette("M8 44Q6 34 16 32Q24 30 30 36Q34 20 44 12Q56 6 60 18Q62 30 52 38Q46 44 40 46L40 52H35L34 47L20 48L20 54H15L14 47Q9 47 8 44ZM12 36a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0M33 36Q38 24 48 18Q55 16 56 22Q50 22 44 26Q39 32 37 38Z");
const EmperorPenguinIcon = silhouette("M28 6Q36 4 39 12L40 18L44 20L40 22Q48 30 48 44Q48 54 42 58H22Q16 54 16 44Q16 28 24 20Q22 10 28 6ZM33 11a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M28 24Q22 34 23 46Q24 54 30 55Q38 54 39 46Q40 34 34 24ZM16 38L8 50L16 46ZM22 58L18 62H28L26 58ZM38 58L36 62H46L42 58Z");
const RavenSilhouetteIcon = silhouette("M14 18Q20 10 28 12L36 16Q46 20 50 30L60 44L54 46L58 52L48 50L42 46L36 48L34 56L30 56L31 47L26 48L25 56L21 56L23 46Q18 40 20 30L14 26L4 26L12 21ZM20 18a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0");
const ChipmunkIcon = silhouette("M30 10Q38 8 42 14L46 18L42 20Q46 26 44 32Q50 38 48 46L52 56H24L26 48Q18 44 18 34Q18 24 26 22Q24 14 30 10ZM34 13a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M36 21Q44 24 40 29Q34 30 34 24ZM24 30Q26 40 32 46L30 47Q24 42 22 32ZM18 46Q6 44 6 30Q6 20 14 18Q10 26 12 34Q14 42 22 44Z");
const HareIcon = silhouette("M40 16L36 2L41 2L44 14L48 2L52 4L48 17Q54 22 52 28L58 30L54 33L46 34L48 50L56 54L54 58L42 56L40 46L28 50L20 58H10L18 50Q8 46 8 36Q10 26 24 26Q34 26 40 22ZM46 23a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0M8 34Q2 32 4 28Q8 28 10 31Z");
const SwordfishIcon = silhouette("M2 31L24 29Q30 24 38 24L42 14L46 25Q52 27 54 30L60 20L62 22L58 32L62 42L60 44L54 34Q50 38 42 39L38 44L36 39Q28 38 24 34L2 32ZM29 30a1.4 1.4 0 1 0 2.8 0a1.4 1.4 0 1 0-2.8 0M36 37L40 46L34 40Z");
const ConeSnailIcon = silhouette("M8 57L38 13Q48 4 58 12Q63 22 53 30ZM22 42L29 34L32 38L26 45ZM33 28L40 21L43 25L36 32ZM47 13Q54 11 56 17Q51 15 47 17ZM16 50L19 47L21 49L18 52Z");
const CrocodileIcon = silhouette("M2 33L14 30L20 31L24 27L28 30L32 26L36 30L40 26L44 30L48 28L56 32L62 36L56 37L48 38L46 44L40 44L40 39L30 39L28 45L22 45L22 39L14 38L2 36ZM12 31a1.3 1.3 0 1 0 2.6 0a1.3 1.3 0 1 0-2.6 0M3 34L13 34L13 35L3 35Z");
const BullSharkIcon = silhouette("M4 34Q10 26 24 26L30 16L36 26Q46 27 52 31L60 22L62 24L58 34L62 44L60 46L52 37Q46 42 34 42L30 48L26 42Q12 42 4 34ZM12 31a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M12 37Q16 38 20 36");
const DolphinIcon = silhouette("M8 40Q10 24 26 18L28 10L34 17Q46 18 54 26L60 22L58 30L62 36L56 34Q50 30 42 30L38 38L34 30Q24 32 18 42L22 46L14 46L10 50ZM12 42Q8 42 6 44L10 38ZM22 25a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0");
const FireflySquidIcon = silhouette("M32 2Q42 10 40 26L40 34L46 52L42 52L38 40L36 58L33 58L33 40L31 40L31 58L28 58L26 40L22 52L18 52L24 34L24 26Q22 10 32 2ZM29 14a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M33 20a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M28 24a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M33 29a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0M27 31a1.6 1.6 0 1 0 3.2 0a1.6 1.6 0 1 0-3.2 0");

export const CHARACTER_ICONS: Record<string, (p: IconProps) => ReactElement> = {
  "WF075": PlanarianIcon,
  "MYST002": AbundantWatersIcon,
  "AVX077": HerculesBeetleIcon,
  "DRS022": MarmosetIcon,
  "XMF028": FireAntIcon,
  "IG058": ElectricRayIcon,
  "AVX066": VelvetWormIcon,
  "AVX019": LeafInsectIcon,
  "XFO004": OrchidMantisIcon,
  "XFO016": BowerbirdIcon,
  "XFC017": FireSalamanderIcon,
  "XFC110": MimicFilefishIcon,
  "SW018": HarrisHawkIcon,
  "IG003": SpringEmergenceIcon,
  "JUS069": ThornShotIcon,
  "JUS068": ForagingIcon,
  "MSW019": AxolotlIcon,
  "MSW020": PuffinIcon,
  "GOTG005": TrapdoorSpiderIcon,
  "JLL002": StagBeetleIcon,
  "MSW027": WeaverbirdIcon,
  "GOTG105": SugarGliderIcon,
  "TAG003": PrairieDogIcon,
  "SKC090": SpittingCobraIcon,
  "CW014": PufferfishIcon,
  "GOTG008": SeasonalRenewalIcon,
  "DPS029": DeathstalkerScorpionIcon,
  "DPS081": GeladaIcon,
  "DPS086": LionfishIcon,
  "DPS063": GiantTortoiseIcon,
  "DPS114": MandrillIcon,
  "DPS047": NutcrackerIcon,
  "DPS062": AfricanWildDogIcon,
  "DPS019": HorseshoeCrabIcon,
  "DPS059": KangarooRatIcon,
  "DPS071": OrbWeaverIcon,
  "DPS004": GrowthSpurtIcon,
  "DPS115": VampireSquidIcon,
  "DPS057": GibbonIcon,
  "DPS102": LionessIcon,
  "DPS003": WildfireIcon,
  "DPS111": MimicOctopusIcon,
  "DPS119": RedSquirrelIcon,
  "DPS142": WombatIcon,
  "DPS043": AssassinBugIcon,
  "DPS033": FrigatebirdIcon,
  "DPS045": CoyoteIcon,
  "DPS027": TasmanianDevilIcon,
  "DPS028": AyeAyeIcon,
  "DPS056": CaracalIcon,
  "DPS031": WoodpeckerIcon,
  "DPS053": AntlionIcon,
  "DPS038": SecretaryBirdIcon,
  "DPS044": MockingbirdIcon,
  "DPS078": PorcupineIcon,
  "DPS077": LeafTailedGeckoIcon,
  "DPS073": SableAntelopeIcon,
  "DPS061": WildBoarIcon,
  "DPS064": BonoboIcon,
  "DPS095": KingCobraIcon,
  "DPS066": SeaCucumberIcon,
  "DPS060": FruitBatIcon,
  "DPS072": CuckooIcon,
  "DPS093": GiantCentipedeIcon,
  "DPS069": HarpyEagleIcon,
  "DPS107": PorbeagleIcon,
  "DPS148": BisonIcon,
  "DPS145": MosquitoIcon,
  "DPS152": StarfishIcon,
  "DPS005": AdaptationIcon,
  "DPS006": PackInstinctIcon,
  "DPS010": ProtectiveBarkIcon,
  "DPS015": StampedeIcon,
  "DPS097": CleanerWrasseIcon,
  "DPS099": DungBeetleIcon,
  "DPS100": PeacockMantisShrimpIcon,
  "DPS116": FireflySquidIcon,
  "DPS113": SaigaIcon,
  "DPS118": ArcticTernIcon,
  "DPS147": QueenBeeIcon,
  "DPS139": WaspIcon,
  "DK-ACT-01": ThistleIcon,
  "DK-ACT-02": FlowerIcon,
  "DK-ACT-03": SeedlingIcon,
  "DK-ACT-04": MushroomIcon,
  "DK-ACT-05": VenusFlytrapIcon,
  "DK-CLAW-01": HoneyBadgerIcon,
  "DK-CLAW-02": WolverineIcon,
  "DK-CLAW-03": GrizzlyBearIcon,
  "DK-CLAW-04": OrcaIcon,
  "DK-CLAW-05": PeregrineFalconIcon,
  "DK-CLAW-06": TigerIcon,
  "DK-CLAW-07": StoatIcon,
  "DK-CLAW-08": CapeBuffaloIcon,
  "DK-CLAW-09": MongooseIcon,
  "DK-CLAW-10": ElectricEelIcon,
  "DK-CLAW-11": ArmyAntIcon,
  "DK-CLAW-12": KomodoDragonIcon,
  "DK-SHELL-01": HippopotamusIcon,
  "DK-SHELL-02": MuskOxIcon,
  "DK-SHELL-03": PangolinIcon,
  "DK-SHELL-04": HermitCrabIcon,
  "DK-SHELL-05": OpossumIcon,
  "DK-SHELL-06": QueenTermiteIcon,
  "DK-SHELL-07": SnappingTurtleIcon,
  "DK-SHELL-08": BoxTurtleIcon,
  "DK-SHELL-09": LeechIcon,
  "DK-SHELL-10": MeerkatIcon,
  "DK-SHELL-11": BombardierBeetleIcon,
  "DK-WING-01": OspreyIcon,
  "DK-WING-02": BarnSwallowIcon,
  "DK-WING-03": HummingbirdIcon,
  "DK-WING-04": MountainGoatIcon,
  "DK-WING-05": MonarchButterflyIcon,
  "DK-WING-06": HomingPigeonIcon,
  "DK-WING-07": GreyhoundIcon,
  "DK-WING-08": AlbatrossIcon,
  "DK-WING-09": SwiftIcon,
  "DK-WING-10": FlyingSquirrelIcon,
  "DK-WING-11": FireflyIcon,
  "DK-WING-12": ArcherfishIcon,
  "DK-EYE-01": BarnOwlIcon,
  "DK-EYE-02": HyenaIcon,
  "DK-EYE-03": AnglerfishIcon,
  "DK-EYE-04": CowbirdIcon,
  "DK-EYE-05": MagpieIcon,
  "DK-EYE-06": RavenIcon,
  "DK-EYE-07": ElephantIcon,
  "DK-EYE-08": FoxIcon,
  "DK-EYE-09": CuttlefishIcon,
  "DK-EYE-10": FrilledLizardIcon,
  "DK-EYE-11": ChameleonIcon,
  "XFC003": PistolShrimpIcon,
  "BFF033": SnowLeopardIcon,
  "CW054": RedPandaIcon,
  "AI013": KoalaIcon,
  "HQ019": ManateeIcon,
  "DOOM017": PlatypusIcon,
  "SWW020": BeardedDragonIcon,
  "SW001": BoxJellyfishIcon,
  "GOTG032": RaccoonIcon,
  "WWE037": PrayingMantisIcon,
  "WOL071": PolarBearIcon,
  "DOOM013": CassowaryIcon,
  "SKC113": SkunkIcon,
  "WWE033": EmperorPenguinIcon,
  "AI031": RavenSilhouetteIcon,
  "GOTG021": ChipmunkIcon,
  "IG068": HareIcon,
  "ASM110": SwordfishIcon,
  "FUS004": ConeSnailIcon,
  "TOA086": CrocodileIcon,
  "THOR021": BullSharkIcon,
  "CW042": DolphinIcon,
};
