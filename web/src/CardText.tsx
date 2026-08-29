import type { ReactNode } from "react";
import { Icon } from "./GameIcon";
import {
  ENERGY_ICONS,
  GENERIC_ENERGY_ICONS,
  SIDEKICK_ICON,
  SPLIT_ENERGY_ICONS,
  WILD_ENERGY_ICON,
  type GameIcon,
} from "./gameIcons";

// Renders a card's printed text: the symbols a real card prints become
// icons, and each "Global:" ability starts its own line with the keyword
// in bold.
//
// This is DISPLAY ONLY. Search still runs against the raw string (see
// cardSearch.ts), so "Pay Mask" finds cards whose text now shows an icon.
// Nothing here touches the data.

const ENERGY = "bolt|fist|mask|shield";

// Proper names that merely CONTAIN one of those words and must stay as
// text. Derived from the catalog rather than guessed: scanning every
// card's rules text for these words found exactly 18 occurrences that
// are not energy or sidekick references, and they are all one of these
// two names ("King Black Bolt" is covered by "Black Bolt"). Everything
// else - including "Pay Bolt Bolt", which really is two icons - is a
// genuine reference. Re-check this list if the catalog gains cards.
const PROTECTED_NAMES = ["Iron Fist", "Black Bolt"];
const PROTECTED_RE = new RegExp(`(${PROTECTED_NAMES.join("|")})`, "gi");

// One pass over the text. In order of precedence:
//
//  1. A raw Discord emoji code. 24 cards have one pasted into the sheet
//     ("<:Fist:366516545284866048>"); only these four forms occur and
//     all are unambiguously energy, so they render like the words do.
//  2. "Bolt/Mask" - two DIFFERENT energies joined by a slash, which the
//     importer writes for the split "either energy" symbol. The `(?<!\/)`
//     and `(?!\s*\/)` guards keep it from eating a pair out of the middle
//     of "Bolt/Fist/Mask/Shield", which is a list of all four types and
//     renders as four separate icons.
//  3. A bare "bolt"/"fist"/"mask"/"shield"/"sidekick", with an optional
//     plural "s" kept as text so "sidekicks" reads as icon + "s".
//  4. "pay 2" - generic energy, printed as a numeral in a circle. The
//     lookahead drops "pay 2 life" (not energy at all) and "Pay 1 SHIELD"
//     (a count of a specific energy, where the icon follows on its own).
//  5. "?" immediately before "energy" - the wild face.
const TOKEN_RE = new RegExp(
  [
    `<:(${ENERGY}):\\d+>`,
    `(?<!\\/)\\b(${ENERGY})\\s*\\/\\s*(${ENERGY})\\b(?!\\s*\\/)`,
    `\\b(${ENERGY}|sidekick)(s?)\\b`,
    `\\b(pays?)\\s+(\\d)\\b(?!\\s*(?:life|${ENERGY}))`,
    `(\\?)(?=\\s*energy\\b)`,
  ].join("|"),
  "gi",
);

/** Replaces printed symbols with icons, leaving protected names alone. */
function withIcons(text: string, keyPrefix: string): ReactNode[] {
  const out: ReactNode[] = [];
  let key = 0;
  const push = (icon: GameIcon) => out.push(<Icon key={`${keyPrefix}-i${key++}`} icon={icon} />);

  // Split on the protected names first so the token scan never sees them.
  for (const chunk of text.split(PROTECTED_RE)) {
    if (chunk.length === 0) continue;
    if (PROTECTED_NAMES.some((n) => n.toLowerCase() === chunk.toLowerCase())) {
      out.push(chunk);
      continue;
    }
    let last = 0;
    for (const m of chunk.matchAll(TOKEN_RE)) {
      const [emoji, splitA, splitB, word, plural, payVerb, payAmount, wildcard] = m.slice(1);
      const at = m.index ?? 0;
      if (at > last) out.push(chunk.slice(last, at));
      if (emoji) {
        push(ENERGY_ICONS[emoji.toLowerCase()]);
      } else if (splitA) {
        push(SPLIT_ENERGY_ICONS[`${splitA.toLowerCase()}/${splitB.toLowerCase()}`]);
      } else if (word) {
        const kind = word.toLowerCase();
        push(kind === "sidekick" ? SIDEKICK_ICON : ENERGY_ICONS[kind]);
        if (plural) out.push(plural);
      } else if (payVerb) {
        // The verb stays as written ("Pay"/"pays"); only the amount
        // becomes a symbol.
        out.push(`${payVerb} `);
        push(GENERIC_ENERGY_ICONS[payAmount]);
      } else if (wildcard) {
        push(WILD_ENERGY_ICON);
      }
      last = at + m[0].length;
    }
    if (last < chunk.length) out.push(chunk.slice(last));
  }
  return out;
}

// "Global:" can appear mid-sentence in the printed text; each one starts
// a new block so a card's Global abilities are visually separate from its
// own text, which is the thing people scan for when building a team.
const GLOBAL_RE = /(?=\bGlobal:)/;

export function CardText({ text }: { text: string }) {
  if (!text) return <span className="hint">(blank text box)</span>;
  const blocks = text.split(GLOBAL_RE).filter((b) => b.trim().length > 0);
  return (
    <>
      {blocks.map((block, i) => {
        const isGlobal = /^Global:/.test(block.trim());
        if (!isGlobal) {
          return <div key={i}>{withIcons(block.trim(), `b${i}`)}</div>;
        }
        const rest = block.trim().replace(/^Global:\s*/, "");
        return (
          <div key={i} className="card-text-global">
            <strong>Global:</strong> {withIcons(rest, `b${i}`)}
          </div>
        );
      })}
    </>
  );
}
