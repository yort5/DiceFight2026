import { useSyncExternalStore } from "react";

export type BuilderLayout = "mobile" | "desktop";
const LAYOUT_KEY = "dicefight.teamBuilder.layout";
const LAYOUT_EVENT = "team-builder-layout-change";
const MOBILE_QUERY = "(max-width: 900px)";
let memoryPreference: BuilderLayout | null = null;

function deviceLayout(): BuilderLayout {
  return window.matchMedia(MOBILE_QUERY).matches ? "mobile" : "desktop";
}

function preference(): BuilderLayout | null {
  try {
    const stored = window.localStorage.getItem(LAYOUT_KEY);
    return stored === "mobile" || stored === "desktop" ? stored : memoryPreference;
  } catch {
    return memoryPreference;
  }
}

function subscribe(callback: () => void) {
  const media = window.matchMedia(MOBILE_QUERY);
  media.addEventListener("change", callback);
  window.addEventListener("storage", callback);
  window.addEventListener(LAYOUT_EVENT, callback);
  return () => {
    media.removeEventListener("change", callback);
    window.removeEventListener("storage", callback);
    window.removeEventListener(LAYOUT_EVENT, callback);
  };
}

/** Presentation preference only; it never changes team data or game routes. */
export function useTeamBuilderLayout() {
  const layout = useSyncExternalStore(subscribe, () => preference() ?? deviceLayout(), () => "desktop" as const);
  const setLayout = (next: BuilderLayout) => {
    memoryPreference = next;
    try { window.localStorage.setItem(LAYOUT_KEY, next); } catch { /* Keep the session preference when storage is unavailable. */ }
    window.dispatchEvent(new Event(LAYOUT_EVENT));
  };
  return { layout, setLayout };
}
