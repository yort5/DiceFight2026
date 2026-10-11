import { useLayoutEffect, useSyncExternalStore } from "react";

export type Theme = "system" | "light" | "dark";
const KEY = "theme";
const listeners = new Set<() => void>();

function readStoredTheme(): Theme {
  try {
    const stored = localStorage.getItem(KEY);
    return stored === "light" || stored === "dark" ? stored : "system";
  } catch {
    return "system";
  }
}
let theme = readStoredTheme();
const systemIsDark = () => window.matchMedia?.("(prefers-color-scheme: dark)").matches ?? false;
const isDark = (value: Theme) => value === "dark" || (value === "system" && systemIsDark());

function applyTheme(value: Theme) {
  document.documentElement.setAttribute("data-theme", isDark(value) ? "dark" : "light");
}
function notify() { listeners.forEach(listener => listener()); }

function onStorage(event: StorageEvent) {
  if (event.key !== KEY && event.key !== null) return;
  theme = readStoredTheme();
  applyTheme(theme);
  notify();
}
function subscribe(listener: () => void) {
  if (listeners.size === 0) window.addEventListener("storage", onStorage);
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) window.removeEventListener("storage", onStorage);
  };
}
function subscribeSystem(listener: () => void) {
  const query = window.matchMedia?.("(prefers-color-scheme: dark)");
  query?.addEventListener("change", listener);
  return () => query?.removeEventListener("change", listener);
}

export function setTheme(next: Theme) {
  theme = next;
  try {
    if (next === "system") localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, next);
  } catch {
    // The current session still works when browser storage is unavailable.
  }
  applyTheme(next);
  notify();
}

export function useIsDark(value: Theme): boolean {
  const systemDark = useSyncExternalStore(subscribeSystem, systemIsDark, () => false);
  return value === "dark" || (value === "system" && systemDark);
}

// One shared snapshot keeps every menu and the existing game settings in sync.
export function useTheme(): [Theme, (next: Theme) => void] {
  const current = useSyncExternalStore(subscribe, () => theme, () => "system" as Theme);
  const dark = useIsDark(current);
  useLayoutEffect(() => {
    document.documentElement.setAttribute("data-theme", dark ? "dark" : "light");
  }, [dark]);
  return [current, setTheme];
}
