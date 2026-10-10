# Game records

Every Dice Kingdom game can be recorded as one JSON file, for tuning teams
on real human play and for teaching the bot. Added 2026-10-04.

## What gets recorded

One file per game, rewritten at the end of every turn (so abandoned games
are kept too) and once more at game over:

- **Header:** game id, start time, engine version (the Cloud Run revision,
  plus a git commit when built from a checkout), the game's random seed,
  and each seat's Champion, team and whether it was the computer. A seat
  counts as the computer if it ever asked for the bot's move.
- **Every action in order:** who, which action, the request as sent, the
  turn and step it was made in, and a checksum of the game state after it.
  Rejected (illegal) moves are kept, with their error. Moves the web
  client makes on a player's behalf (declaring no blocks when nothing
  attacks, closing an empty attack window) are marked `auto`.
- **The result** (winner, life totals) and the game's play-by-play log.

Every roll comes from the game's seeded random generator, so the same
actions replay to the same game. A full 80-turn game is about 180 KB.

Code: `src/DiceFight.Api/Recording/`. The controller records through an
action filter (`V2GamesController.OnActionExecuted`).

## Where records go

Config section `GameRecords` (see `GameRecordSinks.cs`):

| Setting | Effect |
|---|---|
| `Bucket` | Write to this Cloud Storage bucket (wins if both are set) |
| `LocalPath` | Write to this folder |
| `Prefix` | Path prefix, default `dice-kingdom/games/` |

Neither set means recording is off. Development (`appsettings.Development.json`)
writes to `game-records/` at the repo root (git-ignored). Production has
nothing set, so it records nothing until a bucket is configured.

Files are named `<prefix><yyyy>/<MM>/<dd>/<gameId>.json`.

**Players live here too** (2026-10-10, `src/DiceFight.Api/Accounts/`): one
file per player at `dice-kingdom/players/<playerId>.json` in the same bucket
or folder (names, device token hashes, game history). With neither setting,
players are kept in memory only: they work, but a redeploy forgets every
account and history. So the bucket setup below matters for accounts as well as
records. The steps are the same; Storage Object User already includes the reads
that players need.

## Turning it on in GCP (one-time, in the Cloud Console)

1. **Create a bucket.** Cloud Storage → Buckets → Create.
   - Name: anything unique, e.g. `dicefight-game-records`.
   - Location: Region, the same region as the Cloud Run service. Use
     `us-central1`, `us-east1` or `us-west1` if possible: those are the
     always-free regions (5 GB).
   - Storage class: Standard. Access control: Uniform. Keep "Enforce public
     access prevention" on.
2. **Let the Cloud Run service write to it.**
   - Find the service's account: Cloud Run → the service → Security tab
     ("Service account"). If it was never changed, it's the default
     `<project-number>-compute@developer.gserviceaccount.com`.
   - Bucket → Permissions → Grant access → that account → role **Storage
     Object User**. "Storage Object Creator" alone isn't enough, because
     records are overwritten each turn.
3. **Point the app at it.** Cloud Run → the service → Edit & deploy new
   revision → Variables & secrets → add `GameRecords__Bucket` = the bucket
   name (double underscore). Deploy.
4. **Check it.** In the new revision's logs, look for "Recording Dice
   Kingdom games to Cloud Storage bucket ..." and "Keeping players in
   Cloud Storage bucket ...". Play one turn, and a file
   should appear under `dice-kingdom/games/` in the bucket. A failed write
   logs "Couldn't write game record" with the reason and never interrupts
   the game.

No key files are needed. The app gets its credentials from Cloud Run's
metadata server.

## Cost

- **Storage:** about 180 KB per game. The free 5 GB holds about 28,000
  games.
- **Writes:** one per turn. The free tier covers 5,000 writes a month, about
  60 full games; after that it's $0.005 per 1,000 writes, so 1,000 games a
  month comes to about $0.40.
- **Reads** for analysis are negligible.

## Analysing records

Copy them down, then run the reader:

```
gcloud storage cp -r gs://<bucket>/dice-kingdom/games ./game-records
cd tools/GameRecords
dotnet run -c Release -- ../../game-records
dotnet run -c Release -- ../../game-records --disagreements
```

For each game it prints the Champions, which seats were human, the winner
and turns, and whether it replays exactly. It then shows, per kind of
decision, how often each move matched the bot's own choice in the same
spot. Bot seats should show 100%. `--disagreements` lists every human move
the bot would have made differently, with the bot's reasoning, which is
the raw material for improving the bot.

`GameReplayer.Replay(record, onAction)` rebuilds any recorded game and hands
you the state before each move, for any other analysis.

A replay can drift if the engine's rules changed after the game was played.
The checksums catch that, and the reader reports the first move where it
happened.

## Abandoned games

A game someone walks away from keeps its file, with `result.gameOver` false
(the reader shows it as "unfinished"). Leave these out of win rates, or
count them separately; every move in them is still a real decision.

Live games are dropped from server memory once idle (`V2GameStore.ForgetIdle`,
checked every 30 minutes; config section `GameExpiry`):

| What | Dropped after |
|---|---|
| Unfinished game, no move made | 24 hours (`UnfinishedHours`) |
| Finished game | 2 hours (`FinishedHours`) |
| Invite link never joined | 24 hours (`LobbyHours`) |

An unfinished game's record is saved once more as it's dropped, which also
keeps the moves of the turn it was abandoned in. Someone opening a dropped
game within a week is told it has expired.

## Known limits

- **Games still live only in server memory while being played.** A
  redeploy, or Cloud Run shutting down an idle instance, ends any game in
  progress. Its record is kept up to its last completed turn, since a
  shutdown flushes pending writes. Saving games so they can be resumed is a
  separate piece of work.
- **The desktop page has no Global rail.** That's unrelated to recording,
  but it means desktop players can't use Globals.
