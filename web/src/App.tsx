import { useEffect, useMemo, useRef, useState } from "react";
import { api } from "./api";
import { ActionTray } from "./ActionTray";
import { InfiltrateWindowPanel, RangeWindowPanel, TagOutWindowPanel } from "./AttackWindowPanels";
import { DamageSplitPanel, DeclareBlockersPanel } from "./CombatPanel";
import { CombatLane } from "./CombatLane";
import { CommunityCards } from "./CommunityCards";
import { DeclareAttackersPanel } from "./DeclareAttackersPanel";
import { dieLabel } from "./dieHelpers";
import { readPendingGame } from "./gameHandoff";
import { GlobalAbilitiesPanel, type GlobalAbilityFlow } from "./GlobalAbilitiesPanel";
import { HowToPlay } from "./HowToPlay";
import { PendingChoicePanel } from "./PendingChoicePanel";
import { MatchLog } from "./MatchLog";
import { PlayerBoard, type Selection } from "./PlayerBoard";
import { TurnRail } from "./TurnRail";
import { navigate } from "./router";
import { claimSeatFromUrl, inviteLink, nameClaimedSeat } from "./seats";
import { facesFor } from "./dieFaces";
import { isRoll, useDiceRoll, type RollTarget } from "./useDiceRoll";
import type { BlockAssignment, CardDef, DamageSplit, GameState, RangeAssignment, TagOutUse } from "./types";
import "./App.css";

// Turn-based, so a couple of seconds between checks is imperceptible.
const POLL_INTERVAL_MS = 2000;

