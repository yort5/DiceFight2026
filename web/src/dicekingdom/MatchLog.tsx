import { useEffect, useRef } from "react";
import type { GameLogEntry } from "./types";

// Ported from ../MatchLog.tsx verbatim - what has happened, newest at
// the bottom, written by the engine (GameState.Log) rather than the
// client diffing state, so both players read the same account and one
// action that resolves several things is still one line.

export function MatchLog(props: { entries: GameLogEntry[]; nearPlayerId: string }) {
  const listRef = useRef<HTMLOListElement>(null);
  const lastSeq = props.entries.at(-1)?.seq;

  useEffect(() => {
    const list = listRef.current;
    if (list) list.scrollTop = list.scrollHeight;
  }, [lastSeq]);

  return (
    <div className="rail-panel match-log">
      <h3>Log</h3>
      {props.entries.length === 0 ? (
        <p className="empty-hint">Nothing has happened yet.</p>
      ) : (
        <ol className="match-log-list" ref={listRef}>
          {props.entries.map((entry) => (
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
