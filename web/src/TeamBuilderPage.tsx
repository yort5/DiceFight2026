import { useDeferredValue, useEffect, useMemo, useState } from "react";
import { api } from "./api";
import { navigate } from "./router";
import type { CardDef } from "./types";

// First step toward a real team builder (see RULES_ENGINE_DESIGN.md's
// next-steps list) - read-only browse/search/sort for now, no team
// selection yet. Its own page/route (not a modal off the game view) since
// it has standalone value even to someone who never opens the live
// digital game - e.g. building a team to play with physical dice.

type SortKey =
  | "name" | "type" | "affiliations" | "purchaseCost" | "energyTypes" | "dieLimit"
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
  const [showUnimplemented, setShowUnimplemented] = useState(false);
  const [sort, setSort] = useState<{ key: SortKey; direction: "asc" | "desc" }>({
    key: "name",
    direction: "asc",
  });

  useEffect(() => {
    api.getCards().then(setCards).catch((e) => setError(String(e)));
  }, []);

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
      if (needle.length === 0) return true;
      return (
        c.name.toLowerCase().includes(needle) ||
        (c.subtitle?.toLowerCase().includes(needle) ?? false) ||
        c.affiliations.some((a) => a.toLowerCase().includes(needle)) ||
        c.rawText.toLowerCase().includes(needle)
      );
    });
  }, [cards, deferredSearch, activeTypes, activeEnergyTypes, activeAffiliations, showUnimplemented]);

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
                    return (
                      <tr key={c.id} className={c.isImplemented ? "" : "unimplemented"} title={cardTooltip(c)}>
                        <td>
                          {c.name}
                          {c.subtitle && <span className="hint"> — {c.subtitle}</span>}
                        </td>
                        <td>{c.type}</td>
                        <td>{c.affiliations.join(", ") || "-"}</td>
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
      </div>
    </div>
  );
}
