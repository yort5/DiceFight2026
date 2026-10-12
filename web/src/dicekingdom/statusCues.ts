// Die status cues - Claude Design's "face frame" system (option 1b,
// design_handoff_die_status_cues, 2026-10-03; brief in v3/STATUS_CUES_BRIEF.md).
// Every cue is part of the die itself: combat rules on its border plus a
// tab on its lane edge, Deadly as a red corner, a blank as struck-through
// art, a granted keyword as a cream side tab, changed stats as number
// chips. This module turns a die's server-side `statuses` into what the
// tile draws (tileCues) and what its explainer says (explainRows), so the
// two read one source and can't disagree.
import type { CardDef, Die, DieStatus, DieStatusKind } from "./types";
import { printedStats } from "./dieFaces";

export const CUE_COLORS = {
  combat: "oklch(0.85 0.15 88)", // amber - deliberately no energy colour
  ability: "#fff8ec", // cream
  pending: "oklch(0.66 0.2 25)", // red, the damage badge's family
  away: "#9198b0",
  ink: "#14151c",
} as const;

type Category = "combat" | "ability" | "pending" | "away";

interface Meta {
  name: string;
  cat: Category;
  glyph: string;
  /** Desktop's word under the tile. */
  word: string;
  /** Filled = must / has. Outline + slash = can't / off. */
  fill: "solid" | "outline";
  slash?: boolean;
  /** Lower wins. Only combat cues compete for a slot. */
  prio: number;
  mine: string;
  opp: string;
}

const META: Record<DieStatusKind, Meta> = {
  deadly: { name: "Deadly-engaged", cat: "pending", glyph: "KO", word: "KO AT END", fill: "solid", prio: 1,
    mine: "Will be KO'd at Clean Up, whatever its damage.", opp: "Will be KO'd at Clean Up, whatever its damage." },
  cantBlock: { name: "Can't block", cat: "combat", glyph: "B", word: "CAN'T BLOCK", fill: "outline", slash: true, prio: 2,
    mine: "Can't be assigned as a blocker.", opp: "Can't block your attackers." },
  mustBlock: { name: "Must block", cat: "combat", glyph: "B!", word: "MUST BLOCK", fill: "solid", prio: 3,
    mine: "Has to block if any attacker can be blocked. Blocks won't confirm without it.", opp: "Your opponent has to block with this if they can." },
  unblockable: { name: "Unblockable", cat: "combat", glyph: "»", word: "UNBLOCKABLE", fill: "solid", prio: 4,
    mine: "Can't be blocked, and neither can anything in its lane.", opp: "You can't block it, or anything in its lane." },
  mustAttack: { name: "Must attack", cat: "combat", glyph: "A!", word: "MUST ATTACK", fill: "solid", prio: 5,
    mine: "Has to attack if able.", opp: "Will be sent into a lane on their turn if able." },
  cantAttack: { name: "Can't attack", cat: "combat", glyph: "A", word: "CAN'T ATTACK", fill: "outline", slash: true, prio: 6,
    mine: "Can't be sent into a lane.", opp: "Can't attack you." },
  onlyBlocker: { name: "Only blocker", cat: "combat", glyph: "B1", word: "ONLY BLOCKER", fill: "solid", prio: 7,
    mine: "The only die that can block this turn.", opp: "The only die that can block your attack." },
  blanked: { name: "Text blanked", cat: "ability", glyph: "T", word: "NO TEXT", fill: "outline", slash: true, prio: 8,
    mine: "Abilities and keywords are off: a vanilla body.", opp: "Abilities and keywords are off: a vanilla body." },
  granted: { name: "Granted keyword", cat: "ability", glyph: "+", word: "+", fill: "solid", prio: 9,
    mine: "Has a keyword its card doesn't print.", opp: "Has a keyword its card doesn't print." },
  protected: { name: "Shielded", cat: "ability", glyph: "SH", word: "SHIELDED", fill: "solid", prio: 8,
    mine: "Combat damage to it is prevented this turn.", opp: "Combat damage to it is prevented this turn - fighting it won't hurt it." },
  targetProtected: { name: "Targeting protected", cat: "ability", glyph: "TP", word: "TARGET PROTECTED", fill: "solid", prio: 8,
    mine: "Protected from targeting by the listed abilities.", opp: "Protected from targeting by the listed abilities." },
  intimidated: { name: "Intimidated", cat: "away", glyph: "↩", word: "AWAY", fill: "outline", prio: 2,
    mine: "Off the Field: can't block or be targeted, while-active effects are off. Returns on the same face.",
    opp: "Off the Field: can't block or be targeted, while-active effects are off. Returns on the same face." },
};

