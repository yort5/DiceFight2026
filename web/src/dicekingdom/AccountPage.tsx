// /dice-kingdom/account (2026-10-10): who you're playing as, your record,
// your games, and your devices. Every browser is a player already
// (player.ts); a name makes it an account, and a link code puts another
// device on it - no password, no email (yet).
import { useEffect, useState } from "react";
import { navigate } from "../router";
import { championLabel } from "./ChampionPicker";
import { adoptServerGames, playedGameLabel } from "./lobby";
import { players, type Me, type PlayedGame } from "./player";
import { ThemeToggle, useTheme } from "./ThemeToggle";

/** Back to whichever Dice Kingdom page sent you here (?from=mobile). */
function backPath(): "/dice-kingdom" | "/dice-kingdom/mobile" {
  return new URLSearchParams(window.location.search).get("from") === "mobile" ? "/dice-kingdom/mobile" : "/dice-kingdom";
}

/** The start screens' "Playing as ..." line, linking here. */
export function AccountLink({ from }: { from?: "mobile" }) {
  // undefined while loading (so a named player doesn't flash "guest"); null if it failed.
  const [me, setMe] = useState<Me | null | undefined>(undefined);
  useEffect(() => {
    players.me().then(setMe, () => setMe(null));
  }, []);
  const search = from ? `?from=${from}` : "";
  if (me === undefined) return <p className={`dk-account-link${from === "mobile" ? " dkm-account-link" : ""}`}>&nbsp;</p>;
  return (
    <p className={`dk-account-link${from === "mobile" ? " dkm-account-link" : ""}`}>
      {me?.name ? (
        <>
          Playing as <b>{me.name}</b> ·{" "}
        </>
      ) : (
        <>Playing as a guest · </>
      )}
      <a
        href={`/dice-kingdom/account${search}`}
        onClick={(e) => {
          e.preventDefault();
          navigate("/dice-kingdom/account", search);
        }}
      >
        {me?.name ? "Your account" : "Make an account"}
      </a>
    </p>
  );
}

function when(iso: string): string {
  const d = new Date(iso);
  const days = (Date.now() - d.getTime()) / 86400000;
  if (d.toDateString() === new Date().toDateString()) return d.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  if (days < 6) return d.toLocaleDateString([], { weekday: "short" });
  return d.toLocaleDateString([], { month: "short", day: "numeric" });
}

function modeLabel(g: PlayedGame): string {
  if (g.mode === "computer") return "vs computer";
  if (g.mode === "online") return g.opponentName ? `vs ${g.opponentName}` : "online";
  return "pass-and-play";
}

/** Your Champion in a game - your one human seat, if you had one. */
function yourChampion(g: PlayedGame): string | null {
  const human = g.seats.filter((s) => s.token && !s.bot);
  return human.length === 1 ? human[0].championId : null;
}

function Record({ games }: { games: PlayedGame[] }) {
  const decided = games.filter((g) => g.result);
  if (decided.length === 0) return <p className="dk-acct-dim">No finished games yet - wins and losses show up here.</p>;
  const by = new Map<string, { w: number; l: number }>();
  for (const g of decided) {
    const c = yourChampion(g) ?? "?";
    const row = by.get(c) ?? { w: 0, l: 0 };
    if (g.result === "win") row.w++;
    else row.l++;
    by.set(c, row);
  }
  const wins = decided.filter((g) => g.result === "win").length;
  return (
    <>
      <p className="dk-acct-record">
        <b>{wins}</b> W · <b>{decided.length - wins}</b> L
      </p>
      <table className="dk-acct-table">
        <tbody>
          {[...by.entries()]
            .sort((a, b) => b[1].w + b[1].l - (a[1].w + a[1].l))
            .map(([champ, r]) => (
              <tr key={champ}>
                <td>{championLabel(champ)}</td>
                <td>
                  {r.w}–{r.l}
                </td>
              </tr>
            ))}
        </tbody>
      </table>
    </>
  );
}

