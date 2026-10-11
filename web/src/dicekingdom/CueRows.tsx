import { KeywordText } from "../KeywordPreview";
// The status-cue explainer's rows (Claude Design's "face frame" handoff,
// 2026-10-03): one row per cue - its chip, what it is, how long it lasts,
// and "From {source}. {rule}". Shared by the mobile inspect panel and the
// desktop die popover, so both say the same thing.
import { chipColors, type ExplainRow } from "./statusCues";

export function CueRows({ rows }: { rows: ExplainRow[] }) {
  if (rows.length === 0) return null;
  return (
    <div className="dk-cue-rows">
      {rows.map((row, i) => (
        <div key={i} className={`dk-cue-row${row.overridden ? " overridden" : ""}`}>
          <span className={`dk-cue-chip${row.chip.slash ? " slashed" : ""}`} style={chipColors(row.chip)}>
            {row.chip.glyph}
          </span>
          <div className="dk-cue-text">
            <div className="dk-cue-title">
              <KeywordText text={row.title} />
              {row.duration && <span className="dk-cue-dur"> · {row.duration}</span>}
            </div>
            <div className="dk-cue-body"><KeywordText text={row.overridden ? `${row.overridden}. ${row.body}` : row.body} /></div>
          </div>
        </div>
      ))}
    </div>
  );
}
