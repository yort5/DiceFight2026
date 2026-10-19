// Online games where the host picks only their own Champion (2026-09-30,
// user request: "you should be able to start an online game with an
// opponent without having to pick their faction" - it used to take
// picking both, starting, then copying the invite link from inside the
// match). The server holds a pending game until the invited player picks
// (V2GamesController Open/Lobby/Join); these are the three pieces both
// Dice Kingdom pages share: resolving an opened invite link, the host's
// waiting screen, and the invited player's Champion pick.
import { useEffect, useState } from "react";
import { api } from "./api";
import { championLabel, type ChampionChoice } from "./ChampionPicker";
import { CustomTeamSetupChoice } from "./CustomTeamSetupChoice";
import { CUSTOM_TEAM_PICK, pendingCustomTeam } from "./customTeams";
// Shared with the pages' setup screens; they live with the picker.
export { OPPONENT_PICKS, OpponentPicksOption, championLabel, type ChampionChoice } from "./ChampionPicker";
import { players, type Me, type PlayedGame } from "./player";
import { adoptGame, claimSeatFromUrl, forgetSavedGame, nameClaimedSeat, resumeSeats, savedGames, type SavedGame } from "./seats";
import type { GameState, LobbyStatus } from "./types";

/** What an invite link in the URL leads to: a game to play, or a pick to make first. */
export type InviteResolution = { kind: "game"; game: GameState } | { kind: "pick"; lobby: LobbyStatus };

// Replaces the old "claim the seat, then GET the game" join: the game may
// not exist yet, if the host opened it with only their own Champion.
export async function resolveInvite(): Promise<InviteResolution | null> {
  const claim = claimSeatFromUrl();
  if (!claim) return null;
  const lobby = await api.getLobby(claim.gameId);
  if (lobby.yourPlayerId) nameClaimedSeat(claim.gameId, lobby.yourPlayerId);
  if (!lobby.started) return { kind: "pick", lobby };
  return { kind: "game", game: await api.getGame(claim.gameId) };
}

