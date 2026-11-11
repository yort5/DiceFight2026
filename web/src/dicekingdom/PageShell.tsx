// The frame the side pages share (2026-11-11, user, after the launch page's
// redesign: "it looks more like a real game now. Can we give the other
// screens (packs, boss ladder, etc) a similar glow up?"): a dusk bar that
// stays on screen - the installed app has no browser back button, so this is
// the only way out - over the launch page's sky with the page's name on it.
// kingdomPages.css styles it; the sky and the small art are the launch
// page's own (kingdomArt.tsx, mobileHome.css).
import type { ReactNode } from "react";
import { navigate } from "../router";
import { useIsDark, useTheme } from "../theme";
import { BackIcon, Line, Sky } from "./kingdomArt";
import { setSoundMuted, useSoundMuted } from "./sound";
import "./mobileHome.css";
import "./kingdomPages.css";

/** Where a side page's way back leads: the start screen it was opened from. */
export function backPath(): "/dice-kingdom" | "/dice-kingdom/mobile" {
  return new URLSearchParams(window.location.search).get("from") === "mobile" ? "/dice-kingdom/mobile" : "/dice-kingdom";
}

const SunIcon = () => (
  <Line size={20}>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2.5v2.2M12 19.3v2.2M2.5 12h2.2M19.3 12h2.2M5.3 5.3l1.5 1.5M17.2 17.2l1.5 1.5M5.3 18.7l1.5-1.5M17.2 6.8l1.5-1.5" />
  </Line>
);
const MoonIcon = () => <Line size={20}><path d="M20 14.2A8.3 8.3 0 0 1 9.8 4a8.3 8.3 0 1 0 10.2 10.2z" /></Line>;
const SoundIcon = ({ muted }: { muted: boolean }) => (
  <Line size={20}>
    <path d="M4 9.5v5h3.5L12 18.5v-13L7.5 9.5z" />
    {muted ? <path d="M16 9.5l5 5M21 9.5l-5 5" /> : <path d="M15.5 9a4.2 4.2 0 0 1 0 6M18.2 6.5a8 8 0 0 1 0 11" />}
  </Line>
);

function ThemeButton() {
  const [theme, setTheme] = useTheme();
  const isDark = useIsDark(theme);
  return (
    <button type="button" className="dkp-round" aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
      title="Switch between light and dark" onClick={() => setTheme(isDark ? "light" : "dark")}>
      {isDark ? <MoonIcon /> : <SunIcon />}
    </button>
  );
}

/** For the pages that make noise (Packs): the bar's sound switch. */
export function SoundButton() {
  const muted = useSoundMuted();
  return (
    <button type="button" className={`dkp-round${muted ? " is-off" : ""}`} aria-pressed={!muted}
      aria-label="Sound" title={muted ? "Sound is off" : "Sound is on"} onClick={() => setSoundMuted(!muted)}>
      <SoundIcon muted={muted} />
    </button>
  );
}

/**
 * A side page: `tools` sit in the bar beside the theme switch, `art` hangs
 * off the hero's right edge, `hero` goes under the title on the sky.
 */
export function KingdomPage({ className, title, dek, tools, art, hero, children }: {
  className?: string;
  title: string;
  dek?: ReactNode;
  tools?: ReactNode;
  art?: ReactNode;
  hero?: ReactNode;
  children: ReactNode;
}) {
  const back = backPath();
  return (
    <div className={`dicekingdom dk-account dkp${className ? ` ${className}` : ""}`}>
      <header className="dkp-bar">
        <a className="dkp-round" href={back} aria-label="Back to Dice Kingdom"
          onClick={(e) => { e.preventDefault(); navigate(back); }}>
          <BackIcon />
        </a>
        <span className="dkp-bar-title" aria-hidden="true">{title}</span>
        <div className="dkp-bar-tools">
          {tools}
          <ThemeButton />
        </div>
      </header>
      <div className={`dkh-hero is-compact dkp-hero${art ? " has-art" : ""}`}>
        <Sky />
        {art}
        <div className="dkp-hero-in">
          <h1 className="dkp-title" data-text={title}>{title}</h1>
          {dek && <p className="dkp-dek">{dek}</p>}
          {hero}
        </div>
      </div>
      {children}
    </div>
  );
}
