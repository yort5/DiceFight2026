# Discord bots

One server process runs any number of Discord bots. A **feature** is the
command code (slash commands, their handling, any background work); a
**bot** is a token plus the list of features it runs. The same feature can
run in several bots.

| Feature id | Commands | What it does |
|---|---|---|
| `dicemasters` | `/card`, `/team` | Dice Masters card lookup and Teambuilder link preview |
| `radio` | none | Posts each new song a Live365 station plays to channels (from the church bot) |
| `members` | `/introduce`, `/verify`, `/whois` | Members give their real name (set as their nickname) and verify their email with a code, earning a role |
| `game-library` | `/games add · remove · list · available · borrow · return · mine · sell · unsell · forsale` | Board games the members own, to lend and sell |

## Adding a bot

1. In the [Discord developer portal](https://discord.com/developers/applications),
   create an application, add a Bot, and copy its token. Invite it with the
   `bot` and `applications.commands` scopes. It needs no privileged intents. The church bot
   (members) also needs the Manage Nicknames and Manage Roles permissions.
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
  Every game is one member's copy: they add it, can list it for sale, and
  answer borrow requests with the Lend / Not now buttons the bot posts
  (pinging them). The borrower or the owner marks it returned. Game names
  link to a BoardGameGeek search. Bots naming the same `LibraryId` share one
  library (default: the bot's name). Managers (Manage Server or a manager
  role) can remove anyone's game or listing and mark any loan returned.
  Libraries are saved in the game-records bucket as
  `discord/libraries/<id>.json`.

- **members** - `"Members": { "VerifiedRoleId": <role id>, "ManagerRoleIds": [ ... ], "SetNickname": true, "ServerName": "...", "Smtp": { "Host": "smtp.gmail.com", "Port": 587, "UserName": "...", "From": "...", "FromName": "Barnabas" } }`.
  `/introduce` opens a form for a real name and an email. The name becomes
  their server nickname. The email gets a 6-digit code (15 minutes, 5 tries,
  one resend a minute) and entering it - the form's **Enter code** button or
  `/verify` - gives them `VerifiedRoleId`; make private channels require that
  role. `/whois` (Manage Nicknames, Manage Server or a manager role) shows
  what someone gave. Set the SMTP password on the server as
  `DiscordBots__<bot>__Members__Smtp__Password` (for Gmail, an
  [app password](https://myaccount.google.com/apppasswords)); without SMTP,
  emails are saved unverified. The bot's role needs **Manage Nicknames** and
  **Manage Roles** and must sit above the members' roles and the verified
  role; Discord never lets a bot rename the server owner. Saved as
  `discord/members/<server id>.json` in the game-records bucket.

## Adding a feature

Implement `IBotFeature` (`BotFeature.cs`) in `Features/`, register it in
`DiscordBotsRegistration.AddDiscordBots`, and list its `Id` in a bot's
`Features`. Read settings from `bot.Settings.GetSection("<YourFeature>")`.

## Hosting

Each bot keeps a gateway connection open, and the radio polls between
requests, so Cloud Run must keep **one instance always on with CPU always
allocated** (`--min-instances=1 --max-instances=1 --no-cpu-throttling`).
