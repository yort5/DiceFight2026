// The community "Orange Ban" list, from dmunited.eu/the-orange-ban-list/
// (the page embeds a published Google Sheet; this was generated from its
// CSV export rather than transcribed by hand).
//
// Entries are matched by SET + NAME + SUBTITLE. A null subtitle means
// every printing of that character in that set - the list's own "(All)"
// notation, used for Gladiator and Hawkman.
//
// The full list is kept even where our card catalog currently has no
// matching card. The ban list is the authoritative document; the
// catalog is the incomplete one (see the stale-bulk-catalog note in
// DESIGN_LOG). An entry that matches nothing simply bans nothing today
// and starts working when the catalog is filled in - trimming the list
// to fit today's data would quietly lose that.

export interface OrangeBanEntry {
  readonly set: string;
  readonly name: string;
  /** null = all printings of this character in this set. */
  readonly subtitle: string | null;
  /**
   * Alternate spellings this card appears under in OUR catalog.
   *
   * The ban list and the card catalog are transcribed from different
   * sources and disagree on a handful of spellings - in most cases the
   * source spreadsheet the catalog is imported from carries the typo
   * ("Power of Attourney", "Muscle of Hire", "Enchanged Crowbar",
   * "Aggession", "Doomcalibur"), and in one case the ban list does
   * ("Angelo Fortunado" for Angelo Fortunato). Rather than silently
   * "correcting" either source, the mismatch is recorded here so the
   * matcher succeeds and the disagreement stays visible.
   */
  readonly alsoMatches?: readonly { readonly name?: string; readonly subtitle?: string }[];
}

export const ORANGE_BAN_LIST: readonly OrangeBanEntry[] = [
  // AvX
  { set: "AvX", name: "Spider-man", subtitle: "Webslinger" },
  { set: "AvX", name: "Black Widow", subtitle: "Tsarina" },
  { set: "AvX", name: "Green Goblin", subtitle: "Gobby" },
  { set: "AvX", name: "Hulk", subtitle: "Green Goliath" },
  { set: "AvX", name: "Nick Fury", subtitle: "Patch" },
  { set: "AvX", name: "Nova", subtitle: "The Human Rocket" },
  { set: "AvX", name: "Venom", subtitle: "Angelo Fortunado", alsoMatches: [{ subtitle: "Angelo Fortunato" }] },
  // UXM
  { set: "UXM", name: "Imprisoned", subtitle: "Basic Action Card" },
  { set: "UXM", name: "Relentless", subtitle: "Basic Action Card" },
  { set: "UXM", name: "Falcon", subtitle: "Recon" },
  // YGO
  { set: "YGO", name: "Swords of Revealing Light", subtitle: "Basic Action Card" },
  { set: "YGO", name: "Doomcaliber Knight", subtitle: "Fiendish Fighter", alsoMatches: [{ name: "Doomcalibur Knight" }] },
  { set: "YGO", name: "Jinzo", subtitle: "Trap Destroyer" },
  { set: "YGO", name: "Ring of Magnetism", subtitle: "Action Attraction" },
  // BFF
  { set: "BFF", name: "Beholder", subtitle: "Master Aberration" },
  // JL
  { set: "JL", name: "Black Manta", subtitle: "Deep Sea Deviant" },
  { set: "JL", name: "Constantine", subtitle: "Hellblazer" },
  // AOU
  { set: "AOU", name: "Jocasta", subtitle: "Patterned After Janet" },
  { set: "AOU", name: "Magneto", subtitle: "Magnetic Monster" },
  // WOL
  { set: "WOL", name: "Guy Gardner", subtitle: "Blinding Rage" },
  { set: "WOL", name: "Vicious Struggle", subtitle: "Basic Action Card" },
  { set: "WOL", name: "Lantern Ring", subtitle: "Limited Only by Imagination" },
  { set: "WOL", name: "Parallax", subtitle: "Source of Terror" },
  { set: "WOL", name: "Parallax", subtitle: "Fear" },
  // FUS
  { set: "FUS", name: "Half-Elf Bard", subtitle: "Master Lords Alliance" },
  { set: "FUS", name: "Cloudkill", subtitle: "Basic Action Card" },
  { set: "FUS", name: "Delayed Blast Fireball", subtitle: "Basic Action Card" },
  // WF
  { set: "WF", name: "Batgirl", subtitle: "Babs" },
  // CW
  { set: "CW", name: "Ronin", subtitle: "Between Employers" },
  // SWW
  { set: "SWW", name: "Team Up", subtitle: "Basic Action Card" },
  // SMC
  { set: "SMC", name: "Shriek", subtitle: "Sonic Beam" },
  // GOTG
  { set: "GOTG", name: "Cosmic Cube", subtitle: "Energy of the Beyonders" },
  { set: "GOTG", name: "Madame Web", subtitle: "The Great Web Unravels" },
  { set: "GOTG", name: "Norman Osborn", subtitle: "Don't call me \"Gobby\"!" },
  // XFC
  { set: "XFC", name: "Blob", subtitle: "Appetite for Destruction" },
  { set: "XFC", name: "Boom Boom", subtitle: "Meltdown" },
  // TOA
  { set: "TOA", name: "Insect Plague", subtitle: "Basic Action Card" },
  { set: "TOA", name: "Green Devil Mask", subtitle: "Lesser Trap" },
  { set: "TOA", name: "Yuan-ti Pureblood", subtitle: "Epic Humanoid" },
  { set: "TOA", name: "Ring of Winter", subtitle: "Epic Magical Object" },
  // THOR
  { set: "THOR", name: "Hulk", subtitle: "Power of Attorney", alsoMatches: [{ subtitle: "Power of Attourney" }] },
  { set: "THOR", name: "Mr. Fixit", subtitle: "Muscle for Hire", alsoMatches: [{ subtitle: "Muscle of Hire" }] },
  { set: "THOR", name: "Wrecker", subtitle: "Enchanted Crowbar", alsoMatches: [{ subtitle: "Enchanged Crowbar" }] },
  // JUS
  { set: "JUS", name: "Green Lantern", subtitle: "Human" },
  // XMF
  { set: "XMF", name: "Hope Summers", subtitle: "Pluripotent Echopraxia" },
  { set: "XMF", name: "Iceman", subtitle: "Right on Schedule" },
  // TIW
  { set: "TIW", name: "The God Catcher", subtitle: "Famous Walking Statue" },
  // WWE
  { set: "WWE", name: "Jerry Lawler, Ringside Announcer", subtitle: "Basic Action Card" },
  { set: "WWE", name: "Becky Lynch", subtitle: "Maiden Ireland" },
  // IG
  { set: "IG", name: "Drax", subtitle: "The Pacifist" },
  { set: "IG", name: "Thor", subtitle: "Jormungand's Fear" },
  { set: "IG", name: "Spider-Man", subtitle: "Public Menace" },
  { set: "IG", name: "Typhoid Mary", subtitle: "Red Rubber Boots" },
  // DPS
  { set: "DPS", name: "D'Ken", subtitle: "Shi'ar Civil War" },
  { set: "DPS", name: "Gladiator", subtitle: null },
  { set: "DPS", name: "Lilandra", subtitle: "Majestrix" },
  { set: "DPS", name: "Master Mold", subtitle: "Endless Sentinels" },
  { set: "DPS", name: "Vulcan", subtitle: "Aggression", alsoMatches: [{ subtitle: "Aggession" }] },
  // SKC
  { set: "SKC", name: "Hawkman", subtitle: null },
  { set: "SKC", name: "Barry Allen", subtitle: "Master of the Speed Force" },
  { set: "SKC", name: "Wonder Woman", subtitle: "Legendary" },
  // MSW
  { set: "MSW", name: "Invisible Woman", subtitle: "Interdimensional Adventurer" },
  { set: "MSW", name: "Black Panther", subtitle: "Toppling Doomstadt" },
  { set: "MSW", name: "Terrax", subtitle: "Namor's Cabal" },
];

