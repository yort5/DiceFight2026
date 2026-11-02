# Discord bots

One server process runs any number of Discord bots. A **feature** is the
command code (slash commands, their handling, any background work); a
**bot** is a token plus the list of features it runs. The same feature can
run in several bots.

| Feature id | Commands | What it does |
|---|---|---|
| `dicemasters` | `/card`, `/team` | Dice Masters card lookup and Teambuilder link preview |
| `radio` | none | Posts each new song a Live365 station plays to channels (from the church bot) |
| `game-library` | `/games list · mine · checkout · return · add · remove` | A shelf of board games members can borrow |

## Adding a bot

1. In the [Discord developer portal](https://discord.com/developers/applications),
   create an application, add a Bot, and copy its token. Invite it with the
   `bot` and `applications.commands` scopes. It only needs to send messages
   (radio) - no privileged intents.
2. Add an entry under `DiscordBots` in `src/DiceFight.Api/appsettings.json`:

   ```json
   "boardgames": {
     "Features": [ "game-library" ],
     "GameLibrary": { "LibraryId": "boardgames", "ManagerRoleIds": [ 123456789012345678 ] }
   }
   ```

3. Set the token on the server - never in the repo - as
   `DiscordBots__boardgames__Token` (a Cloud Run environment variable, or
   better a Secret Manager secret exposed as that variable). A bot without a
   token is skipped.

Commands register everywhere the bot is installed, which Discord can take up
to an hour to show. While trying things out, add `"DevGuildId": <server id>`
to register in that one server instantly.

## Feature settings

- **radio** - `"Radio": { "PollSeconds": 65, "Stations": [ { "Name": "...", "Url": "https://api.live365.com/station/<id>", "Channels": [ <channel id> ] } ] }`
- **game-library** - `"GameLibrary": { "LibraryId": "...", "ManagerRoleIds": [ ... ] }`.
  Bots naming the same `LibraryId` share one library (default: the bot's name).
  Adding/removing games and returning someone else's loan need Manage Server
  or a manager role. Libraries are saved in the game-records bucket as
  `discord/libraries/<id>.json`.

## Adding a feature

Implement `IBotFeature` (`BotFeature.cs`) in `Features/`, register it in
`DiscordBotsRegistration.AddDiscordBots`, and list its `Id` in a bot's
`Features`. Read settings from `bot.Settings.GetSection("<YourFeature>")`.

## Hosting

Each bot keeps a gateway connection open, and the radio polls between
requests, so Cloud Run must keep **one instance always on with CPU always
allocated** (`--min-instances=1 --max-instances=1 --no-cpu-throttling`).
