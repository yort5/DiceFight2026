import type { ReactElement, ReactNode } from "react";
import wolfChampionArt from "./assets/wolf-champion.jpg";

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
      <path
        fill="currentColor"
        d="M8,54 C10,40 16,24 30,14 C38,9 46,7 56,8 C50,12 45,15 41,21 C46,20 51,20 55,23 C48,26 42,28 38,33 C44,33 49,35 52,38 C44,41 37,42 33,47 C29,51 23,53 18,55 C14,56 10,56 8,54 Z"
      />
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

// Surge (the Tardigrade die's sixth face) provides Wild energy - any of
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
export function TardigradeIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <ellipse cx={32} cy={34} rx={19} ry={13} fill="currentColor" />
      <g fill="currentColor">
        <circle cx={15} cy={24} r={4} />
        <circle cx={13} cy={38} r={4} />
        <circle cx={49} cy={24} r={4} />
        <circle cx={51} cy={38} r={4} />
      </g>
    </Svg>
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

function BarnSwallowIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12,40 C12,26 52,26 52,40 C52,42 12,42 12,40 Z" fill="currentColor" />
      <g fill={CREAM}>
        <circle cx={22} cy={36} r={1.6} />
        <circle cx={32} cy={34} r={1.6} />
        <circle cx={42} cy={36} r={1.6} />
      </g>
    </Svg>
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

export const CHARACTER_ICONS: Record<string, (p: IconProps) => ReactElement> = {
  "IC-CLAW-01": HoneyBadgerIcon,
  "IC-CLAW-02": WolverineIcon,
  "IC-SHELL-01": HippopotamusIcon,
  "IC-SHELL-02": MuskOxIcon,
  "IC-WING-01": OspreyIcon,
  "IC-WING-02": BarnSwallowIcon,
  "IC-EYE-01": BarnOwlIcon,
  "IC-EYE-02": HyenaIcon,
};