function normalize(value: string): string {
  return value
    .toLowerCase()
    .replace(/[\u2018\u2019]/g, "'")
    .replace(/[\u201c\u201d]/g, '"')
    .replace(/[^a-z0-9]+/g, "");
}

const BY_SET_AND_NAME = new Map<string, OrangeBanEntry[]>();
for (const entry of ORANGE_BAN_LIST) {
  const names = new Set([entry.name, ...(entry.alsoMatches ?? []).flatMap((v) => (v.name ? [v.name] : []))]);
  for (const name of names) {
    const key = `${entry.set}|${normalize(name)}`;
    const bucket = BY_SET_AND_NAME.get(key);
    if (bucket) bucket.push(entry);
    else BY_SET_AND_NAME.set(key, [entry]);
  }
}

function subtitleMatches(wantedRaw: string, card: { subtitle: string | null }): boolean {
  const wanted = normalize(wantedRaw);
  const actual = normalize(card.subtitle ?? "");
  // Basic Action cards are listed as "<name>: Basic Action Card" while
  // the catalog stores just "Basic Action" / "Epic Basic Action".
  if (wanted === "basicactioncard") return actual.includes("basicaction");
  return wanted === actual;
}

function matchesEntry(
  entry: OrangeBanEntry,
  card: { name: string; subtitle: string | null },
): boolean {
  if (entry.subtitle === null) return true; // "(All)" - every printing
  if (subtitleMatches(entry.subtitle, card)) return true;
  return (entry.alsoMatches ?? []).some((v) => {
    if (v.name !== undefined && normalize(v.name) !== normalize(card.name)) return false;
    return v.subtitle === undefined ? true : subtitleMatches(v.subtitle, card);
  });
}

export function isOrangeBanned(card: {
  name: string;
  subtitle: string | null;
  set: string | null;
}): boolean {
  if (!card.set) return false;
  const matches = BY_SET_AND_NAME.get(`${card.set}|${normalize(card.name)}`);
  if (!matches) return false;
  return matches.some((entry) => matchesEntry(entry, card));
}
