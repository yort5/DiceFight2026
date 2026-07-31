import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "./api";
import { ActionTray } from "./ActionTray";
import { InfiltrateWindowPanel } from "./AttackWindowPanels";
import { DamageSplitPanel, DeclareBlockersPanel } from "./CombatPanel";
import { GlobalAbilitiesPanel, type GlobalAbilityFlow } from "./GlobalAbilitiesPanel";
import { HowToPlay } from "./HowToPlay";
import { PlayerBoard, type Selection } from "./PlayerBoard";
import type { BlockAssignment, CardDef, DamageSplit, GameState } from "./types";
import "./App.css";

function App() {
  const [cards, setCards] = useState<CardDef[] | null>(null);
  const [game, setGame] = useState<GameState | null>(null);
  const [selection, setSelection] = useState<Selection>({ primary: null, secondary: [] });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showHowToPlay, setShowHowToPlay] = useState(false);
  const [globalFlow, setGlobalFlow] = useState<GlobalAbilityFlow | null>(null);
  // Carries the Declare Blockers decision forward into Assign Combat
  // Damage - the server doesn't remember it between those two calls (see
  // CombatPanel.tsx), and the Action/Global window in between means this
  // can't just be chained automatically. Reset to [] any time we leave
  // the Attack Step (see run()), so it never leaks into a later turn.
  const [combatAssignments, setCombatAssignments] = useState<BlockAssignment[]>([]);
  // `busy` (React state) only disables buttons on the *next* render - a
  // fast double-click can fire a second action before that render happens
  // (e.g. two Declare Attackers calls landing back to back, the second
  // arriving after the sub-step already moved on and bouncing off the
  // server with a confusing "Expected DeclareAttackers, was
  // DeclareBlockers" error). This ref is checked synchronously inside the
  // click handler itself, so it closes that gap regardless of render timing.
  const busyRef = useRef(false);

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
    if (busyRef.current) return;
    busyRef.current = true;
    setError(null);
    setBusy(true);
    try {
      const next = await action();
      setGame(next);
      clearSelection();
      if (next.currentStep !== "Attack") setCombatAssignments([]);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      busyRef.current = false;
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
    if (!globalFlow) return;
    const energyIds = selection.primary ? [selection.primary, ...selection.secondary] : [];
    // Skip the targets stage entirely for a Global that doesn't have one
    // (e.g. Falcon's) instead of showing a "click a target, or Skip"
    // prompt that would only ever be answered with Skip.
    const needsTarget = cardsById.get(globalFlow.cardId)?.globalAbilityNeedsTarget ?? true;
    if (!needsTarget) {
      submitGlobalAbility(energyIds, []);
      return;
    }
    setGlobalFlow((f) => (f ? { ...f, stage: "targets", energyIds } : f));
    clearSelection();
  }

  function cancelGlobalAbility() {
    setGlobalFlow(null);
    clearSelection();
  }

  async function submitGlobalAbility(energyIds: string[], targetIds: string[]) {
    if (!globalFlow || !gameId || busyRef.current) return;
    busyRef.current = true;
    setError(null);
    setBusy(true);
    try {
      const next = await api.useGlobalAbility(gameId, globalFlow.cardId, globalFlow.playerId, energyIds, targetIds);
      setGame(next);
      clearSelection();
      setGlobalFlow(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  // Rule 2.7.2.2 - building up the attacker->blocker(s) map one attacker
  // at a time, reusing the same board-click selection (primary = attacker,
  // secondary = blocker(s) for it) as everywhere else. A blocker die can
  // only block one attacker, so it's dropped from any earlier assignment
  // before being added to this one.
  function addBlockerAssignments() {
    if (!selection.primary || selection.secondary.length === 0) return;
    const attackerDieId = selection.primary;
    setCombatAssignments((prev) => [
      ...prev.filter((a) => !selection.secondary.includes(a.blockerDieId)),
      ...selection.secondary.map((blockerDieId) => ({ attackerDieId, blockerDieId })),
    ]);
    clearSelection();
  }

  function removeBlockerAssignment(blockerDieId: string) {
    setCombatAssignments((prev) => prev.filter((a) => a.blockerDieId !== blockerDieId));
  }

  function confirmBlockers() {
    if (!gameId) return;
    run(() => api.declareBlockers(gameId, combatAssignments));
  }

  function confirmInfiltrate(infiltratingDieIds: string[]) {
    if (!gameId) return;
    run(() => api.resolveInfiltrate(gameId, combatAssignments, infiltratingDieIds));
  }

  async function confirmDamageSplits(splits: DamageSplit[]) {
    if (!gameId) return;
    await run(() => api.assignCombatDamage(gameId, combatAssignments, splits));
  }

  const gameId = game?.gameId;

  // While a Global ability flow is collecting payment, only the chosen
  // payer's own Reserve Pool energy dice are legal to click - everything
  // else gets dimmed and made unclickable in the Reserve Pool (see
  // PlayerBoard) instead of letting the player pick something invalid and
  // find out from a server error. Null means "no restriction" (every
  // other flow/step already only offers legal actions its own way).
  const globalEnergySelectableIds = useMemo(() => {
    if (!game || !globalFlow || globalFlow.stage !== "energy") return null;
    return new Set(
      game.dice
        .filter((d) => d.controllerId === globalFlow.playerId && d.zone === "ReservePool" && d.status === "Energy")
        .map((d) => d.id),
    );
  }, [game, globalFlow]);

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
  const canResolveInfiltrate = game?.currentStep === "Attack" && game.attackSubStep === "InfiltrateWindow";
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
  // Declare Blockers always goes through the DeclareBlockersPanel now (see
  // render below) - even "no blocks" is just confirming an empty list
  // there, so there's no separate quick action for it. Assign Combat
  // Damage keeps its quick "no blocks" shortcut, since that trivial case
  // (nothing was blocked) has nothing worth building a form for.
  if (gameId && canAssignDamage && combatAssignments.length === 0) {
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
                <button disabled={busy || !canDeclareBlockers} onClick={confirmBlockers}>
                  Declare Blockers ({combatAssignments.length === 0 ? "none" : `${combatAssignments.length} assigned`})
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
            ) : canDeclareBlockers ? (
              <DeclareBlockersPanel
                game={game}
                dice={game.dice}
                cardsById={cardsById}
                selection={selection}
                assignments={combatAssignments}
                busy={busy}
                onAddAssignments={addBlockerAssignments}
                onRemoveAssignment={removeBlockerAssignment}
                onClearSelection={clearSelection}
                onConfirm={confirmBlockers}
              />
            ) : canResolveInfiltrate ? (
              <InfiltrateWindowPanel
                game={game}
                dice={game.dice}
                cardsById={cardsById}
                combatAssignments={combatAssignments}
                busy={busy}
                onConfirm={confirmInfiltrate}
              />
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

            {canAssignDamage && combatAssignments.length > 0 && (
              <DamageSplitPanel
                dice={game.dice}
                cardsById={cardsById}
                assignments={combatAssignments}
                busy={busy}
                onConfirm={confirmDamageSplits}
              />
            )}

            <section className="boards">
              <PlayerBoard
                title={`${game.playerOne.name} (${game.playerOne.id})`}
                isActive={game.activePlayerId === game.playerOne.id}
                life={game.playerOne.life}
                dice={game.dice.filter((d) => d.ownerId === game.playerOne.id)}
                cardsById={cardsById}
                selection={selection}
                onGroupClick={handleGroupClick}
                selectableEnergyIds={globalEnergySelectableIds}
              />
              <PlayerBoard
                title={`${game.playerTwo.name} (${game.playerTwo.id})`}
                isActive={game.activePlayerId === game.playerTwo.id}
                life={game.playerTwo.life}
                dice={game.dice.filter((d) => d.ownerId === game.playerTwo.id)}
                cardsById={cardsById}
                selection={selection}
                onGroupClick={handleGroupClick}
                selectableEnergyIds={globalEnergySelectableIds}
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
              globalFlow &&
              submitGlobalAbility(
                globalFlow.energyIds,
                selection.primary ? [selection.primary, ...selection.secondary] : [],
              )
            }
            onSkipTargets={() => globalFlow && submitGlobalAbility(globalFlow.energyIds, [])}
            onCancel={cancelGlobalAbility}
          />
        </div>
      )}
    </div>
  );
}

export default App;
