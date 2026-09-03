import { useEffect, useRef, useState } from "react";
import "./instinct.css";
import { api } from "./api";
import { CHAMPION_ICONS, CHARACTER_ICONS } from "./icons";
import { claimSeatFromUrl, inviteLink, nameClaimedSeat, rememberSeats } from "./seats";
import type { CardDef, Die, GameState } from "./types";

const POLL_INTERVAL_MS = 2000;
const CHAMPIONS = [
  { id: "Lion", energy: "Claw" },
  { id: "Armadillo", energy: "Shell" },
  { id: "GoldenEagle", energy: "Wing" },
  { id: "GreatHornedOwl", energy: "Eye" },
];

function rolled(d: Die): boolean {
  return d.effectiveAttack !== null || d.energySymbolId !== null;
}

function PipBadge({ type, amount }: { type: string; amount: number }) {
  const cssVar = type === "Wild" ? "var(--wild)" : `var(--${type.toLowerCase()})`;
  return (
    <span className="pip" style={{ background: cssVar }}>
      {amount} {type}
    </span>
  );
}

function DieTile({
  die,
  cardsById,
  onClick,
  clickable,
  picked,
  accent,
}: {
  die: Die;
  cardsById: Map<string, { name: string }>;
  onClick?: () => void;
  clickable?: boolean;
  picked?: boolean;
  accent?: string;
}) {
  const isRolled = rolled(die);
  const Avatar = die.cardId ? CHARACTER_ICONS[die.cardId] : null;
  const label = die.cardId ? (cardsById.get(die.cardId)?.name ?? die.cardId) : "Tardigrade";
  const cls = ["dietile", clickable ? "clickable" : "", picked ? "picked" : ""].filter(Boolean).join(" ");
  const style = accent ? ({ textAlign: "center", ["--cc" as string]: accent, color: accent } as const) : { textAlign: "center" as const };
  return (
    <button type="button" className={cls} onClick={onClick} disabled={!clickable} style={style}>
      {!isRolled ? (
        <>
          <div className="lbl">{label}</div>
          <div className="stat">—</div>
        </>
      ) : die.effectiveAttack === null ? (
        <>
          <div className="lbl">Surge</div>
          <div className="stat">—</div>
          {die.energySymbolId && die.energyAmount > 0 && (
            <PipBadge type={die.energySymbolId} amount={die.energyAmount} />
          )}
        </>
      ) : (
        <>
          {Avatar && <Avatar size={34} />}
          <div className="stat">
            {die.effectiveAttack}/{die.effectiveDefense}
          </div>
          <div className="lbl">
            L{die.level}
            {die.isTardigrade && !die.energySymbolId ? " · free" : ""}
          </div>
          {die.energySymbolId && die.energyAmount > 0 && (
            <PipBadge type={die.energySymbolId} amount={die.energyAmount} />
          )}
        </>
      )}
    </button>
  );
}

