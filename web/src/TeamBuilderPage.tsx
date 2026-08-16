import { useDeferredValue, useEffect, useMemo, useState } from "react";
import { api } from "./api";
import { stashPendingGame } from "./gameHandoff";
import { navigate } from "./router";
import { SET_NAMES } from "./sets";
import type { CardDef } from "./types";

// Now a real team builder (see RULES_ENGINE_DESIGN.md's next-steps
// list) - browse/search/sort plus actually selecting cards into a
// team, with a shareable URL. Its own page/route (not a modal off the
// game view) since it has standalone value even to someone who never
// opens the live digital game - e.g. building a team to play with
// physical dice. The engine itself never enforces team-construction
// legality (house rules/alternate formats are common - see
// TeamSetup.cs's own remarks) - only this page's "Strict rules"
// checkbox does, and it can be turned off.

const BASIC_ACTION_TYPES = new Set(["BasicAction", "EpicBasicAction"]);
const MAX_UNIQUE_CARDS = 8;
const MAX_DICE = 20;
const MAX_BASIC_ACTIONS = 2;

function isBasicActionFamily(card: CardDef): boolean {
  return BASIC_ACTION_TYPES.has(card.type);
}

// Matches the old community Teambuilder tool's own URL style
// (`?cards=<count>x<slug>;<count>x<slug>...`, see its `maketeamlink`/
// `num2cardname`) so pasted-in old links resolve, not just a similarly
// -shaped scheme of our own. We always generate `<count>x<OURID>`
// (`4xMSW018`) - the old tool's own lowercase, reversed
// `<number><setcode>` slugs (`18msw`) only ever appear on the read
// side, translated below.
function encodeTeam(team: Map<string, number>): string {
  return [...team.entries()].map(([id, count]) => `${count}x${id}`).join(";");
}

// The old tool's slug is `(1-based position within its per-set array)
// + (set code, lowercased)` - e.g. "18msw" for the 18th card in MSW.
// Verified (see DESIGN_LOG.md) that this position lines up exactly
// with our own sheet-derived `SET+number` ids, so this is a pure
// string transform, not a lookup table: split the trailing letters off
// as the set code, uppercase it, zero-pad the leading digits to 3.
const OLD_SLUG_RE = /^(\d+)([a-z]+)$/i;

function toOurId(rawId: string): string {
  const m = OLD_SLUG_RE.exec(rawId);
  if (!m) return rawId; // already one of our own ids (or unresolvable - caller drops it)
  const [, number, setCode] = m;
  return `${setCode.toUpperCase()}${number.padStart(3, "0")}`;
}

// "<count>x<id>" - matched with a regex rather than split("x"), since
// some set codes contain their own "x" (AvX, XFC, XMF, XFO) that a
// naive split would cut on too.
const TEAM_ENTRY_RE = /^(\d+)x(.+)$/;

function decodeTeam(search: string): Map<string, number> {
  const params = new URLSearchParams(search);
  const raw = params.get("cards");
  const team = new Map<string, number>();
  if (!raw) return team;
  for (const entry of raw.split(";")) {
    const m = TEAM_ENTRY_RE.exec(entry);
    if (!m) continue;
    const [, countStr, rawId] = m;
    const count = Number(countStr);
    if (Number.isInteger(count) && count > 0) team.set(toOurId(rawId), count);
  }
  return team;
}

type SortKey =
  | "name" | "type" | "affiliations" | "set" | "purchaseCost" | "energyTypes" | "dieLimit"
  | "fieldingCost" | "attack" | "defense" | "implemented";

interface Level1Face {
  fieldingCost: number;
  attack: number;
  defense: number;
}

// Deliberate simplification vs. the old reference tool (which sorted by
// all 3 levels) - only Level 1 stats are sortable/shown as columns here;
// the full level progression is still available via the row's tooltip.
// Action/Basic Action cards have no levels at all.
function level1(card: CardDef): Level1Face | null {
  const face = card.levels[0];
  return face ? { fieldingCost: face.fieldingCost, attack: face.attack, defense: face.defense } : null;
}

function sortValue(card: CardDef, key: SortKey): string | number {
  const l1 = level1(card);
  switch (key) {
    case "name":
      return card.name.toLowerCase();
    case "type":
      return card.type;
    case "affiliations":
      return card.affiliations.join(",");
    case "set":
      return card.set ?? "";
    case "purchaseCost":
      return card.purchaseCost;
    case "energyTypes":
      return card.energyTypes.join(",");
    case "dieLimit":
      return card.dieLimit;
    case "fieldingCost":
      return l1?.fieldingCost ?? -1;
    case "attack":
      return l1?.attack ?? -1;
    case "defense":
      return l1?.defense ?? -1;
    case "implemented":
      return card.isImplemented ? 1 : 0;
  }
}

