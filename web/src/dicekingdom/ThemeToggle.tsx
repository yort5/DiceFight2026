import { useEffect, useRef, useState } from "react";
import { SettingsIcon } from "./icons";

import type { Theme } from "../theme";
export type { Theme } from "../theme";
export { useTheme } from "../theme";

function effectiveIsDark(theme: Theme): boolean {
  if (theme === "dark") return true;
  if (theme === "light") return false;
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false;
}

export function ThemeToggle({ theme, setTheme }: { theme: Theme; setTheme: (t: Theme) => void }) {
  const isDark = effectiveIsDark(theme);
  return (
    <button
      type="button"
      className="theme-toggle"
      onClick={() => setTheme(isDark ? "light" : "dark")}
      title="Switch between light and dark"
    >
      {isDark ? "Dark mode" : "Light mode"}
    </button>
  );
}

// The live board's compact stand-in for ThemeToggle above - direct
// feedback (2026-09-08): "'dark mode' and 'how to play' cause a lot of
// vertical space to be taken up... a gear for settings (in which they
// can set dark mode)." A single icon button opening a small popover,
// not a text pill, so it can sit inline with the step ribbon instead of
// wrapping its own row. Only one setting exists today (theme) - this
// is still a menu, not a direct toggle, so a second setting has
// somewhere to go without a redesign. ThemeToggle itself is untouched
// and still used as-is on the pre-game setup screen, which has no
// ribbon row to share space with.
export function SettingsMenu({ theme, setTheme }: { theme: Theme; setTheme: (t: Theme) => void }) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    function handlePointerDown(e: PointerEvent) {
      if (!(e.target instanceof Node) || !wrapRef.current?.contains(e.target)) setOpen(false);
    }
    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [open]);
  const isDark = effectiveIsDark(theme);
  return (
    <div ref={wrapRef} className="icon-menu-wrap">
      <button type="button" className="icon-btn" aria-label="Settings" onClick={() => setOpen((o) => !o)}>
        <SettingsIcon size={16} />
      </button>
      {open && (
        <div className="icon-menu-popover">
          <div className="icon-menu-row">
            <span>Theme</span>
            <button type="button" className="theme-toggle" onClick={() => setTheme(isDark ? "light" : "dark")}>
              {isDark ? "Dark" : "Light"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
