import { useEffect, useMemo, useState } from "react";
import { api } from "./api";
import { ActionTray } from "./ActionTray";
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

  const gameId = game?.gameId;

  // The Roll & Reroll step's dice: this turn's fresh draw plus whatever
  // Clear & Draw carried over from the Prep Area. Rolling and finalizing
  // always act on all of them at once - there's no reason to make the
  // player select them individually just to roll.
  const rollStepDice = game
    ? game.dice.filter(
        (d) =>
          d.controllerId === game.activePlayerId &&
          (d.zone === "DiceFromBag" || d.zone === "DiceFromPrep"),
      )
    : [];
  const hasUnrolledStepDice = rollStepDice.some((d) => d.status === "Unrolled");
  const hasRolledPendingStepDice = rollStepDice.length > 0 && !hasUnrolledStepDice;

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
        <>
          <section className="status-bar">
            <div>
              <strong>Step:</strong> {game.currentStep}
              {game.currentStep === "Attack" && <> / {game.attackSubStep}</>}
            </div>
            <div>
              <strong>Active:</strong> {game.activePlayerId}
            </div>
            {game.isFirstTurn && <div className="badge">First turn</div>}
          </section>

          <section className="turn-controls">
            <span className="turn-controls-label">Turn controls:</span>
            <button disabled={busy} onClick={() => run(() => api.clearAndDraw(gameId))}>
              Clear &amp; Draw
            </button>
            <button
              disabled={busy || (game.currentStep === "RollAndReroll" && hasUnrolledStepDice)}
              onClick={() =>
                run(async () => {
                  // Rolled but hasn't finalized a reroll decision yet - treat
                  // advancing as "keep everything as rolled" (rule 2.4.3
                  // allows rerolling none) rather than leaving these dice
                  // stuck in DiceFromBag/DiceFromPrep forever.
                  if (game.currentStep === "RollAndReroll" && hasRolledPendingStepDice) {
                    await api.finishRoll(gameId, []);
                  }
                  return api.advanceStep(gameId);
                })
              }
            >
              Advance Step
            </button>
            {game.currentStep === "RollAndReroll" && hasUnrolledStepDice && (
              <button disabled={busy} onClick={() => run(() => api.roll(gameId))}>
                Roll ({rollStepDice.length} dice)
              </button>
            )}
            <button disabled={busy} onClick={() => run(() => api.enterAttackStep(gameId))}>
              Enter Attack Step
            </button>
            <button disabled={busy} onClick={() => run(() => api.skipAttackStep(gameId))}>
              Skip Attack Step
            </button>
            <button disabled={busy} onClick={() => run(() => api.declareBlockers(gameId, []))}>
              Declare Blockers (none)
            </button>
            <button disabled={busy} onClick={() => run(() => api.assignCombatDamage(gameId, [], []))}>
              Assign Combat Damage (no blocks)
            </button>
            <button disabled={busy} onClick={() => run(() => api.cleanUp(gameId))}>
              Clean Up
            </button>
          </section>

          <ActionTray
            game={game}
            dice={game.dice}
            cardsById={cardsById}
            selection={selection}
            busy={busy}
            onRun={run}
            onClear={clearSelection}
          />

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
        </>
      )}
    </div>
  );
}

export default App;
