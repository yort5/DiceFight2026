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
  return (
    <div className="step-ribbon" role="list" aria-label="Turn sequence">
      {STEPS.map((step, i) => {
        const state = i < currentIndex ? "past" : i === currentIndex ? "current" : "future";
        return (
          <span key={step.label} role="listitem" className={`ribbon-chip ${state}`} aria-current={state === "current" || undefined}>
            {step.label}
          </span>
        );
      })}
    </div>
  );
}
