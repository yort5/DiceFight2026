import { ChampionPicker } from "./ChampionPicker";
import type { ChampionChoice } from "./ChampionPicker";
import { CUSTOM_TEAM_PICK, type CustomTeam } from "./customTeams";

/** Shared setup UI for desktop and mobile. Custom teams have no Champion. */
export function CustomTeamSetupChoice({
  champions, team, value, onPick,
}: {
  champions: ChampionChoice[];
  team: CustomTeam | null;
  value: string | null;
  onPick: (value: string | null) => void;
}) {
  if (!team) return <ChampionPicker champions={champions} value={value} onPick={(id) => onPick(id)} />;
  const isCustom = value === CUSTOM_TEAM_PICK;
  return <>
    <button type="button" className="btn ghost" onClick={() => onPick(isCustom ? null : CUSTOM_TEAM_PICK)}>
      {isCustom ? "Choose a Champion team instead" : "Use my custom team"}
    </button>
    {isCustom ? <p className="hint">Custom Team — no Champion · {team.cards.reduce((n, c) => n + c.diceCount, 0)} card dice · {team.tardigrades.reduce((n, d) => n + d.diceCount, 0)} Tardigrades</p>
      : <ChampionPicker champions={champions} value={value} onPick={(id) => onPick(id)} />}
  </>;
}
