// The launch page's small art (MobileHome.tsx), shared with the side pages
// since 2026-11-11 ("Can we give the other screens (packs, boss ladder, etc)
// a similar glow up?"): the dusk sky, the crown, the coin, the foil pack. The
// styles are in mobileHome.css, under `:is(.dk-mobile, .dkp)`.
import type { CSSProperties, ReactNode } from "react";
import { paintingSrc } from "./cardArt";

// ---- Small line icons (24 grid, currentColor) ----

export function Line({ size = 22, children }: { size?: number; children: ReactNode }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  );
}
export const BackIcon = () => <Line><path d="M14.5 5 7.5 12l7 7" /></Line>;
export const CheckIcon = ({ size = 14 }: { size?: number }) => <Line size={size}><path d="M5 12.5 9.5 17 19 7.5" /></Line>;
export const LockIcon = ({ size = 16 }: { size?: number }) => (
  <Line size={size}>
    <rect x="5" y="10.5" width="14" height="10" rx="2.5" />
    <path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5" />
  </Line>
);
export const BuilderIcon = ({ size }: { size?: number }) => (
  <Line size={size}>
    <rect x="3.5" y="3.5" width="7" height="7" rx="2" />
    <rect x="13.5" y="3.5" width="7" height="7" rx="2" />
    <rect x="3.5" y="13.5" width="7" height="7" rx="2" />
    <path d="M17 14v6M14 17h6" />
  </Line>
);
export const PawIcon = ({ size = 18 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <ellipse cx="6" cy="10.5" rx="2" ry="2.6" />
    <ellipse cx="10" cy="6.5" rx="2" ry="2.7" />
    <ellipse cx="14.5" cy="6.5" rx="2" ry="2.7" />
    <ellipse cx="18.3" cy="10.5" rx="2" ry="2.6" />
    <path d="M12.2 11.5c-3 0-5.7 3.3-5.7 5.8 0 1.6 1.2 2.5 2.7 2.5 1.2 0 2-.6 3-.6s1.8.6 3 .6c1.5 0 2.7-.9 2.7-2.5 0-2.5-2.7-5.8-5.7-5.8z" />
  </svg>
);
export const CoinIcon = ({ size = 18 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
    <circle cx="12" cy="12" r="10" fill="currentColor" />
    <circle cx="12" cy="12" r="6.6" fill="none" stroke="rgba(0,0,0,0.28)" strokeWidth="1.6" />
    <path d="M12 8.3l1.1 2.3 2.5.3-1.8 1.7.5 2.5-2.3-1.2-2.3 1.2.5-2.5-1.8-1.7 2.5-.3z" fill="rgba(0,0,0,0.3)" />
  </svg>
);

/** An energy's colour as `--tone`, for a tile that takes its tint from one. */
export const tone = (energy: string): CSSProperties => ({ ["--tone" as string]: `var(--${energy.toLowerCase()})` });

/** The dusk behind a hero band: stars, and rays turning behind whatever stands on it. */
export function Sky() {
  return (
    <div className="dkh-sky" aria-hidden="true">
      <span className="dkh-stars" />
      <span className="dkh-rays" />
    </div>
  );
}

/** A filled crown: on the logo's K, the boss tile, the ladder's rungs. */
export function Crown({ className }: { className?: string }) {
  return (
    <svg className={`dkh-crown${className ? ` ${className}` : ""}`} viewBox="-2 -2 48 36" aria-hidden="true">
      <path className="dkh-crown-body" d="M6 30 3 9l10 8L22 3l9 14 10-8-3 21z" />
      <path className="dkh-crown-band" d="M9 24.5h26" />
      <circle cx="3" cy="9" r="2.6" />
      <circle cx="22" cy="3" r="2.6" />
      <circle cx="41" cy="9" r="2.6" />
    </svg>
  );
}

// The card that stands for each boss - one from its team (the server's
// DiceKingdomBosses.cs). A boss not listed shows its first painted card.
export const BOSS_FACES: Record<string, string> = {
  "tag-jubilee": "DPS027", // Tasmanian Devil
  "xmen-colossus": "DK-EYE-06", // Basilisk
  sidekicks: "THOR036", // Moose
};

/** A foil pack with two cards on their way out of it. */
export function PackArt() {
  return (
    <span className="dkh-art dkh-art-pack" aria-hidden="true">
      <img className="is-a" src={paintingSrc("DK-WING-08")!} alt="" loading="lazy" />
      <img className="is-b" src={paintingSrc("CW054")!} alt="" loading="lazy" />
      <span className="dkh-pack"><PawIcon /></span>
      <i className="dkh-spark is-a" />
      <i className="dkh-spark is-b" />
      <i className="dkh-spark is-c" />
    </span>
  );
}
