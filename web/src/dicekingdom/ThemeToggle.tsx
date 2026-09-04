import { useEffect, useState } from "react";

// The site otherwise only ever followed the OS's prefers-color-scheme,
// with no way to override it - real feedback: "at the very least can we
// go with the dark theme? ... this light, faded stuff is horrible for
// people who don't do well with colors." An explicit choice, stored so
// it survives a reload, wins over the OS setting either direction (see
// index.css's `:root[data-theme]` rules) - "system" (no attribute) is
// the third state, used only before the visitor has ever chosen.
export type Theme = "system" | "light" | "dark";
const KEY = "theme";

function effectiveIsDark(theme: Theme): boolean {
  if (theme === "dark") return true;
  if (theme === "light") return false;
  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false;
}

// Applies and persists the choice - called unconditionally at the top of
// DiceKingdomPage (not just from within the live-game view) so a stored
// preference re-applies on the pre-game/setup screen too. A component
// nested only inside the live view would apply the attribute only once
// a game exists, then lose it again on the next full reload straight
// back to setup - the actual bug this session hit first.
export function useTheme(): [Theme, (t: Theme) => void] {
  const [theme, setTheme] = useState<Theme>(() => {
    try {
      const stored = localStorage.getItem(KEY);
      return stored === "light" || stored === "dark" ? stored : "system";
    } catch {
      return "system";
    }
  });

  useEffect(() => {
    if (theme === "system") document.documentElement.removeAttribute("data-theme");
    else document.documentElement.setAttribute("data-theme", theme);
    try {
      if (theme === "system") localStorage.removeItem(KEY);
      else localStorage.setItem(KEY, theme);
    } catch {
      // Storage blocked - the choice still applies for this page load.
    }
  }, [theme]);

  return [theme, setTheme];
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
