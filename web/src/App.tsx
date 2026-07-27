import { useEffect, useMemo, useState } from "react";
import { api } from "./api";
import type { CardDef, Die, GameState } from "./types";
import { ZONES } from "./types";
import "./App.css";

// First-pass "dev console" UI: proves the whole stack (React -> API ->
// DiceFight.Engine) works end to end with the real sample teams. Die
// selection is a flat click-to-toggle list, reused across actions
// (Purchase/Field treat the first selected die as "the die" and the rest
// as energy/targets) rather than a polished board - a real game board
// comes later.
function App() {
  const [cards, setCards] = useState<CardDef[] | null>(null);
  const [game, setGame] = useState<GameState | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.getCards().then(setCards).catch((e) => setError(String(e)));
  }, []);

  const cardsById = useMemo(() => {
    const map = new Map<string, CardDef>();
    for (const c of cards ?? []) map.set(c.id, c);
    return map;
  }, [cards]);

  function dieLabel(die: Die): string {
    if (!die.cardId) return "Sidekick";
    return cardsById.get(die.cardId)?.name ?? die.cardId;
  }

  function toggleSelect(dieId: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(dieId)) next.delete(dieId);
      else next.add(dieId);
      return next;
    });
  }

  async function run(action: () => Promise<GameState>) {
    setError(null);
    setBusy(true);
    try {
      const next = await action();
      setGame(next);
      setSelected(new Set());
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  const selectedIds = Array.from(selected);
  const gameId = game?.gameId;

  return (
    <div className="app">
      <header className="app-header">
        <h1>DiceFight2026</h1>
        <button disabled={busy} onClick={() => run(() => api.createGame())}>
          New Game (Team A vs Team B)
        </button>
        {error && <div className="error">{error}</div>}
      </header>

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
            <div>
              <strong>{game.playerOne.name}:</strong> {game.playerOne.life} life
            </div>
            <div>
              <strong>{game.playerTwo.name}:</strong> {game.playerTwo.life} life
            </div>
          </section>

          <section className="actions">
            <ActionButton busy={busy} onClick={() => run(() => api.clearAndDraw(gameId))}>
              Clear &amp; Draw
            </ActionButton>
            <ActionButton busy={busy} onClick={() => run(() => api.advanceStep(gameId))}>
              Advance Step
            </ActionButton>
            <ActionButton busy={busy} onClick={() => run(() => api.rollAndReroll(gameId, selectedIds))}>
              Roll &amp; Reroll (selected = reroll)
            </ActionButton>
            <ActionButton
              busy={busy}
              onClick={() => run(() => api.purchase(gameId, selectedIds[0], selectedIds.slice(1)))}
            >
              Purchase (1st selected, rest = energy)
            </ActionButton>
            <ActionButton
              busy={busy}
              onClick={() => run(() => api.field(gameId, selectedIds[0], selectedIds.slice(1)))}
            >
              Field (1st selected, rest = energy)
            </ActionButton>
            <ActionButton
              busy={busy}
              onClick={() => run(() => api.useActionDie(gameId, selectedIds[0], selectedIds.slice(1)))}
            >
              Use Action Die (1st selected, rest = targets)
            </ActionButton>
            <ActionButton busy={busy} onClick={() => run(() => api.enterAttackStep(gameId))}>
              Enter Attack Step
            </ActionButton>
            <ActionButton busy={busy} onClick={() => run(() => api.skipAttackStep(gameId))}>
              Skip Attack Step
            </ActionButton>
            <ActionButton
              busy={busy}
              onClick={() => run(() => api.declareAttackers(gameId, selectedIds))}
            >
              Declare Attackers (selected)
            </ActionButton>
            <ActionButton busy={busy} onClick={() => run(() => api.declareBlockers(gameId, []))}>
              Declare Blockers (none)
            </ActionButton>
            <ActionButton
              busy={busy}
              onClick={() => run(() => api.assignCombatDamage(gameId, [], []))}
            >
              Assign Combat Damage (no blocks)
            </ActionButton>
            <ActionButton busy={busy} onClick={() => run(() => api.cleanUp(gameId))}>
              Clean Up
            </ActionButton>
          </section>

          <section className="boards">
            <PlayerBoard
              title={`${game.playerOne.name} (${game.playerOne.id})`}
              dice={game.dice.filter((d) => d.ownerId === game.playerOne.id)}
              dieLabel={dieLabel}
              selected={selected}
              onToggle={toggleSelect}
            />
            <PlayerBoard
              title={`${game.playerTwo.name} (${game.playerTwo.id})`}
              dice={game.dice.filter((d) => d.ownerId === game.playerTwo.id)}
              dieLabel={dieLabel}
              selected={selected}
              onToggle={toggleSelect}
            />
          </section>
        </>
      )}
    </div>
  );
}

function ActionButton(props: { busy: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button disabled={props.busy} onClick={props.onClick}>
      {props.children}
    </button>
  );
}

function PlayerBoard(props: {
  title: string;
  dice: Die[];
  dieLabel: (die: Die) => string;
  selected: Set<string>;
  onToggle: (dieId: string) => void;
}) {
  return (
    <div className="board">
      <h2>{props.title}</h2>
      {ZONES.map((zone) => {
        const inZone = props.dice.filter((d) => d.zone === zone);
        if (inZone.length === 0) return null;
        return (
          <div key={zone} className="zone">
            <h3>
              {zone} ({inZone.length})
            </h3>
            <div className="dice">
              {inZone.map((die) => (
                <button
                  key={die.id}
                  className={`die-chip${props.selected.has(die.id) ? " selected" : ""}`}
                  onClick={() => props.onToggle(die.id)}
                  title={die.id}
                >
                  {props.dieLabel(die)}
                  {die.status === "Energy" && ` (${die.energyKind})`}
                  {(die.status === "Character" || die.status === "SidekickCharacter") &&
                    ` L${die.level}${die.damage > 0 ? ` -${die.damage}dmg` : ""}`}
                </button>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

export default App;