export function InstinctClashPage() {
  const [game, setGame] = useState<GameState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [setupA, setSetupA] = useState<string | null>(null);
  const [setupB, setSetupB] = useState<string | null>(null);

  // Local, not-yet-submitted UI state for the two multi-step interactions
  // the API can't drive from a single button click: which energy dice pay
  // a field/purchase cost, and which blocker goes on which attacker.
  const [pending, setPending] = useState<{ type: "field" | "purchase"; dieId?: string; cardId?: string; selected: string[] } | null>(null);
  const [attackers, setAttackers] = useState<string[]>([]);
  const [blocks, setBlocks] = useState<Record<string, string | null>>({});
  const [blockPick, setBlockPick] = useState<string | null>(null);
  const [cardsById, setCardsById] = useState<Map<string, CardDef>>(new Map());

  useEffect(() => {
    api.getCards().then((cards) => setCardsById(new Map(cards.map((c) => [c.id, c]))));
  }, []);

  // Invite-link join, same shape as ../App.tsx's own effect.
  useEffect(() => {
    const claim = claimSeatFromUrl();
    if (!claim) return;
    api
      .getGame(claim.gameId)
      .then((joined) => {
        if (joined.yourPlayerId) nameClaimedSeat(claim.gameId, joined.yourPlayerId);
        setGame(joined);
      })
      .catch((e) => setError(`Could not join that game: ${e instanceof Error ? e.message : String(e)}`));
  }, []);

  // Poll for the other player's moves - same version-compare shape as v1.
  const gameId = game?.gameId ?? null;
  const gameVersion = game?.version ?? 0;
  useEffect(() => {
    if (!gameId) return;
    let cancelled = false;
    const timer = window.setInterval(async () => {
      if (busyRef.current) return;
      try {
        const latest = await api.getGame(gameId);
        if (!cancelled && latest.version !== gameVersion) setGame(latest);
      } catch {
        // quiet - the next poll in two seconds either works or it doesn't matter yet
      }
    }, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [gameId, gameVersion]);

  async function run(fn: () => Promise<GameState>) {
    setBusy(true);
    busyRef.current = true;
    setError(null);
    try {
      const next = await fn();
      setGame(next);
      return next;
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      throw e;
    } finally {
      setBusy(false);
      busyRef.current = false;
    }
  }

  async function startMatch() {
    if (!setupA || !setupB) return;
    await run(async () => {
      const created = await api.createGame(setupA, setupB);
      rememberSeats(created.game.gameId, created.seats);
      return created.game;
    });
  }

  if (!game) {
    return (
      <div className="instinct">
        <p className="eyebrow" style={{ opacity: 0.6, fontSize: 12, textTransform: "uppercase", letterSpacing: "0.1em" }}>
          DiceFight v3
        </p>
        <h1>Instinct Clash</h1>
        <p className="dek">
          Pass-and-play, or send the other seat an invite link once the match starts. Runs on the real rules
          engine - a small pool, simple abilities, mostly for reacting to how the system feels.
        </p>
        {error && <p className="error">{error}</p>}
        <div className="panel">
          <h3 style={{ margin: "0 0 10px" }}>Player 1</h3>
          <div className="champ-pick">
            {CHAMPIONS.map((c) => {
              const Icon = CHAMPION_ICONS[c.id];
              return (
                <button
                  key={c.id}
                  type="button"
                  className={`champ-opt${setupA === c.id ? " selected" : ""}`}
                  style={{ ["--sel" as string]: `var(--${c.energy.toLowerCase()})`, color: `var(--${c.energy.toLowerCase()})` }}
                  onClick={() => setSetupA(c.id)}
                >
                  <Icon />
                  <div className="cname" style={{ color: "var(--text-h)" }}>
                    {c.id.replace(/([A-Z])/g, " $1").trim()}
                  </div>
                </button>
              );
            })}
          </div>
          <h3 style={{ margin: "0 0 10px" }}>Player 2</h3>
          <div className="champ-pick">
            {CHAMPIONS.map((c) => {
              const Icon = CHAMPION_ICONS[c.id];
              return (
                <button
                  key={c.id}
                  type="button"
                  className={`champ-opt${setupB === c.id ? " selected" : ""}`}
                  style={{ ["--sel" as string]: `var(--${c.energy.toLowerCase()})`, color: `var(--${c.energy.toLowerCase()})` }}
                  onClick={() => setSetupB(c.id)}
                >
                  <Icon />
                  <div className="cname" style={{ color: "var(--text-h)" }}>
                    {c.id.replace(/([A-Z])/g, " $1").trim()}
                  </div>
                </button>
              );
            })}
          </div>
          <button className="btn" disabled={!setupA || !setupB || busy} onClick={startMatch}>
            Start Match
          </button>
        </div>
      </div>
    );
  }

  const you = game.yourPlayerId ?? game.playerOne.id;
  const isYourTurn = you === game.activePlayerId;
  const opponentId = you === game.playerOne.id ? game.playerTwo.id : game.playerOne.id;

  function diceFor(playerId: string, zone?: string) {
    return game!.dice.filter((d) => d.controllerId === playerId && (!zone || d.zone === zone));
  }

  function beginPayment(type: "field" | "purchase", dieId?: string, cardId?: string) {
    setPending({ type, dieId, cardId, selected: [] });
  }

  function costFor(): { amount: number; matchType: string | null } {
    if (!pending) return { amount: 0, matchType: null };
    if (pending.type === "field") {
      const die = game!.dice.find((d) => d.id === pending.dieId)!;
      if (!die.cardId || die.level === null) return { amount: 0, matchType: null }; // Tardigrade - free
      const card = cardsById.get(die.cardId);
      return { amount: card?.levels[die.level - 1]?.fieldingCost ?? 0, matchType: null };
    }
    const card = cardsById.get(pending.cardId!);
    return { amount: card?.purchaseCost ?? 0, matchType: card?.energyTypes[0] ?? null };
  }

  async function confirmPayment() {
    if (!pending) return;
    if (pending.type === "field") {
      await run(() => api.field(game!.gameId, pending.dieId!, pending.selected));
    } else {
      const unpurchased = game!.dice.find((d) => d.cardId === pending.cardId && d.zone === "Unpurchased")!;
      await run(() => api.purchase(game!.gameId, unpurchased.id, pending.selected));
    }
    setPending(null);
  }

  function renderBoard(playerId: string, label: string) {
    const player = playerId === game!.playerOne.id ? game!.playerOne : game!.playerTwo;
    const ChampIcon = player.champion ? CHAMPION_ICONS[player.champion.id] : null;
    const accent = player.champion ? `var(--${player.champion.energySymbolId.toLowerCase()})` : undefined;
    const field = diceFor(playerId, "FieldZone");
    const reserve = diceFor(playerId, "ReservePool");
    const prep = diceFor(playerId, "PrepArea");
    const unpurchased = diceFor(playerId, "Unpurchased");
    const unpurchasedByCard = new Map<string, Die[]>();
    for (const d of unpurchased) {
      if (!d.cardId) continue;
      unpurchasedByCard.set(d.cardId, [...(unpurchasedByCard.get(d.cardId) ?? []), d]);
    }
    const active = playerId === game!.activePlayerId;

    return (
      <div key={playerId}>
        {player.champion && ChampIcon && (
          <div className="champbanner" style={{ color: accent, ["--cc" as string]: accent }}>
            <ChampIcon />
            <div>
              <div className="cbname" style={{ color: "var(--text-h)" }}>
                {label} — {player.champion.name}
              </div>
              <div className="cbpassive">{player.champion.passiveText}</div>
            </div>
          </div>
        )}
        <div className="zone">
          <h4>
            Field <span className="count">{field.length}</span>
          </h4>
          <div className="dierow">
            {field.map((d) => (
              <DieTile
                key={d.id}
                die={d}
                cardsById={cardsById}
                accent={accent}
                clickable={active && isYourTurn && game!.currentStepId === "select-attackers" && playerId === you}
                picked={attackers.includes(d.id)}
                onClick={() =>
                  setAttackers((a) => (a.includes(d.id) ? a.filter((x) => x !== d.id) : [...a, d.id]))
                }
              />
            ))}
          </div>
        </div>
        <div className="zone">
          <h4>
            Reserve Pool <span className="count">{reserve.length}</span>
          </h4>
          <div className="dierow">
            {reserve.map((d) => {
              const eligibleField =
                isYourTurn && playerId === you && game!.currentStepId === "main" && !pending && rolled(d) && d.effectiveAttack !== null;
              const eligibleSpend =
                pending &&
                isYourTurn &&
                playerId === you &&
                d.id !== pending.dieId &&
                d.energyAmount > 0 &&
                (pending.type === "field" || d.energySymbolId === costFor().matchType || d.energySymbolId === "Wild");
              return (
                <DieTile
                  key={d.id}
                  die={d}
                  cardsById={cardsById}
                  accent={accent}
                  clickable={!!(eligibleField || eligibleSpend)}
                  picked={pending?.selected.includes(d.id)}
                  onClick={() => {
                    if (eligibleField) beginPayment("field", d.id);
                    else if (eligibleSpend)
                      setPending((p) =>
                        p
                          ? { ...p, selected: p.selected.includes(d.id) ? p.selected.filter((x) => x !== d.id) : [...p.selected, d.id] }
                          : p,
                      );
                  }}
                />
              );
            })}
          </div>
        </div>
        {prep.length > 0 && (
          <div className="zone">
            <h4>
              Prep Area <span className="count">{prep.length}</span>
            </h4>
            <div className="dierow">
              {prep.map((d) => (
                <DieTile key={d.id} die={d} cardsById={cardsById} accent={accent} />
              ))}
            </div>
          </div>
        )}
        {isYourTurn && playerId === you && game!.currentStepId === "main" && !pending && unpurchasedByCard.size > 0 && (
          <div className="zone">
            <h4>Available to purchase</h4>
            <div className="dierow">
              {[...unpurchasedByCard.entries()].map(([cardId, dice]) => {
                const card = cardsById.get(cardId);
                const Avatar = CHARACTER_ICONS[cardId];
                return (
                  <button
                    key={cardId}
                    type="button"
                    className="dietile clickable"
                    style={accent ? ({ ["--cc" as string]: accent, color: accent } as const) : undefined}
                    onClick={() => beginPayment("purchase", undefined, cardId)}
                  >
                    {Avatar && <Avatar size={34} />}
                    <div className="lbl">Buy {card?.name ?? cardId}</div>
                    <div className="stat">
                      {card?.purchaseCost} {card?.energyTypes[0]}
                    </div>
                    <div className="lbl">{dice.length} left</div>
                  </button>
                );
              })}
            </div>
          </div>
        )}
      </div>
    );
  }

  const cost = costFor();
  const link = inviteLink(game.gameId);

  return (
    <div className="instinct">
      <p className="eyebrow" style={{ opacity: 0.6, fontSize: 12, textTransform: "uppercase", letterSpacing: "0.1em" }}>
        DiceFight v3
      </p>
      <h1>Instinct Clash</h1>
      {error && <p className="error">{error}</p>}
      {link && (
        <p className="invite">
          Invite link for the other seat: <a href={link}>{link}</a>
        </p>
      )}

      <details className="how">
        <summary>How to play</summary>
        <ul>
          <li>Draw, then Roll - each die may be rerolled once before Finish Roll.</li>
          <li>Field a rolled creature (Tardigrades are free; your Character costs energy, any type) or Purchase another copy of your Character (matching type or Wild only).</li>
          <li>Proceed to Attack, pick attackers; the other seat assigns blockers, then Resolve Combat.</li>
        </ul>
      </details>

      <div className="topbar">
        <div className="lifebox" style={{ borderColor: game.activePlayerId === game.playerOne.id ? `var(--${game.playerOne.champion?.energySymbolId.toLowerCase()})` : undefined }}>
          {game.playerOne.champion && CHAMPION_ICONS[game.playerOne.champion.id] && (
            <span style={{ color: `var(--${game.playerOne.champion.energySymbolId.toLowerCase()})` }}>
              {(() => {
                const I = CHAMPION_ICONS[game.playerOne.champion.id];
                return <I />;
              })()}
            </span>
          )}
          <span className="lnum">{game.playerOne.life}</span>
        </div>
        <div className="phasepill">
          {game.activePlayerId === you ? "Your" : "Opponent's"} turn · {game.currentStepId}
        </div>
        <div className="lifebox">
          {game.playerTwo.champion && CHAMPION_ICONS[game.playerTwo.champion.id] && (
            <span style={{ color: `var(--${game.playerTwo.champion.energySymbolId.toLowerCase()})` }}>
              {(() => {
                const I = CHAMPION_ICONS[game.playerTwo.champion.id];
                return <I />;
              })()}
            </span>
          )}
          <span className="lnum">{game.playerTwo.life}</span>
        </div>
      </div>

      {game.pendingChoice && you === game.pendingChoice.controllerId ? (
        <div className="panel">
          <p>
            <b>{game.pendingChoice.description}</b>
          </p>
          <PendingChoiceChips
            candidateIds={game.pendingChoice.candidateIds}
            max={game.pendingChoice.maxCount}
            dice={game.dice}
            cardsById={cardsById}
            onSubmit={(ids) => run(() => api.resolvePendingChoice(game.gameId, ids))}
          />
        </div>
      ) : game.pendingChoice ? (
        <p className="dek">Waiting on the other player's choice…</p>
      ) : !isYourTurn && game.currentStepId !== "assign-blockers" ? (
        <p className="dek">Waiting on the other player…</p>
      ) : !isYourTurn ? null : (
        <div className="actionrow" style={{ margin: "10px 0" }}>
          {game.currentStepId === "start-of-turn" && (
            <button className="btn" disabled={busy} onClick={() => run(() => api.clearAndDraw(game.gameId))}>
              Draw
            </button>
          )}
          {game.currentStepId === "roll-and-reroll" && (
            <RollControls game={game} you={you} run={run} />
          )}
          {game.currentStepId === "main" && !pending && (
            <button className="btn" disabled={busy} onClick={() => run(() => api.enterAttackStep(game.gameId))}>
              Proceed to Attack
            </button>
          )}
          {game.currentStepId === "select-attackers" && (
            <button
              className="btn"
              disabled={busy}
              onClick={async () => {
                await run(() => api.declareAttackers(game.gameId, attackers));
                setAttackers([]);
              }}
            >
              Confirm Attackers ({attackers.length})
            </button>
          )}
          {game.currentStepId === "return-to-field" && (
            <button className="btn" disabled={busy} onClick={() => run(() => api.cleanUp(game.gameId))}>
              End Turn
            </button>
          )}
        </div>
      )}

      {game.currentStepId === "assign-blockers" && !game.pendingChoice && !isYourTurn && (
        <BlockPanel
          game={game}
          you={you}
          blocks={blocks}
          setBlocks={setBlocks}
          blockPick={blockPick}
          setBlockPick={setBlockPick}
          run={run}
          onResolved={() => {
            setBlocks({});
            setBlockPick(null);
          }}
        />
      )}
      {game.currentStepId === "action-global-window" && !game.pendingChoice && isYourTurn && (
        <div className="panel">
          <button
            className="btn"
            disabled={busy}
            onClick={() =>
              run(() =>
                api.assignCombatDamage(
                  game.gameId,
                  Object.entries(blocks)
                    .filter(([, b]) => b)
                    .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId: blockerDieId! })),
                ),
              )
            }
          >
            Resolve Combat
          </button>
        </div>
      )}

      {pending && (
        <div className="panel">
          <p>
            <b>
              {pending.type === "field"
                ? `Field ${cardsById.get(game.dice.find((d) => d.id === pending.dieId)?.cardId ?? "")?.name ?? "Tardigrade"}`
                : `Purchase ${cardsById.get(pending.cardId!)?.name}`}
            </b>{" "}
            — cost {cost.amount}. {pending.type === "field" ? "Any energy type counts." : `Only ${cost.matchType} or Wild energy counts.`}{" "}
            Selected:{" "}
            {pending.selected.reduce((sum, id) => sum + (game.dice.find((d) => d.id === id)?.energyAmount ?? 0), 0)} / {cost.amount}
          </p>
          <div className="actionrow">
            <button
              className="btn"
              disabled={
                busy ||
                pending.selected.reduce((sum, id) => sum + (game.dice.find((d) => d.id === id)?.energyAmount ?? 0), 0) < cost.amount
              }
              onClick={confirmPayment}
            >
              Confirm
            </button>
            <button className="btn ghost" onClick={() => setPending(null)}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {renderBoard(you, "You")}
      <hr style={{ border: "none", borderTop: "1px dashed var(--border)", margin: "18px 0" }} />
      {renderBoard(opponentId, "Opponent")}
    </div>
  );
}

function RollControls({ game, you, run }: { game: GameState; you: string; run: (fn: () => Promise<GameState>) => Promise<GameState> }) {
  const [rerolled, setRerolled] = useState<string[]>([]);
  const prep = game.dice.filter((d) => d.controllerId === you && d.zone === "PrepArea");
  const anyRolled = prep.some(rolled);
  if (!anyRolled) {
    return (
      <button className="btn" onClick={() => run(() => api.roll(game.gameId))}>
        Roll Reserve
      </button>
    );
  }
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
      <div className="dierow">
        {prep.map((d) => (
          <button
            key={d.id}
            type="button"
            className={`dietile${rerolled.includes(d.id) ? "" : " clickable"}`}
            disabled={rerolled.includes(d.id)}
            onClick={async () => {
              await run(() => api.reroll(game.gameId, [d.id]));
              setRerolled((r) => [...r, d.id]);
            }}
          >
            <div className="stat">
              {d.effectiveAttack ?? "—"}
              {d.effectiveDefense !== null ? `/${d.effectiveDefense}` : ""}
            </div>
            <div className="lbl">{rerolled.includes(d.id) ? "kept" : "tap to reroll"}</div>
          </button>
        ))}
      </div>
      <button className="btn" onClick={() => run(() => api.finishRoll(game.gameId))}>
        Continue to Main Phase
      </button>
    </div>
  );
}

