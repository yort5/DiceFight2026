import { useEffect, useRef } from "react";
import type { GameState } from "./types";

// Ported from ../StepRibbon.tsx - horizontal step chips in the title bar,
// read-only (the Now/action buttons in the rail are the only way
// forward). V2's currentStepId is finer-grained than v1's currentStep
// (three real step ids inside Attack alone), so each ribbon entry
// matches a set of step ids rather than one exact key.
const STEPS: { label: string; match: (stepId: string) => boolean }[] = [
  { label: "Clear & Draw", match: (id) => id === "start-of-turn" },
  { label: "Roll & Reroll", match: (id) => id === "roll-and-reroll" },
  { label: "Main", match: (id) => id === "main" },
  { label: "Attack", match: (id) => id === "select-attackers" || id === "assign-blockers" || id === "action-global-window" },
  { label: "Clean Up", match: (id) => id === "return-to-field" },
];

export function StepRibbon({ game }: { game: GameState }) {
  const currentIndex = STEPS.findIndex((s) => s.match(game.currentStepId));
  // Scrolls the current chip into the middle of the strip instead of
  // just wrapping the whole ribbon onto a second line - direct feedback
  // (2026-09-11): "have the turn ribbon scroll horizontally rather than
  // wrap, and center on the current step." `inline: "center"` is a
  // no-op once every chip already fits (the common desktop case), so
  // this doesn't fight the layout there.
  const currentRef = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    currentRef.current?.scrollIntoView({ behavior: "smooth", inline: "center", block: "nearest" });
  }, [currentIndex]);
  return (
    <div className="step-ribbon" role="list" aria-label="Turn sequence">
      {STEPS.map((step, i) => {
        const state = i < currentIndex ? "past" : i === currentIndex ? "current" : "future";
        return (
          <span
            key={step.label}
            ref={state === "current" ? currentRef : undefined}
            role="listitem"
            className={`ribbon-chip ${state}`}
            aria-current={state === "current" || undefined}
          >
            {step.label}
          </span>
        );
      })}
    </div>
  );
}