const CAT_COLOR: Record<Category, string> = {
  combat: CUE_COLORS.combat, ability: CUE_COLORS.ability, pending: CUE_COLORS.pending, away: CUE_COLORS.away,
};

// Two-letter keyword codes for the granted tab ("+OC"). The panel spells
// the keyword out.
const KEYWORD_CODES: Record<string, string> = {
  Fast: "FA", Overcrush: "OC", Deadly: "DE", Intimidate: "IN", Infiltrate: "IF", Attune: "AT", Obscure: "OB",
  Energize: "EN", "Energy Drain": "ED", "Tag Out": "TO", Sacrifice: "SA", Range: "RA", Aftershock: "AS", "Breath Weapon": "BW",
};

export function keywordCode(keyword: string): string {
  const m = keyword.match(/^(.*?)(?: (\d+))?$/)!;
  const code = KEYWORD_CODES[m[1]] ?? m[1].slice(0, 2).toUpperCase();
  return code + (m[2] ?? "");
}

/** A small labelled chip - the combat tab, and each explainer row's chip. */
export interface CueChip {
  glyph: string;
  fill: "solid" | "outline";
  slash: boolean;
  color: string;
}

function chipFor(status: DieStatus): CueChip {
  const meta = META[status.kind];
  return {
    glyph: status.kind === "granted" && status.keyword ? "+" + keywordCode(status.keyword) : meta.glyph,
    fill: meta.fill,
    slash: !!meta.slash,
    color: CAT_COLOR[meta.cat],
  };
}

export interface TileCues {
  /** The face's border, replacing its normal 1px one. */
  frame: { width: number; style: "solid" | "dashed" | "double"; color: string } | null;
  /** The one combat cue that wins the lane-edge slot. */
  tab: CueChip | null;
  /** Which edge the tab sits on: yours below, theirs above. */
  tabEdge: "bottom" | "top";
  blanked: boolean;
  /** "+OC" etc. */
  granted: string | null;
  deadly: boolean;
  intimidated: boolean;
  atk: "up" | "down" | null;
  def: "up" | "down" | null;
  /** Desktop's word under the tile - the top cue. */
  word: string | null;
  /** Anything at all to explain (the tile becomes tappable for it). */
  any: boolean;
}

function sorted(statuses: DieStatus[]): DieStatus[] {
  return [...statuses].sort((a, b) => META[a.kind].prio - META[b.kind].prio);
}

/** What the tile draws. `mine` = the viewer's own die. */
export function tileCues(die: Die, cardsById: Map<string, CardDef>, mine: boolean): TileCues {
  const statuses = sorted(die.statuses ?? []);
  const has = (k: DieStatusKind) => statuses.some((s) => s.kind === k);
  // Can't block wins over Must block - the engine resolves it that way.
  const combat = statuses.find((s) => META[s.kind].cat === "combat" && !(s.kind === "mustBlock" && has("cantBlock"))) ?? null;

  let frame: TileCues["frame"] = null;
  if (has("intimidated")) frame = { width: 2, style: "dashed", color: CUE_COLORS.away };
  else if (combat) {
    const meta = META[combat.kind];
    frame = combat.kind === "unblockable"
      ? { width: 3.5, style: "double", color: CUE_COLORS.combat }
      : { width: 2, style: meta.fill === "outline" ? "dashed" : "solid", color: CUE_COLORS.combat };
  }

  // Compared against the printed value PLUS the Champion's passive: Wolf's
  // +1 ATK or Armadillo's +1 DEF applies to every die all game, so chipping
  // it would mark every die and bury the changes that matter. The tap
  // breakdown still lists it.
  const printed = printedStats(die, cardsById);
  const passive = (mods: { label: string; delta: number }[] | null | undefined) =>
    (mods ?? []).filter((m) => m.label.endsWith("(Champion)")).reduce((sum, m) => sum + m.delta, 0);
  const cmp = (cur: number | null, base: number | undefined) =>
    cur === null || base === undefined || cur === base ? null : cur > base ? "up" : "down";
  const live = die.zone === "FieldZone" || die.zone === "AttackZone";
  const atk = live && printed ? cmp(die.effectiveAttack, printed.attack + passive(die.attackModifiers)) : null;
  const def = live && printed ? cmp(die.effectiveDefense, printed.defense + passive(die.defenseModifiers)) : null;

  const grantedStatus = statuses.find((s) => s.kind === "granted" && s.keyword);
  const top = statuses.find((s) => !(s.kind === "mustBlock" && has("cantBlock")));
  let word: string | null = top ? (top.kind === "granted" && top.keyword ? "+" + top.keyword.toUpperCase() : META[top.kind].word) : null;
  if (!word && def === "down" && printed) word = `DEF ${die.effectiveDefense} (was ${printed.defense})`;
  if (!word && atk === "down" && printed) word = `ATK ${die.effectiveAttack} (was ${printed.attack})`;

  return {
    frame,
    tab: combat ? chipFor(combat) : null,
    tabEdge: mine ? "bottom" : "top",
    blanked: has("blanked"),
    // Armadillo's shield shares the cream side tab when no keyword needs it.
    granted: grantedStatus?.keyword ? "+" + keywordCode(grantedStatus.keyword) : has("protected") ? "SH" : has("targetProtected") ? "TP" : null,
    deadly: has("deadly"),
    intimidated: has("intimidated"),
    atk,
    def,
    word,
    any: statuses.length > 0 || atk !== null || def !== null || (die.damage ?? 0) > 0,
  };
}

