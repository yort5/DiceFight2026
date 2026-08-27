import { Fragment, useDeferredValue, useEffect, useMemo, useState } from "react";
import { matchesQuery } from "./cardSearch";
import { api } from "./api";
import { stashPendingGame } from "./gameHandoff";
import { navigate } from "./router";
import { SET_NAMES } from "./sets";
import { FORMATS } from "./formats";
import { ORANGE_BAN_LIST, isOrangeBanned } from "./orangeBan";
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
  | "level1" | "level2" | "level3" | "implemented";

// One column per level, replacing the old single-level Field/Atk/Def
// trio: which level you want is usually the whole question, and the
// catalog only ever showed level 1.
//
// The parts are separated by a bullet rather than run together, because
// run-together digits are genuinely ambiguous once a stat reaches double
// figures - "3108" reads as either 3/10/8 or 3/1/08, which is exactly
// the confusion the sheet's own stat lines caused for Slifer, Ra and
// White Lantern Dove (see DESIGN_LOG.md). "3•10•8" cannot be misread.
const STAT_SEPARATOR = "\u2022";

function levelText(card: CardDef, index: number): string {
  const face = card.levels[index];
  if (!face) return "-";
  return [face.fieldingCost, face.attack, face.defense].join(STAT_SEPARATOR);
}

// Sorting a composite F-A-D cell has to pick something; attack is what
// people actually rank characters by, and the column header says so.
function levelSortValue(card: CardDef, index: number): number {
  return card.levels[index]?.attack ?? -1;
}

function sortValue(card: CardDef, key: SortKey): string | number {
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
    case "level1":
      return levelSortValue(card, 0);
    case "level2":
      return levelSortValue(card, 1);
    case "level3":
      return levelSortValue(card, 2);
    case "implemented":
      return card.isImplemented ? 1 : 0;
  }
}

// The rarity TIERS a card can be filtered by, in printed order. "Super"
// and "Super-Rare" are one tier spelled two ways across the sheet's tabs
// (171 vs 16 cards), so they collapse into a single option - a "Super
// Rare only" format has to catch both or it silently drops 16 cards.
const RARITY_TIERS = ["Common", "Uncommon", "Rare", "Super Rare", "Chase", "Promo"] as const;

function rarityTier(rarity: string | null): string | null {
  if (rarity === "Super" || rarity === "Super-Rare") return "Super Rare";
  return rarity;
}

// Rarity colour-coding, matching the old Teambuilder's stripe colours.
// "Super" and "Super-Rare" are the same tier spelled two ways across the
// sheet's tabs, so they share a class. Chase (4 cards in the whole
// catalog) is rarer still and has no assigned colour yet - it gets its
// own class so it is at least distinguishable rather than silently
// falling in with Common.
function rarityClass(rarity: string | null): string {
  switch (rarityTier(rarity)) {
    case "Common": return "rarity-common";
    case "Uncommon": return "rarity-uncommon";
    case "Rare": return "rarity-rare";
    case "Super Rare": return "rarity-super";
    case "Chase": return "rarity-chase";
    case "Promo": return "rarity-promo";
    default: return "";
  }
}

// Rendered before the row cap below applies, so typing narrows the
// visible count too - keeps a huge future catalog from ever forcing a
// full re-render on every keystroke (see the design doc's scaling note).
const MAX_ROWS = 200;

const LEVEL_TITLE = `Fielding cost ${STAT_SEPARATOR} attack ${STAT_SEPARATOR} defense. Sorts by attack.`;