function cardTooltip(card: CardDef): string {
  const header = card.subtitle ? `${card.name} — ${card.subtitle}` : card.name;
  return `${header}\n\n${card.rawText || "(blank text box)"}`;
}

// Rendered before the row cap below applies, so typing narrows the
// visible count too - keeps a huge future catalog from ever forcing a
// full re-render on every keystroke (see the design doc's scaling note).
const MAX_ROWS = 200;

const COLUMNS: { key: SortKey; label: string }[] = [
  { key: "name", label: "Name" },
  { key: "type", label: "Type" },
  { key: "affiliations", label: "Affiliation" },
  { key: "set", label: "Set" },
  { key: "purchaseCost", label: "Cost" },
  { key: "energyTypes", label: "Energy" },
  { key: "dieLimit", label: "Max" },
  { key: "fieldingCost", label: "Field" },
  { key: "attack", label: "Atk" },
  { key: "defense", label: "Def" },
  { key: "implemented", label: "OK" },
];

export function TeamBuilderPage() {
  const [cards, setCards] = useState<CardDef[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search);
  const [activeTypes, setActiveTypes] = useState<Set<string>>(new Set());
  const [activeEnergyTypes, setActiveEnergyTypes] = useState<Set<string>>(new Set());
  const [activeAffiliations, setActiveAffiliations] = useState<Set<string>>(new Set());
  const [activeSets, setActiveSets] = useState<Set<string>>(new Set());
  const [showUnimplemented, setShowUnimplemented] = useState(false);
  const [sort, setSort] = useState<{ key: SortKey; direction: "asc" | "desc" }>({
    key: "name",
    direction: "asc",
  });
  const [team, setTeam] = useState<Map<string, number>>(new Map());
  const [strictRules, setStrictRules] = useState(true);
  const [copied, setCopied] = useState(false);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  useEffect(() => {
    api.getCards().then(setCards).catch((e) => setError(String(e)));
  }, []);

  // Load a shared team link once the catalog is available to resolve
  // its ids against - silently drops any id that doesn't resolve
  // (stale/typo'd link) rather than hard-failing.
  useEffect(() => {
    if (!cards) return;
    const fromUrl = decodeTeam(window.location.search);
    if (fromUrl.size === 0) return;
    const cardById = new Map(cards.map((c) => [c.id, c]));
    const resolved = new Map<string, number>();
    for (const [id, count] of fromUrl) {
      if (cardById.has(id)) resolved.set(id, count);
    }
    setTeam(resolved);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cards !== null]);

  const cardById = useMemo(() => new Map((cards ?? []).map((c) => [c.id, c])), [cards]);

  const teamEntries = useMemo(
    () =>
      [...team.entries()]
        .map(([id, count]) => ({ card: cardById.get(id), count }))
        .filter((e): e is { card: CardDef; count: number } => e.card !== undefined),
    [team, cardById],
  );
  const characterEntries = useMemo(() => teamEntries.filter((e) => !isBasicActionFamily(e.card)), [teamEntries]);
  const basicActionEntries = useMemo(() => teamEntries.filter((e) => isBasicActionFamily(e.card)), [teamEntries]);
  const uniqueNames = useMemo(() => new Set(characterEntries.map((e) => e.card.name)), [characterEntries]);
  const totalDice = useMemo(() => characterEntries.reduce((sum, e) => sum + e.count, 0), [characterEntries]);

  // Real rules 2.1.1/2.1.3/2.1.4/2.1.5: up to 8 unique-named Character/
  // Action cards, 1..dieLimit dice each summing to at most 20, exactly
  // 2 Basic Action cards (excluded from the dice cap). Only "over the
  // cap" counts as a violation - a team still being built naturally
  // passes through 0/1 Basic Actions or fewer than 8 cards on the way
  // to a complete team, that's not illegal, just incomplete.
  const violations = useMemo(() => {
    const list: string[] = [];
    if (uniqueNames.size > MAX_UNIQUE_CARDS) list.push(`${uniqueNames.size}/${MAX_UNIQUE_CARDS} unique cards`);
    if (totalDice > MAX_DICE) list.push(`${totalDice}/${MAX_DICE} dice`);
    if (basicActionEntries.length > MAX_BASIC_ACTIONS) list.push(`${basicActionEntries.length}/${MAX_BASIC_ACTIONS} Basic Actions`);
    return list;
  }, [uniqueNames, totalDice, basicActionEntries]);

  function canAddCard(card: CardDef): { ok: boolean; reason?: string } {
    if (team.has(card.id)) return { ok: false, reason: "Already on the team." };
    if (!strictRules) return { ok: true };
    if (isBasicActionFamily(card)) {
      if (basicActionEntries.length >= MAX_BASIC_ACTIONS) {
        return { ok: false, reason: `Already have ${MAX_BASIC_ACTIONS} Basic Actions.` };
      }
      return { ok: true };
    }
    if (uniqueNames.has(card.name)) return { ok: false, reason: `Already have a card named "${card.name}".` };
    if (uniqueNames.size >= MAX_UNIQUE_CARDS) return { ok: false, reason: `Already have ${MAX_UNIQUE_CARDS} cards.` };
    if (totalDice + 1 > MAX_DICE) return { ok: false, reason: `Would exceed ${MAX_DICE} dice.` };
    return { ok: true };
  }

  function canIncrement(card: CardDef, count: number): boolean {
    if (count >= card.dieLimit) return false;
    if (strictRules && totalDice + 1 > MAX_DICE) return false;
    return true;
  }

  function addCard(card: CardDef) {
    const next = new Map(team);
    next.set(card.id, isBasicActionFamily(card) ? card.dieLimit : 1);
    setTeam(next);
  }

  function removeCard(cardId: string) {
    const next = new Map(team);
    next.delete(cardId);
    setTeam(next);
  }

  function setCount(cardId: string, count: number) {
    const next = new Map(team);
    next.set(cardId, count);
    setTeam(next);
  }

  async function copyTeamLink() {
    const url = `${window.location.origin}${window.location.pathname}?cards=${encodeTeam(team)}`;
    await navigator.clipboard.writeText(url);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  // The opponent (Team B) is always a fresh random roster drawn from
  // IsImplemented cards server-side (RandomTeamBuilder) - there's no
  // opponent-selection UI, only a "build your own Team A" one.
  async function startGame() {
    setStarting(true);
    setStartError(null);
    try {
      const cardIds = [...characterEntries, ...basicActionEntries].map((e) => e.card.id);
      const game = await api.createGame(cardIds);
      stashPendingGame(game);
      navigate("/game");
    } catch (e) {
      setStartError(e instanceof Error ? e.message : String(e));
    } finally {
      setStarting(false);
    }
  }

  const allTypes = useMemo(() => [...new Set((cards ?? []).map((c) => c.type))].sort(), [cards]);
  const allEnergyTypes = useMemo(
    () => [...new Set((cards ?? []).flatMap((c) => c.energyTypes))].sort(),
    [cards],
  );
  // Affiliation is a proper checkbox filter, not folded into free-text
  // search - real players often recognize an affiliation by its printed
  // icon rather than its exact name, and this is mainly used to build a
  // single-affiliation team (e.g. "all X-Men"), which a filter serves
  // better than fuzzy text matching. No icon assets exist in this repo
  // (only energy-face icons - see DieIcon.tsx) and recreating trademarked
  // team logos ourselves isn't something to do casually, so this is
  // text-only for now. Values are the card data's own affiliation
  // strings (already deduplicated there - e.g. both Legion of Doom
  // Villains printings share the plain "Villains" string, not two
  // separate icon-specific variants), collapsed under a <details> since
  // the list can get long once more cards are added.
  const allAffiliations = useMemo(
    () => [...new Set((cards ?? []).flatMap((c) => c.affiliations))].sort(),
    [cards],
  );
  // Same collapsible-checkbox treatment as Affiliation, for the same
  // reason - "just the cards in a set" is a filtering task, not a
  // free-text search. A card without a known Set never matches an
  // active filter (same as it'd never match a real value).
  const allSets = useMemo(
    () => [...new Set((cards ?? []).map((c) => c.set).filter((s): s is string => s !== null))].sort(),
    [cards],
  );

  function toggle(set: Set<string>, setSet: (s: Set<string>) => void, value: string) {
    const next = new Set(set);
    if (next.has(value)) next.delete(value);
    else next.add(value);
    setSet(next);
  }

  function toggleSort(key: SortKey) {
    setSort((prev) =>
      prev.key === key ? { key, direction: prev.direction === "asc" ? "desc" : "asc" } : { key, direction: "asc" },
    );
  }

  const filtered = useMemo(() => {
    const needle = deferredSearch.trim().toLowerCase();
    return (cards ?? []).filter((c) => {
      if (!showUnimplemented && !c.isImplemented) return false;
      if (activeTypes.size > 0 && !activeTypes.has(c.type)) return false;
      if (activeEnergyTypes.size > 0 && !c.energyTypes.some((e) => activeEnergyTypes.has(e))) return false;
      if (activeAffiliations.size > 0 && !c.affiliations.some((a) => activeAffiliations.has(a))) return false;
      if (activeSets.size > 0 && (!c.set || !activeSets.has(c.set))) return false;
      if (needle.length === 0) return true;
      const setFullName = c.set ? SET_NAMES[c.set] : undefined;
      return (
        c.name.toLowerCase().includes(needle) ||
        (c.subtitle?.toLowerCase().includes(needle) ?? false) ||
        c.affiliations.some((a) => a.toLowerCase().includes(needle)) ||
        (c.set?.toLowerCase().includes(needle) ?? false) ||
        (setFullName?.toLowerCase().includes(needle) ?? false) ||
        c.rawText.toLowerCase().includes(needle)
      );
    });
  }, [cards, deferredSearch, activeTypes, activeEnergyTypes, activeAffiliations, activeSets, showUnimplemented]);

  const sorted = useMemo(() => {
    const dir = sort.direction === "asc" ? 1 : -1;
    return [...filtered].sort((a, b) => {
      const av = sortValue(a, sort.key);
      const bv = sortValue(b, sort.key);
      if (av < bv) return -1 * dir;
      if (av > bv) return 1 * dir;
      return a.name.localeCompare(b.name);
    });
  }, [filtered, sort]);

  const visible = sorted.slice(0, MAX_ROWS);

  return (
    <div className="app">
      <header className="app-header">
        <h1>DiceFight2026</h1>
        <button onClick={() => navigate("/game")}>Play Game</button>
        {error && <div className="error">{error}</div>}
      </header>

      <div className="app-layout">
        <div className="main-column">
          <h2>Team Builder - Card Search</h2>
          <p className="hint">
            Browse the full card catalog. "OK" means the card's full printed text is correctly modeled by the
            engine - unimplemented cards are hidden by default.
          </p>

          <div className="card-catalog-filters">
            <input
              type="text"
              placeholder="Search name, subtitle, or text..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <fieldset>
              <legend>Type</legend>
              {allTypes.map((t) => (
                <label key={t}>
                  <input
                    type="checkbox"
                    checked={activeTypes.has(t)}
                    onChange={() => toggle(activeTypes, setActiveTypes, t)}
                  />
                  {t}
                </label>
              ))}
            </fieldset>
            <fieldset>
              <legend>Energy</legend>
              {allEnergyTypes.map((t) => (
                <label key={t}>
                  <input
                    type="checkbox"
                    checked={activeEnergyTypes.has(t)}
                    onChange={() => toggle(activeEnergyTypes, setActiveEnergyTypes, t)}
                  />
                  {t}
                </label>
              ))}
            </fieldset>
            <details className="card-catalog-affiliations">
              <summary>
                Affiliation{activeAffiliations.size > 0 ? ` (${activeAffiliations.size} selected)` : ` (${allAffiliations.length})`}
              </summary>
              <div className="card-catalog-affiliations-options">
                {allAffiliations.map((a) => (
                  <label key={a}>
                    <input
                      type="checkbox"
                      checked={activeAffiliations.has(a)}
                      onChange={() => toggle(activeAffiliations, setActiveAffiliations, a)}
                    />
                    {a}
                  </label>
                ))}
              </div>
            </details>
            <details className="card-catalog-affiliations">
              <summary>
                Set{activeSets.size > 0 ? ` (${activeSets.size} selected)` : ` (${allSets.length})`}
              </summary>
              <div className="card-catalog-affiliations-options">
                {allSets.map((s) => (
                  <label key={s} title={SET_NAMES[s] ?? s}>
                    <input
                      type="checkbox"
                      checked={activeSets.has(s)}
                      onChange={() => toggle(activeSets, setActiveSets, s)}
                    />
                    {s}
                  </label>
                ))}
              </div>
            </details>
            <label>
              <input
                type="checkbox"
                checked={showUnimplemented}
                onChange={(e) => setShowUnimplemented(e.target.checked)}
              />
              Show not-yet-fully-implemented cards
            </label>
          </div>

          {cards === null ? (
            <p className="hint">Loading catalog...</p>
          ) : (
            <>
              <p className="hint">
                {sorted.length} card(s) match{sorted.length > MAX_ROWS ? ` (showing first ${MAX_ROWS} - narrow your search to see more)` : ""}.
              </p>
              <table className="card-catalog-table">
                <thead>
                  <tr>
                    <th />
                    {COLUMNS.map((col) => (
                      <th key={col.key} onClick={() => toggleSort(col.key)}>
                        {col.label}
                        {sort.key === col.key && <span className="sort-arrow">{sort.direction === "asc" ? " ▲" : " ▼"}</span>}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {visible.map((c) => {
                    const l1 = level1(c);
                    const add = canAddCard(c);
                    return (
                      <tr key={c.id} className={c.isImplemented ? "" : "unimplemented"} title={cardTooltip(c)}>
                        <td>
                          <button
                            className="team-add-button"
                            disabled={!add.ok}
                            title={add.reason}
                            onClick={() => addCard(c)}
                          >
                            +
                          </button>
                        </td>
                        <td>
                          {c.name}
                          {c.subtitle && <span className="hint"> — {c.subtitle}</span>}
                        </td>
                        <td>{c.type}</td>
                        <td>{c.affiliations.join(", ") || "-"}</td>
                        <td title={c.set ? SET_NAMES[c.set] : undefined}>{c.set ?? "-"}</td>
                        <td>{c.purchaseCost}</td>
                        <td>{c.energyTypes.join("/")}</td>
                        <td>{c.dieLimit}</td>
                        <td>{l1?.fieldingCost ?? "-"}</td>
                        <td>{l1?.attack ?? "-"}</td>
                        <td>{l1?.defense ?? "-"}</td>
                        <td>{c.isImplemented ? "✓" : ""}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </>
          )}
        </div>

        <div className="team-sidebar">
          <h3>Team</h3>
          <p className="hint">
            {uniqueNames.size}/{MAX_UNIQUE_CARDS} cards, {totalDice}/{MAX_DICE} dice,{" "}
            {basicActionEntries.length}/{MAX_BASIC_ACTIONS} Basic Actions
          </p>
          {violations.length > 0 && (
            <p className="team-violations">Over the rules: {violations.join(", ")}</p>
          )}

          {characterEntries.length === 0 && basicActionEntries.length === 0 ? (
            <p className="hint">No cards yet - click "+" on a card to add it.</p>
          ) : (
            <ul className="team-list">
              {[...characterEntries, ...basicActionEntries].map(({ card, count }) => (
                <li key={card.id} className="team-list-item">
                  <div>
                    <div className="team-card-name">{card.name}</div>
                    {card.subtitle && <div className="hint">{card.subtitle}</div>}
                  </div>
                  {isBasicActionFamily(card) ? (
                    <span className="hint">{count} dice</span>
                  ) : (
                    <span className="team-stepper">
                      <button
                        disabled={count <= 1}
                        onClick={() => setCount(card.id, count - 1)}
                      >
                        −
                      </button>
                      {count}
                      <button
                        disabled={!canIncrement(card, count)}
                        onClick={() => setCount(card.id, count + 1)}
                      >
                        +
                      </button>
                    </span>
                  )}
                  <button className="team-remove-button" onClick={() => removeCard(card.id)}>
                    Remove
                  </button>
                </li>
              ))}
            </ul>
          )}

          <label className="team-strict-toggle">
            <input
              type="checkbox"
              checked={strictRules}
              onChange={(e) => setStrictRules(e.target.checked)}
            />
            Strict rules (2.1.1/2.1.3-2.1.5)
          </label>

          <button onClick={copyTeamLink} disabled={team.size === 0}>
            {copied ? "Copied!" : "Copy team link"}
          </button>

          <button
            className="team-start-game-button"
            disabled={team.size === 0 || violations.length > 0 || starting}
            title={
              team.size === 0
                ? "Add some cards first."
                : violations.length > 0
                  ? `Fix team violations first: ${violations.join(", ")}`
                  : undefined
            }
            onClick={startGame}
          >
            {starting ? "Starting..." : "Start Game with This Team"}
          </button>
          {startError && <div className="error">{startError}</div>}
        </div>
      </div>
    </div>
  );
}