function App() {
  const [cards, setCards] = useState<CardDef[] | null>(null);
  // A game started from /teambuilder's "Start Game" arrives via
  // sessionStorage (see gameHandoff.ts) rather than the "New Game" button
  // below - lazy initializer so it's picked up on the very first render,
  // not after a flash of the empty pre-game state.
  const [game, setGame] = useState<GameState | null>(() => readPendingGame());
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
  // Roll animation state. Transient and client-only: the result itself is
  // already decided by the server before any of this runs.
  const { spins, offsets, rolling, launch: launchRoll } = useDiceRoll();

  useEffect(() => {
    api.getCards().then(setCards).catch((e) => setError(String(e)));
  }, []);

  // An invite link carries a game id and a seat token. Claim the seat,
  // load the game, and let the board render from whichever side the
  // server says that token holds.
  useEffect(() => {
    const claim = claimSeatFromUrl();
    if (!claim) return;
    api.getGame(claim.gameId)
      .then((joined) => {
        if (joined.yourPlayerId) nameClaimedSeat(claim.gameId, joined.yourPlayerId);
        setGame(joined);
      })
      .catch((e) => setError(`Could not join that game: ${e instanceof Error ? e.message : String(e)}`));
  }, []);

  // While a game is open, watch for the other player's moves. Turn-based,
  // so a couple of seconds is imperceptible - and comparing one version
  // number keeps a quiet game from redrawing the board every tick.
  const gameId = game?.gameId ?? null;
  const gameVersion = game?.version ?? 0;
  useEffect(() => {
    if (!gameId) return;
    let cancelled = false;
    const timer = window.setInterval(async () => {
      if (busyRef.current) return; // never race our own action
      try {
        const latest = await api.getGame(gameId);
        if (!cancelled && latest.version !== gameVersion) setGame(latest);
      } catch {
        // A poll failing is not worth an error banner - the next one in
        // two seconds either works or the player is already stuck.
      }
    }, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [gameId, gameVersion]);

  // The board is drawn from the seat this browser holds, not from a fixed
  // side. `you` is the near mat, the amber half of the lane, and the
  // "yours" colour in the log; `them` is the far one. Playing alone, both
  // seats are held and `you` follows whichever is being played.
  const you = game?.yourPlayerId ?? game?.playerOne.id ?? "";
  const them = game && you === game.playerTwo.id ? game.playerOne : game?.playerTwo;
  const near = game && you === game.playerTwo.id ? game.playerTwo : game?.playerOne;

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

  // `rolledDieIds` names the dice an action deliberately rolled. Without
  // it a reroll that lands the same face again would not move at all, and
  // the player could not tell it had happened.
  async function run(action: () => Promise<GameState>, rolledDieIds?: string[]) {
    if (busyRef.current) return;
    busyRef.current = true;
    setError(null);
    setBusy(true);
    try {
      const previous = game;
      const next = await action();
      setGame(next);
      if (previous) animateRolledDice(previous, next, rolledDieIds);
      clearSelection();
      if (next.currentStep !== "Attack") setCombatAssignments([]);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  }

  // Every server call comes through run(), so comparing the state before
  // and after is enough to catch a roll wherever it came from - the Roll
  // button, a reroll, or a card effect that rerolls dice on its own. What
  // counts as a roll rather than a spin is isRoll()'s call.
  function animateRolledDice(previous: GameState, next: GameState, rolledDieIds?: string[]) {
    const before = new Map(previous.dice.map((d) => [d.id, d]));
    const explicit = new Set(rolledDieIds ?? []);
    const targets: RollTarget[] = [];
    for (const die of next.dice) {
      const was = before.get(die.id);
      if (!was) continue;
      if (!explicit.has(die.id) && !isRoll(was, die)) continue;
      const { index } = facesFor(die, cardsById);
      targets.push({ dieId: die.id, faceIndex: index });
    }
    launchRoll(targets);
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

  // WhenFielded targeting (Intimidate, Dazzler, God Emperor Doom, Polaris)
  // - same shape as the Global ability flow: board clicks feed the shared
  // `selection` instead of the Action Tray while this is set (see render),
  // with its own small Confirm/Cancel panel since (unlike Global) there's
  // no sidebar for it to live in.
  interface FieldTargetFlow {
    dieId: string;
    energyIds: string[];
  }
  const [fieldTargetFlow, setFieldTargetFlow] = useState<FieldTargetFlow | null>(null);

  function startFieldTargetFlow(dieId: string, energyIds: string[]) {
    setFieldTargetFlow({ dieId, energyIds });
    clearSelection();
  }

  function cancelFieldTarget() {
    setFieldTargetFlow(null);
    clearSelection();
  }

  async function submitFieldTarget(targetIds: string[]) {
    if (!fieldTargetFlow || !gameId || busyRef.current) return;
    busyRef.current = true;
    setError(null);
    setBusy(true);
    try {
      const next = await api.field(gameId, fieldTargetFlow.dieId, fieldTargetFlow.energyIds, targetIds);
      setGame(next);
      clearSelection();
      setFieldTargetFlow(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
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

  function confirmTagOut(uses: TagOutUse[]) {
    if (!gameId) return;
    run(() => api.resolveTagOut(gameId, uses));
  }

  function confirmRange(active: RangeAssignment[], inactive: RangeAssignment[]) {
    if (!gameId) return;
    run(() => api.resolveRange(gameId, active, inactive));
  }

  function declareAttackers(attackerDieIds: string[], targetDieIds: string[]) {
    if (!gameId) return;
    run(() => api.declareAttackers(gameId, attackerDieIds, targetDieIds));
  }

  async function confirmDamageSplits(splits: DamageSplit[]) {
    if (!gameId) return;
    await run(() => api.assignCombatDamage(gameId, combatAssignments, splits));
  }


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
  const canDeclareAttackers = game?.currentStep === "Attack" && game.attackSubStep === "DeclareAttackers";
  const canDeclareBlockers = game?.currentStep === "Attack" && game.attackSubStep === "DeclareBlockers";
  const canResolveInfiltrate = game?.currentStep === "Attack" && game.attackSubStep === "InfiltrateWindow";
  const canResolveTagOut = game?.currentStep === "Attack" && game.attackSubStep === "TagOutWindow";
  const canResolveRange = game?.currentStep === "Attack" && game.attackSubStep === "RangeWindow";
  const canAssignDamage = game?.currentStep === "Attack" && game.attackSubStep === "ActionAndGlobalWindow";
  const canCleanUp = game?.currentStep === "CleanUp";

  // The one or two buttons worth putting front and center: whatever
  // actually moves the turn forward from exactly where it is right now.
  type AdvanceOption = {
    key: string;
    label: string;
    run: () => Promise<GameState>;
    /** Dice this option deliberately rolls - see run(). */
    rolledDieIds?: string[];
  };
  // Every entry below is the ACTIVE player's move (the defender's
  // decisions go through their own panels), so none of them belong to a
  // player waiting for their turn. The server would refuse them with a
  // 403 anyway - this is so the button is never offered in the first
  // place, which matters now that the other seat is a different person.
  const yourTurn = game !== null && game.activePlayerId === you;
  const advanceOptions: AdvanceOption[] = [];
  if (gameId && yourTurn && canClearAndDraw) {
    advanceOptions.push({ key: "clear-and-draw", label: "Clear & Draw", run: () => api.clearAndDraw(gameId) });
  }
  if (gameId && yourTurn && canAdvanceToRollAndReroll) {
    advanceOptions.push({ key: "to-roll", label: "Roll & Reroll ▶", run: () => api.advanceStep(gameId) });
  }
  if (gameId && yourTurn && canRoll) {
    advanceOptions.push({
      key: "roll",
      label: `Roll (${unrolledStepDice.length} dice)`,
      run: () => api.roll(gameId),
      rolledDieIds: unrolledStepDice.map((d) => d.id),
    });
  }
  if (gameId && yourTurn && canAdvanceToMain) {
    advanceOptions.push({ key: "to-main", label: "Main ▶", run: () => api.advanceStep(gameId) });
  }
  if (gameId && yourTurn && canEnterAttack) {
    advanceOptions.push({ key: "enter-attack", label: "Attack ▶", run: () => api.enterAttackStep(gameId) });
  }
  if (gameId && yourTurn && canSkipAttack) {
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
  if (gameId && yourTurn && canCleanUp) {
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
        <button onClick={() => navigate("/teambuilder")}>Team Builder</button>
        {error && <div className="error">{error}</div>}
      </header>

      {showHowToPlay && <HowToPlay onClose={() => setShowHowToPlay(false)} />}

      {game && gameId && (
        <div className="app-layout game-layout">
          <div className="main-column">
            {/* Step and life live in the rail now (see TurnRail); this
                keeps only what is about the table itself. */}
            <section className="status-bar">
              <div>
                <strong>Active:</strong> {game.activePlayerId}
              </div>
              {game.isFirstTurn && <div className="badge">First turn</div>}
              {canDeclareAttackers && (
                <span className="hint">Select attacker(s) on the board, then use the panel below.</span>
              )}
            </section>

            {/* The old "Manual step actions (advanced)" panel is gone: every
                button on it is now in the rail's Now panel, which shows only
                what is actually legal. Declare Blockers was already routed
                through DeclareBlockersPanel rather than a quick action. */}

            {game.pendingChoice ? (
              <PendingChoicePanel
                pendingChoice={game.pendingChoice}
                dice={game.dice}
                cardsById={cardsById}
                busy={busy}
                onConfirm={(ids) => run(() => api.resolvePendingChoice(gameId, ids))}
              />
            ) : globalFlow ? (
              <div className="action-tray global-flow-notice">
                <p>
                  Selecting {globalFlow.stage === "energy" ? "energy" : "target(s)"} for a Global ability - see the
                  Global Abilities panel.
                </p>
              </div>
            ) : fieldTargetFlow ? (
              <div className="action-tray combat-panel">
                <p className="hint">This card needs a target when fielded - click it on the board.</p>
                <div className="selection-summary">
                  {selection.primary === null && <span className="empty-hint">no target selected</span>}
                  {selection.primary && (
                    <span className="primary-chip">
                      {dieLabel(game.dice.find((d) => d.id === selection.primary)!, cardsById)}
                    </span>
                  )}
                  {selection.secondary.map((id) => (
                    <span key={id} className="secondary-chip">
                      {dieLabel(game.dice.find((d) => d.id === id)!, cardsById)}
                    </span>
                  ))}
                  <button className="clear-btn" onClick={clearSelection}>
                    Clear selection
                  </button>
                </div>
                <div className="tray-actions">
                  <div className="tray-action">
                    <button
                      disabled={busy || selection.primary === null}
                      onClick={() =>
                        submitFieldTarget(selection.primary ? [selection.primary, ...selection.secondary] : [])
                      }
                    >
                      Confirm Target(s) ▶
                    </button>
                  </div>
                  <div className="tray-action">
                    <button disabled={busy} onClick={cancelFieldTarget}>
                      Cancel
                    </button>
                  </div>
                </div>
              </div>
            ) : canResolveRange ? (
              <RangeWindowPanel
                game={game}
                dice={game.dice}
                cardsById={cardsById}
                selection={selection}
                busy={busy}
                onClearSelection={clearSelection}
                onConfirm={confirmRange}
              />
            ) : canDeclareAttackers ? (
              <DeclareAttackersPanel
                game={game}
                dice={game.dice}
                cardsById={cardsById}
                selection={selection}
                busy={busy}
                onClearSelection={clearSelection}
                onSubmit={declareAttackers}
              />
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
            ) : canResolveTagOut ? (
              <TagOutWindowPanel
                dice={game.dice}
                cardsById={cardsById}
                selection={selection}
                busy={busy}
                onClearSelection={clearSelection}
                onConfirm={confirmTagOut}
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
                onFieldNeedsTarget={startFieldTargetFlow}
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

            {/* The table: the two mats face each other across the combat
                lane, so both Field Zones sit against it and an attacker
                stands opposite whatever is blocking it. Player two is on
                the far side, mirrored. */}
            <section className="game-table">
              <PlayerBoard
                title={`${them!.name} (${them!.id})`}
                isActive={game.activePlayerId === them!.id}
                mine={false}
                mirrored
                dice={game.dice.filter((d) => d.ownerId === them!.id)}
                cardsById={cardsById}
                selection={selection}
                onGroupClick={handleGroupClick}
                selectableEnergyIds={globalEnergySelectableIds}
                spins={spins}
                turnOffsets={offsets}
                rolling={rolling}
              />

              {/* Between the two mats, because it belongs to neither. */}
              <CommunityCards
                dice={game.dice}
                cardsById={cardsById}
                nearPlayerId={you}
                selection={selection}
                onGroupClick={handleGroupClick}
              />

              <CombatLane
                dice={game.dice}
                cardsById={cardsById}
                assignments={combatAssignments}
                nearPlayerId={you}
                selection={selection}
                onGroupClick={handleGroupClick}
                spins={spins}
                turnOffsets={offsets}
              />

              <PlayerBoard
                title={`${near!.name} (${near!.id})`}
                isActive={game.activePlayerId === near!.id}
                mine
                dice={game.dice.filter((d) => d.ownerId === near!.id)}
                cardsById={cardsById}
                selection={selection}
                onGroupClick={handleGroupClick}
                selectableEnergyIds={globalEnergySelectableIds}
                spins={spins}
                turnOffsets={offsets}
                rolling={rolling}
              />
            </section>
          </div>

          <div className="side-column">
            <TurnRail
              game={game}
              nearPlayerId={you}
              inviteLink={inviteLink(game.gameId)}
              note={canDeclareAttackers ? "Select your attackers on the board first." : undefined}
              actions={advanceOptions.map((opt) => ({
                key: opt.key,
                label: opt.label,
                disabled: busy || !!game.pendingChoice,
                onClick: () => run(opt.run, opt.rolledDieIds),
              }))}
            />

            <MatchLog entries={game.log} nearPlayerId={you} />

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
        </div>
      )}
    </div>
  );
}

export default App;
