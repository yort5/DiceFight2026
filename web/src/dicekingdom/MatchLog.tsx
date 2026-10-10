import { useRef } from "react";
import { useLogAutoFollow } from "./useLogAutoFollow";
import type { GameLogEntry } from "./types";

// Ported from ../MatchLog.tsx verbatim - what has happened, newest at
// the bottom, written by the engine (GameState.Log) rather than the
// client diffing state, so both players read the same account and one
// action that resolves several things is still one line.

export function MatchLog(props: { entries: GameLogEntry[]; nearPlayerId: string }) {
  const listRef = useRef<HTMLOListElement>(null);
  const lastSeq = props.entries.at(-1)?.seq;

  const logScrollHandlers = useLogAutoFollow(listRef, lastSeq);

  return (
    <div className="rail-panel match-log">
      <h3>Log</h3>
      {props.entries.length === 0 ? (
        <p className="empty-hint">Nothing has happened yet.</p>
      ) : (
        <ol className="match-log-list" ref={listRef} tabIndex={0} aria-label="Match log history" {...logScrollHandlers}>
          {props.entries.map((entry) => entry.isTurnStart ? (
            <li key={entry.seq} className="log-line neutral log-turn-start">
              <span className="log-text">— — — {entry.text} — — —</span>
            </li>
          ) : (
            <li
              key={entry.seq}
              className={
                entry.playerId === null
                  ? "log-line neutral"
                  : entry.playerId === props.nearPlayerId
                    ? "log-line yours"
                    : "log-line theirs"
              }
            >
              <span className="log-seq">{entry.seq}</span>
              <span className="log-text">{entry.text}</span>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}
