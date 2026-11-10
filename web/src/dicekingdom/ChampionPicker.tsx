import { useEffect, useState } from "react";
import { api } from "./api";
import { CHAMPION_ICONS } from "./icons";

/** Setup-screen value for Player 2's column: the opponent picks their own. */
export const OPPONENT_PICKS = "__opponent_picks__";

export interface ChampionChoice {
  id: string;
  energy: string;
}

export function championLabel(id: string): string {
  return id.replace(/([A-Z])/g, " $1").trim();
}

/** Player 2's "let them choose" option on the setup screen. */
export function OpponentPicksOption({ selected, onPick }: { selected: boolean; onPick: () => void }) {
  return (
    <button
      type="button"
      className={`champ-opt dk-opponent-picks${selected ? " selected" : ""}`}
      style={{ ["--sel" as string]: "var(--text-h)" }}
      onClick={onPick}
    >
      <div className="cname" style={{ color: "var(--text-h)" }}>
        Opponent picks
      </div>
      <small>Send an invite link</small>
    </button>
  );
}

// How each Champion tends to play. The power itself comes from the server
// (Champion.passiveText - ChampionPowers.Describe, the same text the card
// reference prints), so it can't drift from the rules when a power changes.
export const PLAYSTYLES: Record<string, string> = {
  Wolf: "Aggressive: push damage by strengthening a key attacker.",
  Armadillo: "Defensive: protect a valuable creature during combat.",
  GoldenEagle: "Efficient: deploy creatures while conserving energy for other actions.",
  GreatHornedOwl: "Tactical: manipulate creature levels to create an advantage.",
};

// One fetch of the Champions' power text, shared by every picker on the page.
let powerText: Promise<Record<string, string>> | null = null;
export function usePowerText(): Record<string, string> {
  const [text, setText] = useState<Record<string, string>>({});
  useEffect(() => {
    powerText ??= api
      .getChampions()
      .then((champions) => Object.fromEntries(champions.map((c) => [c.id, c.passiveText])))
      .catch(() => {
        powerText = null; // try again next time
        return {};
      });
    let live = true;
    powerText.then((t) => live && setText(t));
    return () => {
      live = false;
    };
  }, []);
  return text;
}

export function ChampionPicker({ champions, value, onPick, allowOpponentPicks = false }: {
  champions: ChampionChoice[];
  value: string | null;
  onPick: (id: string) => void;
  allowOpponentPicks?: boolean;
}) {
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const powers = usePowerText();
  const detail = expandedId === value && value && value !== OPPONENT_PICKS
    ? { ability: powers[value], playstyle: PLAYSTYLES[value] }
    : undefined;
  return (
    <div className="champion-picker">
      <div className="champ-pick">
        {allowOpponentPicks && (
          <OpponentPicksOption selected={value === OPPONENT_PICKS} onPick={() => { setExpandedId(null); onPick(OPPONENT_PICKS); }} />
        )}
        {champions.map((c) => {
          const Icon = CHAMPION_ICONS[c.id];
          const expanded = expandedId === c.id && value === c.id;
          return (
            <button key={c.id} type="button"
              className={`champ-opt${value === c.id ? " selected" : ""}`}
              style={{ ["--sel" as string]: `var(--${c.energy.toLowerCase()})`, color: `var(--${c.energy.toLowerCase()})` }}
              aria-expanded={expanded}
              onClick={() => { setExpandedId(expanded ? null : c.id); onPick(c.id); }}>
              <Icon />
              <div className="cname" style={{ color: "var(--text-h)" }}>{championLabel(c.id)}</div>
            </button>
          );
        })}
      </div>
      <div className={`champ-info-outer${detail ? " is-open" : ""}`}>
        <div className="champ-info-inner">
          {detail && (
            <div className="champ-info" role="region" aria-label={`${championLabel(value!)} information`}>
              <strong>{championLabel(value!)}</strong>
              {detail.ability && <div className="champ-info-section"><b>Champion ability</b><p>{detail.ability}</p></div>}
              {detail.playstyle && <div className="champ-info-section"><b>Playstyle</b><p>{detail.playstyle}</p></div>}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
