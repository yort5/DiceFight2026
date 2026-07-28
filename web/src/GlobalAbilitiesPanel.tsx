import { dieLabel } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die, GameState } from "./types";

// A rough client-side affordability check - not a strict guarantee (the
// server's real SpendEnergy is the authority, e.g. for the rare
// multi-required-type ordering edge case noted in the design doc), just
// enough to grey out a Global a player plainly can't pay for right now
// rather than making them click "Use" to find out.
function canAffordGlobal(dice: Die[], playerId: string, cost: NonNullable<CardDef["globalAbilityCost"]>): boolean {
  const energy = dice.filter((d) => d.controllerId === playerId && d.zone === "ReservePool" && d.status === "Energy");
  const total = energy.reduce((sum, d) => sum + d.energyAmount, 0);
  if (total < cost.amount) return false;
  if (!cost.requiredType) return true;
  return energy.some(
    (d) => d.energyKind === "Wild" || (d.energyKind === "Specific" && d.providedEnergyType === cost.requiredType),
  );
}

export type GlobalAbilityStage = "energy" | "targets";

export interface GlobalAbilityFlow {
  cardId: string;
  playerId: string;
  stage: GlobalAbilityStage;
  energyIds: string[];
}

function costLabel(cost: CardDef["globalAbilityCost"]): string {
  if (!cost) return "cost unknown";
  return cost.requiredType ? `Pay ${cost.amount} ${cost.requiredType}` : `Pay ${cost.amount}`;
}

// rawText holds the card's full text box, which for cards like Falcon
// ("Teamwatch - ... Global: Pay [F]. ...") includes a non-Global clause
// too - this panel is about the Global specifically, so trim to just the
// "Global: ..." sentence(s) when that marker is present.
function globalAbilityText(card: CardDef): string {
  const idx = card.rawText.indexOf("Global:");
  return idx >= 0 ? card.rawText.slice(idx) : card.rawText;
}

function selectedIds(selection: Selection): string[] {
  return selection.primary ? [selection.primary, ...selection.secondary] : [];
}

// Global abilities aren't tied to a specific die the way everything else
// in the Action Tray is (rule 2.6.5.2 - either player, any card in the
// catalog, regardless of who owns/controls a die of it) - so instead of a
// contextual tray action, this is a standing sidebar listing every card
// with a scripted Global, each launching its own little energy-then-
// targets flow that reuses the same board click-to-select the rest of the
// UI already uses (App.tsx just points board clicks at this flow's
// `selection` instead of the normal one while it's in progress).
export function GlobalAbilitiesPanel(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  cards: CardDef[];
  busy: boolean;
  flow: GlobalAbilityFlow | null;
  selection: Selection;
  onStart: (cardId: string) => void;
  onChoosePlayer: (playerId: string) => void;
  onConfirmEnergy: () => void;
  onConfirmTargets: () => void;
  onSkipTargets: () => void;
  onCancel: () => void;
}) {
  const { game, dice, cardsById, flow, selection, busy } = props;
  const globals = props.cards.filter((c) => c.abilityTriggers.includes("Global"));
  const activeCard = flow ? props.cards.find((c) => c.id === flow.cardId) : undefined;
  const chosenIds = selectedIds(selection);

  return (
    <aside className="global-sidebar">
      <h3>Global Abilities</h3>
      {globals.length === 0 && <p className="empty-hint">No cards with a scripted Global ability yet.</p>}
      <ul className="global-list">
        {globals.map((c) => {
          const affordable =
            !c.globalAbilityCost ||
            canAffordGlobal(dice, game.playerOne.id, c.globalAbilityCost) ||
            canAffordGlobal(dice, game.playerTwo.id, c.globalAbilityCost);
          return (
            <li key={c.id} className={`global-list-item${affordable ? "" : " unaffordable"}`}>
              <div>
                <div className="global-card-name">{c.name}</div>
                <div className="global-card-cost">{costLabel(c.globalAbilityCost)}</div>
                <div className="global-card-text">{globalAbilityText(c)}</div>
              </div>
              <button
                disabled={busy || flow !== null || !affordable}
                title={affordable ? undefined : "Neither player has enough matching Reserve Pool energy right now"}
                onClick={() => props.onStart(c.id)}
              >
                Use
              </button>
            </li>
          );
        })}
      </ul>

      {flow && activeCard && (
        <div className="global-flow">
          <div className="global-flow-title">Using {activeCard.name}'s Global</div>
          <div className="global-flow-cost">{costLabel(activeCard.globalAbilityCost)}</div>

          <fieldset className="global-flow-player">
            <legend>Paid by</legend>
            {[game.playerOne, game.playerTwo].map((p) => (
              <label key={p.id}>
                <input
                  type="radio"
                  name="global-payer"
                  checked={flow.playerId === p.id}
                  onChange={() => props.onChoosePlayer(p.id)}
                />
                {p.name}
              </label>
            ))}
          </fieldset>

          {flow.stage === "energy" && (
            <>
              <p className="hint">
                Click {flow.playerId}'s Reserve Pool energy dice on the board to select payment - other dice are
                dimmed while you're paying for this.
              </p>
              <div className="global-flow-selection">
                {chosenIds.length === 0 && <span className="empty-hint">none selected</span>}
                {chosenIds.map((id) => {
                  const die = dice.find((d) => d.id === id);
                  return die ? (
                    <span key={id} className="secondary-chip">
                      {dieLabel(die, cardsById)}
                    </span>
                  ) : null;
                })}
              </div>
              <button disabled={busy || chosenIds.length === 0} onClick={props.onConfirmEnergy}>
                Confirm Energy
              </button>
            </>
          )}

          {flow.stage === "targets" && (
            <>
              <p className="hint">This ability needs a target - click it on the board.</p>
              <div className="global-flow-selection">
                {chosenIds.length === 0 && <span className="empty-hint">none selected</span>}
                {chosenIds.map((id) => {
                  const die = dice.find((d) => d.id === id);
                  return die ? (
                    <span key={id} className="secondary-chip">
                      {dieLabel(die, cardsById)}
                    </span>
                  ) : null;
                })}
              </div>
              <button disabled={busy} onClick={props.onConfirmTargets}>
                Confirm Target(s)
              </button>
              <button disabled={busy} onClick={props.onSkipTargets}>
                Skip (no target)
              </button>
            </>
          )}

          <button className="global-flow-cancel" disabled={busy} onClick={props.onCancel}>
            Cancel
          </button>
        </div>
      )}
    </aside>
  );
}
