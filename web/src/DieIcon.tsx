import type { ReactNode } from "react";

// A small icon per die face, loosely matching the real dice's printed
// symbols (bolt/shield/mask/fist energy faces, a pawn for a Sidekick's
// character face, "!" for an Action face). Plain inline SVG rather than
// emoji glyphs - this sandbox's headless-Chromium build has no color
// emoji font (renders tofu boxes), and SVG guarantees the same look
// everywhere regardless of what fonts happen to be installed.
export type IconKind = "Fist" | "Bolt" | "Shield" | "Mask" | "Wild" | "Generic" | "Pawn" | "Action";

function IconSvg(props: { children: ReactNode; size: number }) {
  return (
    <svg className="die-icon" viewBox="0 0 24 24" width={props.size} height={props.size} aria-hidden="true">
      {props.children}
    </svg>
  );
}

const ICON_SHAPES: Record<IconKind, ReactNode> = {
  Bolt: <polygon points="13,1 4,14 10,14 8,23 20,9 13,9" fill="currentColor" />,
  Shield: (
    <path
      d="M12 2 L20 5.5 V11 C20 16.5 16.5 20.5 12 22 C7.5 20.5 4 16.5 4 11 V5.5 Z"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
    />
  ),
  Mask: (
    <>
      <ellipse cx="6.5" cy="12" rx="4.5" ry="3.5" fill="none" stroke="currentColor" strokeWidth="2" />
      <ellipse cx="17.5" cy="12" rx="4.5" ry="3.5" fill="none" stroke="currentColor" strokeWidth="2" />
      <line x1="11" y1="11" x2="13" y2="11" stroke="currentColor" strokeWidth="2" />
    </>
  ),
  Fist: (
    <>
      <rect x="5" y="9" width="14" height="10" rx="4" fill="none" stroke="currentColor" strokeWidth="2" />
      <path d="M4 13 C1.5 13 1.5 17 4 17" fill="none" stroke="currentColor" strokeWidth="2" />
      <line x1="9" y1="9" x2="9" y2="19" stroke="currentColor" strokeWidth="1.3" />
      <line x1="13" y1="9" x2="13" y2="19" stroke="currentColor" strokeWidth="1.3" />
    </>
  ),
  Wild: (
    <text x="12" y="18" fontSize="18" fontWeight="700" textAnchor="middle" fill="currentColor">
      ?
    </text>
  ),
  Generic: <polygon points="12,2 21,7 21,17 12,22 3,17 3,7" fill="none" stroke="currentColor" strokeWidth="2" />,
  Pawn: (
    <>
      <circle cx="12" cy="6" r="3.2" fill="currentColor" />
      <path d="M9 12 C9 9.5 15 9.5 15 12 L17 19 H7 Z" fill="currentColor" />
      <rect x="5.5" y="19" width="13" height="2.5" rx="1" fill="currentColor" />
    </>
  ),
  Action: (
    <text x="12" y="19" fontSize="19" fontWeight="700" textAnchor="middle" fill="currentColor">
      !
    </text>
  ),
};

export function DieIcon(props: { kind: IconKind | null; size?: number }) {
  if (!props.kind) return null;
  return <IconSvg size={props.size ?? 12}>{ICON_SHAPES[props.kind]}</IconSvg>;
}
