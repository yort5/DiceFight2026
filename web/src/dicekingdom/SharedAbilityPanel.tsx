import { useState, type ReactNode } from "react";
import { CHARACTER_ICONS, EnergyBadge } from "./icons";
import type { Die } from "./types";
import type { AbilityCommand, AbilityOptions } from "./sharedAbilities";

interface Props {
  variant: "desktop" | "mobile";
  abilities: AbilityOptions;
  onExecute: (command: AbilityCommand) => void;
  nameOf: (die: Die) => string;
  actionTextOf: (die: Die) => string | null | undefined;
  renderDie: (die: Die) => ReactNode;
}

// Global text arrives from the API as plain text (for example, "Pay 1 Shell").
// Replace only energy names used in payment costs with the existing glyphs.
function globalTextWithEnergyIcons(text: string): ReactNode {
  const costPattern = /\b(Pay\s+\d+\s+)(Claw|Shell|Wing|Eye|Wild)\b/g;
  const content: ReactNode[] = [];
  let lastIndex = 0;

  for (const match of text.matchAll(costPattern)) {
    const index = match.index;
    content.push(text.slice(lastIndex, index), match[1]);
    content.push(
      <span key={index} title={match[2]} aria-label={`${match[2]} energy`}
        style={{ display: "inline-flex", verticalAlign: "middle", margin: "0 2px" }}>
        <EnergyBadge type={match[2]} size={13} />
      </span>
    );
    lastIndex = index + match[0].length;
  }

  if (content.length === 0) return text;
  content.push(text.slice(lastIndex));
  return content;
}

// One data-driven list of Global and action-die abilities. The wrapper
// class controls placement and appearance, not which abilities exist.
export function SharedAbilityPanel({ variant, abilities, onExecute, nameOf, actionTextOf, renderDie }: Props) {
  const [openText, setOpenText] = useState<string | null>(null);
  return (
    <div className={variant === "mobile" ? "dkm-global-rail" : "dk-desktop-ability-rail"}>
      <div className="dkm-global-caption">
        <span className="dkm-global-title">{variant === "mobile" ? "Global" : "Global Abilities"}</span>
        <span className="dkm-global-note">either player</span>
      </div>
      {abilities.globals.length === 0 && <div className="dkm-global-empty">{variant === "mobile" ? "No Global abilities available yet." : "No Global abilities in this game."}</div>}
      <div className="dkm-global-list">
        {abilities.globals.map((g) => {
          const Icon = CHARACTER_ICONS[g.card.id];
          const open = variant === "desktop" || openText === g.card.id;
          const heading = <>
            <span className="dkm-global-icon">{Icon && <Icon size={18} />}</span>
            <span>{g.card.name}</span>
            <span className="dkm-global-cost">
              {Array.from({ length: g.global.cost }, (_, i) =>
                g.global.energyType ? <EnergyBadge key={i} type={g.global.energyType} size={12} /> : null)}
            </span>
          </>;
          return <div key={g.card.id} className="dkm-global-item">
            {variant === "desktop" ? (
              <div className="dkm-global-name" style={{ cursor: "default" }}>{heading}</div>
            ) : (
              <button type="button" className="dkm-global-name" aria-expanded={open}
                onClick={() => setOpenText(open ? null : g.card.id)}>{heading}</button>
            )}
            <button type="button" className="dkm-chip-btn dkm-global-use"
              disabled={g.command === null} title={g.blocked ?? undefined}
              onClick={() => g.command && onExecute(g.command)}>Use</button>
            {open && <p className="dkm-global-text">{globalTextWithEnergyIcons(g.global.text)}</p>}
          </div>;
        })}
      </div>
      {abilities.actionDice.length > 0 && <div className="dkm-ready-actions">
        <span className="dkm-field-label">Your actions</span>
        {abilities.actionDice.map((a) => <div key={a.die.id} className="dkm-ready-action">
          {renderDie(a.die)}
          <div className="dkm-ready-action-body"><b>{nameOf(a.die)}</b><span>{actionTextOf(a.die)}</span></div>
          <button type="button" className="dkm-chip-btn dkm-global-use"
            disabled={a.command === null} title={a.blocked ?? undefined}
            onClick={() => a.command && onExecute(a.command)}>Use</button>
        </div>)}
      </div>}
      {variant === "desktop" && abilities.foresightDice.length > 0 && <div className="dkm-ready-actions">
        <span className="dkm-field-label">Foresight — reroll one Reserve die</span>
        {abilities.foresightDice.map((a) => <div key={a.die.id} className="dkm-ready-action">
          {renderDie(a.die)}
          <div className="dkm-ready-action-body"><b>{nameOf(a.die)}</b></div>
          <button type="button" className="dkm-chip-btn dkm-global-use"
            disabled={a.command === null} title={a.blocked ?? undefined}
            onClick={() => a.command && onExecute(a.command)}>Foresight</button>
        </div>)}
      </div>}
    </div>
  );
}