function LinkDevices({ me, onChanged }: { me: Me; onChanged: (me: Me) => void }) {
  const [code, setCode] = useState<{ code: string; expiresUtc: string } | null>(null);
  const [entered, setEntered] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [linked, setLinked] = useState<string | null>(null);

  // Clear a shown code once it runs out.
  useEffect(() => {
    if (!code) return;
    const timer = window.setTimeout(() => setCode(null), new Date(code.expiresUtc).getTime() - Date.now());
    return () => window.clearTimeout(timer);
  }, [code]);

  async function run(work: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await work();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="panel">
      <h3>Play on another device</h3>
      {error && <p className="error">{error}</p>}
      {linked && <p className="dk-acct-ok">{linked}</p>}
      <div className="dk-acct-link-cols">
        <div>
          <p>
            <b>Add a device to this account.</b> Show a code here, then type it on the other device's account page.
          </p>
          {code ? (
            <p className="dk-acct-code" aria-label="Link code">
              {code.code.slice(0, 3)} {code.code.slice(3)}
              <small>Works once, for 10 minutes.</small>
            </p>
          ) : (
            <button
              type="button"
              className="btn"
              disabled={busy || !me.name}
              title={me.name ? undefined : "Pick a name first"}
              onClick={() => run(async () => setCode(await players.linkCode()))}
            >
              Show a code
            </button>
          )}
          {!me.name && <p className="dk-acct-dim">Pick a name above first.</p>}
        </div>
        <div>
          <p>
            <b>Got a code?</b> Enter it to make this device part of that account. Games played here move over too.
          </p>
          <form
            className="dk-acct-row"
            onSubmit={(e) => {
              e.preventDefault();
              void run(async () => {
                const next = await players.link(entered);
                adoptServerGames(next);
                setEntered("");
                setLinked(`This device is now signed in as ${next.name ?? "that account"}.`);
                onChanged(next);
              });
            }}
          >
            <input
              value={entered}
              onChange={(e) => setEntered(e.target.value.toUpperCase())}
              placeholder="ABC 123"
              autoCapitalize="characters"
              autoComplete="one-time-code"
              maxLength={8}
              aria-label="Code from your other device"
            />
            <button type="submit" className="btn" disabled={busy || entered.replace(/\W/g, "").length < 6}>
              Link
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}

export function AccountPage() {
  const [theme, setTheme] = useTheme();
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [editing, setEditing] = useState(false);

  useEffect(() => {
    players.me().then(
      (m) => {
        setMe(m);
        setName(m.name ?? "");
        setEditing(!m.name);
      },
      (e) => setError(e instanceof Error ? e.message : String(e)),
    );
  }, []);

  async function act(work: () => Promise<Me | void>) {
    setError(null);
    try {
      const next = await work();
      if (next) setMe(next);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }

  const back = backPath();
  const live = me?.games.filter((g) => g.status !== "finished") ?? [];
  const finished = me?.games.filter((g) => g.status === "finished") ?? [];

  return (
    <div className="dicekingdom dk-account">
      <div className="dk-titlebar-right" style={{ float: "right" }}>
        <ThemeToggle theme={theme} setTheme={setTheme} />
      </div>
      <p className="dk-acct-back">
        <a
          href={back}
          onClick={(e) => {
            e.preventDefault();
            navigate(back);
          }}
        >
          ← Dice Kingdom
        </a>
      </p>
      <h1>{me?.name ?? "Your account"}</h1>
      {error && <p className="error">{error}</p>}
      {!me && !error && <p className="dk-acct-dim">Loading…</p>}
      {me && (
        <>
          <div className="panel">
            <h3>Name</h3>
            {!me.name && (
              <p>
                You're playing as a guest - this device already keeps your games. Pick a name to make it an account,
                then add your other devices below. No password needed.
              </p>
            )}
            {editing ? (
              <form
                className="dk-acct-row"
                onSubmit={(e) => {
                  e.preventDefault();
                  void act(async () => {
                    const next = await players.setName(name);
                    setEditing(false);
                    return next;
                  });
                }}
              >
                <input value={name} onChange={(e) => setName(e.target.value)} maxLength={24} placeholder="Your name" aria-label="Your name" />
                <button type="submit" className="btn" disabled={!name.trim()}>
                  {me.name ? "Save" : "Make account"}
                </button>
                {me.name && (
                  <button type="button" className="btn dk-acct-quiet" onClick={() => setEditing(false)}>
                    Cancel
                  </button>
                )}
              </form>
            ) : (
              <p className="dk-acct-row">
                <span>
                  Other players see you as <b>{me.name}</b>.
                </span>
                <button type="button" className="btn dk-acct-quiet" onClick={() => setEditing(true)}>
                  Rename
                </button>
              </p>
            )}
          </div>

          <div className="panel">
            <h3>Record</h3>
            <Record games={finished} />
          </div>

          {live.length > 0 && (
            <div className="panel">
              <h3>In progress</h3>
              {live.map((g) => (
                <div key={g.gameId} className="dk-acct-game">
                  <span className="dk-acct-game-label">
                    {playedGameLabel(g)}
                    <small>
                      {" "}
                      · {modeLabel(g)} · {when(g.startedUtc)}
                    </small>
                  </span>
                  <button
                    type="button"
                    className="btn"
                    onClick={() => {
                      adoptServerGames(me);
                      navigate(back, `?resume=${encodeURIComponent(g.gameId)}`);
                    }}
                  >
                    Resume
                  </button>
                </div>
              ))}
            </div>
          )}

          <div className="panel">
            <h3>Games</h3>
            {finished.length === 0 && <p className="dk-acct-dim">Nothing finished yet.</p>}
            {finished.map((g) => (
              <div key={g.gameId} className="dk-acct-game">
                <span className="dk-acct-game-label">
                  {playedGameLabel(g)}
                  <small>
                    {" "}
                    · {modeLabel(g)} · {when(g.endedUtc ?? g.startedUtc)}
                  </small>
                </span>
                {g.result ? (
                  <span className={`dk-acct-result dk-acct-${g.result}`}>{g.result === "win" ? "Won" : "Lost"}</span>
                ) : (
                  <span className="dk-acct-result">
                    {g.winnerSeatId
                      ? `${championLabel(g.seats.find((s) => s.seatId === g.winnerSeatId)?.championId ?? "")} won`
                      : "Over"}
                  </span>
                )}
              </div>
            ))}
          </div>

          <LinkDevices
            me={me}
            onChanged={(next) => {
              setMe(next);
              setName(next.name ?? "");
              setEditing(!next.name);
            }}
          />

          <div className="panel">
            <h3>Devices</h3>
            {me.devices.map((d) => (
              <div key={d.id} className="dk-acct-game">
                <span className="dk-acct-game-label">
                  {d.label ?? "A browser"}
                  {d.thisDevice && <b> · this device</b>}
                  <small> · last used {when(d.lastSeenUtc)}</small>
                </span>
                {d.thisDevice ? (
                  me.name && (
                    <button
                      type="button"
                      className="btn dk-acct-quiet"
                      onClick={() => {
                        const only = me.devices.length === 1;
                        if (
                          !window.confirm(
                            only
                              ? `This is ${me.name}'s only device - signing out loses the account for good. Add another device first to keep it. Sign out anyway?`
                              : `Sign this device out of ${me.name}? It goes back to being a guest.`,
                          )
                        )
                          return;
                        void act(async () => {
                          await players.signOut(me);
                          const fresh = await players.me();
                          setName("");
                          setEditing(true);
                          return fresh;
                        });
                      }}
                    >
                      Sign out
                    </button>
                  )
                ) : (
                  <button
                    type="button"
                    className="btn dk-acct-quiet"
                    onClick={() => {
                      if (window.confirm(`Remove ${d.label ?? "that device"} from this account?`))
                        void act(() => players.removeDevice(d.id));
                    }}
                  >
                    Remove
                  </button>
                )}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