function PendingChoiceChips({
  candidateIds,
  max,
  dice,
  cardsById,
  onSubmit,
}: {
  candidateIds: string[];
  max: number;
  dice: Die[];
  cardsById: Map<string, { name: string }>;
  onSubmit: (ids: string[]) => void;
}) {
  const [picked, setPicked] = useState<string[]>([]);
  return (
    <>
      <div className="chiprow">
        {candidateIds.map((id) => {
          const die = dice.find((d) => d.id === id);
          const name = die?.cardId ? (cardsById.get(die.cardId)?.name ?? die.cardId) : "Tardigrade";
          return (
            <span
              key={id}
              className={`chip${picked.includes(id) ? " on" : ""}`}
              onClick={() =>
                setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : p.length < max ? [...p, id] : p))
              }
            >
              {name} {die?.effectiveAttack}/{die?.effectiveDefense}
            </span>
          );
        })}
      </div>
      <button className="btn" disabled={picked.length === 0} onClick={() => onSubmit(picked)}>
        Confirm Choice
      </button>
    </>
  );
}

function BlockPanel({
  game,
  you,
  blocks,
  setBlocks,
  blockPick,
  setBlockPick,
  run,
  onResolved,
}: {
  game: GameState;
  you: string;
  blocks: Record<string, string | null>;
  setBlocks: (u: (b: Record<string, string | null>) => Record<string, string | null>) => void;
  blockPick: string | null;
  setBlockPick: (id: string | null) => void;
  run: (fn: () => Promise<GameState>) => Promise<GameState>;
  onResolved: () => void;
}) {
  const attackerDice = game.dice.filter((d) => d.zone === "AttackZone");
  const blockers = game.dice.filter((d) => d.controllerId === you && d.zone === "FieldZone");
  const used = new Set(Object.values(blocks).filter((v): v is string => !!v));
  return (
    <div className="panel">
      <p>
        <b>Assign blockers.</b> Click an attacker, then a defender to block it (or leave unblocked).
      </p>
      <div className="chiprow">
        {attackerDice.map((a) => (
          <span key={a.id} className="chip" onClick={() => setBlockPick(a.id)}>
            {a.effectiveAttack}/{a.effectiveDefense} ← {blocks[a.id] ? "blocked" : "unblocked"}
          </span>
        ))}
      </div>
      {blockPick && (
        <div className="chiprow">
          <span className="chip" onClick={() => setBlocks((b) => ({ ...b, [blockPick]: null }))}>
            no block
          </span>
          {blockers
            .filter((b) => !used.has(b.id) || blocks[blockPick] === b.id)
            .map((b) => (
              <span
                key={b.id}
                className={`chip${blocks[blockPick] === b.id ? " on" : ""}`}
                onClick={() => setBlocks((prev) => ({ ...prev, [blockPick]: b.id }))}
              >
                {b.effectiveAttack}/{b.effectiveDefense}
              </span>
            ))}
        </div>
      )}
      <button
        className="btn"
        onClick={async () => {
          const assignments = Object.entries(blocks)
            .filter(([, v]) => v)
            .map(([attackerDieId, blockerDieId]) => ({ attackerDieId, blockerDieId: blockerDieId! }));
          await run(() => api.declareBlockers(game.gameId, assignments));
          onResolved();
        }}
      >
        Confirm Blocks
      </button>
    </div>
  );
}
