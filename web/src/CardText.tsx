import type { ReactNode } from "react";
import boltIcon from "./assets/energy-bolt.png";
import fistIcon from "./assets/energy-fist.png";
import maskIcon from "./assets/energy-mask.png";
import shieldIcon from "./assets/energy-shield.png";
import sidekickIcon from "./assets/sidekick.png";

// Renders a card's printed text: energy/sidekick words become icons, and
// each "Global:" ability starts its own line with the keyword in bold.
//
// This is DISPLAY ONLY. Search still runs against the raw string (see
// cardSearch.ts), so "Pay Mask" finds cards whose text now shows an icon.
// Nothing here touches the data.
//
// The icons are the old Teambuilder's own (e1-e4.png and pawn.png),
// copied into src/assets so they resolve offline and get hashed by Vite.

const ICONS: Record<string, { src: string; label: string }> = {
  bolt: { src: boltIcon, label: "Bolt" },
  fist: { src: fistIcon, label: "Fist" },
  mask: { src: maskIcon, label: "Mask" },
  shield: { src: shieldIcon, label: "Shield" },
  sidekick: { src: sidekickIcon, label: "Sidekick" },
};

// Proper names that merely CONTAIN one of those words and must stay as
// text. Derived from the catalog rather than guessed: scanning every
// card's rules text for these words found exactly 18 occurrences that
// are not energy or sidekick references, and they are all one of these
// two names ("King Black Bolt" is covered by "Black Bolt"). Everything
// else - including "Pay Bolt Bolt", which really is two icons - is a
// genuine reference. Re-check this list if the catalog gains cards.
const PROTECTED_NAMES = ["Iron Fist", "Black Bolt"];

// 24 cards write their energy as a raw Discord emoji code the sheet
// author pasted in - "<:Fist:366516545284866048>". Only these four forms
// occur, all unambiguously energy, so they become icons too rather than
// being displayed as the noise they currently are.
const EMOJI_CODE_RE = /<:(bolt|fist|mask|shield):\d+>/gi;

const WORD_RE = new RegExp(
  `${EMOJI_CODE_RE.source}|\\b(${Object.keys(ICONS).join("|")})(s?)\\b`,
  "gi",
);
const PROTECTED_RE = new RegExp(`(${PROTECTED_NAMES.join("|")})`, "gi");

function EnergyIcon({ kind }: { kind: string }) {
  const icon = ICONS[kind];
  // alt carries the word so the text still reads correctly when copied,
  // read aloud, or when the image fails to load.
  return <img className="card-icon" src={icon.src} alt={icon.label} title={icon.label} />;
}

/** Replaces energy/sidekick words with icons, leaving protected names alone. */
function withIcons(text: string, keyPrefix: string): ReactNode[] {
  const out: ReactNode[] = [];
  let key = 0;
  // Split on the protected names first so the word scan never sees them.
  for (const chunk of text.split(PROTECTED_RE)) {
    if (chunk.length === 0) continue;
    if (PROTECTED_NAMES.some((n) => n.toLowerCase() === chunk.toLowerCase())) {
      out.push(chunk);
      continue;
    }
    let last = 0;
    for (const m of chunk.matchAll(WORD_RE)) {
      const at = m.index ?? 0;
      if (at > last) out.push(chunk.slice(last, at));
      // Group 1 is the emoji-code form, group 2 the bare word.
      const kind = (m[1] ?? m[2]).toLowerCase();
      out.push(<EnergyIcon key={`${keyPrefix}-i${key++}`} kind={kind} />);
      // Keep a trailing plural: "sidekicks" reads as icon + "s".
      if (m[3]) out.push(m[3]);
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
