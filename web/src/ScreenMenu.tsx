import { useEffect, useId, useRef, useState } from "react";
import { navigate, type Route } from "./router";
import { useTeamBuilderLayout } from "./teamBuilderLayout";
import { useIsDark, useTheme } from "./theme";
import "./ScreenMenu.css";

export function ScreenMenu({ screen, startRoute, onStartGame }: {
  screen: "start" | "builder" | "game";
  startRoute: Route;
  onStartGame?: () => void;
}) {
  const [open, setOpen] = useState(false);
  // Back from How to Play returns to the mobile page if that's where you were.
  const howToPlaySearch = startRoute === "/dice-kingdom/mobile" ? "?from=mobile" : "";
  const howToPlay = "/dice-kingdom/how-to-play" + howToPlaySearch;
  const { layout, setLayout } = useTeamBuilderLayout();
  const [theme, setTheme] = useTheme();
  const dark = useIsDark(theme);
  const id = useId();
  const ref = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const dismiss = (e: PointerEvent) => {
      if (e.target instanceof Node && !ref.current?.contains(e.target)) setOpen(false);
    };
    const escape = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      setOpen(false);
      buttonRef.current?.focus();
    };
    document.addEventListener("pointerdown", dismiss);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("pointerdown", dismiss);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);
  return (
    <div className="screen-menu" ref={ref} onBlur={(e) => {
      if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false);
    }}>
      <button type="button" className="screen-menu-button" ref={buttonRef}
        aria-label="Open navigation menu" aria-expanded={open} aria-controls={id}
        onClick={() => setOpen((old) => !old)}>
        <svg width="22" height="22" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M4 6h16M4 12h16M4 18h16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
        </svg>
      </button>
      {open && <nav id={id} className="screen-menu-panel" aria-label="Screen navigation">
        <a href={startRoute} aria-current={screen === "start" ? "page" : undefined}
          onClick={(e) => { if (e.button === 0 && !e.metaKey && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); setOpen(false); onStartGame?.(); navigate(startRoute); } }}>Start a Game</a>
        <a href="/teambuilder" aria-current={screen === "builder" ? "page" : undefined}
          onClick={(e) => { if (e.button === 0 && !e.metaKey && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); setOpen(false); navigate("/teambuilder"); } }}>Team Builder</a>
        {/* Mid-game it opens in a new tab, so the game stays where it is. */}
        {screen === "game"
          ? <a href="/dice-kingdom/how-to-play" target="_blank" rel="noopener" onClick={() => setOpen(false)}>How to Play ↗</a>
          : <a href={howToPlay}
            onClick={(e) => { if (e.button === 0 && !e.metaKey && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); setOpen(false); navigate("/dice-kingdom/how-to-play", howToPlaySearch); } }}>How to Play</a>}
        <div className="screen-menu-layout">
          <button type="button" role="switch" aria-checked={dark} aria-label="Dark mode"
            className="screen-menu-theme" onClick={() => setTheme(dark ? "light" : "dark")}>
            <span>Dark mode</span>
            <span className="screen-menu-switch" aria-hidden="true"><span /></span>
          </button>
        </div>
        <div className="screen-menu-layout">
          <span>Team Builder layout: <strong>{layout === "mobile" ? "Mobile" : "Desktop"}</strong></span>
          <button type="button" onClick={() => setLayout(layout === "mobile" ? "desktop" : "mobile")}>
            Use {layout === "mobile" ? "desktop" : "mobile"} layout
          </button>
        </div>
      </nav>}
    </div>
  );
}
