import type { GameState } from "./types";

// The design handoff's chosen step-UI variant - "horizontal step chips in
// the title bar rather than a vertical rail" (design_handoff_match_table/
// README.md's Fidelity section: "ribbon" is the reviewed default, "rail"
// the alternative). Stage 4 of the match-table redesign (DESIGN_LOG.md,
// 2026-08-30) built the rail instead; this is the ribbon it didn't.
//
// Read-only, same as the rail it replaces: rule 2.2.4 forbids going back
// a step, so this is a progress display only - the Now panel in TurnRail
// is still the only way forward.
const STEPS = [
  { key: "ClearAndDraw", label: "Clear & Draw" },
  { key: "RollAndReroll", label: "Roll & Reroll" },
  { key: "Main", label: "Main" },
  { key: "Attack", label: "Attack" },
  { key: "CleanUp", label: "Clean Up" },
];

export function StepRibbon({ game }: { game: GameState }) {
  const currentIndex = STEPS.findIndex((s) => s.key === game.currentStep);
  return (
    <div className="step-ribbon" role="list" aria-label="Turn sequence">
      {STEPS.map((step, i) => {
        const state = i < currentIndex ? "past" : i === currentIndex ? "current" : "future";
        return (
          <span key={step.key} role="listitem" className={`ribbon-chip ${state}`} aria-current={state === "current" || undefined}>
            {step.label}
          </span>
        );
      })}
    </div>
  );
}
