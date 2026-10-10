import { useEffect, useLayoutEffect, useRef, type KeyboardEvent, type RefObject } from "react";

const RESUME_AFTER_IDLE_MS = 5000;
const BOTTOM_TOLERANCE_PX = 3;

// Shared by the desktop log and the mobile expanded log. Scrolling back
// pauses tail-following; five seconds after the last interaction it resumes.
export function useLogAutoFollow(
  scrollRef: RefObject<HTMLElement | null>,
  newestSeq: number | undefined,
  enabled = true,
) {
  const pausedRef = useRef(false);
  const idleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const enabledRef = useRef(enabled);
  enabledRef.current = enabled;

  useLayoutEffect(() => {
    if (!enabled || pausedRef.current) return;
    const list = scrollRef.current;
    if (list) list.scrollTop = list.scrollHeight;
  }, [enabled, newestSeq, scrollRef]);

  useEffect(() => () => {
    if (idleTimerRef.current !== null) clearTimeout(idleTimerRef.current);
  }, []);

  function pauseFollowing() {
    if (!enabledRef.current) return;
    pausedRef.current = true;
    if (idleTimerRef.current !== null) clearTimeout(idleTimerRef.current);
    idleTimerRef.current = setTimeout(() => {
      idleTimerRef.current = null;
      pausedRef.current = false;
      if (enabledRef.current && scrollRef.current) {
        scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
      }
    }, RESUME_AFTER_IDLE_MS);
  }

  function onScroll() {
    const list = scrollRef.current;
    if (!enabledRef.current || !list) return;
    // Programmatic tail-following also fires scroll. Ignore it when the
    // viewport is at the bottom; genuine scroll-back extends the idle timer.
    if (list.scrollHeight - list.scrollTop - list.clientHeight > BOTTOM_TOLERANCE_PX) {
      pauseFollowing();
    }
  }

  function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (["ArrowUp", "ArrowDown", "PageUp", "PageDown", "Home", "End", " "].includes(event.key)) {
      pauseFollowing();
    }
  }

  return {
    onWheel: pauseFollowing,
    onPointerDown: pauseFollowing,
    onTouchStart: pauseFollowing,
    onTouchMove: pauseFollowing,
    onScroll,
    onKeyDown,
  };
}
