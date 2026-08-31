import { Fragment, useDeferredValue, useEffect, useMemo, useState } from "react";
import { matchesQuery } from "./cardSearch";
import { CardText } from "./CardText";
import { AffiliationBadge, AffiliationIcons } from "./AffiliationIcons";
import { buildAffiliationIconIndex } from "./affiliationIndex";
import { DieFace, DIE_FACE_TITLE } from "./DieFace";
import { EnergyTypes } from "./GameIcon";
import { api } from "./api";
import { stashPendingGame } from "./gameHandoff";
import { navigate } from "./router";
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

const BASIC_ACTION_TYPES = new Set(["BasicAction", "EpicBasicAction"]);

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

// One pip per die the CARD allows, filled to the number you own - so the
// per-card ceiling is visible without hovering or reading a fraction.
// Above about a dozen the pips stop being countable at a glance and the
// fraction does the job better; no real card comes close, but bulk data
// has surprised this catalog before.
const MAX_LEGIBLE_PIPS = 12;

// At module scope, not inside the component with the other storage keys:
// the roster-view state reads it in its lazy initialiser, which runs
// before those declarations are reached.
const ROSTER_VIEW_KEY = "dicefight.teamBuilder.rosterView";

function DicePips({ count, limit }: { count: number; limit: number }) {
  if (limit > MAX_LEGIBLE_PIPS) return <span className="slot-dice-count">{count}/{limit}</span>;
  return (
    <span className="slot-pips" role="img" aria-label={`${count} of ${limit} dice`}>
      {Array.from({ length: limit }, (_, i) => (
        <span key={i} className={`slot-pip${i < count ? " owned" : ""}`} />
      ))}
    </span>
  );
}

// Rendered before the row cap below applies, so typing narrows the
// visible count too - keeps a huge future catalog from ever forcing a
// full re-render on every keystroke (see the design doc's scaling note).
const MAX_ROWS = 200;