/** The host, after opening: the invite link, and a wait for the other player to pick. */
export function WaitingForOpponent({
  gameId,
  hostLabel,
  link,
  onStarted,
  onCancel,
}: {
  gameId: string;
  hostLabel: string;
  link: string | null;
  onStarted: (game: GameState) => void;
  onCancel: () => void;
}) {
  const [copied, setCopied] = useState<"copied" | "failed" | null>(null);

  useEffect(() => {
    let done = false;
    const timer = window.setInterval(async () => {
      try {
        const lobby = await api.getLobby(gameId);
        if (lobby.started && !done) {
          done = true;
          onStarted(await api.getGame(gameId));
        }
      } catch {
        // quiet - the next poll tries again
      }
    }, 1500);
    return () => {
      done = true;
      window.clearInterval(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gameId]);

  return (
    <div className="panel dk-lobby">
      <h3 style={{ margin: "0 0 6px" }}>Waiting for your opponent</h3>
      <p style={{ margin: "0 0 12px" }}>
        You're playing <b>{hostLabel}</b>. Send this link - your opponent picks their own Champion or custom team, and
        the match starts as soon as they do.
      </p>
      {link && (
        <div className="dk-lobby-link">
          <input readOnly value={link} onFocus={(e) => e.currentTarget.select()} />
          <button
            type="button"
            className="btn"
            onClick={() =>
              navigator.clipboard?.writeText(link).then(
                () => setCopied("copied"),
                () => setCopied("failed"),
              )
            }
          >
            {copied === "copied" ? "Copied ✓" : copied === "failed" ? "Couldn't copy" : "Copy link"}
          </button>
          {/* Phones (and the installed app, which has no address bar) get the system share sheet. */}
          {typeof navigator.share === "function" && (
            <button
              type="button"
              className="btn"
              onClick={() => navigator.share({ title: "Dice Kingdom", text: "Play Dice Kingdom with me", url: link }).catch(() => {})}
            >
              Share
            </button>
          )}
        </div>
      )}
      <button type="button" className="btn dk-lobby-cancel" onClick={onCancel}>
        Cancel
      </button>
    </div>
  );
}

/** The invited player, before the game exists: see the host's pick, choose yours - a Champion, or the custom team they staged from the Team Builder. */
export function PickYourChampion({
  lobby,
  champions,
  onJoined,
}: {
  lobby: LobbyStatus;
  champions: ChampionChoice[];
  onJoined: (game: GameState) => void;
}) {
  const [pick, setPick] = useState<string | null>(() => (pendingCustomTeam() ? CUSTOM_TEAM_PICK : null));
  const custom = pendingCustomTeam();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function join() {
    if (!pick) return;
    setBusy(true);
    setError(null);
    try {
      onJoined(await api.joinGame(lobby.gameId, pick === CUSTOM_TEAM_PICK && custom ? custom : pick));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  }

  return (
    <div className="panel dk-lobby">
      <h3 style={{ margin: "0 0 6px" }}>You've been invited</h3>
      <p style={{ margin: "0 0 12px" }}>
        Your opponent is playing <b>{lobby.hostLabel}</b>. Pick your Champion{custom ? " or use your custom team" : ""} to
        start the match.
      </p>
      {error && <p className="error">{error}</p>}
      <CustomTeamSetupChoice champions={champions} team={custom} value={pick} onPick={setPick} />
      <button type="button" className="btn" disabled={!pick || busy} onClick={join} style={{ marginTop: 14 }}>
        Start Match
      </button>
    </div>
  );
}

/** Where resuming a saved game leads: back into play, or back to waiting for the opponent to pick. */
export type Resumed =
  | { kind: "game"; game: GameState; vsComputer: boolean }
  | { kind: "waiting"; gameId: string; hostLabel: string };

function ago(ms: number): string {
  const minutes = Math.round((Date.now() - ms) / 60000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  return hours < 24 ? `${hours} h ago` : `${Math.round(hours / 24)} d ago`;
}

/** "Wolf vs Armadillo", or "Wolf · waiting for an opponent". */
export function playedGameLabel(g: PlayedGame): string {
  if (g.status === "waiting") {
    const host = g.seats.find((s) => s.championId);
    return `${host?.championId ? championLabel(host.championId) : "Game"} · waiting for an opponent`;
  }
  return g.seats.map((s) => (s.championId ? championLabel(s.championId) : "Custom team")).join(" vs ");
}

/** Unfinished games the server has for this player, added to this browser's saved list (seats.ts). */
export function adoptServerGames(me: Me): void {
  for (const g of me.games) {
    if (g.status === "finished") continue;
    const mine = g.seats.filter((s) => s.token);
    const human = mine.find((s) => !s.bot) ?? mine[0];
    if (!human) continue;
    adoptGame(
      g.gameId,
      mine.map((s) => ({ playerId: s.seatId, token: s.token! })),
      human.seatId,
      playedGameLabel(g),
      g.mode === "computer",
    );
  }
}

/**
 * The start screen's "Resume a game" list (2026-10-08): games this browser
 * holds a seat in (seats.ts) - and, since 2026-10-10, unfinished games this
 * player started on their other devices (player.ts). `?resume=<gameId>` in
 * the URL (the account page's Resume) resumes that one straight away.
 */
export function ResumeGames({ onResume }: { onResume: (r: Resumed) => void }) {
  const [games, setGames] = useState<SavedGame[]>(() => savedGames());
  const [note, setNote] = useState<string | null>(null);
  // Read once, before the effect strips it - StrictMode runs effects twice.
  const [wanted] = useState(() => new URLSearchParams(window.location.search).get("resume"));

  useEffect(() => {
    let cancelled = false;
    if (wanted) window.history.replaceState(null, "", window.location.pathname);
    players
      .me()
      .then(adoptServerGames)
      .catch(() => {
        // Offline or no player yet - this browser's own list still works.
      })
      .finally(() => {
        if (cancelled) return;
        const all = savedGames();
        setGames(all);
        const g = wanted ? all.find((x) => x.gameId === wanted) : undefined;
        if (g) void resume(g);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (games.length === 0 && !note) return null;

  async function resume(g: SavedGame) {
    setNote(null);
    resumeSeats(g.gameId);
    try {
      const lobby = await api.getLobby(g.gameId);
      if (!lobby.started) {
        onResume({ kind: "waiting", gameId: g.gameId, hostLabel: lobby.hostLabel });
        return;
      }
      onResume({ kind: "game", game: await api.getGame(g.gameId), vsComputer: g.vsComputer ?? false });
    } catch (e) {
      // Gone from the server (idle too long, or a redeploy) - drop it.
      forgetSavedGame(g.gameId);
      setGames(savedGames());
      setNote(`${g.label ?? "That game"} can't be resumed: ${e instanceof Error ? e.message : String(e)}`);
    }
  }

  return (
    <div className="panel dk-resume">
      <h3 style={{ margin: "0 0 6px" }}>Resume a game</h3>
      {note && <p className="dk-resume-note">{note}</p>}
      {games.map((g) => (
        <div key={g.gameId} className="dk-resume-row">
          <span className="dk-resume-label">
            {g.label ?? `Game ${g.gameId}`}
            {g.vsComputer ? " · vs computer" : g.seats.length > 1 ? " · both seats" : ""}
            <small> · {ago(g.savedAt)}</small>
          </span>
          <button type="button" className="btn" onClick={() => resume(g)}>
            Resume
          </button>
          <button
            type="button"
            className="dk-resume-forget"
            aria-label="Forget this game"
            title="Forget this game"
            onClick={() => {
              forgetSavedGame(g.gameId);
              setGames(savedGames());
            }}
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  );
}

