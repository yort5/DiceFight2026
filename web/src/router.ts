import { useSyncExternalStore } from "react";

// A hand-rolled router, deliberately - package.json has zero dependencies
// beyond react/react-dom, and this app only needs a few flat routes. Program.
// cs already falls back to index.html for any unrecognized path (so a hard
// refresh on /teambuilder works server-side), and Vite's dev server does
// the same by default - this only needs to read the current path and react
// to browser back/forward.
export type Route = "/game" | "/teambuilder" | "/instinct-clash";

function normalize(pathname: string): Route {
  // "/" and anything unrecognized fall back to /game - preserves today's
  // existing behavior/bookmarks (the site was just "/" before this).
  if (pathname === "/teambuilder") return "/teambuilder";
  if (pathname === "/instinct-clash") return "/instinct-clash";
  return "/game";
}

function subscribe(callback: () => void) {
  window.addEventListener("popstate", callback);
  return () => window.removeEventListener("popstate", callback);
}

export function useRoute(): Route {
  return useSyncExternalStore(subscribe, () => normalize(window.location.pathname));
}

export function navigate(path: Route) {
  if (window.location.pathname === path) return;
  window.history.pushState(null, "", path);
  // pushState doesn't fire popstate itself - useRoute's useSyncExternalStore
  // subscription needs this nudge to notice the URL changed.
  window.dispatchEvent(new PopStateEvent("popstate"));
}