export interface ExplainRow {
  chip: CueChip;
  title: string;
  duration: string | null;
  body: string;
  /** Listed but not in force (Must block under Can't block). */
  overridden?: string;
}

function durationText(s: DieStatus): string | null {
  if (s.duration === "turn") return "This turn";
  if (s.duration === "cleanup") return "Until Clean Up";
  if (s.duration === "whileActive") return s.source ? `While ${s.source} is active` : "While its source is active";
  return null;
}

/** The tap explainer's rows, in priority order, written for whoever is looking. */
export function explainRows(die: Die, mine: boolean): ExplainRow[] {
  const statuses = sorted(die.statuses ?? []);
  const has = (k: DieStatusKind) => statuses.some((s) => s.kind === k);
  const rows: ExplainRow[] = statuses.map((s) => {
    const meta = META[s.kind];
    const rule = s.description ?? (mine ? meta.mine : meta.opp);
    const title = s.kind === "granted" && s.keyword ? `Has ${s.keyword}` : meta.name;
    const body = s.kind === "granted" && s.keyword
      ? `${s.source ? `From ${s.source}. ` : ""}Has ${s.keyword} this turn, though its card doesn't print it.`
      : s.kind === "deadly"
        ? `${s.source ? `Fought ${s.source}. ` : ""}${rule}`
        : `${s.source ? `From ${s.source}. ` : ""}${rule}`;
    return {
      chip: chipFor(s),
      title,
      duration: durationText(s),
      body,
      overridden: s.kind === "mustBlock" && has("cantBlock") ? "Overridden by Can't block" : undefined,
    };
  });
  const damage = die.damage ?? 0;
  if (damage > 0 && die.effectiveDefense !== null) {
    const left = Math.max(0, die.effectiveDefense - damage);
    rows.push({
      chip: { glyph: `−${damage}`, fill: "solid", slash: false, color: "oklch(0.62 0.2 25)" },
      title: `${damage} damage marked`,
      duration: "Until Clean Up",
      body: `KO'd when damage reaches DEF ${die.effectiveDefense} (${left} more).`,
    });
  }
  return rows;
}

/** "Your die · Attack · Lane 2" for the explainer's sub-line. */
export function whereText(die: Die, mine: boolean): string {
  const zone = die.zone === "AttackZone" ? `Attack${die.lane !== null ? ` · Lane ${die.lane + 1}` : ""}`
    : die.zone === "FieldZone" ? "Field"
    : die.zone === "Intimidated" ? "Intimidated"
    : die.zone;
  return `${mine ? "Your die" : "Their die"} · ${zone}`;
}

/** Glyph colours for a chip (fill and ink). */
export function chipColors(chip: CueChip): { background: string; color: string; borderColor: string } {
  const solid = chip.fill === "solid";
  const lightInk = chip.color === CUE_COLORS.pending || chip.color.startsWith("oklch(0.62");
  return {
    background: solid ? chip.color : "rgba(20,21,28,.92)",
    color: solid ? (lightInk ? "#ffffff" : CUE_COLORS.ink) : chip.color,
    borderColor: chip.color,
  };
}
