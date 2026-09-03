import { CommunityCards } from "./CommunityCards";
import { GlobalAbilitiesPanel, type GlobalAbilityFlow } from "./GlobalAbilitiesPanel";
import type { Selection } from "./PlayerBoard";
import type { CardDef, Die, GameState } from "./types";

// The design handoff's Column 1 - "a shared left sideboard for Basic
// Actions and Global Abilities, which belong to both players rather than
// either roster" (design_handoff_match_table/README.md). Stages 1-5 of
// the match-table redesign (DESIGN_LOG.md, 2026-08-30) built everything
// else from that handoff but left Basic Actions in the centre
// (CommunityCards, between the two mats) and Global Abilities in the
// right rail - this is the actual left column the handoff called for,
// wrapping those same two components rather than re-deriving their logic.
//
// Out of Play is deliberately NOT summarized here a second time - it's
// already a real zone on each player's own mat (PlayerBoard.tsx), and
// duplicating it as a sideboard-only strip is exactly the kind of
// "invisible until you go looking for it" gap v3's Dice Kingdom board
// just got bitten by (see v3/DESIGN_NOTES.md, 2026-09-03).
export function Sideboard(props: {
  game: GameState;
  dice: Die[];
  cardsById: Map<string, CardDef>;
  cards: CardDef[];
  nearPlayerId: string;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
  busy: boolean;
  globalFlow: GlobalAbilityFlow | null;
  onStartGlobal: (cardId: string) => void;
  onChooseGlobalPlayer: (playerId: string) => void;
  onConfirmGlobalEnergy: () => void;
  onConfirmGlobalTargets: () => void;
  onSkipGlobalTargets: () => void;
  onCancelGlobal: () => void;
}) {
  return (
    <aside className="sideboard">
      <CommunityCards
        dice={props.dice}
        cardsById={props.cardsById}
        nearPlayerId={props.nearPlayerId}
        selection={props.selection}
        onGroupClick={props.onGroupClick}
      />
      <GlobalAbilitiesPanel
        game={props.game}
        dice={props.dice}
        cardsById={props.cardsById}
        cards={props.cards}
        busy={props.busy}
        flow={props.globalFlow}
        selection={props.selection}
        onStart={props.onStartGlobal}
        onChoosePlayer={props.onChooseGlobalPlayer}
        onConfirmEnergy={props.onConfirmGlobalEnergy}
        onConfirmTargets={props.onConfirmGlobalTargets}
        onSkipTargets={props.onSkipGlobalTargets}
        onCancel={props.onCancelGlobal}
      />
    </aside>
  );
}
