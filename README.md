# GeoConquest · Command Room

A single-player, turn-based strategy game inspired by Risk. You command one country and fight AI-driven NPCs
for a 10-country world using dice-based battles. NPCs explain every decision, and an LLM commentates every battle live.
Anyone with a game's link can watch it in real time; only the logged-in owner can play it.

**Stack:** ASP.NET Core MVC (.NET 8, C#) · MongoDB Atlas · Bootstrap 5 · D3 world map · Google Gemini (commentary)

---

## For the markers

| | |
|---|---|
| **Live site** | https://geoconquest-066c.onrender.com/ |
| **Player account** | username `Asanda` · password given in the Amathuba submission note |
| **Admin account** | username given in the Amathuba submission note |
| **Database** | MongoDB Atlas. Project access granted to ammarcanani@gmail.com, mc.tanner@uct.ac.za and ridewaan.hanslo@uct.ac.za |
| **Source code** | the attached zip (also on GitHub: github.com/Brazo21/Geo-Conquest, branch `master`) |

> Passwords are kept out of the code on purpose, so they are never published on GitHub.
>
> The site runs on Render's free plan, which sleeps after a period without visitors.
> **The first visit can take about a minute to wake up**; after that it is fast.

### What to try
1. **Watch without an account:** open *Live Games* and click any game. You see the board, dice and NPC reasoning live but get no buttons.
2. **Play:** log in as the player (or sign up), pick a country on *New Game*, reinforce, attack, roll, transfer, end turn. Use 1×/2×/4× to fast-forward the NPC turns.
3. **Alliances:** in a game, use the *Alliances* panel to offer a pact (the Defender is the most willing). Your ally disappears from the attack targets. *Break alliance* betrays them, and they never trust you again.
4. **Share link:** press *Copy share link* in a game and open it in a private window to watch it update in real time.
5. **Play on a phone:** open *📲 Get the app* (footer or account menu). Install it on Android or a computer with one tap, add it from Safari's *Share → Add to Home Screen* on iPhone, or scan the QR code.
6. **Admin:** log in as the admin → *★ Admin panel*: search accounts, suspend (the user is signed out at once), reactivate, change roles, send a reset link.
7. **Forgot password:** *Log in → Forgot password?* emails a single-use link valid for 30 minutes.
8. **POPIA:** consent at sign-up, the full notice under *Privacy*, and *Your data* to view or delete your information.
9. **Settings:** sound, music, vibration (Android), brightness, text size, dice animation and your General's look.

---

## Features

- **Game engine** (`Services/GameEngineService.cs`): setup, reinforcements (+2 per country from round 2), dice combat (attacker keeps 1 soldier at home, higher die wins, ties change nothing), conquest, transfers, elimination and victory. All rules run on the server; the browser only displays results.
- **NPCs** (`Services/Npc`): three personalities (Aggressor, Defender, Opportunist) that score every possible attack using exact win odds (`BattleOddsCalculator`) and record *why* they chose each move.
- **Alliances:** on your turn, offer one NPC a pact (lasts the round plus 3 more; allies can't attack each other). Each personality decides in character and logs its reason: the Defender joins against the strongest player, the Opportunist against a runaway leader, and the Aggressor only allies with someone stronger. NPCs also ally with each other against the leader. Breaking a pact is a betrayal: that NPC refuses you forever and prefers your countries as targets. Rules live in `GameSession` (`FormAlliance`, `BreakAlliance`, `ExpireAlliances`, ally check in `BeginAttack`) and `NpcStrategy` (`ConsiderAlliance`, `SeekAlliance`).
- **Anti-cheat / fair play:** every die is rolled on the server and saved before the page shows it, so refreshing, closing the tab or a second tab changes nothing. A battle abandoned for 5 minutes (e.g. browser closed mid-attack) counts as a retreat; a turn idle for 30 minutes ends itself, so a game can never be frozen (`GameEngineService.ApplyTimeoutIfDue`, run when anyone views or polls the game). The page warns before leaving mid-battle, buttons lock after one click so a double-click can't send two moves, every move is checked for owner and turn on the server, optimistic concurrency stops two tabs overwriting each other, and moves are rate-limited to 60 a minute per player.
- **AI commentary** (`Services/Commentary`): Gemini through the OpenAI-compatible API with a fallback model, and built-in lines if the AI is busy, so a game never stalls.
- **Live replay and spectating:** every event is stored in the game document; the page replays new events step by step, and spectators poll every 3 seconds.
- **Accounts and security:** an Account security page to change password or email (current password required; other devices signed out), PBKDF2-hashed passwords, cookie authentication, Visitor / Player / Admin roles, anti-forgery tokens on every form, login rate limiting, a Content-Security-Policy with per-request nonces, security headers, HSTS, sessions that end immediately on suspension or password reset, and optimistic concurrency so two tabs can't corrupt a game.
- **POPIA:** server-checked consent with version and date, a full privacy notice, and a self-service *Your data* page (access and erasure).
- **Installable phone app (PWA):** a web app manifest, home-screen icons and a service worker make GeoConquest installable on Android, iPhone and computers without an app store. It opens full screen, updates itself, and shows an offline screen without a signal. Pages are never cached, because they can hold personal data and every move must be checked live by the server.
- **Extras:** leaderboard, personal analytics with territory-over-time replay, synthesised sound effects and music, vibration, 3-D animated dice, an animated General avatar, light/dark themes and a settings page.

### Architecture
- **Thin controllers** (`Controllers/`) only read input, call a service and pick a view.
- **Services** hold every rule: `GameEngineService`, `AccountService`, `AdminService`, `AnalyticsService`.
- **Repository pattern:** `IGameRepository` / `IUserRepository` with MongoDB implementations in `Repositories/`.
- **One document per game:** players, countries and the full event log live in a single `GameSession` aggregate, so a game loads in one query.
- **Encapsulation:** domain entities (`Models/Domain`) use private setters; state only changes through methods that check the rules.

---

## Run it on your own computer

**Needs:** .NET 8 SDK and a MongoDB Atlas cluster (the free M0 tier is enough).

1. In Atlas, add your IP under *Network Access* and create a database user.
2. Store the secrets with User Secrets (never in `appsettings.json`). In Visual Studio: right-click the project → *Manage User Secrets*. Or from a terminal in the project folder:
   ```bash
   dotnet user-secrets set "MongoDb:ConnectionString" "mongodb+srv://<user>:<password>@<cluster>.mongodb.net/"
   dotnet user-secrets set "Commentary:ApiKey"   "<Gemini API key>"            # optional: built-in lines without it
   dotnet user-secrets set "Admin:Username"      "admin"
   dotnet user-secrets set "Admin:Email"         "you@example.com"
   dotnet user-secrets set "Admin:Password"      "<strong password>"
   # Optional, for password-reset emails (Gmail needs an "app password"):
   dotnet user-secrets set "Email:Host"          "smtp.gmail.com"
   dotnet user-secrets set "Email:Username"      "you@gmail.com"
   dotnet user-secrets set "Email:Password"      "<app password>"
   dotnet user-secrets set "Email:FromAddress"   "you@gmail.com"
   ```
3. Run it:
   ```bash
   dotnet run --launch-profile https
   ```
   then open https://localhost:7177.

The admin account is created on first start. Without email settings, the *Forgot password* page shows the reset link on screen, but only in development.

---

## Deploy to Render

The repository includes a `Dockerfile` and a `render.yaml` blueprint.

1. Push the code to GitHub.
2. In Render: **New → Blueprint**, choose the repository and confirm. Render builds the Docker image and asks for the secret values:

   | Variable | Value |
   |---|---|
   | `MongoDb__ConnectionString` | Atlas connection string |
   | `Commentary__ApiKey` | Gemini API key |
   | `Admin__Username`, `Admin__Email`, `Admin__Password` | first administrator |
   | `Email__ApiKey` | Brevo API key (Render's free plan blocks SMTP, so email goes through Brevo's HTTPS API) |
   | `Email__FromAddress` | the sender address you verified in Brevo |
   | `Email__PublicBaseUrl` | the site address, e.g. `https://geoconquest-066c.onrender.com` |
   | `Privacy__InformationOfficer`, `Privacy__ContactEmail` | shown in the POPIA notice (optional) |

3. In Atlas → *Network Access*, add Render's outbound IP ranges (Render service → **Connect** → **Outbound**), or `0.0.0.0/0`. The database is still protected by its username and password.
4. Wait for the deploy to show **Live**, then open the URL. `/healthz` returns `ok` when the app is up.

**Production details already handled:**
- Render's HTTPS proxy is trusted only when running on Render (`RENDER=true` / `BehindProxy=true`), so cookies stay secure and the login rate limit sees real visitor IPs.
- Cookie-encryption keys are stored in MongoDB, so restarts and redeploys don't log anyone out.
- Reset links are built from `Email__PublicBaseUrl`, never from the request, so a faked Host header can't redirect them.
- The container runs as a non-root user.

---

## Project layout

```
Controllers/       thin MVC controllers (Games, Account, Admin, Stats, Home)
Services/          game engine, NPC strategy, odds, commentary, accounts, admin, email, analytics
Repositories/      MongoDB repositories behind interfaces
Models/Domain/     GameSession aggregate, Player, Country, GameEvent, UserAccount
Models/ViewModels/ view models and validated input models
Security/          CSP and security headers, session validation, POPIA consent filter, input guards
Views/             Razor views (Command Room theme)
wwwroot/js/        map, live feed, odds, dice, sound, General, settings
wwwroot/sw.js, manifest.webmanifest, icons/   installable phone app (PWA)
Dockerfile, render.yaml   deployment
```
