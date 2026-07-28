import { useEffect, useMemo, useState } from "react";
import { api } from "./api";
import { ActionTray } from "./ActionTray";
import { GlobalAbilitiesPanel, type GlobalAbilityFlow } from "./GlobalAbilitiesPanel";
import { HowToPlay } from "./HowToPlay";
import { PlayerBoard, type Selection } from "./PlayerBoard";
import type { CardDef, GameState } from "./types";
import "./App.css";

function App() {
  const [cards, setCards] = useState<CardDef[] | null>(null);
  const [game, setGame] = useState<GameState | null>(null);
  const [selection, setSelection] = useState<Selection>({ primary: null, secondary: [] });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showHowToPlay, setShowHowToPlay] = useState(false);
  const [globalFlow, setGlobalFlow] = useState<GlobalAbilityFlow | null>(null);

  useEffect(() => {
    api.getCards().then(setCards).catch((e) => setError(String(e)));
  }, []);

  const cardsById = useMemo(() => {
    const map = new Map<string, CardDef>();
    for (const c of cards ?? []) map.set(c.id, c);
    return map;
  }, [cards]);

  function clearSelection() {
    setSelection({ primary: null, secondary: [] });
  }

  // Clicking a group cycles through picking successive instances from it:
  // first click sets/adds one, repeated clicks add more (up to the
  // group's count), and once every instance in the group is selected the
  // next click removes the most recently added one - lets you pick "2 of
  // these 3 identical Sidekicks" without the grouping hiding the option.
  function handleGroupClick(ids: string[]) {
    setSelection((sel) => {
      const available = ids.filter((id) => id !== sel.primary && !sel.secondary.includes(id));
      if (sel.primary === null) return { primary: ids[0], secondary: [] };
      if (ids.includes(sel.primary) && available.length === 0) return { primary: null, secondary: [] };
      if (available.length > 0) return { ...sel, secondary: [...sel.secondary, available[0]] };
      const toRemove = [...sel.secondary].reverse().find((id) => ids.includes(id));
      return toRemove ? { ...sel, secondary: sel.secondary.filter((id) => id !== toRemove) } : sel;
    });
  }

  async function run(action: () => Promise<GameState>) {
    setError(null);
    setBusy(true);
    try {
      const next = await action();
      setGame(next);
      clearSelection();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  // A Global ability isn't tied to a die selection the way everything
  // else is (rule 2.6.5.2 - either player, any card) - it gets its own
  // little energy-then-targets flow instead of a contextual Action Tray
  // entry. Board clicks keep populating the same `selection` state used
  // everywhere else; this flow just reads it at each stage instead of the
  // Action Tray reading it, and the Action Tray is hidden meanwhile (see
  // render) so the two don't fight over what a click means.
  function startGlobalAbility(cardId: string) {
    if (!game) return;
    setGlobalFlow({ cardId, playerId: game.activePlayerId, stage: "energy", energyIds: [] });
    clearSelection();
  }

  function chooseGlobalAbilityPlayer(playerId: string) {
    setGlobalFlow((f) => (f ? { ...f, playerId } : f));
    clearSelection();
  }

  function confirmGlobalAbilityEnergy() {
    setGlobalFlow((f) =>
      f
        ? { ...f, stage: "targets", energyIds: selection.primary ? [selection.primary, ...selection.secondary] : [] }
        : f,
    );
    clearSelection();
  }

  function cancelGlobalAbility() {
    setGlobalFlow(null);
    clearSelection();
  }

  async function submitGlobalAbility(targetIds: string[]) {
    if (!globalFlow || !gameId) return;
    setError(null);
    setBusy(true);
    try {
      const next = await api.useGlobalAbility(
        gameId,
        globalFlow.cardId,
        globalFlow.playerId,
        globalFlow.energyIds,
        targetIds,
      );
      setGame(next);
      clearSelection();
      setGlobalFlow(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  const gameId = game?.gameId;

  // The Roll & Reroll step's not-yet-rolled dice: this turn's fresh draw
  // plus whatever Clear & Draw carried over from the Prep Area. Rolling
  // always acts on all of them at once - there's no reason to make the
  // player select them individually just to roll. Once Roll runs, these
  // zones empty out (the dice land straight in the Reserve Pool), so
  // finding any die here at all means they're still unrolled - and, while
  // still in the Clear & Draw step, means Clear & Draw hasn't been run
  // yet either (it's the same zones, just one step earlier).
  const unrolledStepDice = game
    ? game.dice.filter(
        (d) =>
          d.controllerId === game.activePlayerId &&
          (d.zone === "DiceFromBag" || d.zone === "DiceFromPrep"),
      )
    : [];

  // What's actually legal to click right now, mirroring TurnEngine's own
  // step/sub-step guards - so the UI doesn't hand out buttons that just
  // bounce off the server with an error.
  const canClearAndDraw = game?.currentStep === "ClearAndDraw" && unrolledStepDice.length === 0;
  const canRoll = game?.currentStep === "RollAndReroll" && unrolledStepDice.length > 0;
  const canAdvanceToRollAndReroll = game?.currentStep === "ClearAndDraw" && unrolledStepDice.length > 0;
  const canAdvanceToMain = game?.currentStep === "RollAndReroll" && unrolledStepDice.length === 0;
  const canEnterAttack = game?.currentStep === "Main";
  const canSkipAttack = game?.currentStep === "Main";
  const canDeclareBlockers = game?.currentStep === "Attack" && game.attackSubStep === "DeclareBlockers";
  const canAssignDamage = game?.currentStep === "Attack" && game.attackSubStep === "ActionAndGlobalWindow";
  const canCleanUp = game?.currentStep === "CleanUp";

  // The one or two buttons worth putting front and center: whatever
  // actually moves the turn forward from exactly where it is right now.
  type AdvanceOption = { key: string; label: string; run: () => Promise<GameState> };
  const advanceOptions: AdvanceOption[] = [];
  if (gameId && canClearAndDraw) {
    advanceOptions.push({ key: "clear-and-draw", label: "Clear & Draw", run: () => api.clearAndDraw(gameId) });
  }
  if (gameId && canAdvanceToRollAndReroll) {
    advanceOptions.push({ key: "to-roll", label: "Roll & Reroll ▶", run: () => api.advanceStep(gameId) });
  }
  if (gameId && canRoll) {
    advanceOptions.push({ key: "roll", label: `Roll (${unrolledStepDice.length} dice)`, run: () => api.roll(gameId) });
  }
  if (gameId && canAdvanceToMain) {
    advanceOptions.push({ key: "to-main", label: "Main ▶", run: () => api.advanceStep(gameId) });
  }
  if (gameId && canEnterAttack) {
    advanceOptions.push({ key: "enter-attack", label: "Attack ▶", run: () => api.enterAttackStep(gameId) });
  }
  if (gameId && canSkipAttack) {
    advanceOptions.push({
      key: "skip-attack",
      label: "Clean Up (skip attack) ▶",
      run: () => api.skipAttackStep(gameId),
    });
  }
  if (gameId && canDeclareBlockers) {
    advanceOptions.push({
      key: "declare-blockers",
      label: "Declare Blockers (none) ▶",
      run: () => api.declareBlockers(gameId, []),
    });
  }
  if (gameId && canAssignDamage) {
    advanceOptions.push({
      key: "assign-damage",
      label: "Assign Combat Damage (no blocks) ▶",
      run: () => api.assignCombatDamage(gameId, [], []),
    });
  }
  if (gameId && canCleanUp) {
    advanceOptions.push({ key: "clean-up", label: "End Turn ▶", run: () => api.cleanUp(gameId) });
  }

  return (
    <div className="app">
      <header className="app-header">
        <h1>DiceFight2026</h1>
        <button disabled={busy} onClick={() => run(() => api.createGame())}>
          New Game (Team A vs Team B)
        </button>
        <button onClick={() => setShowHowToPlay(true)}>How to Play</button>
        {error && <div className="error">{error}</div>}
      </header>

      {showHowToPlay && <HowToPlay onClose={() => setShowHowToPlay(false)} />}

      {game && gameId && (
        <div className="app-layout">
          <div className="main-column">
            <section className="status-bar">
              <div>
                <strong>Step:</strong> {game.currentStep}
                {game.currentStep === "Attack" && <> / {game.attackSubStep}</>}
              </div>
              <div>
                <strong>Active:</strong> {game.activePlayerId}
              </div>
              {game.isFirstTurn && <div className="badge">First turn</div>}

              <span className="advance-label">Advance to:</span>
              {advanceOptions.map((opt) => (
                <button key={opt.key} className="advance-btn" disabled={busy} onClick={() => run(opt.run)}>
                  {opt.label}
                </button>
              ))}
              {game.currentStep === "Attack" && game.attackSubStep === "DeclareAttackers" && (
                <span className="hint">Select attacker(s) on the board, then use the Action Tray.</span>
              )}
            </section>

            <details className="turn-controls">
              <summary>Manual step actions (advanced)</summary>
              <div className="turn-controls-buttons">
                <button disabled={busy || !canClearAndDraw} onClick={() => run(() => api.clearAndDraw(gameId))}>
                  Clear &amp; Draw
                </button>
                <button
                  disabled={busy || !(canAdvanceToRollAndReroll || canAdvanceToMain)}
                  onClick={() => run(() => api.advanceStep(gameId))}
                >
                  Advance Step
                </button>
                <button disabled={busy || !canRoll} onClick={() => run(() => api.roll(gameId))}>
                  Roll {canRoll ? `(${unrolledStepDice.length} dice)` : ""}
                </button>
                <button disabled={busy || !canEnterAttack} onClick={() => run(() => api.enterAttackStep(gameId))}>
                  Enter Attack Step
                </button>
                <button disabled={busy || !canSkipAttack} onClick={() => run(() => api.skipAttackStep(gameId))}>
                  Skip Attack Step
                </button>
                <button
                  disabled={busy || !canDeclareBlockers}
                  onClick={() => run(() => api.declareBlockers(gameId, []))}
                >
                  Declare Blockers (none)
                </button>
                <button
                  disabled={busy || !canAssignDamage}
                  onClick={() => run(() => api.assignCombatDamage(gameId, [], []))}
                >
                  Assign Combat Damage (no blocks)
                </button>
                <button disabled={busy || !canCleanUp} onClick={() => run(() => api.cleanUp(gameId))}>
                  End Turn (Clean up)
                </button>
              </div>
            </details>

            {globalFlow ? (
              <div className="action-tray global-flow-notice">
                <p>
                  Selecting {globalFlow.stage === "energy" ? "energy" : "target(s)"} for a Global ability - see the
                  Global Abilities panel.
                </p>
              </div>
            ) : (
              <ActionTray
                game={game}
                dice={game.dice}
                cardsById={cardsById}
                selection={selection}
                busy={busy}
                onRun={run}
                onClear={clearSelection}
              />
            )}

            <section className="boards">
              <PlayerBoard
                title={`${game.playerOne.name} (${game.playerOne.id})`}
                isActive={game.activePlayerId === game.playerOne.id}
                life={game.playerOne.life}
                virtualGenericEnergy={game.playerOne.virtualGenericEnergy}
                dice={game.dice.filter((d) => d.ownerId === game.playerOne.id)}
                cardsById={cardsById}
                selection={selection}
                onGroupClick={handleGroupClick}
              />
              <PlayerBoard
                title={`${game.playerTwo.name} (${game.playerTwo.id})`}
                isActive={game.activePlayerId === game.playerTwo.id}
                life={game.playerTwo.life}
                virtualGenericEnergy={game.playerTwo.virtualGenericEnergy}
                dice={game.dice.filter((d) => d.ownerId === game.playerTwo.id)}
                cardsById={cardsById}
                selection={selection}
                onGroupClick={handleGroupClick}
              />
            </section>
          </div>

          <GlobalAbilitiesPanel
            game={game}
            dice={game.dice}
            cardsById={cardsById}
            cards={cards ?? []}
            busy={busy}
            flow={globalFlow}
            selection={selection}
            onStart={startGlobalAbility}
            onChoosePlayer={chooseGlobalAbilityPlayer}
            onConfirmEnergy={confirmGlobalAbilityEnergy}
            onConfirmTargets={() =>
              submitGlobalAbility(selection.primary ? [selection.primary, ...selection.secondary] : [])
            }
            onSkipTargets={() => submitGlobalAbility([])}
            onCancel={cancelGlobalAbility}
          />
        </div>
      )}
    </div>
  );
}

export default App;
