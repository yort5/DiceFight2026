import { useEffect, useRef } from "react";
import type { GameLogEntry } from "./types";

// What has happened, newest at the bottom.
//
// The lines come from the engine (GameState.Log), not from the client
// diffing state: one action can resolve a whole cascade of abilities, and
// only the engine knows that was one thing the player did. It also means
// both players read the same account.
//
// The text is written in the third person by the engine, because the same
// state is served to both sides and only the client knows which side is
// reading. The colour is what says whose line it is.

export function MatchLog(props: { entries: GameLogEntry[]; nearPlayerId: string }) {
  const listRef = useRef<HTMLOListElement>(null);
  const lastSeq = props.entries.at(-1)?.seq;

  // Follow the tail, which is where the newest line is.
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