const COLUMNS: { key: SortKey; label: string; title?: string }[] = [
  { key: "set", label: "Set" },
  { key: "name", label: "Name" },
  { key: "type", label: "Type" },
  { key: "affiliations", label: "Affiliation" },
  { key: "purchaseCost", label: "Cost" },
  { key: "energyTypes", label: "Energy" },
  { key: "dieLimit", label: "Max" },
  { key: "level1", label: "L1", title: LEVEL_TITLE },
  { key: "level2", label: "L2", title: LEVEL_TITLE },
  { key: "level3", label: "L3", title: LEVEL_TITLE },
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
  const [activeRarities, setActiveRarities] = useState<Set<string>>(new Set());
  // null means "unbounded" so the filter stays off until the user touches
  // it - and so a catalog that later gains a costlier card is not silently
  // excluded by a default that was baked in today.
  const [minCost, setMinCost] = useState<number | null>(null);
  const [maxCost, setMaxCost] = useState<number | null>(null);
  // Defaults to TRUE: the catalog is useful as a reference long before the
  // simulator covers it, and in this phase it gets far more use than the
  // game does, so hiding four fifths of the cards by default is backwards.
  const [showUnimplemented, setShowUnimplemented] = useState(true);
  // Format is a single-select preset (the presets are mutually
  // exclusive release-order cutoffs), while the Orange Ban list is a
  // separate checkbox layered on top - it is normally used in
  // conjunction with a format rather than instead of one.
  const [formatId, setFormatId] = useState<string>("");
  const [applyOrangeBan, setApplyOrangeBan] = useState(false);
  const [sort, setSort] = useState<{ key: SortKey; direction: "asc" | "desc" }>({
    key: "name",
    direction: "asc",
  });
  const [team, setTeam] = useState<Map<string, number>>(new Map());
  const [teamRestored, setTeamRestored] = useState(false);
  const [strictRules, setStrictRules] = useState(true);
  const [copied, setCopied] = useState(false);
  const [copiedOld, setCopiedOld] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  useEffect(() => {
    api.getCards().then(setCards).catch((e) => setError(String(e)));
  }, []);

  // Teams survive a browser restart. A shared ?cards= link still wins, so
  // opening someone else's team never silently resurrects your own; only
  // when there is no link do we fall back to what was saved here.
  // Everything is wrapped in try/catch: storage throws outright in some
  // privacy modes rather than just coming back empty.
  const STORAGE_KEY = "dicefight.teamBuilder.team";

  function loadSavedTeam(): Map<string, number> {
    try {
      const raw = window.localStorage.getItem(STORAGE_KEY);
      if (!raw) return new Map();
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return new Map();
      const out = new Map<string, number>();
      for (const entry of parsed) {
        if (Array.isArray(entry) && typeof entry[0] === "string" && Number.isInteger(entry[1]) && entry[1] > 0) {
          out.set(entry[0], entry[1]);
        }
      }
      return out;
    } catch {
      return new Map();
    }
  }

  // Load a shared team link once the catalog is available to resolve
  // its ids against - silently drops any id that doesn't resolve
  // (stale/typo'd link) rather than hard-failing.
  useEffect(() => {
    if (!cards) return;
    const fromUrl = decodeTeam(window.location.search);
    const source = fromUrl.size > 0 ? fromUrl : loadSavedTeam();
    if (source.size === 0) {
      setTeamRestored(true);
      return;
    }
    const cardById = new Map(cards.map((c) => [c.id, c]));
    const resolved = new Map<string, number>();
    for (const [id, count] of source) {
      if (cardById.has(id)) resolved.set(id, count);
    }
    setTeam(resolved);
    setTeamRestored(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cards !== null]);

  // Only save AFTER the restore has run, or the initial empty team would
  // overwrite what we are about to load.
  useEffect(() => {
    if (!teamRestored) return;
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify([...team.entries()]));
    } catch {
      // Storage unavailable (private mode, blocked site data) - the tool
      // still works, teams just do not survive a restart.
    }
  }, [team, teamRestored]);

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

  // The old Teambuilder (tb.dicecoalition.com) takes a team as
  //   ?view&cards=<count>x<code>;<count>x<code>...
  // with non-Basic-Action cards first, matching its own maketeamlink().
  // Codes come from the API (CardDef.oldTeamBuilderCode) rather than being
  // derived from our ids - that tool files promo REPRINTS under the set
  // they were first printed in, so 104 cards would otherwise be wrong.
  // Useful until this Team Builder has card images of its own.
  const OLD_TEAM_BUILDER_URL = "https://tb.dicecoalition.com/index.php";

  function buildOldTeamLink(): { url: string; missing: CardDef[] } {
    const missing: CardDef[] = [];
    const parts: string[] = [];
    for (const { card, count } of [...characterEntries, ...basicActionEntries]) {
      if (card.oldTeamBuilderCode) parts.push(`${count}x${card.oldTeamBuilderCode}`);
      else missing.push(card);
    }
    return { url: `${OLD_TEAM_BUILDER_URL}?view&cards=${parts.join(";")}`, missing };
  }

  async function copyOldTeamLink() {
    const { url, missing } = buildOldTeamLink();
    await navigator.clipboard.writeText(url);
    setCopiedOld(
      missing.length === 0
        ? "Copied!"
        : `Copied without ${missing.length} card(s) the old tool lacks`,
    );
    setTimeout(() => setCopiedOld(null), 3000);
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

  // Built from the catalog, not hardcoded: the old Teambuilder's list
  // stopped at 10 and our data already has a 12 (Supreme Intelligence:
  // Merciless), which a fixed list would have made unreachable.
  const costRange = useMemo(() => {
    const costs = (cards ?? []).map((c) => c.purchaseCost);
    if (costs.length === 0) return [];
    const lo = Math.min(...costs);
    const hi = Math.max(...costs);
    return Array.from({ length: hi - lo + 1 }, (_, i) => lo + i);
  }, [cards]);

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

  const activeFormat = useMemo(() => FORMATS.find((f) => f.id === formatId), [formatId]);

  const filtered = useMemo(() => {
    const needle = deferredSearch.trim().toLowerCase();
    return (cards ?? []).filter((c) => {
      if (!showUnimplemented && !c.isImplemented) return false;
      if (activeTypes.size > 0 && !activeTypes.has(c.type)) return false;
      if (activeEnergyTypes.size > 0 && !c.energyTypes.some((e) => activeEnergyTypes.has(e))) return false;
      if (activeAffiliations.size > 0 && !c.affiliations.some((a) => activeAffiliations.has(a))) return false;
      if (activeSets.size > 0 && (!c.set || !activeSets.has(c.set))) return false;
      if (minCost !== null && c.purchaseCost < minCost) return false;
      if (maxCost !== null && c.purchaseCost > maxCost) return false;
      const tier = rarityTier(c.rarity);
      if (activeRarities.size > 0 && (!tier || !activeRarities.has(tier))) return false;
      if (activeFormat && (!c.set || !activeFormat.sets.has(c.set))) return false;
      if (applyOrangeBan && isOrangeBanned(c)) return false;
      // Set code and set name are deliberately NOT searched: "Thor"
      // should find Thor, not all 137 cards in the Thor set. Picking a set
      // is a filtering task and has its own checkboxes. Operator syntax
      // (& | ~ ^) lives in cardSearch.ts.
      return matchesQuery(c, needle);
    });
  }, [cards, deferredSearch, activeTypes, activeEnergyTypes, activeAffiliations, activeSets, activeRarities, minCost, maxCost, showUnimplemented, activeFormat, applyOrangeBan]);

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

      <div className="app-layout team-builder-layout">
        <div className="main-column">
          <h2>Team Builder - Card Search</h2>
          <p className="hint">
            Browse the full card catalog. "OK" marks the cards whose full printed text is already modeled by
            the engine; the rest are searchable here but not yet playable in a game.
          </p>

          <div className="card-catalog-filters">
            <div className="card-catalog-search">
            <input
              type="text"
              placeholder="Search name, subtitle, or text..."
              title={"Matches name, subtitle, affiliation and rules text.\n\n" +
                     "a & b   both      a | b   either\n" +
                     "~a      exclude   ^a      name starts with"}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {/* Stated, not just a tooltip - nobody discovers operators by
                hovering a text box. */}
            <div className="hint card-catalog-search-syntax">
              <code>a &amp; b</code> both · <code>a | b</code> either ·{" "}
              <code>~a</code> exclude · <code>^a</code> starts with
            </div>
            </div>
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
            <fieldset>
              <legend>Rarity</legend>
              {RARITY_TIERS.map((r) => (
                <label key={r} className={rarityClass(r)}>
                  <input
                    type="checkbox"
                    checked={activeRarities.has(r)}
                    onChange={() => toggle(activeRarities, setActiveRarities, r)}
                  />
                  {r}
                </label>
              ))}
            </fieldset>
            <fieldset className="card-catalog-cost">
              <legend>Cost</legend>
              <label>
                Min
                <select
                  value={minCost ?? ""}
                  onChange={(e) => {
                    const v = e.target.value === "" ? null : Number(e.target.value);
                    setMinCost(v);
                    // Keep the range coherent rather than letting the user
                    // land on min > max, which silently matches nothing.
                    if (v !== null && maxCost !== null && v > maxCost) setMaxCost(v);
                  }}
                >
                  <option value="">Any</option>
                  {costRange.map((n) => <option key={n} value={n}>{n}</option>)}
                </select>
              </label>
              <label>
                Max
                <select
                  value={maxCost ?? ""}
                  onChange={(e) => {
                    const v = e.target.value === "" ? null : Number(e.target.value);
                    setMaxCost(v);
                    if (v !== null && minCost !== null && v < minCost) setMinCost(v);
                  }}
                >
                  <option value="">Any</option>
                  {costRange.map((n) => <option key={n} value={n}>{n}</option>)}
                </select>
              </label>
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
            <label title={activeFormat?.description ?? "No format restriction."}>
              Format{" "}
              <select value={formatId} onChange={(e) => setFormatId(e.target.value)}>
                <option value="">No format</option>
                {FORMATS.map((f) => (
                  <option key={f.id} value={f.id} title={f.description}>
                    {f.label}
                  </option>
                ))}
              </select>
            </label>
            <label title={`Hide the ${ORANGE_BAN_LIST.length} cards on the community Orange Ban list. Applies on top of the selected format.`}>
              <input
                type="checkbox"
                checked={applyOrangeBan}
                onChange={(e) => setApplyOrangeBan(e.target.checked)}
              />
              Apply Orange Ban list
            </label>
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
              {/* The table has more columns than the narrowed main column
                  can always fit, so it scrolls inside its own box rather
                  than sliding under the sticky team sidebar. */}
              <div className="card-catalog-scroll">
              <table className="card-catalog-table">
                <thead>
                  <tr>
                    <th />
                    {COLUMNS.map((col) => (
                      <th key={col.key} title={col.title} onClick={() => toggleSort(col.key)}>
                        {col.label}
                        {sort.key === col.key && <span className="sort-arrow">{sort.direction === "asc" ? " ▲" : " ▼"}</span>}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {visible.map((c) => {
                    const add = canAddCard(c);
                    return (
                      <Fragment key={c.id}>
                      <tr className={`card-row ${rarityClass(c.rarity)}`}>
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
                        <td title={c.set ? SET_NAMES[c.set] : undefined}>{c.set ?? "-"}</td>
                        <td>
                          {c.name}
                          {c.subtitle && <span className="hint"> — {c.subtitle}</span>}
                        </td>
                        <td>{c.type}</td>
                        <td>{c.affiliations.join(", ") || "-"}</td>
                        <td>{c.purchaseCost}</td>
                        <td>{c.energyTypes.join("/")}</td>
                        <td>{c.dieLimit}</td>
                        <td className="card-level" title={LEVEL_TITLE}>{levelText(c, 0)}</td>
                        <td className="card-level" title={LEVEL_TITLE}>{levelText(c, 1)}</td>
                        <td className="card-level" title={LEVEL_TITLE}>{levelText(c, 2)}</td>
                        <td>{c.isImplemented ? "✓" : ""}</td>
                      </tr>
                      {/* Printed text on its own full-width row rather than
                          in the Name cell: it spans every column, so it gets
                          real room to read instead of squeezing the stats. */}
                      <tr className={`card-text-row ${rarityClass(c.rarity)}`}>
                        <td colSpan={COLUMNS.length + 1}>
                          {c.rawText || <span className="hint">(blank text box)</span>}
                        </td>
                      </tr>
                      </Fragment>
                    );
                  })}
                </tbody>
              </table>
              </div>
            </>
          )}
        </div>

        <div className="team-sidebar">
          <div className="team-sidebar-header">
            <h3>Team</h3>
            <button
              className="team-clear-button"
              disabled={team.size === 0}
              onClick={() => {
                if (window.confirm("Remove all cards from this team?")) setTeam(new Map());
              }}
            >
              Clear
            </button>
          </div>
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
                <li key={card.id} className={`team-list-item ${rarityClass(card.rarity)}`}>
                  <div className="team-card-header">
                    <div className="team-card-identity">
                      <div className="team-card-name">{card.name}</div>
                      {card.subtitle && <div className="hint">{card.subtitle}</div>}
                    </div>
                  {/* "n / max" rather than a bare n: the + button stops at
                      the die limit, but people who track dice physically
                      never discover the limit that way. */}
                  {isBasicActionFamily(card) ? (
                    <span className="team-dice-count" title="Dice used / max dice for this card">
                      {count}/{card.dieLimit} dice
                    </span>
                  ) : (
                    <span className="team-stepper" title="Dice used / max dice for this card">
                      <button
                        disabled={count <= 1}
                        onClick={() => setCount(card.id, count - 1)}
                      >
                        −
                      </button>
                      <span className="team-dice-count">
                        {count}<span className="team-dice-max">/{card.dieLimit}</span>
                      </span>
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
                  </div>
                  {/* Cost / energy / affiliation, the details that actually
                      drive team selection - energy especially, since you are
                      usually either balancing it or committing to one type.
                      Type and Set are deliberately left out: neither changes
                      a build decision, and the panel is narrow. */}
                  <div className="team-card-meta">
                    <span className="team-card-cost" title="Purchase cost">{card.purchaseCost}</span>
                    <span>{card.energyTypes.join("/") || "No energy type"}</span>
                    {card.affiliations.length > 0 && <span>{card.affiliations.join(", ")}</span>}
                  </div>
                  {card.levels.length > 0 && (
                    // Every level, not just level 1: whether a die is worth
                    // running often turns on what its level 2/3 faces do, and
                    // the main table only ever shows level 1.
                    <div className="team-card-levels" title="Per level: fielding cost / attack / defense">
                      {card.levels.map((l, i) => (
                        <span key={i}>
                          <span className="hint">L{i + 1}</span> {l.fieldingCost}/{l.attack}/{l.defense}
                        </span>
                      ))}
                    </div>
                  )}
                  <div className="team-card-text">{card.rawText || "(blank text box)"}</div>
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
            onClick={copyOldTeamLink}
            disabled={team.size === 0}
            title={`Opens the team in the old Teambuilder at ${OLD_TEAM_BUILDER_URL}, which has card images.`}
          >
            {copiedOld ?? "Copy OLD team link"}
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