const COLUMNS: { key: SortKey; label: string; title?: string; className?: string }[] = [
  { key: "set", label: "Set" },
  { key: "name", label: "Name" },
  { key: "type", label: "Type" },
  { key: "affiliations", label: "Affiliation", className: "card-affiliation" },
  { key: "purchaseCost", label: "Cost" },
  { key: "energyTypes", label: "Energy", className: "card-energy" },
  { key: "dieLimit", label: "Max" },
  { key: "level1", label: "L1", title: DIE_FACE_TITLE },
  { key: "level2", label: "L2", title: DIE_FACE_TITLE },
  { key: "level3", label: "L3", title: DIE_FACE_TITLE },
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
  const RULESET_KEY = "dicefight.teamBuilder.ruleset";

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
  const caps = useMemo(() => capsFor(ruleset, customCaps), [ruleset, customCaps]);

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
                  <EnergyTypes types={[t]} /> {t}
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
            {/* Affiliation and Set open across the full width of the
                filter bar rather than down a narrow column: 128 and 49
                options stacked vertically pushed the results table off
                the screen, which is the one thing you need to still see
                while you pick. Affiliation is logos only - the logo is
                all a card itself shows, so it is what people match on -
                with the name on hover. */}
            <details className="card-catalog-chips">
              <summary>
                Affiliation{activeAffiliations.size > 0 ? ` (${activeAffiliations.size} selected)` : ` (${allAffiliations.length})`}
              </summary>
              <div className="card-catalog-chip-options">
                {allAffiliations.map((a) => (
                  <label
                    key={a}
                    className={`affiliation-chip${activeAffiliations.has(a) ? " selected" : ""}`}
                    title={a}
                  >
                    <input
                      type="checkbox"
                      checked={activeAffiliations.has(a)}
                      onChange={() => toggle(activeAffiliations, setActiveAffiliations, a)}
                    />
                    <AffiliationBadge name={a} code={affiliationIconIndex[a]} />
                  </label>
                ))}
              </div>
            </details>
            <details className="card-catalog-chips">
              <summary>
                Set{activeSets.size > 0 ? ` (${activeSets.size} selected)` : ` (${allSets.length})`}
              </summary>
              <div className="card-catalog-chip-options">
                {allSets.map((s) => (
                  <label
                    key={s}
                    className={`set-chip${activeSets.has(s) ? " selected" : ""}`}
                    title={SET_NAMES[s] ?? s}
                  >
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
                      <th key={col.key} className={col.className} title={col.title} onClick={() => toggleSort(col.key)}>
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
                        <td className="card-affiliation"><AffiliationIcons codes={c.affiliationIcons} names={c.affiliations} index={affiliationIconIndex} /></td>
                        <td>{c.purchaseCost}</td>
                        <td className="card-energy"><EnergyTypes types={c.energyTypes} /></td>
                        <td>{c.dieLimit}</td>
                        <td className="card-level"><DieFace face={c.levels[0]} /></td>
                        <td className="card-level"><DieFace face={c.levels[1]} /></td>
                        <td className="card-level"><DieFace face={c.levels[2]} /></td>
                        <td>{c.isImplemented ? "✓" : ""}</td>
                      </tr>
                      {/* Printed text on its own full-width row rather than
                          in the Name cell: it spans every column, so it gets
                          real room to read instead of squeezing the stats. */}
                      <tr className={`card-text-row ${rarityClass(c.rarity)}`}>
                        <td colSpan={COLUMNS.length + 1}>
                          <CardText text={c.rawText} />
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
                <span className="ruleset-note">{option.note}</span>
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
                        <span className="slot-set">{card.set}</span>
                        <button
                          className="slot-remove"
                          onClick={() => removeCard(card.id)}
                          aria-label={`Remove ${card.name}`}
                          title={`Remove ${card.name}`}
                        >
                          ×
                        </button>
                      </div>
                      <div className="slot-name" title={card.name}>{card.name}</div>
                      {card.subtitle && <div className="slot-subtitle" title={card.subtitle}>{card.subtitle}</div>}
                      <div className="slot-meta">
                        <span className="slot-cost" title="Purchase cost">{card.purchaseCost}</span>
                        {card.energyTypes.length > 0 && <EnergyTypes types={card.energyTypes} />}
                        {card.affiliations.length > 0 && (
                          <span className="slot-affiliations">
                            <AffiliationIcons
                              codes={card.affiliationIcons}
                              names={card.affiliations}
                              index={affiliationIconIndex}
                            />
                          </span>
                        )}
                      </div>
                      <div className="slot-dice">
                        <button
                          disabled={count <= 1}
                          onClick={() => setCount(card.id, count - 1)}
                          aria-label={`One fewer ${card.name} die`}
                        >
                          −
                        </button>
                        <DicePips count={count} limit={card.dieLimit} />
                        <button
                          disabled={!canIncrement(card, count)}
                          onClick={() => setCount(card.id, count + 1)}
                          aria-label={`One more ${card.name} die`}
                        >
                          +
                        </button>
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
                        <span className="slot-empty-label">Basic Action — empty</span>
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
                      <div className="slot-name" title={card.name}>{card.name}</div>
                      <div className="slot-ba-dice">{count} dice · outside the dice cap</div>
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
                  <div className="team-card-meta">
                    <span className="team-card-cost" title="Purchase cost">{card.purchaseCost}</span>
                    <span>{card.energyTypes.length > 0 ? <EnergyTypes types={card.energyTypes} /> : "No energy type"}</span>
                    {card.affiliations.length > 0 && (
                      <span><AffiliationIcons codes={card.affiliationIcons} names={card.affiliations} index={affiliationIconIndex} /></span>
                    )}
                  </div>
                  <div className="team-card-text"><CardText text={card.rawText} /></div>
                </li>
              ))}
            </ul>
          )}

          {/* One primary action, everything else demoted to a ghost row.
              Before this, "Start Game" sat third in a stack of equals and
              read as the least important of the three. */}
          <button
            className="team-play-button"
            disabled={team.size === 0 || !legality.ok || starting}
            title={team.size === 0 ? "Add some cards first." : !legality.ok ? legality.note : undefined}
            onClick={startGame}
          >
            {starting ? "Starting..." : "Play with this team"}
          </button>

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
            <button
              onClick={copyOldTeamLink}
              disabled={team.size === 0}
              title={`Opens the team in the old Teambuilder at ${OLD_TEAM_BUILDER_URL}, which has card images.`}
            >
              {copiedOld ?? "Old builder"}
            </button>
            <button
              disabled={team.size === 0}
              onClick={() => {
                if (window.confirm("Remove all cards from this team?")) setTeam(new Map());
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
            <h1>Dice Masters team</h1>
            <p className="team-print-summary">
              {cardCount} {cardCount === 1 ? "card" : "cards"} · {totalDice} dice ·{" "}
              {basicActionEntries.length} basic {basicActionEntries.length === 1 ? "action" : "actions"} ·{" "}
              {RULESETS.find((r) => r.id === ruleset)?.label ?? ruleset}
            </p>
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
                      {card.subtitle && <> — {card.subtitle}</>}
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
