import { useDeferredValue, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { matchesQuery } from "./cardSearch";
import { CardText } from "./CardText";
import { AffiliationBadge, AffiliationIcons } from "./AffiliationIcons";
import { buildAffiliationIconIndex, SHOW_AFFILIATIONS } from "./affiliationIndex";
import { DieFace } from "./DieFace";
import { EnergyTypes } from "./GameIcon";
import { CHAMPION_ICONS, CHARACTER_ICONS, ENERGY_ICONS as KINGDOM_ENERGY_ICONS } from "./dicekingdom/icons";
import { api as kingdomApi } from "./dicekingdom/api";
import type { Champion } from "./dicekingdom/types";
import { api } from "./api";
import { loadTeamBuilderCards, USE_DICE_KINGDOM_CATALOG } from "./teamBuilderCatalog";
import { stashPendingGame } from "./gameHandoff";
import { navigate } from "./router";
import { ScreenMenu } from "./ScreenMenu";
import { TeamStartDialog } from "./TeamStartDialog";
import { useTeamBuilderLayout } from "./teamBuilderLayout";
import "./TeamBuilderLayout.css";
import { TARDIGRADE_TYPES, MAX_STARTING_TARDIGRADES, basicActionFits, capTardigrades, changeTardigradeCount, tardigradeTotal, tardigradesForChampion, stageCustomTeam, type TardigradeType } from "./dicekingdom/customTeams";
import { SET_NAMES } from "./sets";
import { FORMATS } from "./formats";
import {
  capsFor, enforcesUniqueNames, isCapped, legalityOf, RULESETS, STANDARD_CAPS,
  type Caps, type RulesetId,
} from "./rulesets";
import { ORANGE_BAN_LIST, isOrangeBanned } from "./orangeBan";
import type { CardDef } from "./types";

// Now a real team builder (see RULES_ENGINE_DESIGN.md's next-steps
// list) - browse/search/sort plus actually selecting cards into a
// team, with a shareable URL. Its own page/route (not a modal off the
// game view) since it has standalone value even to someone who never
// opens the live digital game - e.g. building a team to play with
// physical dice. The engine itself never enforces team-construction
// legality (house rules/alternate formats are common - see
// TeamSetup.cs's own remarks) - only this page does, and which limits it
// applies is the player's choice of ruleset (see rulesets.ts).

function CardAvatar({ card, size = 24 }: { card: CardDef; size?: number }) {
  const Avatar = CHARACTER_ICONS[card.id];
  return Avatar ? <span className="builder-card-avatar" role="img" aria-label={`${card.name} icon`}>
    <Avatar size={size} />
  </span> : null;
}

function CardBadges({ card }: { card: CardDef }) {
  return <span className="builder-card-badges">
    {card.hasErrata && <span className="card-errata" role="img" aria-label="Errata: adjusted card"
      title="Errata: this card has been adjusted from its reference version.">
      <svg width="12" height="12" viewBox="0 0 24 24" aria-hidden="true" fill="none"
        stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M13 3H5v18h14v-7M8 8h4M8 12h2" />
        <path d="m12 15 1-4 7-7 3 3-7 7-4 1ZM18 6l3 3" />
      </svg>
      <span aria-hidden="true">Errata</span>
    </span>}
    {!card.isImplemented && <span className="result-paper"
      title="Implementation is incomplete. Remove this card before starting a game.">Incomplete</span>}
  </span>;
}

const BASIC_ACTION_TYPES = new Set(["BasicAction", "EpicBasicAction"]);

// Share Dice Kingdom's existing energy glyphs rather than showing plain
// "Claw"/"Shell" strings in a builder designed for Dice Masters symbols.
function BuilderEnergyTypes({ types }: { types: readonly string[] }) {
  return <>
    {types.map((t) => {
      const KingdomIcon = KINGDOM_ENERGY_ICONS[t];
      return KingdomIcon
        ? <span key={t} title={`${t} energy`} aria-label={`${t} energy`}><KingdomIcon size={16} /></span>
        : <EnergyTypes key={t} types={[t]} />;
    })}
  </>;
}

// The three numbers a Custom ruleset lets you set. Minimums are 1 rather
// than 0 - a cap of zero is a team you cannot build, which is never what
// someone reaching for a house format wants.
const CUSTOM_CAP_FIELDS: readonly { key: keyof Caps; label: string; min: number }[] = [
  { key: "cards", label: "Cards", min: 1 },
  { key: "dice", label: "Dice", min: 1 },
  { key: "basicActions", label: "Basic Actions", min: 0 },
];

// The dice a team has against the cap it is allowed, one pip per die.
// Reading "14/20" takes a moment; reading a bar does not.
function DiceMeter({ used, cap }: { used: number; cap: number }) {
  // Uncapped, the meter still has to be *some* length - it shows the
  // standard 20 as a frame of reference, growing if the team goes past it.
  const slots = isCapped(cap) ? Math.max(cap, used) : Math.max(STANDARD_CAPS.dice, used);
  return (
    <span className="dice-meter" role="img" aria-label={`${used} dice${isCapped(cap) ? ` of ${cap}` : ""}`}>
      {Array.from({ length: slots }, (_, i) => (
        <span
          key={i}
          className={`dice-pip${i < used ? (isCapped(cap) && i >= cap ? " over" : " filled") : ""}`}
        />
      ))}
    </span>
  );
}

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
// The API sends the enum name ("BasicAction"); nothing on a real card is
// spelled that way. Split on the capitals rather than mapping each value
// so a new card type reads correctly without being added here.
function typeLabel(type: string): string {
  return type.replace(/([a-z])([A-Z])/g, "$1 $2");
}

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

// Rarity as a LETTER as well as a colour. Colour alone fails anyone who
// cannot separate the red and green stripes, and it fails everyone in
// print. The colours themselves stay exactly as they are - they match the
// old Teambuilder's stripes, which is a deliberate fidelity choice.
function rarityLetter(rarity: string | null): string | null {
  switch (rarityTier(rarity)) {
    case "Common": return "C";
    case "Uncommon": return "U";
    case "Rare": return "R";
    case "Super Rare": return "SR";
    case "Chase": return "CH";
    case "Promo": return "P";
    default: return null;
  }
}

function RarityBadge({ rarity }: { rarity: string | null }) {
  const letter = rarityLetter(rarity);
  if (!letter) return null;
  return (
    <span className={`rarity-badge ${rarityClass(rarity)}`} title={rarityTier(rarity) ?? undefined}>
      {letter}
    </span>
  );
}

const MAX_LEGIBLE_PIPS = 12;

function DicePips({ count, limit }: { count: number; limit: number }) {
  if (limit > MAX_LEGIBLE_PIPS) return null;
  return (
    <span className="slot-pips" role="img" aria-label={`${count} of ${limit} dice`}>
      {Array.from({ length: limit }, (_, i) => (
        <span key={i} className={`slot-pip${i < count ? " owned" : ""}`} />
      ))}
    </span>
  );
}

// The Slots and List views use the same controls and selected/max die squares.
function TeamDiceControls({
  name, count, limit, canIncrease, onDecrease, onIncrease,
}: {
  name: string;
  count: number;
  limit: number;
  canIncrease: boolean;
  onDecrease: () => void;
  onIncrease: () => void;
}) {
  return (
    <span className="team-dice-controls" aria-label={`${count} of ${limit} dice for ${name}`}>
      <button type="button" disabled={count <= 1} onClick={onDecrease} aria-label={`One fewer ${name} die`}>−</button>
      <DicePips count={count} limit={limit} />
      <button type="button" disabled={!canIncrease} onClick={onIncrease} aria-label={`One more ${name} die`}>+</button>
      <span className="team-dice-label">Dice</span>
    </span>
  );
}

function FixedDiceDisplay({ count, limit }: { count: number; limit: number }) {
  return (
    <span className="team-dice-controls" aria-label={`${count} of ${limit} dice`}>
      <DicePips count={count} limit={limit} />
      <span className="team-dice-label">Dice</span>
    </span>
  );
}

// At module scope, not inside the component with the other storage keys:
// the roster-view state reads it in its lazy initialiser, which runs
// before those declarations are reached.
const ROSTER_VIEW_KEY = "dicefight.teamBuilder.rosterView";

// Rendered before the row cap below applies, so typing narrows the
// visible count too - keeps a huge future catalog from ever forcing a
// full re-render on every keystroke (see the design doc's scaling note).
const MAX_ROWS = 200;

// Sorting used to live in eleven clickable table headers. Rows have no
// headers, and eight of those eleven were sorts nobody reaches for -
// what people actually order this catalog by is a name, a cost, how hard
// the die hits, or which set it came from.
const SORTS: { key: SortKey; label: string; title: string }[] = [
  { key: "name", label: "Name", title: "Sort by card name" },
  { key: "purchaseCost", label: "Cost", title: "Sort by purchase cost" },
  { key: "level1", label: "L1 Attack", title: "Sort by level 1 attack" },
  { key: "set", label: "Set", title: "Sort by set" },
];

export function TeamBuilderPage() {
  const { layout } = useTeamBuilderLayout();
  const [mobilePanel, setMobilePanel] = useState<"cards" | "filters" | "team">("cards");
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
  // Format is a single-select preset (the presets are mutually
  // exclusive release-order cutoffs), while the Orange Ban list is a
  // separate checkbox layered on top - it is normally used in
  // conjunction with a format rather than instead of one.
  const [formatId, setFormatId] = useState<string>("");
  // The rail is 272px and there are ~128 affiliations; without a way to
  // narrow them the section is a scroll hunt. Sets get away without one
  // (49, and people know the code they want).
  const [affiliationFilter, setAffiliationFilter] = useState("");
  const [applyOrangeBan, setApplyOrangeBan] = useState(false);
  const [sort, setSort] = useState<{ key: SortKey; direction: "asc" | "desc" }>({
    key: "name",
    direction: "asc",
  });
  const [team, setTeam] = useState<Map<string, number>>(new Map());
  const [teamRestored, setTeamRestored] = useState(false);
  // Replaces the old `strictRules` on/off checkbox - see rulesets.ts for
  // why a house format deserves caps of its own rather than "validation
  // off". Restored from localStorage below, alongside the team.
  const [ruleset, setRuleset] = useState<RulesetId>("standard");
  const [customCaps, setCustomCaps] = useState<Caps>(STANDARD_CAPS);
  // Slots draws the team's shape; List is denser and shows rules text.
  // Persisted because it is a working preference, not part of the team -
  // it deliberately does NOT travel in the share link, which describes
  // what the team IS rather than how you happen to be looking at it.
  const [rosterView, setRosterView] = useState<"slots" | "list">(() => {
    try {
      return window.localStorage.getItem(ROSTER_VIEW_KEY) === "list" ? "list" : "slots";
    } catch {
      return "slots";
    }
  });
  const [copied, setCopied] = useState(false);
  const [copiedOld, setCopiedOld] = useState<string | null>(null);
  const [tardigrades, setTardigrades] = useState<Record<TardigradeType, number>>({
    Claw: 8, Shell: 0, Wing: 0, Eye: 0,
  });
  const tardigradeCount = tardigradeTotal(tardigrades);
  // The team's Champion (2026-10-19): its power in the game, and the energy
  // its Basic Action must match. None is a team without a Champion.
  const [championId, setChampionId] = useState<string | null>(null);
  const [champions, setChampions] = useState<Champion[]>([]);
  const champion = champions.find((c) => c.id === championId) ?? null;
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);
  const [startWarning, setStartWarning] = useState<{ messages: string[]; canProceed: boolean } | null>(null);

  useEffect(() => {
    loadTeamBuilderCards().then(setCards).catch((e) => setError(String(e)));
    if (USE_DICE_KINGDOM_CATALOG) kingdomApi.getChampions().then(setChampions).catch(() => setChampions([]));
  }, []);

  // Teams survive a browser restart. A shared ?cards= link still wins, so
  // opening someone else's team never silently resurrects your own; only
  // when there is no link do we fall back to what was saved here.
  // Everything is wrapped in try/catch: storage throws outright in some
  // privacy modes rather than just coming back empty.
  const STORAGE_KEY = USE_DICE_KINGDOM_CATALOG ? "dicefight.teamBuilder.kingdom.team" : "dicefight.teamBuilder.team";
  const RULESET_KEY = USE_DICE_KINGDOM_CATALOG ? "dicefight.teamBuilder.kingdom.ruleset" : "dicefight.teamBuilder.ruleset";

  const TARDIGRADE_KEY = "dicefight.teamBuilder.kingdom.tardigrades";
  const CHAMPION_KEY = "dicefight.teamBuilder.kingdom.champion";

  // Same precedence as the Tardigrades: a shared link, else what was saved here.
  function readChampion(): string | null {
    const params = new URLSearchParams(window.location.search);
    if (params.has("cards")) return params.get("champion") || null;
    try { return window.localStorage.getItem(CHAMPION_KEY) || null; } catch { return null; }
  }

  function readTardigrades(): Record<TardigradeType, number> {
    let fromUrl = new URLSearchParams(window.location.search).get("tardigrades");
    if (fromUrl === null) {
      try { fromUrl = window.localStorage.getItem(TARDIGRADE_KEY); } catch { /* storage blocked */ }
    }
    const counts: Record<TardigradeType, number> = { Claw: 8, Shell: 0, Wing: 0, Eye: 0 };
    if (fromUrl !== null) {
      for (const type of TARDIGRADE_TYPES) counts[type] = 0;
      for (const part of fromUrl.split(";")) {
        const match = /^(\d+)x(Claw|Shell|Wing|Eye)$/.exec(part);
        if (match) counts[match[2] as TardigradeType] = Number(match[1]);
      }
    }
    return capTardigrades(counts);
  }

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

  // The ruleset is restored once, on mount.
  //
  // No migration is needed despite the redesign brief expecting one: the
  // old "Strict rules" checkbox was never persisted (only the team was),
  // so nobody has a saved `strictRules: false` to carry over. Everyone
  // starts on Standard, which is what the checkbox defaulted to anyway.
  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(RULESET_KEY);
      if (raw) {
        const parsed: unknown = JSON.parse(raw);
        if (parsed && typeof parsed === "object") {
          const saved = parsed as { ruleset?: unknown; caps?: unknown };
          if (saved.ruleset === "standard" || saved.ruleset === "freeform" || saved.ruleset === "custom") {
            setRuleset(saved.ruleset);
          }
          const caps = saved.caps as Partial<Caps> | undefined;
          if (caps && [caps.cards, caps.dice, caps.basicActions].every((n) => Number.isInteger(n) && (n as number) >= 0)) {
            setCustomCaps(caps as Caps);
          }
        }
      }
    } catch {
      // Storage unavailable - the default ruleset is the right fallback.
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!teamRestored) return;
    try {
      window.localStorage.setItem(RULESET_KEY, JSON.stringify({ ruleset, caps: customCaps }));
    } catch {
      // Same as the team above - the tool works, the choice just does not
      // survive a restart.
    }
  }, [ruleset, customCaps, teamRestored]);

  // Load a shared team link once the catalog is available to resolve
  // its ids against - silently drops any id that doesn't resolve
  // (stale/typo'd link) rather than hard-failing.
  useEffect(() => {
    if (!cards) return;
    const fromUrl = decodeTeam(window.location.search);
    // An explicit shared link may contain Tardigrades but no character cards.
    const hasSharedTeam = new URLSearchParams(window.location.search).has("cards");
    const source = hasSharedTeam ? fromUrl : loadSavedTeam();
    if (source.size === 0) {
      if (USE_DICE_KINGDOM_CATALOG) { setTardigrades(readTardigrades()); setChampionId(readChampion()); }
      setTeamRestored(true);
      return;
    }
    const cardById = new Map(cards.map((c) => [c.id, c]));
    const resolved = new Map<string, number>();
    for (const [id, count] of source) {
      if (cardById.has(id)) resolved.set(id, count);
    }
    setTeam(resolved);
    if (USE_DICE_KINGDOM_CATALOG) { setTardigrades(readTardigrades()); setChampionId(readChampion()); }
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

  useEffect(() => {
    if (!teamRestored || !USE_DICE_KINGDOM_CATALOG) return;
    try {
      window.localStorage.setItem(TARDIGRADE_KEY,
        TARDIGRADE_TYPES.map((type) => `${tardigrades[type]}x${type}`).join(";"));
    } catch { /* no persistence in private mode */ }
  }, [tardigrades, teamRestored]);

  useEffect(() => {
    if (!teamRestored || !USE_DICE_KINGDOM_CATALOG) return;
    try {
      if (championId) window.localStorage.setItem(CHAMPION_KEY, championId);
      else window.localStorage.removeItem(CHAMPION_KEY);
    } catch { /* no persistence in private mode */ }
  }, [championId, teamRestored]);

  function pickChampion(id: string | null) {
    setChampionId(id);
    const energy = champions.find((c) => c.id === id)?.energySymbolId;
    if (energy) setTardigrades((old) => tardigradesForChampion(old, energy));
  }

  useEffect(() => {
    try {
      window.localStorage.setItem(ROSTER_VIEW_KEY, rosterView);
    } catch {
      // Same as the team above - unavailable storage costs a preference,
      // not the page.
    }
  }, [rosterView]);

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
  const mismatchedBasicActions = basicActionEntries.filter(({ card }) =>
    !basicActionFits(card.dieEnergyType, champion?.energySymbolId));
  const uniqueNames = useMemo(() => new Set(characterEntries.map((e) => e.card.name)), [characterEntries]);

  // The cap counts CARDS, not distinct names. Under Standard the two are
  // the same thing, because rule 2.1.5 forbids duplicates - but Freeform
  // and Custom allow two copies of a character, and two cards should
  // count as two.
  const cardCount = characterEntries.length;
  const totalDice = useMemo(() => characterEntries.reduce((sum, e) => sum + e.count, 0), [characterEntries]);

  // Real rules 2.1.1/2.1.3/2.1.4/2.1.5: up to 8 unique-named Character/
  // Action cards, 1..dieLimit dice each summing to at most 20, exactly
  // 2 Basic Action cards (excluded from the dice cap). Only "over the
  // cap" counts as a violation - a team still being built naturally
  // passes through 0/1 Basic Actions or fewer than 8 cards on the way
  // to a complete team, that's not illegal, just incomplete.
  const caps = useMemo(() => {
      const chosen = capsFor(ruleset, customCaps);
      return USE_DICE_KINGDOM_CATALOG && ruleset === "standard"
        ? { ...chosen, basicActions: 1 }
        : chosen;
    }, [ruleset, customCaps]);

  // How many tiles to draw. Never fewer than the team has cards, so
  // switching Standard -> Freeform (or dropping a Custom cap below the
  // team) can never hide a card that is still on the team - the extras
  // show and colour as over-cap instead. Uncapped, there are always two
  // ghost slots to grow into rather than a grid that ends exactly where
  // you stopped.
  const characterSlotCount = isCapped(caps.cards)
    ? Math.max(caps.cards, cardCount)
    : cardCount + 2;
  const basicActionSlotCount = isCapped(caps.basicActions)
    ? Math.max(caps.basicActions, basicActionEntries.length)
    : basicActionEntries.length + 1;

  // The shape of the team, measured in DICE rather than cards - a card
  // you run four of shapes the team four times over, and the dice are
  // what you actually draw.
  const teamShape = useMemo(() => {
    const byCost = new Map<number, number>();
    const byEnergy = new Map<string, number>();
    const byAffiliation = new Map<string, number>();
    let costTotal = 0;
    let playableDice = 0;

    for (const { card, count } of characterEntries) {
      byCost.set(card.purchaseCost, (byCost.get(card.purchaseCost) ?? 0) + count);
      costTotal += card.purchaseCost * count;
      if (card.isImplemented) playableDice += count;
      // A die with two energy types counts once for each: a Crossover
      // character really is a body for both, which is the whole question
      // the energy mix is being asked.
      for (const e of card.energyTypes) byEnergy.set(e, (byEnergy.get(e) ?? 0) + count);
      for (const a of card.affiliations) byAffiliation.set(a, (byAffiliation.get(a) ?? 0) + count);
    }

    const curve = [...byCost.entries()].sort((a, b) => a[0] - b[0]).map(([cost, dice]) => ({ cost, dice }));
    const energyTotal = [...byEnergy.values()].reduce((a, b) => a + b, 0);
    return {
      curve,
      peak: curve.reduce((m, c) => Math.max(m, c.dice), 0),
      energy: [...byEnergy.entries()].sort((a, b) => b[1] - a[1]).map(([type, dice]) => ({
        type,
        dice,
        share: energyTotal === 0 ? 0 : dice / energyTotal,
      })),
      affiliations: [...byAffiliation.entries()].sort((a, b) => b[1] - a[1]).map(([name, dice]) => ({ name, dice })),
      averageCost: totalDice === 0 ? 0 : costTotal / totalDice,
      playableDice,
    };
  }, [characterEntries, totalDice]);

  const legality = useMemo(
    () => legalityOf(ruleset, caps, {
      cards: cardCount,
      dice: totalDice,
      basicActions: basicActionEntries.length,
    }),
    [ruleset, caps, cardCount, totalDice, basicActionEntries],
  );

  function canAddCard(card: CardDef): { ok: boolean; reason?: string } {
    if (team.has(card.id)) return { ok: false, reason: "Already on the team." };
    if (isBasicActionFamily(card)) {
      if (champion && !basicActionFits(card.dieEnergyType, champion.energySymbolId)) {
        return { ok: false, reason: `${champion.name} takes only ${champion.energySymbolId} Basic Actions.` };
      }
      if (basicActionEntries.length >= caps.basicActions) {
        return { ok: false, reason: `Already have ${caps.basicActions} Basic Actions.` };
      }
      return { ok: true };
    }
    // Rule 2.1.5 - one card per name. Standard only: running two copies
    // of a character is a normal house-format thing to do, so Freeform
    // and Custom allow it (see rulesets.ts).
    if (enforcesUniqueNames(ruleset) && uniqueNames.has(card.name)) {
      return { ok: false, reason: `Already have a card named "${card.name}" — allowed under Freeform or Custom.` };
    }
    if (cardCount >= caps.cards) {
      return { ok: false, reason: `Already have ${caps.cards} cards — raise the cap in Custom, or switch to Freeform.` };
    }
    if (totalDice + 1 > caps.dice) {
      return { ok: false, reason: `Would exceed the ${caps.dice}-dice cap — raise it in Custom, or switch to Freeform.` };
    }
    return { ok: true };
  }

  function canIncrement(card: CardDef, count: number): boolean {
    if (count >= card.dieLimit) return false;
    // Basic Action dice sit outside the dice cap (rule 2.1.4).
    if (!isBasicActionFamily(card) && totalDice + 1 > caps.dice) return false;
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
    const tardigradePart = USE_DICE_KINGDOM_CATALOG
      ? `&tardigrades=${encodeURIComponent(TARDIGRADE_TYPES.map((type) => `${tardigrades[type]}x${type}`).join(";"))}`
      : "";
    const championPart = USE_DICE_KINGDOM_CATALOG && championId ? `&champion=${encodeURIComponent(championId)}` : "";
    const url = `${window.location.origin}${window.location.pathname}?cards=${encodeTeam(team)}${tardigradePart}${championPart}`;
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

  function playCustomTeam(confirmed = false) {
    const errors: string[] = [];
    if (!legality.ok) errors.push(legality.note);
    const incomplete = teamEntries.filter(({ card }) => !card.isImplemented);
    if (incomplete.length > 0) {
      errors.push(`Incomplete cards must be removed before playing: ${incomplete.map(({ card }) => card.name + (card.subtitle ? ` — ${card.subtitle}` : "")).join(", ")}.`);
    }
    if (tardigradeCount !== MAX_STARTING_TARDIGRADES) {
      errors.push(`Select exactly ${MAX_STARTING_TARDIGRADES} starting Tardigrades (currently ${tardigradeCount}/${MAX_STARTING_TARDIGRADES}).`);
    }
    const offEnergy = mismatchedBasicActions.map(({ card }) => card.name);
    if (champion && offEnergy.length > 0) {
      errors.push(`${champion.name} takes only ${champion.energySymbolId} Basic Actions - remove ${offEnergy.join(", ")}.`);
    }
    if (errors.length > 0) {
      setStartWarning({ messages: errors, canProceed: false });
      return;
    }
    const warnings: string[] = [];
    if (basicActionEntries.length === 0) warnings.push("No Basic Action selected.");
    if (isCapped(caps.cards) && cardCount < caps.cards) {
      warnings.push(`Only ${cardCount} of ${caps.cards} card slots are filled.`);
    }
    if (isCapped(caps.dice) && totalDice < caps.dice) {
      warnings.push(`Only ${totalDice} of ${caps.dice} character dice are selected.`);
    }
    if (!confirmed && warnings.length > 0) {
      setStartWarning({ messages: warnings, canProceed: true });
      return;
    }
    setStartWarning(null);
    stageCustomTeam({
      cards: teamEntries.map(({ card, count }) => ({ cardId: card.id, diceCount: count })),
      tardigrades: TARDIGRADE_TYPES.map((energyType) => ({ energyType, diceCount: tardigrades[energyType] })),
      championId,
    });
    navigate(layout === "mobile" ? "/dice-kingdom/mobile" : "/dice-kingdom");
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
  // One logo per affiliation name, learned from the catalog - see
  // affiliationIcons.ts. Used by the filter, which has only a name to go
  // on, and as the fallback for cards the old tool never had.
  const affiliationIconIndex = useMemo(() => buildAffiliationIconIndex(cards ?? []), [cards]);
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

  // Click a cost to select just it; shift-click, or drag across, to make
  // it a range. Clicking the only selected cost clears the filter, so the
  // control undoes itself without a separate "any" option.
  const [costDragAnchor, setCostDragAnchor] = useState<number | null>(null);
  // A drag ending on a different pip still fires a click, which would
  // collapse the range straight back to one cost. This remembers that the
  // pointer moved, so that click can be ignored.
  //
  // It is cleared on mouse DOWN rather than by the click that reads it:
  // a drag that ends on a different pip fires its click on the container,
  // not on a pip, so nothing would consume the flag and it would swallow
  // the next genuine click instead.
  const costDragged = useRef(false);

  function selectCostRange(a: number, b: number) {
    setMinCost(Math.min(a, b));
    setMaxCost(Math.max(a, b));
  }

  function clickCost(n: number, extend: boolean) {
    if (costDragged.current) return;
    if (extend && minCost !== null) {
      selectCostRange(minCost, n);
      return;
    }
    if (minCost === n && maxCost === n) {
      setMinCost(null);
      setMaxCost(null);
      return;
    }
    selectCostRange(n, n);
  }

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

  const shownAffiliations = useMemo(() => {
    const needle = affiliationFilter.trim().toLowerCase();
    if (!needle) return allAffiliations;
    return allAffiliations.filter((a) => a.toLowerCase().includes(needle));
  }, [allAffiliations, affiliationFilter]);

  // Clears the FILTERS, not the search box. Search is a thing you typed
  // and can see; the chips are selections scattered down a rail, which is
  // exactly what gets left on by accident.
  function clearAllFilters() {
    setActiveTypes(new Set());
    setActiveEnergyTypes(new Set());
    setActiveRarities(new Set());
    setActiveAffiliations(new Set());
    setActiveSets(new Set());
    setMinCost(null);
    setMaxCost(null);
    setFormatId("");
    setApplyOrangeBan(false);
  }

  function removeFrom(set: Set<string>, setSet: (s: Set<string>) => void, value: string) {
    const next = new Set(set);
    next.delete(value);
    setSet(next);
  }

  // Every active filter as one removable chip, so what is narrowing the
  // results is visible from the results - a selection made three sections
  // down a scrolled rail is otherwise invisible from where its effect is.
  const filterChips = useMemo(() => {
    // `name` is the plain-text version of the label. Several chips are
    // an icon and nothing else (an affiliation logo, an energy symbol),
    // and a button whose only content is an image has no accessible name
    // at all - so every chip carries one for its tooltip and for a
    // screen reader, whatever it happens to look like.
    const chips: { key: string; name: string; label: ReactNode; remove: () => void }[] = [];
    for (const t of activeTypes) {
      chips.push({
        key: `type:${t}`,
        name: typeLabel(t),
        label: typeLabel(t),
        remove: () => removeFrom(activeTypes, setActiveTypes, t),
      });
    }
    for (const e of activeEnergyTypes) {
      chips.push({
        key: `energy:${e}`,
        name: `${e} energy`,
        label: <><BuilderEnergyTypes types={[e]} /> {e}</>,
        remove: () => removeFrom(activeEnergyTypes, setActiveEnergyTypes, e),
      });
    }
    for (const r of activeRarities) {
      chips.push({
        key: `rarity:${r}`,
        name: r,
        label: <><RarityBadge rarity={r} /> {r}</>,
        remove: () => removeFrom(activeRarities, setActiveRarities, r),
      });
    }
    for (const a of SHOW_AFFILIATIONS ? activeAffiliations : []) {
      chips.push({
        key: `affiliation:${a}`,
        name: a,
        label: <AffiliationBadge name={a} code={affiliationIconIndex[a]} />,
        remove: () => removeFrom(activeAffiliations, setActiveAffiliations, a),
      });
    }
    for (const st of activeSets) {
      chips.push({
        key: `set:${st}`,
        name: SET_NAMES[st] ?? st,
        label: st,
        remove: () => removeFrom(activeSets, setActiveSets, st),
      });
    }
    if (minCost !== null || maxCost !== null) {
      const label =
        minCost !== null && maxCost !== null
          ? minCost === maxCost ? `cost ${minCost}` : `cost ${minCost}–${maxCost}`
          : minCost !== null ? `cost ${minCost}+` : `cost up to ${maxCost}`;
      chips.push({ key: "cost", name: label, label, remove: () => { setMinCost(null); setMaxCost(null); } });
    }
    if (activeFormat) {
      chips.push({ key: "format", name: activeFormat.label, label: activeFormat.label, remove: () => setFormatId("") });
    }
    if (applyOrangeBan) {
      chips.push({ key: "ban", name: "Orange Ban list", label: "Orange Ban list", remove: () => setApplyOrangeBan(false) });
    }
    return chips;
  }, [
    activeTypes, activeEnergyTypes, activeRarities, activeAffiliations, activeSets,
    minCost, maxCost, activeFormat, applyOrangeBan, affiliationIconIndex,
  ]);

  const filtered = useMemo(() => {
    const needle = deferredSearch.trim().toLowerCase();
    return (cards ?? []).filter((c) => {
      // Keep unfinished definitions available for saved-team validation, but never offer them for selection.
      if (!c.isImplemented) return false;
      if (activeTypes.size > 0 && !activeTypes.has(c.type)) return false;
      if (activeEnergyTypes.size > 0 && !c.energyTypes.some((e) => activeEnergyTypes.has(e))) return false;
      if (SHOW_AFFILIATIONS && activeAffiliations.size > 0 && !c.affiliations.some((a) => activeAffiliations.has(a))) return false;
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
  }, [cards, deferredSearch, activeTypes, activeEnergyTypes, activeAffiliations, activeSets, activeRarities, minCost, maxCost, activeFormat, applyOrangeBan]);

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
    <div className="app team-builder-page" data-builder-layout={layout} data-mobile-panel={mobilePanel}>
      <header className="app-header team-builder-header">
        <h1>DiceFight2026</h1>
        <span className="builder-layout-note">{layout === "mobile" ? "Mobile" : "Desktop"} layout</span>
        <ScreenMenu screen="builder" startRoute={USE_DICE_KINGDOM_CATALOG
          ? layout === "mobile" ? "/dice-kingdom/mobile" : "/dice-kingdom"
          : "/game"} />
        {error && <div className="error">{error}</div>}
      </header>

      <div className="builder-panel-buttons" role="group" aria-label="Team Builder panels">
        <button type="button" aria-pressed={mobilePanel === "cards"} aria-controls="builder-catalog"
          onClick={() => setMobilePanel("cards")}>Browse</button>
        <button type="button" aria-pressed={mobilePanel === "filters"} aria-controls="builder-filter-options"
          onClick={() => setMobilePanel("filters")}>Filters{filterChips.length > 0 ? ` (${filterChips.length})` : ""}</button>
        <button type="button" aria-pressed={mobilePanel === "team"} aria-controls="builder-team"
          onClick={() => setMobilePanel("team")}>Your team ({team.size})</button>
      </div>
      <div className="app-layout team-builder-layout">
        {/* The filters live in a rail of their own rather than in a band
            above the results. Stacked above, every section you opened
            pushed the table further down, so choosing a filter meant
            losing sight of the thing it filtered. */}
        <aside className="filter-rail">
          <section className="rail-panel">
            <h3 className="rail-label">Search</h3>
            <input
              type="text"
              className="rail-search"
              placeholder="name or ability text…"
              title={"Matches name, subtitle and rules text.\n\n" +
                     "a & b   both      a | b   either\n" +
                     "~a      exclude   ^a      name starts with"}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            {/* Stated, not just a tooltip - nobody discovers operators by
                hovering a text box. */}
            <div className="rail-syntax">
              <code>a &amp; b</code> both · <code>a | b</code> either ·{" "}
              <code>~a</code> exclude · <code>^a</code> starts with
            </div>
          </section>

          <section className="rail-panel" id="builder-filter-options">
            <div className="rail-section">
              <div className="rail-section-head">
                <h3 className="rail-label">Type</h3>
                {activeTypes.size > 0 && <span className="rail-summary">{activeTypes.size} selected</span>}
              </div>
              <div className="rail-chips">
                {allTypes.map((t) => (
                  <button
                    key={t}
                    className={`rail-chip${activeTypes.has(t) ? " on" : ""}`}
                    aria-pressed={activeTypes.has(t)}
                    onClick={() => toggle(activeTypes, setActiveTypes, t)}
                  >
                    {typeLabel(t)}
                  </button>
                ))}
              </div>
            </div>

            <div className="rail-section">
              <div className="rail-section-head">
                <h3 className="rail-label">Energy</h3>
                {activeEnergyTypes.size > 0 && <span className="rail-summary">{activeEnergyTypes.size} selected</span>}
              </div>
              <div className="rail-chips">
                {allEnergyTypes.map((t) => (
                  <button
                    key={t}
                    className={`rail-chip${activeEnergyTypes.has(t) ? " on" : ""}`}
                    aria-pressed={activeEnergyTypes.has(t)}
                    onClick={() => toggle(activeEnergyTypes, setActiveEnergyTypes, t)}
                  >
                    <BuilderEnergyTypes types={[t]} /> {t}
                  </button>
                ))}
              </div>
            </div>

            {!USE_DICE_KINGDOM_CATALOG && <div className="rail-section">
              <div className="rail-section-head">
                <h3 className="rail-label">Rarity</h3>
                <span className="rail-summary">letter + colour</span>
              </div>
              <div className="rail-chips">
                {RARITY_TIERS.map((r) => (
                  <button
                    key={r}
                    className={`rail-chip${activeRarities.has(r) ? " on" : ""}`}
                    aria-pressed={activeRarities.has(r)}
                    onClick={() => toggle(activeRarities, setActiveRarities, r)}
                  >
                    <RarityBadge rarity={r} /> {r}
                  </button>
                ))}
              </div>
            </div>}

            <div className="rail-section">
              <div className="rail-section-head">
                <h3 className="rail-label">Purchase cost</h3>
                <span className="rail-cost-value">
                  {minCost === null && maxCost === null
                    ? "any"
                    : minCost === maxCost ? minCost : `${minCost}–${maxCost}`}
                </span>
              </div>
              {/* One pip per cost the CATALOG actually has, rather than a
                  hardcoded 1-12: costs run 0-12 today and a future set
                  should not silently fall off the end of the control. */}
              <div className="cost-pips" onMouseLeave={() => setCostDragAnchor(null)}>
                {costRange.map((n) => {
                  const on = minCost !== null && maxCost !== null && n >= minCost && n <= maxCost;
                  return (
                    <button
                      key={n}
                      className={`cost-pip${on ? " on" : ""}`}
                      aria-pressed={on}
                      title={`Purchase cost ${n}`}
                      onMouseDown={() => {
                        costDragged.current = false;
                        setCostDragAnchor(n);
                      }}
                      onMouseEnter={() => {
                        if (costDragAnchor === null) return;
                        costDragged.current = true;
                        selectCostRange(costDragAnchor, n);
                      }}
                      onMouseUp={() => setCostDragAnchor(null)}
                      onClick={(e) => clickCost(n, e.shiftKey)}
                    >
                      {n}
                    </button>
                  );
                })}
              </div>
              <p className="rail-hint">click a cost, drag or shift-click for a range</p>
            </div>

            {!USE_DICE_KINGDOM_CATALOG && <>
            {SHOW_AFFILIATIONS && <div className="rail-section">
              <div className="rail-section-head">
                <h3 className="rail-label">Affiliation</h3>
                <span className="rail-summary">
                  {activeAffiliations.size > 0
                    ? `${activeAffiliations.size} of ${allAffiliations.length}`
                    : allAffiliations.length}
                </span>
              </div>
              <input
                type="text"
                className="rail-find"
                placeholder="find an affiliation…"
                value={affiliationFilter}
                onChange={(e) => setAffiliationFilter(e.target.value)}
              />
              {/* Logos only - the logo is all a card itself shows, so it
                  is what people match on - with the name on hover. */}
              <div className="rail-chips scroll">
                {shownAffiliations.map((a) => (
                  <button
                    key={a}
                    className={`rail-chip icon${activeAffiliations.has(a) ? " on" : ""}`}
                    aria-pressed={activeAffiliations.has(a)}
                    aria-label={a}
                    title={a}
                    onClick={() => toggle(activeAffiliations, setActiveAffiliations, a)}
                  >
                    <AffiliationBadge name={a} code={affiliationIconIndex[a]} />
                  </button>
                ))}
                {shownAffiliations.length === 0 && <p className="rail-hint">No affiliation matches that.</p>}
              </div>
            </div>}

            <div className="rail-section">
              <div className="rail-section-head">
                <h3 className="rail-label">Set</h3>
                <span className="rail-summary">
                  {activeSets.size > 0 ? `${activeSets.size} of ${allSets.length}` : allSets.length}
                </span>
              </div>
              <div className="rail-chips scroll">
                {allSets.map((st) => (
                  <button
                    key={st}
                    className={`rail-chip code${activeSets.has(st) ? " on" : ""}`}
                    aria-pressed={activeSets.has(st)}
                    title={SET_NAMES[st] ?? st}
                    onClick={() => toggle(activeSets, setActiveSets, st)}
                  >
                    {st}
                  </button>
                ))}
              </div>
            </div>

            <div className="rail-section">
              <h3 className="rail-label">Format</h3>
              <div className="rail-options">
                {[{ id: "", label: "No format", description: "Every set in the catalog." }, ...FORMATS].map((f) => (
                  <button
                    key={f.id}
                    className={`rail-option${formatId === f.id ? " on" : ""}`}
                    aria-pressed={formatId === f.id}
                    onClick={() => setFormatId(f.id)}
                  >
                    <span className="rail-option-label">{f.label}</span>
                    <span className="rail-option-note">{f.description}</span>
                  </button>
                ))}
              </div>
              <label className="rail-toggle">
                <input
                  type="checkbox"
                  checked={applyOrangeBan}
                  onChange={(e) => setApplyOrangeBan(e.target.checked)}
                />
                <span className="rail-toggle-track" aria-hidden="true"><span className="rail-toggle-knob" /></span>
                <span>Apply Orange Ban list ({ORANGE_BAN_LIST.length} cards)</span>
              </label>

            </div>
            </>}
          </section>
        </aside>

        <div className="main-column" id="builder-catalog">
          <h2>{USE_DICE_KINGDOM_CATALOG ? "Dice Kingdom — Team Builder" : "Team Builder - Card Search"}</h2>
          {!USE_DICE_KINGDOM_CATALOG && <p className="hint">
            Browse cards that are implemented and available to use in the game.
          </p>}

          {/* What is narrowing the results, shown WITH the results. A chip
              selected three sections down a scrolled rail is otherwise
              invisible from here, which is how you end up staring at an
              empty table wondering why. */}
          <div className="active-filter-bar">
            <span className="active-filter-label">Filtering</span>
            {/* The search box is not a chip - Clear all deliberately leaves
                it alone - but it still narrows the results, so this line
                must not claim the whole catalog is showing when it is
                not. */}
            {filterChips.length === 0 && (
              <span className="rail-hint">
                {search.trim() ? "search only — playable cards" : "nothing — showing all playable cards"}
              </span>
            )}
            {filterChips.map((chip) => (
              <button
                key={chip.key}
                className="active-filter-chip"
                onClick={chip.remove}
                aria-label={`Remove filter: ${chip.name}`}
                title={`Remove filter: ${chip.name}`}
              >
                {chip.label}
                <span className="active-filter-x" aria-hidden="true">×</span>
              </button>
            ))}
            {filterChips.length > 0 && (
              <button className="active-filter-clear" onClick={clearAllFilters}>
                Clear all
              </button>
            )}
            <span className="result-sorts">
              <span className="active-filter-label">Sort</span>
              {SORTS.filter((option) => !USE_DICE_KINGDOM_CATALOG || option.key !== "set").map((option) => (
                <button
                  key={option.key}
                  className={`sort-chip${sort.key === option.key ? " on" : ""}`}
                  aria-pressed={sort.key === option.key}
                  title={`${option.title} (click again to reverse)`}
                  onClick={() => toggleSort(option.key)}
                >
                  {option.label}
                  {sort.key === option.key && (
                    <span className="sort-arrow">{sort.direction === "asc" ? "▲" : "▼"}</span>
                  )}
                </button>
              ))}
            </span>
            <span className="active-filter-count">
              <strong>{sorted.length}</strong> {sorted.length === 1 ? "card" : "cards"}
              {sorted.length > MAX_ROWS && <span className="rail-hint"> · showing first {MAX_ROWS}</span>}
            </span>
          </div>

          {cards === null ? (
            <p className="hint">Loading catalog...</p>
          ) : (
            <>
              {/* Rows, not a table. The stats a card is chosen on - cost,
                  energy, its three levels - are a shape you compare
                  across rows, and a die drawn as a die is read faster
                  than three numbers in three columns. The printed text
                  stays fully visible on every row: it is the main thing
                  people read here, so it gets no hover and no expander. */}
              <div className="result-rows">
                {visible.map((c) => {
                  const add = canAddCard(c);
                  const onTeam = team.has(c.id);
                  return (
                    <div className={`result-row ${rarityClass(c.rarity)}`} key={c.id}>
                      <div className="result-add">
                        <button
                          className={`result-add-button${onTeam ? " on-team" : add.ok ? "" : " blocked"}`}
                          disabled={!add.ok}
                          // The reason names the FIX where there is one -
                          // canAddCard's cap messages say "raise it in
                          // Custom, or switch to Freeform" rather than
                          // just refusing.
                          title={onTeam ? "Already on the team" : (add.reason ?? `Add ${c.name}`)}
                          aria-label={onTeam ? `${c.name} is already on the team` : `Add ${c.name}`}
                          onClick={() => addCard(c)}
                        >
                          {onTeam ? "✓" : "+"}
                        </button>
                        <span className="result-set" title={c.set ? SET_NAMES[c.set] : undefined}>
                          {c.set ?? "-"}
                        </span>
                      </div>

                      <div className="result-identity">
                        <div className="result-headline">
                          <RarityBadge rarity={c.rarity} />
                          <CardAvatar card={c} />
                          <span className="result-name">{c.name}</span>
                          {c.subtitle && !isBasicActionFamily(c) && <span className="result-subtitle">{c.subtitle}</span>}
                          <span className="result-type">{typeLabel(c.type)}</span>
                          <CardBadges card={c} />
                        </div>
                        <div className="result-text"><CardText text={c.rawText} /></div>
                      </div>

                      <div className="result-stats">
                        <div className="stat">
                          <span className="stat-caption">cost</span>
                          <span className="stat-cost">{c.purchaseCost}</span>
                        </div>
                        {c.energyTypes.length > 0 && (
                          <div className="stat">
                            <span className="stat-caption">energy</span>
                            <span className="stat-energy"><BuilderEnergyTypes types={c.energyTypes} /></span>
                          </div>
                        )}
                        {isBasicActionFamily(c) && c.dieEnergyType && (
                          <div className="stat">
                            <span className="stat-caption">die energy</span>
                            <span className="stat-energy"><BuilderEnergyTypes types={[c.dieEnergyType]} /></span>
                          </div>
                        )}
                        {c.levels.length > 0 && (
                          <div className="stat">
                            <span className="stat-caption">levels · max {c.dieLimit}</span>
                            <span className="stat-faces">
                              {c.levels.map((face, i) => (
                                <span className="stat-face" key={i}>
                                  <DieFace face={face} />
                                  <span className="stat-face-label">L{i + 1}</span>
                                </span>
                              ))}
                            </span>
                          </div>
                        )}
                        {SHOW_AFFILIATIONS && c.affiliations.length > 0 && (
                          <div className="stat">
                            <span className="stat-caption">affil</span>
                            <span className="stat-affiliations">
                              <AffiliationIcons
                                codes={c.affiliationIcons}
                                names={c.affiliations}
                                index={affiliationIconIndex}
                              />
                            </span>
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
              <p className="result-footer">
                showing {visible.length} of {sorted.length}
                {sorted.length > MAX_ROWS && " - narrow your search to see the rest"}
              </p>
            </>
          )}
        </div>

        <div className="team-sidebar" id="builder-team">
          <div className="team-sidebar-header">
            <h3>Your team</h3>
            <div className="roster-view-toggle" role="group" aria-label="Roster view">
              {(["slots", "list"] as const).map((view) => (
                <button
                  key={view}
                  className={rosterView === view ? "selected" : ""}
                  aria-pressed={rosterView === view}
                  onClick={() => setRosterView(view)}
                >
                  {view === "slots" ? "Slots" : "List"}
                </button>
              ))}
            </div>
          </div>
          {/* The ruleset drives everything below it: the dice meter's
              length, what counts as over, and which adds are blocked. */}
          <div className="ruleset-picker" role="group" aria-label="Ruleset">
            {RULESETS.map((option) => (
              <button
                key={option.id}
                className={`ruleset-option${ruleset === option.id ? " selected" : ""}`}
                aria-pressed={ruleset === option.id}
                onClick={() => setRuleset(option.id)}
              >
                <span className="ruleset-label">{option.label}</span>
                <span className="ruleset-note">{USE_DICE_KINGDOM_CATALOG && option.id === "standard" ? "8 cards · 20 dice · 1 BA" : option.note}</span>
              </button>
            ))}
          </div>

          {ruleset === "custom" && (
            <div className="custom-caps">
              {CUSTOM_CAP_FIELDS.map(({ key, label, min }) => (
                <div className="custom-cap" key={key}>
                  <span className="custom-cap-label">{label}</span>
                  <span className="custom-cap-stepper">
                    <button
                      disabled={customCaps[key] <= min}
                      onClick={() => setCustomCaps({ ...customCaps, [key]: customCaps[key] - 1 })}
                      aria-label={`Fewer ${label}`}
                    >
                      −
                    </button>
                    <span className="custom-cap-value">{customCaps[key]}</span>
                    <button
                      onClick={() => setCustomCaps({ ...customCaps, [key]: customCaps[key] + 1 })}
                      aria-label={`More ${label}`}
                    >
                      +
                    </button>
                  </span>
                </div>
              ))}
            </div>
          )}

          <div className="legality-strip">
            <div className="legality-counts">
              <span className="legality-dice">
                <span className="legality-caption">Dice</span>
                <strong>{totalDice}{isCapped(caps.dice) ? `/${caps.dice}` : ""}</strong>
              </span>
              <span className="legality-cards">
                {cardCount}{isCapped(caps.cards) ? `/${caps.cards}` : ""} cards ·{" "}
                {basicActionEntries.length}{isCapped(caps.basicActions) ? `/${caps.basicActions}` : ""} basic actions
              </span>
            </div>
            <DiceMeter used={totalDice} cap={caps.dice} />
            <p className={`legality-note${legality.ok ? "" : " over"}`}>{legality.note}</p>
          </div>

          {USE_DICE_KINGDOM_CATALOG && champions.length > 0 && <div className="dk-builder-champion">
            <div className="dk-builder-champion-head">
              <span>Champion</span>
              <span className="dk-builder-tardigrade-summary">{champion ? champion.name : "None"}</span>
            </div>
            <div className="dk-builder-champion-options" role="radiogroup" aria-label="Champion">
              <button type="button" role="radio" aria-checked={!champion} className={!champion ? "selected" : ""}
                onClick={() => pickChampion(null)}>None</button>
              {champions.map((c) => {
                const Icon = CHAMPION_ICONS[c.id];
                return <button key={c.id} type="button" role="radio" aria-checked={championId === c.id}
                  className={championId === c.id ? "selected" : ""} title={c.passiveText}
                  onClick={() => pickChampion(c.id)}>
                  {Icon && <Icon size={20} />}
                  <span>{c.name}</span>
                  <BuilderEnergyTypes types={[c.energySymbolId]} />
                </button>;
              })}
            </div>
            <p className="hint">
              {champion
                ? <>{champion.passiveText.replace(/\.?$/, ".")} Its Basic Action must be {champion.energySymbolId}.</>
                : "No Champion power. Pick one to play with its power - its Basic Action then has to share its energy."}
            </p>
          </div>}

          {USE_DICE_KINGDOM_CATALOG && <details className="dk-builder-tardigrades">
            <summary>
              <span>Starting Tardigrades</span>
              <span className="dk-builder-tardigrade-summary">{tardigradeCount}/{MAX_STARTING_TARDIGRADES} Dice</span>
            </summary>
            <p className="hint">Choose your starting energy types (up to eight dice total). To switch types when full, remove a die first.</p>
            <div className="dk-builder-tardigrade-options">
              {TARDIGRADE_TYPES.map((type) => <div className="dk-builder-tardigrade-option" key={type}>
                <BuilderEnergyTypes types={[type]} />
                <span className="dk-builder-tardigrade-name">{type}</span>
                <button type="button" disabled={tardigrades[type] === 0} aria-label={`Remove ${type} Tardigrade`}
                  onClick={() => setTardigrades((old) => changeTardigradeCount(old, type, -1))}>−</button>
                <strong>{tardigrades[type]}</strong>
                <button type="button" disabled={tardigradeCount >= MAX_STARTING_TARDIGRADES} aria-label={`Add ${type} Tardigrade`}
                  onClick={() => setTardigrades((old) => changeTardigradeCount(old, type, 1))}>+</button>
              </div>)}
            </div>
          </details>}

          {/* The roster drawn as its real SHAPE - one tile per slot the
              ruleset allows, dice as pips - so legality is read at a
              glance rather than parsed out of "3/8 cards, 7/20 dice".
              The list view is kept for the same reason the old community
              builder kept one: when you are checking text rather than
              shape, a dense list beats tiles. */}
          {rosterView === "slots" ? (
            <div className="roster-scroll">
              <div className="slot-grid">
                {Array.from({ length: characterSlotCount }, (_, i) => {
                  const entry = characterEntries[i];
                  if (!entry) {
                    return (
                      <div className="slot-tile empty" key={`empty-${i}`}>
                        <span className="slot-number">{i + 1}</span>
                        <span className="slot-empty-label">Empty slot</span>
                      </div>
                    );
                  }
                  const { card, count } = entry;
                  const overCap = isCapped(caps.cards) && i >= caps.cards;
                  return (
                    <div
                      className={`slot-tile ${rarityClass(card.rarity)}${overCap ? " over" : ""}`}
                      key={card.id}
                      title={overCap ? `Past the ${caps.cards}-card cap for this ruleset.` : undefined}
                    >
                      <div className="slot-top">
                        <RarityBadge rarity={card.rarity} />
                        <div className="slot-name-line">
                          <CardAvatar card={card} />
                          <span className="slot-name" title={card.name}>{card.name}</span>
                          {card.set && <span className="slot-set">({card.set})</span>}
                          <CardBadges card={card} />
                        </div>
                        <button
                          className="slot-remove"
                          onClick={() => removeCard(card.id)}
                          aria-label={`Remove ${card.name}`}
                          title={`Remove ${card.name}`}
                        >
                          ×
                        </button>
                      </div>
                      {card.subtitle && !isBasicActionFamily(card) && <div className="slot-subtitle" title={card.subtitle}>{card.subtitle}</div>}
                      <div className="slot-meta">
                        <span className="slot-cost" title="Purchase cost">Cost {card.purchaseCost}</span>
                        {card.energyTypes.length > 0 && <BuilderEnergyTypes types={card.energyTypes} />}
                        {SHOW_AFFILIATIONS && card.affiliations.length > 0 && (
                          <span className="slot-affiliations">
                            <AffiliationIcons
                              codes={card.affiliationIcons}
                              names={card.affiliations}
                              index={affiliationIconIndex}
                            />
                          </span>
                        )}
                      </div>
                      {card.rawText.trim() && <div className="slot-ability"><CardText text={card.rawText} /></div>}
                      <div className="slot-dice">
                        <TeamDiceControls
                          name={card.name}
                          count={count}
                          limit={card.dieLimit}
                          canIncrease={canIncrement(card, count)}
                          onDecrease={() => setCount(card.id, count - 1)}
                          onIncrease={() => setCount(card.id, count + 1)}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>

              {/* Basic Actions get their own grid, in the same violet the
                  match table uses for community property - they are shared
                  with the opponent, and their dice sit outside the dice cap
                  (rule 2.1.4). Two facts worth stating where they apply. */}
              <div className="slot-grid basic-action-grid">
                {Array.from({ length: basicActionSlotCount }, (_, i) => {
                  const entry = basicActionEntries[i];
                  if (!entry) {
                    return (
                      <div className="slot-tile basic-action empty" key={`ba-empty-${i}`}>
                        <span className="slot-empty-label">Basic Action — empty{champion ? ` (${champion.energySymbolId})` : ""}</span>
                      </div>
                    );
                  }
                  const { card, count } = entry;
                  return (
                    <div className="slot-tile basic-action" key={card.id}>
                      <div className="slot-top">
                        <span className="slot-caption">Basic Action</span>
                        <button
                          className="slot-remove"
                          onClick={() => removeCard(card.id)}
                          aria-label={`Remove ${card.name}`}
                          title={`Remove ${card.name}`}
                        >
                          ×
                        </button>
                      </div>
                      <div className="slot-name-line">
                        <CardAvatar card={card} />
                          <span className="slot-name" title={card.name}>{card.name}</span>
                        {card.set && <span className="slot-set">({card.set})</span>}
                        <CardBadges card={card} />
                      </div>
                      <div className="slot-meta">
                        <span className="slot-cost" title="Purchase cost">Cost {card.purchaseCost}</span>
                        {card.dieEnergyType && <span title="Energy provided by the die">Die: <BuilderEnergyTypes types={[card.dieEnergyType]} /></span>}
                      </div>
                      {card.rawText.trim() && <div className="slot-ability"><CardText text={card.rawText} /></div>}
                      {champion && !basicActionFits(card.dieEnergyType, champion.energySymbolId) &&
                        <div className="slot-mismatch">Not {champion.energySymbolId} - {champion.name} can't use this one.</div>}
                      <div className="slot-ba-dice"><FixedDiceDisplay count={count} limit={card.dieLimit} /> <span>· outside the dice cap</span></div>
                    </div>
                  );
                })}
              </div>
            </div>
          ) : characterEntries.length === 0 && basicActionEntries.length === 0 ? (
            <p className="hint">No cards yet - click "+" on a card to add it.</p>
          ) : (
            <ul className="team-list">
              {[...characterEntries, ...basicActionEntries].map(({ card, count }) => (
                <li key={card.id} className={`team-list-item ${rarityClass(card.rarity)}`}>
                  <div className="team-card-header">
                    <RarityBadge rarity={card.rarity} />
                    <div className="team-card-identity">
                      <div className="team-card-name-line">
                        <CardAvatar card={card} />
                        <span className="team-card-name">{card.name}</span>
                        {card.set && <span className="team-card-set">({card.set})</span>}
                        <CardBadges card={card} />
                      </div>
                      {card.subtitle && !isBasicActionFamily(card) && <div className="hint">{card.subtitle}</div>}
                    </div>
                  {isBasicActionFamily(card) ? (
                    <FixedDiceDisplay count={count} limit={card.dieLimit} />
                  ) : (
                    <TeamDiceControls
                      name={card.name}
                      count={count}
                      limit={card.dieLimit}
                      canIncrease={canIncrement(card, count)}
                      onDecrease={() => setCount(card.id, count - 1)}
                      onIncrease={() => setCount(card.id, count + 1)}
                    />
                  )}
                  <button className="team-remove-button" onClick={() => removeCard(card.id)}>
                    Remove
                  </button>
                  </div>
                  <div className="team-card-meta">
                    <span className="team-card-cost" title="Purchase cost">Cost {card.purchaseCost}</span>
                    <span>{card.energyTypes.length > 0 ? <BuilderEnergyTypes types={card.energyTypes} /> : card.dieEnergyType ? <>Die: <BuilderEnergyTypes types={[card.dieEnergyType]} /></> : "No energy type"}</span>
                    {SHOW_AFFILIATIONS && card.affiliations.length > 0 && (
                      <span><AffiliationIcons codes={card.affiliationIcons} names={card.affiliations} index={affiliationIconIndex} /></span>
                    )}
                  </div>
                  <div className="team-card-text"><CardText text={card.rawText} /></div>
                </li>
              ))}
            </ul>
          )}

          {/* Aggregate feedback the slot grid cannot give: the grid says
              WHICH cards are on the team, this says what they add up to.
              Hidden while the team is empty - four empty charts are not a
              useful thing to look at. */}
          {characterEntries.length > 0 && (
            <div className="team-shape">
              <h3 className="rail-label">Team shape</h3>

              <div className="shape-curve" role="img" aria-label={
                teamShape.curve.map((c) => `${c.dice} ${c.dice === 1 ? "die" : "dice"} at cost ${c.cost}`).join(", ")
              }>
                {teamShape.curve.map(({ cost, dice }) => (
                  <div className="shape-curve-column" key={cost}>
                    <span className="shape-curve-count">{dice}</span>
                    <span
                      className="shape-curve-bar"
                      style={{ height: `${4 + (dice / teamShape.peak) * 40}px` }}
                    />
                    <span className="shape-curve-cost">{cost}</span>
                  </div>
                ))}
              </div>
              <p className="shape-caption">dice by purchase cost</p>

              {teamShape.energy.length > 0 && (
                <>
                  <div className="shape-energy">
                    {teamShape.energy.map(({ type, dice, share }) => (
                      <span
                        key={type}
                        className={`shape-energy-segment energy-${type.toLowerCase()}`}
                        style={{ flexGrow: share }}
                        title={`${dice} ${dice === 1 ? "die" : "dice"} with ${type}`}
                      />
                    ))}
                  </div>
                  <div className="shape-energy-legend">
                    {teamShape.energy.map(({ type, dice }) => (
                      <span key={type} className="shape-energy-item">
                        <BuilderEnergyTypes types={[type]} /> {dice}
                      </span>
                    ))}
                  </div>
                </>
              )}

              <div className="shape-stats">
                <div className="shape-stat">
                  <span className="shape-stat-value">{teamShape.averageCost.toFixed(1)}</span>
                  <span className="shape-stat-caption">avg cost / die</span>
                </div>
                <div
                  className="shape-stat"
                  title="Dice whose card the game engine can actually run. The rest are fine on a table."
                >
                  <span className="shape-stat-value">{teamShape.playableDice}/{totalDice}</span>
                  <span className="shape-stat-caption">playable in app</span>
                </div>
                {SHOW_AFFILIATIONS && <div className="shape-stat">
                  <span className="shape-stat-value">{teamShape.affiliations.length}</span>
                  <span className="shape-stat-caption">affiliations</span>
                </div>}
              </div>

              {/* The fastest read on whether an affiliation-dependent team
                  actually has the bodies: "X-Men · 3 dice" answers it,
                  where a list of card names does not. */}
              {SHOW_AFFILIATIONS && teamShape.affiliations.length > 0 && (
                <div className="shape-affiliations">
                  {teamShape.affiliations.map(({ name, dice }) => (
                    <span className="shape-affiliation" key={name}>
                      <AffiliationBadge name={name} code={affiliationIconIndex[name]} />
                      {dice}
                    </span>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* One primary action, everything else demoted to a ghost row.
              Before this, "Start Game" sat third in a stack of equals and
              read as the least important of the three. */}
          {USE_DICE_KINGDOM_CATALOG && tardigradeCount !== MAX_STARTING_TARDIGRADES && (
            <p className="hint" role="status">
              Select exactly {MAX_STARTING_TARDIGRADES} starting Tardigrades before playing
              {` (currently ${tardigradeCount}/${MAX_STARTING_TARDIGRADES}).`}
            </p>
          )}
          {!USE_DICE_KINGDOM_CATALOG ? <button
            className="team-play-button"
            disabled={team.size === 0 || !legality.ok || starting}
            title={team.size === 0 ? "Add some cards first." : !legality.ok ? legality.note : undefined}
            onClick={startGame}
          >
            {starting ? "Starting..." : "Play with this team"}
          </button> : <button className="team-play-button"
            title={tardigradeCount !== MAX_STARTING_TARDIGRADES
              ? `Select exactly ${MAX_STARTING_TARDIGRADES} starting Tardigrades before playing.`
              : !legality.ok ? legality.note : undefined}
            onClick={() => playCustomTeam()}>Play with this team</button>}
          {startWarning && <TeamStartDialog
            messages={startWarning.messages}
            canProceed={startWarning.canProceed}
            onClose={() => setStartWarning(null)}
            onProceed={() => playCustomTeam(true)}
          />}

          <div className="team-secondary-actions">
            <button onClick={copyTeamLink} disabled={team.size === 0}>
              {copied ? "Copied!" : "Share link"}
            </button>
            <button
              onClick={() => window.print()}
              disabled={team.size === 0}
              title="Print a plain team sheet for physical play."
            >
              Print list
            </button>
            {!USE_DICE_KINGDOM_CATALOG && <button
              onClick={copyOldTeamLink}
              disabled={team.size === 0}
              title={`Opens the team in the old Teambuilder at ${OLD_TEAM_BUILDER_URL}, which has card images.`}
            >
              {copiedOld ?? "Old builder"}
            </button>}
            <button
              disabled={team.size === 0 && (!USE_DICE_KINGDOM_CATALOG || tardigradeCount === 0)}
              onClick={() => {
                if (window.confirm(USE_DICE_KINGDOM_CATALOG
                  ? "Remove all cards from this team and reset all Tardigrades to 0?"
                  : "Remove all cards from this team?")) {
                  setTeam(new Map());
                  if (USE_DICE_KINGDOM_CATALOG) setTardigrades({ Claw: 0, Shell: 0, Wing: 0, Eye: 0 });
                }
              }}
            >
              Clear
            </button>
          </div>
          <p className="team-actions-footnote">Team autosaves · link keeps counts and ruleset</p>

          {/* What "Print list" prints. Hidden on screen, and the only
              thing NOT hidden on paper - printing the page itself would
              produce three columns of dark panels and a 200-row catalog.
              A team sheet for physical play wants the opposite: the cards,
              their dice counts, and the text, in ink you can read. */}
          <div className="team-print-sheet" aria-hidden="true">
            <h1>{USE_DICE_KINGDOM_CATALOG ? "Dice Kingdom team" : "Dice Masters team"}</h1>
            <p className="team-print-summary">
              {cardCount} {cardCount === 1 ? "card" : "cards"} · {totalDice} dice ·{" "}
              {basicActionEntries.length} basic {basicActionEntries.length === 1 ? "action" : "actions"} ·{" "}
              {RULESETS.find((r) => r.id === ruleset)?.label ?? ruleset}
              {USE_DICE_KINGDOM_CATALOG && <> · {tardigradeCount} starting Tardigrades</>}
            </p>
            {USE_DICE_KINGDOM_CATALOG && <section className="team-print-tardigrades">
              <h2>Starting Tardigrades</h2>
              <p>{TARDIGRADE_TYPES.map(type => `${type}: ${tardigrades[type]}`).join(" · ")}</p>
            </section>}
            <table>
              <thead>
                <tr>
                  <th>Dice</th>
                  <th>Card</th>
                  <th>Cost</th>
                  <th>Set</th>
                  <th>Text</th>
                </tr>
              </thead>
              <tbody>
                {[...characterEntries, ...basicActionEntries].map(({ card, count }) => (
                  <tr key={card.id}>
                    <td>{count}</td>
                    <td>
                      <strong>{card.name}</strong>
                      {card.subtitle && !isBasicActionFamily(card) && <> — {card.subtitle}</>}
                      {/* The letter, not the colour: a rarity stripe is
                          the first thing a monochrome printer loses. */}
                      {rarityLetter(card.rarity) && <> [{rarityLetter(card.rarity)}]</>}
                    </td>
                    <td>{card.purchaseCost}</td>
                    <td>{card.set}</td>
                    <td>{card.rawText}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {startError && <div className="error">{startError}</div>}
        </div>
      </div>
    </div>
  );
}
