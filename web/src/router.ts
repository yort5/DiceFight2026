import { useSyncExternalStore } from "react";

// A hand-rolled router, deliberately - package.json has zero dependencies
// beyond react/react-dom, and this app only needs a few flat routes. Program.
// cs already falls back to index.html for any unrecognized path (so a hard
// refresh on /teambuilder works server-side), and Vite's dev server does
// the same by default - this only needs to read the current path and react
// to browser back/forward.
export type Route =
  | "/game"
  | "/teambuilder"
  | "/dice-kingdom"
  | "/dice-kingdom/mobile"
  | "/dice-kingdom/account"
  | "/dice-kingdom/packs"
  | "/dice-kingdom/art-test"
  | "/dice-kingdom/how-to-play";

function normalize(pathname: string): Route {
  // "/" and anything unrecognized fall back to /game - preserves today's
  // existing behavior/bookmarks (the site was just "/" before this).
  if (pathname === "/teambuilder") return "/teambuilder";
  // Mobile refresh (2026-09) - a genuinely separate route/UI, not a
  // responsive breakpoint of /dice-kingdom (see DiceKingdomMobilePage.tsx's
  // own remarks: two completely separate front ends sharing the same
  // engine, by design, for now).
  if (pathname === "/dice-kingdom/mobile") return "/dice-kingdom/mobile";
  if (pathname === "/dice-kingdom") return "/dice-kingdom";
  // Your player: name, games, devices (dicekingdom/AccountPage.tsx).
  if (pathname === "/dice-kingdom/account") return "/dice-kingdom/account";
  // Coins, random packs and your collection (dicekingdom/PacksPage.tsx).
  if (pathname === "/dice-kingdom/packs") return "/dice-kingdom/packs";
  // Candidate art styles side by side (dicekingdom/ArtTestPage.tsx), unlinked.
  if (pathname === "/dice-kingdom/art-test") return "/dice-kingdom/art-test";
  // The basic rules (dicekingdom/HowToPlayPage.tsx).
  if (pathname === "/dice-kingdom/how-to-play") return "/dice-kingdom/how-to-play";
  return "/game";
}

function subscribe(callback: () => void) {
  window.addEventListener("popstate", callback);
  return () => window.removeEventListener("popstate", callback);
}

export function useRoute(): Route {
  return useSyncExternalStore(subscribe, () => normalize(window.location.pathname));
}

export function navigate(path: Route, search = "") {
  if (window.location.pathname === path && !search) return;
  window.history.pushState(null, "", path + search);
  // pushState doesn't fire popstate itself - useRoute's useSyncExternalStore
  // subscription needs this nudge to notice the URL changed.
  window.dispatchEvent(new PopStateEvent("popstate"));
}
