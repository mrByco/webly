# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## What this is

**Webly**: a self-service SaaS where somebody describes their website in a chat and gets one — versioned,
on their own domain, published to the internet, without touching a line of code. Product UI and generated
sites are English; code and comments are English.

**A site is a real Next.js project**, one bare git repository per site, edited by a coding agent (Claude
Code or OpenCode) running in a sandbox. Webly's own code never writes a page: it owns the repository, the
versions, the sandbox, the preview proxy and the publish. `docs/domain-plan.md` §1 is the argument for that
and what it costs; the structured-document model it replaced is in git history, not in this tree.

`PROJECT.md` is the product and the lineage: what Webly is, which two projects its architecture comes from
(**cookta-rework** for the layering, the auth and the dev stack; **auto-grader** for the run substrate),
and the decisions that are settled. `MASTER_PLAN.md` is the plan of record, `whats_next.md` says where work
actually stopped, and `docs/` holds the long-form reasoning: `domain-plan.md` (sites, source, versions),
`agent-plan.md` (the agent, the sandbox, the run substrate), `deploy-plan.md` (publishing, domains,
Vercel). Read the relevant one before designing in that area — the "why" is written down there rather than
re-derived.

## Status: running

**The whole product loop has been driven through the running app.** A verified account, a site whose bare
repository holds the template in one commit, three agent turns over the hub each committing one version, the
preview served through Webly's own origin, and a published page at `/published/{nanoid}/` saying what the
person typed. The backend compiles, the schema is real migrations applied to a real Postgres, the 173-test
suite is green, and `client/src/app/api/` is the real generated client that the Angular app type-checks and
prerenders against.

Two harnesses drive it, and they answer different questions:

- **`tools/e2e/run.mjs`** stands in for the C# and drives everything underneath it — real git plumbing, the
  real sandbox agent, the real `claude` CLI, the real Next.js dev server and build — in seventeen steps from "a
  new site is the template" to "a compile error stops being reported once it is fixed". No .NET, no Docker, no
  database, no credentials. `tools/e2e/README.md` lists what it has caught.
- **`tools/e2e/screens.mjs`** answers the question neither of the others can: *is the page wrong to look at?*
  It walks every screen of the app and of a published site in a real browser at two widths and fails on a page
  error, a 5xx, a blank screen, **a pane that has started scrolling sideways**, **an obvious accessibility
  mistake** (an icon-only control with no name, an `<img>` with no `alt`, a field with nothing naming it, a
  field whose border is under 3:1 against what is behind it, a page with no `h1` or several), or **a 404 on
  anything the page asked for** — which is the defect it was
  written for: a published site whose every stylesheet and chunk 404ed behind a document that was 200 and HTML
  that was perfect. It **presses one thing** on the screens that have something the default selection does not
  reach — the oldest version, a photograph — because three defects in a row were found by clicking once on
  screens this sweep had walked clean a dozen times. Deliberately one click and never a sequence: a harness
  that drives a flow is a test that breaks when the flow changes, and this one's job is to look.
  It walks the **signed-out** screens too — login, register, forgotten-password — because
  they are the product's first five minutes, and it renders the **sign-in-with-Google** branch by stubbing
  `/api/auth/providers`: that button appears only when a client id is configured, so no development machine
  ever drew it, and it carried the word "vagy" — Hungarian for "or", from the reference project — on the two
  screens every new customer sees first. A branch nobody can render is a branch nobody checks. It skips the
  published screens for a site nobody has published, and asserts they answer
  404 instead, because a harness that is red when the product is right is one nobody reads.
  The sideways-scroll rule reads class names rather than computed style, deliberately: CSS gives `overflow-x`
  the used value `auto` the moment `overflow-y` is not visible, so a column that scrolls vertically and a strip
  meant to scroll horizontally are reported identically by the browser, and only the author's intent — which
  this app writes in its classes — tells them apart. The accessibility rules are deliberately a handful rather
  than an audit: a real audit needs a dependency and produces a report somebody has to triage, and what is
  here is the set that is unambiguous, that a component can regress silently, and that each make a page
  unusable for somebody. The contrast one is the clearest case for being a number rather than a review: a
  border drawn too faintly *photographs* as a design choice, so every screenshot this sweep has ever taken
  showed fields at 1.5:1 and nothing looked wrong. It resolves the colour through a canvas, because `oklch()`,
  `color-mix()` and a translucent border are things a computed style hands back unresolved — only the browser
  can composite them — and it measures only a field that draws a border, so a deliberately borderless one is
  not something to work around. They apply to the published site too, which is the half `AGENTS.md` asks the agent
  for. It exists because five defects in one afternoon — an unstyled published
  site, a 404 that served Webly's dashboard, a title that named no site, pill-shaped form fields, a deleted
  site still serving — had all been "checked" by reading HTML that was perfect.
- **`tools/e2e/turn.mjs`** does the opposite: it talks to the running backend over the real hub, so what it
  exercises is the C# — `ChatRunLauncher`, `AgentTurnService`, `SiteWorkspaceRegistry`, the sandbox provider,
  `CommitSiteVersion`. It exists because a turn cannot be started over HTTP; `StartChat` is a hub method, so a
  SignalR client is the only way to press the button.

**What is still unrun**, and treat each as the plan rather than as working code:

- **`OpenCodeAgent` and `VercelDeploymentTarget`** — written against another product's documented interface,
  never executed. `docs/deploy-plan.md` §6 says what to reconcile in the Vercel one first.
- **`E2bSandboxProvider`** — never run. The contract every provider speaks is the one `tools/e2e` exercises,
  which is the point of having it. **`DockerSandboxProvider` has now run**: a container started, published a
  loopback port, answered `/health`, took a seeded tree, ran a turn's agent and handed the tree back, and a
  version was committed from it. Two things it had wrong are fixed below. What has still not run in it is a
  `next dev` — see the note on the font.
- **The real agent inside the app.** `ClaudeStreamJsonParser` is covered by a recorded transcript and the
  `claude` CLI has run under `tools/e2e/run.mjs`, but every turn through the running app so far has been the
  mock. That needs `Agent:ClaudeCode:ApiKey` in user secrets and nothing else.
- **Google sign-in and Resend**, which are configuration away and absent by design without it.

### Docker here, and what it is actually good for

A daemon **can** be started in this kind of container — `dockerd --iptables=false --ip6tables=false`, as
root — and containers run. What does not work is the **image registry**: a pull answers 403 at the egress
proxy. So `docker build` on `deploy/sandbox/Dockerfile` is out, because its base image cannot be fetched.

The way round it, when the Docker provider needs exercising: assemble a rootfs on the host — the node
install, `git`, a handful of `/bin` tools with their libraries, `tools/sandbox-agent`, and the template's
`node_modules` under `/workspace` — `tar` it, and `docker import --change 'CMD …'`. That is how
`DockerSandboxProvider` was first run. It is not the real image and it does not have to be: what it proves is
the provider's plumbing, not the Dockerfile's.

### Getting a .NET SDK where there is not one

`apt-get install dotnet-sdk-10.0` — it is in the Ubuntu archive (`noble-updates/universe`), which matters
because the official installer host is blocked on plenty of networks and that is what made this repository
"written, not run" for its first several commits. `apt-get update` first: the index and the pool rotate
independently and a stale index 404s. `dotnet tool install --global dotnet-ef` for migrations.

The client needs **Node ≥ 22.22.3** (Angular 22's own floor) — `nvm use 24` where the system node is older.

### Running it with nothing installed

The development defaults are chosen so that a fresh clone works with **node, git and a Postgres**, and
nothing else — no Docker, no model key, no hosting account:

| Piece | Default | What it means |
|---|---|---|
| `Sandbox:Provider` | `local` | `LocalSandboxProvider` spawns `tools/sandbox-agent` as a child process. **Not isolation**, and refused outside Development. |
| `Agent:Mock:Enabled` | `true` | `MockCodingAgent` makes one real, deterministic edit. A real key takes over without a settings change. |
| `Deployment:Provider` | `filesystem` | The real `next build` as the publish gate, then the export written to `.run/published/{nanoid}` and served at `/published/{nanoid}/`. |

Each of those is a development-only substitute and each refuses to run in production, in code rather than in
a comment. Swapping any one of them for the real thing is one configuration key.

## Work philosophy

Hobby project; **clean, maintainable code is the point**. No deadline pressure to trade against it. Prefer
the clear solution over the clever one and the small diff that fits the surrounding code over a rewrite.
**Every feature gets validated in the running app** (`https://localhost:5000`), not just in tests — and for
this product that means a real turn on a real site: the agent writing files, the preview updating by
itself, and a published page on the internet. The editor looking right proves the least interesting third
of it.

Write the decision down next to the code, not only the behaviour. Both reference projects are still
readable because of that habit, and a comment explaining why something is *not* the obvious shape is worth
more than one restating what the line does.

## Developer commands

**Prefer the `webly-dev` MCP server** (`tools/dev-mcp/index.js`, wired in `.mcp.json`). The `run-app` skill
(`.claude/skills/run-app`) is the short version of this table.

| Task | MCP tool | Raw fallback |
|---|---|---|
| Start / stop / restart / status | `app_start`, `app_stop`, `app_restart`, `app_status` | `./run-app.ps1 start\|stop\|status` |
| Build backend (stops it first if running) | `app_build` | `dotnet build Webly.slnx` |
| Backend tests | `app_test` (optional `filter`) | `dotnet test Webly.Tests/Webly.Tests.csproj` — set `WEBLY_TEST_POSTGRES` to a server's connection string to use it instead of Testcontainers (no Docker needed) |
| Tail logs | `app_logs` | `./run-app.ps1 logs -Only backend` |
| Persistent dev DB up/down | `db_compose_up`, `db_compose_down` | `docker compose -f docker-compose.dev.yml up -d` |
| Inspect the DB | `db_status`, `db_query` (read-only SQL) | `docker exec webly-postgres-dev psql -U webly -d webly` |
| Regenerate the Angular API client | `regen_api` | `./regen-api.ps1` or `yarn --cwd client regen-api` |
| Angular typecheck / build | `client_typecheck`, `client_build` | `yarn typecheck`, `yarn build` in `client/` |
| Migrations | — | `dotnet ef migrations add <Name> --project Webly.Data --startup-project Webly.Data` |
| Read a sent email | — | open the newest file in `.run/mail/` (dev sends nothing; it logs and saves) |
| Build the sandbox image | — | `docker build -f deploy/sandbox/Dockerfile -t byc0/margareta:webly_sandbox .` |
| Inspect a live sandbox | — | `docker ps --filter name=webly-sandbox`, then `docker exec -it <name> sh` |
| Look at a site's repository | — | `git --git-dir .run/repositories/<nanoid>.git log --stat` |
| **Drive the whole product loop without the backend** | — | `node tools/e2e/run.mjs --agent mock` (or `--agent claude`) |
| **Drive one turn through the running backend** | — | `node tools/e2e/turn.mjs --site <nanoid> --cookies <curl jar> "<message>"` |
| **Look at every screen, in a browser** | — | `node tools/e2e/screens.mjs --email … --password …` (needs `playwright-core` and a Chromium; neither is a dependency) |

### Running-the-stack facts that cost time if unknown

- **Single origin.** The browser only ever talks to the backend at `https://localhost:5000`. It serves
  `/api/*`, `/health`, `/hubs/realtime` and `/swagger` itself and **reverse-proxies everything else**
  (YARP, `Program.cs`) to the Angular dev server on plain `http://localhost:4200`. In production the same
  proxy points at the built Angular SSR node server (`node dist/client/server/server.mjs`, :4000).
  **There is no CORS and there must not be** — same-origin is by construction. Do not point the browser at
  :4200.
- **Hybrid dev database.** On startup in Development the backend tries whatever Postgres is on
  **`localhost:5434`** — the docker-compose one in `docker-compose.dev.yml`, or one installed locally; the
  port is all it can tell apart, which is why `dbSource` reports `persistent` rather than naming compose.
  Not 5432, which a locally installed Postgres commonly owns, and not 5433, which is the reference project's
  dev DB — connecting to *that* by accident is worse than not connecting at all, because its schema is close
  enough to look plausible and wrong. If nothing answers it falls back to an ephemeral Testcontainers
  Postgres (needs Docker) — **that data is gone on the next restart** — and if Docker is absent too it says
  so in a sentence naming both remedies rather than throwing a Testcontainers stack trace.
  `GET /health` → `{ status, dbSource }` says which is active; so does `app_status`.
- **The app does not care what directory it is started from**, and that took a class to arrange.
  `Webly.Api/Infrastructure/PathAnchor.cs` resolves the five settings that name something on disk against the
  repository root when the walk up from the binary finds `Webly.slnx`, and against the content root (which is
  the binary's own directory) when it does not — so development works from anywhere and `/app` in the
  container is the same case. Before it, `run-app.ps1` and the dev MCP server each started the app in a
  working directory that broke the other, because `dotnet run` ignores the shell's and uses the project's.
- **The site template is read once and cached**, so editing `templates/next-site` and then creating a site
  gives you the template as it was when the backend started. That is right in production — it ships inside the
  image and never changes at runtime — and it is a wasted verification in development, where the new site's
  repository quietly holds the old file. `DirectorySiteTemplateSource` says so; restart the backend after
  touching the template, and check the commit rather than the screen if something looks unchanged.
- **Start order: backend first.** The frontend's `prestart` (`ng-openapi-gen`) reads the backend's live
  swagger. `run-app.ps1` and `app_start` both do this in the right order.
- **A template change that does not appear in the browser means a stale lazy chunk, not a wrong change.**
  `ng serve`'s incremental build sometimes keeps serving the previous version of a lazily loaded component's
  template — the rebuild logs "Application bundle generation complete", the new chunk is on disk and the page
  loads the old one. It has cost two diagnoses here, each spent looking for a bug in perfectly correct code.
  Restart the dev server before doubting the code.
- Trust the dev cert once: `dotnet dev-certs https --trust`.
- Logs and pidfiles live in `.run/` (gitignored). `Webly.Api` locks its build output while running —
  `app_build` handles stop/build/start.
- **A stopped backend used to leave a `next dev` behind, once per open site.** The dev server is spawned
  *detached*, in its own process group, so killing the sandbox agent's group misses it — and a killed agent
  never runs the handler that would have taken it down. It records its process id in a `.devpid` file beside
  the workspace (never inside: that file would travel into the site's next commit), and
  `LocalSandboxProvider`'s first-use sweep kills what it finds before deleting the directories, in that order,
  because a dev server whose workspace has just been deleted does not exit — it spins at 100% of a core for
  ever. Sixteen of them is what a wedged development machine looks like, and nothing says why.
- **Editing a site needs `node` and `git` on PATH**, and by default nothing else. `Sandbox:Provider` is
  `local`, so a turn spawns `tools/sandbox-agent` as a child process with a workspace under
  `.run/workspaces`; set it to `docker` for real isolation (build the image first — see the table above) or
  the first message fails with "the sandbox container never became reachable". Idle sandboxes are reaped
  after ten minutes either way.
- **A site's repository is `.run/repositories/{nanoid}.git`** locally, which is gitignored: a developer's
  test sites are their own, and they are not backed up. `git --git-dir … log --stat` is how to see what a
  turn actually did.
- **An agent turn without a model key gets the mock agent**, which makes one real edit to the home page's
  headline — enough to exercise the workspace, the commit, the preview and the publish. Put
  `Agent:ClaudeCode:ApiKey` in user secrets and the real agent takes over with no settings change. With
  `Agent:Mock:Enabled` false and no key, `/api/sites/{nanoid}/chat/status` reports `enabled: false` and the
  client hides the chat rather than failing inside it.
- **Publishing goes to a directory by default.** `Deployment:Provider` is `filesystem`, which runs the real
  `next build` and writes the export to `.run/published/{nanoid}`, served at
  `https://localhost:5000/published/{nanoid}/`. So the publish path — including a failed build blocking it — is
  testable with no hosting account. Set it to `vercel` once `Deployment:Vercel:Token` is in user secrets.
- The solution file is `Webly.slnx` (the new .NET 10 format).
- Package manager for `client/` is **yarn** (classic). Central Package Management for .NET: versions live
  in `Directory.Packages.props`, csproj files carry bare `<PackageReference>`s, and transitive pinning is
  on (Npgsql asks for an older EF Core Relational than the version we pin).

## Architecture

### Backend layering

`Webly.Api` (controllers, the hub, middleware, DI, the reverse proxy) → `Webly.Services` (all business
logic) → `Webly.Data` (EF Core `WeblyDbContext`, entities, repositories, migrations). Strict and
one-directional: `Webly.Api` never references `Webly.Data` directly, and EF entities never leak above
`Webly.Data`. `Webly.Tests` is NUnit + Testcontainers, except `GitSiteRepositoryStoreTests`, which needs
only git and a temporary directory.

Controllers are thin: no business logic, no `object` in return types. `Webly.Services` splits into
`Services/` (stateful/infrastructure — `Repositories/` for git, `Sandboxes/`, `Workspaces/`, `Realtime/`,
`Deployments/`), `UseCases/` (single-operation classes with an `Execute`) and `Agent/` (the coding-agent
interface and its two implementations). DTOs live one-per-file under `Webly.Services/DTO/`. Entities are
addressed by nanoid strings across the API surface; integer `Id`s stay internal to `Webly.Data`.

**There is no model SDK anywhere in the solution** — no `Microsoft.Extensions.AI`, no Anthropic or OpenAI
package. The agent is another product's CLI, running in a sandbox. `Directory.Packages.props` says so where
the entries used to be, and adding one back is a decision to argue for in `docs/agent-plan.md` §1 first.

**`Webly.Data/Models/` is organized by domain**, one folder per area, plus `Interfaces/` for the
cross-cutting entity contracts: `Authentication/`, `Sites/`, `Deployments/`, `Chat/`. Namespaces follow the
folders.

`WeblyDbContext.SaveChanges` fills in `Nanoid` on insert and stamps `CreatedAt`/`UpdatedAt` for anything
implementing those interfaces, so no use case, seeder or test builder has to remember. `SiteVersion` and
`ConversationMessage` carry a `CreatedAt` **without** `IHasTimestamps`, because an entity that can be
updated is not what either of them is; they are stamped by the same clock in the same method.

### Authentication

Cookie-based JWT, ported from the reference project with its known defects already fixed. Two `HttpOnly`
cookies: `webly_access` (15 min) and `webly_refresh` (60 days, rotated on every use).

- **There is no refresh endpoint, by design.** `CookieAuthenticationMiddleware` turns the cookies into an
  `Authorization` header and, if the access token is dead but the refresh cookie is live, rotates silently
  mid-request and sets fresh cookies on the response. The client never learns that access tokens expire.
  The middleware must stay registered **before** `UseAuthentication()`.
- **Authorization is default-deny** (`FallbackPolicy` requires an authenticated user). Anything public
  needs `[AllowAnonymous]` — including `HealthController` and, load-bearingly, `MapReverseProxy()`. Without
  the latter nobody can load the login page in order to log in.
- **Logout ends one session, not all of them.** It revokes the presented refresh token and puts the access
  token's `jti` in `IAccessTokenBlacklist` — an `IMemoryCache` whose entries expire exactly when the token
  would have. **Per process**: a restart or a second instance forgets it. One of the three pieces to move
  to shared storage if Webly ever scales out (the others are `RunRegistry` and `AgentBudget`).
- **And it now ends the hub connection too, which is the longest-lived credential in the product.** A hub
  reads its caller's `ClaimsPrincipal` once, during the handshake, and never again — so signing out revoked the
  session everywhere except on the socket the page already had open. Demonstrated rather than reasoned about:
  connect, log out over HTTP until `/api/sites` answers 401, then invoke `StartChat` on that same connection,
  and a real turn started as the signed-out user, on their site, spending their budget. Three things close it,
  and each covers what the others cannot: `AuthService.logout` calls `RealtimeService.disconnect()` first, which
  is the ordinary case; `RealtimeHub.CallerId()` asks the blacklist on **every** invocation, which does not
  depend on the client doing anything; and `SignOut` ends the **session's** live connections through
  `IRealtimeSessions`, which is what reaches a socket the client will not close.
- **`RefreshToken.SessionId` is what made the third one possible**, and it is the only thing about a session
  that does not change while it lasts: the access token's `jti` is replaced every fifteen minutes and the
  refresh hash every use, so neither could name the session a five-hour-old socket belongs to. It is minted at
  sign-in, **carried across every rotation** (`IssueAsync(user, continuingSessionId)`), and rides in the access
  token as `sid`. Per session and not per user, which is a distinction that cost a run to learn: ending a
  *user's* connections also killed the socket on their other devices, and `Context.Abort` closes cleanly enough
  that the SignalR client never reconnects — the other device sat there with a dead connection, which is worse
  than the gap being closed. Watched both ways afterwards: the signed-out session's socket dies, the other
  session stays `Connected` and still answers. A connection whose token predates the claim carries no `sid` and
  is ended whenever its user signs out anywhere, because being unable to name something is not a reason to
  leave it running.
- **The preview cookie reads `sid` too, so signing out ends it.** It used to outlive the session by the rest of
  its twelve hours — narrow, because it reaches one site's preview and nothing else, and real, because a site
  nobody has published is not otherwise readable and a shared machine is exactly where somebody signs out. It
  could not be fixed before this: the token named a *user*, and a user is not a session. A token minted before
  the change has three fields instead of four and is honoured until it expires, deliberately — the alternative
  is every open editor's preview breaking the moment a deployment lands, to close a window that closes itself
  by the next morning. `IAccessTokenBlacklist` gained the session half of its job for this (`RevokeSession` /
  `IsSessionRevoked`), which is also what makes the check cheap enough for a path that runs per chunk. The
  window it remembers is a **day**, not the refresh token's sixty: the longest-lived thing a session can mint is
  that twelve-hour cookie, and remembering a revocation after everything it could revoke has expired is paying
  memory for nothing.
- **Refresh tokens are single-use, with a thirty-second window.** Replaying a spent one is treated as theft
  and revokes the whole chain — but not immediately, and the exception is not a weakening. A browser sends
  requests in parallel, so when the access token dies they all arrive carrying the same live refresh cookie:
  one rotates and the rest are replays of a token revoked a millisecond ago. Inside `RotateRefreshToken.
  ReuseGrace` a replay is served an access token and **no new refresh token**, so the successor stays the only
  live one. The window is keyed on `RefreshToken.ReplacedAt`, not `RevokedAt`, because both a rotation and a
  sign-out revoke — and a sign-out that keeps working for another thirty seconds is not a sign-out. Only the
  HMAC hash is stored.
- **The verification gate lives in the accessors, not in an attribute.** `GetUserId()` /
  `GetUserIdIfLoggedIn()` return a caller **only if their email is verified** and throw
  `EmailNotVerifiedException` (→ 403, `email_not_verified`) otherwise; `GetUserIdUnverified()` is the
  deliberate opt-out used by `/me`, `logout`, `resend` and `password/change`. New endpoints are gated by
  default because the default accessor is. The hub restates the same gate as
  `ClaimsPrincipal.GetUserIdVerified()` — a hub invocation never reaches the HTTP middleware that turns
  that exception into a 403, so it throws `HubException` instead.
- **An account can be closed, and that is the one operation that asks for the password again.** `DELETE
  /api/auth/account` deletes each site through `DeleteSite` first — a site is a git repository, a warm sandbox and
  a provider project as well as rows, and only that use case knows about all three — and then removes the user
  with `ExecuteDelete`, because EF cannot: a message points at the version it produced and that version points
  back, so the client-side cascade reports a circular dependency and sends nothing. It uses
  `GetUserIdUnverified`, deliberately: somebody who never confirmed their address is the person most entitled to
  leave.
- **Verification is a blocking onboarding step.** Registering lands on `/verify-email` and nothing else is
  reachable until the address is proven: a site publishes to the public internet under our infrastructure.
  Google-created accounts arrive verified and skip it.
- **The sixth digit of the confirmation code is what submits it.** It used to fill the box and light a button,
  which is one step more than a one-time code needs: the length is known, there is exactly one thing that can
  happen next, and the code is on a phone while the box is on a laptop. `onCodeInput` calls `submitCode`, which
  already refused to re-enter while a request was in flight. The screen's own closing line had promised "this
  page moves on by itself" about the *link* the whole time.
- **An HTML comment in an email template is an HTML comment in the message.** No client draws one, so three
  paragraphs about a header colour and a bowl of stew travelled to every customer's inbox unseen until somebody
  read a sent email as text. `EmailLayout`'s reasoning lives in `<remarks>` now and `EmailTemplateTests` fails
  on `<!--` in any of the seven messages.
- **The onboarding chain is written down once, in `AuthService.nextStop()`** — prove the address, then have
  a site, unless a `redirect` says the visitor was already on their way somewhere. Sign-in, registration,
  verification and `verifiedGuard` all ask it, so no path can disagree.
- `SameSite=Lax`, not `Strict`: a Strict cookie is withheld on navigations arriving from another site, so
  returning from Google's consent screen would look logged out until a reload.
- **Google sign-in** is registered **only when `Authentication:Google:ClientId` is configured**, so the app
  boots and tests pass without credentials; `GET /api/auth/providers` tells the client whether to show the
  button. Account linking is by **verified** email.

**Mail**: `IEmailSender` — `ResendEmailSender` when `Email:Resend:ApiKey` is set, otherwise
`LoggingEmailSender`, which logs and **writes the HTML to `.run/mail/`** so you can open the real thing in a
browser. Templates are hand-written HTML in C# raw strings over `EmailLayout`; email needs tables and
inline styles, so a template engine buys nothing at seven messages. Links are built from `Email:BaseUrl`,
never the request host (host-header poisoning).

- **The shell is Webly's, and for months it was not.** The templates came from the reference project and
  nobody opened one, so every Webly email went out in that project's paprika header, with a bowl-of-stew
  emoji beside the name and `lang="hu"` on the document. An email is the one part of this product a customer
  sees when they are not looking at it, which is exactly why nothing caught it. `EmailTemplateTests` now
  fails on those values by name — a comment saying "do not put the paprika back" is not something a build can
  enforce. The palette lives in `EmailLayout` as hex, converted from the same OKLCH numbers `styles.css`
  uses, because email cannot read a variable: change one and change the other.
- **The footer belongs to the message**, and `EmailLayout.Wrap` **requires** one. "If you did not ask for it,
  you can ignore it" is true under a sign-up code and a lie under a notice about somebody's own website, which
  is where it sat. The first fix left a `DefaultFooter` behind and six of the seven messages took it — including
  the password reset, the other message that can reach somebody who did nothing — so the decision the comment
  described was being made once. A default is how a per-message decision stops being made; there is nothing to
  fall back to now.
- **A failed publish carries the build log.** The mail said "the error is below" with nothing below, because
  the detail stayed on the settings screen — the one place the person reading the mail is not.

### Sites and ownership

**A site has exactly one owner.** No memberships, no roles, no owner column beside a role enum — the
reference project spent real effort keeping `ownerSub` in step with an owner role and found households with
neither. Collaboration, when it lands, is a membership table plus one clause inside
`ISiteRepository.FindForOwnerAsync`.

- **`FindForOwnerAsync` is the one place the ownership check lives.** Every use case starts from what it
  returns, so none of them can express the check wrongly or forget it. Its light variant skips the version
  rows for the operations that only touch the site row — and it is the one the **preview proxy** calls on
  every single request, including every chunk and the hot-reload socket, so it has to stay cheap.
- **Not yours reads as 404, never 403.** A 403 would confirm that a guessed nanoid names a real site.
  `SiteController.Failure` is the one place that mapping lives, and `SiteIsolationTests` is what keeps the
  sixteenth caller honest: it drives every route under a site as a second account and fails on anything that is
  not a 404. The one deliberate exception is `chat/status`, which never looks at the site — it says whether this
  deployment has an agent — and the test proves that by checking an invented nanoid gets the same answer.
- **A site never exists without a version.** `CreateSite` writes the row, initializes the bare repository
  from the template, commits it, and points `HeadVersionId` at that commit — so no code anywhere else has to
  handle a site with no source. It saves the row first, because the repository is keyed by the nanoid the
  context stamps on insert.
- **`User.CurrentSiteId` is which site the editor opens on**, the reference project's `CurrentFamilyId` in
  the same role. Read it **before** deleting anything: the FK nulls it on cascade, so a check afterwards
  can no longer tell "was looking at this site" from "was looking at nothing". Its successor when that site
  goes is the **most** recently updated of the rest — `ListForOwnerAsync` orders by `UpdatedAt` descending, and
  `DeleteSite` took the *last* of that list, which is the site its owner has cared about least. Three tests in
  `SiteDeletionTests` pin the three cases, and the first of them is red on the old line.
- **The slug does not follow the name.** A rename leaves the address alone, because the address may already
  be published, linked to and indexed. What the name *does* reach, when the person asks for it, is the site's
  own source — see `SiteIdentity` under "A site's source".

### A site's source

The whole of a site's content is a **Next.js project in a bare git repository**, one per site, under
`Repositories:Root`. There is no document, no section catalogue and no renderer in this codebase; the agent
writes `.tsx` files and Next.js renders them. `docs/domain-plan.md` §1 is the argument, including what the
structured-document model it replaced was better at.

- **`ISiteRepositoryStore` is the only thing that touches git**, and it drives the real binary with
  plumbing commands, never a working copy: blobs are hashed into a per-call `GIT_INDEX_FILE`, a tree is
  written from it, and `commit-tree` + `update-ref` move the branch. So two sites cannot collide over a
  checkout and the API needs no disk beyond the bare repositories.
- **Arguments, never a shell.** Every git call goes through `ProcessStartInfo.ArgumentList`. A nanoid
  reaches a path, and a path concatenated into a command line is an injection waiting for its first
  customer.
- **A tree coming back from a sandbox is untrusted input.** `PathFor` refuses a site id that is not
  `[A-Za-z0-9-_]{1,40}`; `RejectUnsafePath` refuses an absolute path, a `..` or `.` segment, a `.git` component
  in any case, a doubled slash, a backslash and a control character; and the file-count and byte limits are
  configured. It was assembled by a language model on a machine we do not own.
  **git refuses every one of those paths itself**, and the rule is written here anyway for two reasons: a
  protection that exists only because of what the tool we shell out to happens to do disappears in a refactor
  nobody connects to it, and git's own sentence — `fatal: git update-index: --cacheinfo cannot add ../evil.txt`
  — reaches the chat as a failed turn nobody can act on. A *leading dot* is deliberately fine: `.gitignore` and
  `.env.example` are ordinary files in a Next.js project.
- **A new site knows its own name.** Somebody types "Ridgeway Cycles" on the screen that creates a site, and
  until `SiteIdentity` that name reached the dashboard and nothing else: the header, the footer and every
  page's title said "Your site" until an agent turn changed them. It writes the name into `src/site.ts` as
  `siteName`, which the whole project imports, and into `content/brand.md` as the first **confirmed** fact, so
  the first turn starts knowing it rather than asking. **And a rename can write it again**, which it could
  not: renaming "My Shop" to "Ridgeway Cycles" changed one row, so the dashboard said one thing and the
  website — header, footer, every page's title, the card a shared link draws — said the other, with nothing
  on screen admitting it. Found by renaming a site and reading its published home page. The settings screen
  offers it ticked, because renaming a business and wanting the business's site to say so is one thought;
  unticking is for a label over pages the agent has since reworded. It is a version like everything else that
  changes a site — `SiteIdentity`'s rewrite applied to the head commit, committed through `CommitSiteVersion`
  — and safe by construction rather than by care: `TemplateFile` leaves a file that has moved or been reshaped
  exactly as it is, and a tree identical to its parent commits nothing. The **web address** still does not
  follow the name, and that is a different case rather than an inconsistency: an address may already be
  published, linked to, indexed, and printed on a van. **It is customer
  input reaching a source file that gets compiled**, so it is escaped for a single-quoted TypeScript literal —
  "Joe's Garage" is an ordinary business name and an unescaped apostrophe is a site that does not build, in a
  sandbox, reported as a chat message nobody can act on. `TemplateFile.Rewritten` is the one rule both this
  and `SiteLooks` share: a file that has moved or been reshaped is left exactly as it is.
- **One template, five looks.** `SiteLooks` rewrites three numbers in `src/app/look.css` — an OKLCH hue, a
  chroma and a card radius — in the template's tree on the way to a site's first commit, at random. **And the
  same colour twice more**, in the two places the stylesheet cannot reach. `src/app/icon.svg` is a file the
  browser fetches, so it has no access to the page's CSS variables; `src/app/opengraph-image.tsx` is drawn at
  build time by Satori, which has no CSS engine at all and refuses `oklch()` outright — so that one carries a
  **hex**, produced by `Oklch.ToHex`, whose test asserts against colours sampled out of a real browser. The
  card's rewrite matches `const brand = '#…'` by **name**: matching "a background that is a hex" painted the
  card's white rule brand-coloured on a brand background, which is an element still there and impossible to
  see. Found by publishing a terracotta site and looking at its card. The whole
  palette including the neutrals is derived from those, so a site's character changes without a line of its
  markup changing, and "make it green" stays an edit to one small file. Random rather than asked for: the
  first screen of this product asks for a name and nothing else, and a palette picker before anybody has
  described their business is a decision about something that does not exist yet. A template whose file has
  moved or been reshaped is left exactly as it is — a site with the default look is a small disappointment and
  a site whose stylesheet we corrupted is a broken website. Four full templates were the alternative, and they
  would be four copies of every convention to keep in step the next time a rule changes.
- **`templates/next-site` is what a new site starts as**, and it is a normal project somebody can open and
  `npm run build`. It carries what a published business site owes a search engine: `robots.ts`, a `sitemap.ts`,
  a `not-found.tsx` that is a page of the site rather than the host's default, canonical and Open Graph
  metadata, a generated **share card** and an **`icon.svg`**. Three of those are worth their own sentence.
  The sitemap **reads the folders under `src/app`** instead of listing pages by hand: the rule it replaced —
  "add a line when you add a page" — is exactly the kind a busy turn skips, and skipping it is invisible,
  because the page works, the site looks finished, and search engines find it late or never. The share card
  (`opengraph-image.tsx`) exists because a link to a small business's site pasted into a message was a grey
  rectangle with a URL under it, which is how a real website ends up looking like a broken link. And the icon
  is there because Next's file convention works under `output: 'export'` and without one every published Webly
  site showed the browser's blank-page icon in the tab — the first thing a visitor sees of a business, and the
  last thing anybody thinks to check. All of those need an absolute URL, and a static export has no server to ask for one later — so the
  publish passes the site's **address** as `NEXT_PUBLIC_SITE_URL` (`src/site.ts` reads it) and the build is the
  moment it is known. The address rather than the deployment's own URL, because a canonical that changed with
  every publish is not a canonical. Two of its files are product rather than scaffolding: **`AGENTS.md`** carries the
  standing rules (never invent a fact, never write a testimonial nobody gave you, keep the build working,
  stay in the stack) and **`content/brand.md`** is where the agent records facts it learns, because its
  session does not outlive the workspace. `CLAUDE.md` in the template just points at `AGENTS.md`, so both
  CLIs read one file.
- **A photograph in the Code tab is shown, not described.** A file that is not text gets "there is nothing
  to show" — which is right for a font and wrong for one of the owner's own pictures, when a route serving
  its bytes already exists for the settings screen's thumbnails. Only under `public/images/`, which is where
  uploads go and the only place that route reads from, and only when the file really is not text, so an
  `.svg` still shows as the source it is.
- **The agent may read the source and so may the customer** — `GET /api/sites/{nanoid}/files` and
  `/file?path=`, shown by the editor's Code tab — because "you never have to touch the code" is not "you are
  not allowed to see it". The path is a query parameter, not a route catch-all, so `src/app/page.tsx` cannot be
  ambiguous against the routes beside it. **Read-only, deliberately:** a save button there would be a second
  way for a site to change and so a second definition of what a version is. If hand editing lands, it lands
  through `CommitSiteVersion` like everything else.
- **And they may take it away.** `GET /api/sites/{nanoid}/export` answers with a **git bundle** of the whole
  repository — `git clone site.bundle` is a working project with every version and every commit message. A
  bundle rather than a zip because a zip is a snapshot; the point is leaving with the history, which is what
  makes "it is your code" checkable rather than a slogan. There is deliberately no import: a commit Webly never
  validated could break the build or the next turn.

### Versions

**A version is a commit.** `Site.HeadVersionId` is what the editor is changing; `Site.PublishedVersionId` is
what the world sees. The `SiteVersion` row is the *index* into the repository — commit sha, summary, origin,
changed-file count, who and which message — and duplicates nothing git already stores.

- **`CommitSiteVersion` is the only writer.** A second write path would be a second definition of history.
- **A turn that changed nothing commits nothing.** The tree is compared against the parent's and an
  identical one returns null. A history of "no changes" entries is not a history.
- **The agent never sees git.** The sandbox gets a working tree; Webly commits what comes back, with the
  person as author. So the agent cannot rewrite history and needs no credential that could.
- **`HasUnpublishedChanges` is computed** from the two pointers. A boolean beside them is a third fact that
  can disagree with both.
- **Restore writes the old tree forward** as a new commit, never repoints the branch backwards: the
  intervening history stays reachable, the restore itself appears in the history, and undoing an undo is the
  same operation again. It also **re-seeds the warm workspace** (`ISiteWorkspaceRegistry.ReseedAsync`) rather than
  releasing it, so the dev server recompiles the restored files and the preview shows them by itself. Releasing was
  the first shape and it failed in the most visible way there is: the preview somebody was looking at when they
  pressed "bring this back" became "your preview is not running, send a message to wake it up".
- **A version links to the chat message that produced it** and the message links back, which is what makes
  the history read as the conversation that caused it — and the editor says so: every reply that committed
  something carries a "see what changed" link into `history?version={nanoid}`. The link existed in the database
  and in `ChatMessageResponse.ProducedVersionNanoid` and nowhere in between, because the messages query had no
  `Include` and the field came back null on every message.
- **`(SiteId, CommitSha)` is unique**, which is what stops a second row claiming the same commit and making
  the history show a change that is not one.
- **The history shows a diff, not a preview.** There is one dev server per site and it runs the working
  tree, so there is nothing to point an iframe at for a commit from last Tuesday. The diff is **grouped by
  file** — `models/unified-diff.ts` parses it, sixty hand-written lines rather than a dependency, with a spec
  against output the real command produced — and each file is a native `<details>`, open when the version
  touched three files or fewer. A turn changes one file and should not need a click; a site's first commit is
  eighteen, and opening all of them buries the list of what they are under a few thousand lines of template.

### Deferred foreign keys

`Site` points at two of its own versions, a version at the message that produced it, a deployment at the
version it published. All of those are `NoAction` in the model **and must be made `DEFERRABLE INITIALLY
DEFERRED` by raw SQL in the migration** — EF cannot express it.

They have to refuse a version somebody still points at, and they do. But deleting an account cascades
sites → versions, conversations → messages and deployments away in one statement, and Postgres checks a
constraint after each triggered action rather than after all of them: undeferred, whichever cascade ran
second loses, and an account that had ever published could not be deleted.
`WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them` is the canary. Do not
change these behaviours without running it. `DeleteSite` also clears the site's own pointers before
removing the row, so the ordinary delete does not depend on the deferral at all.

### The agent, the sandbox and the run substrate

`docs/agent-plan.md` is the full write-up. What matters when touching it:

- **The agent is another product's CLI**, run inside the sandbox: `claude -p --output-format stream-json`,
  or `opencode run`. `ICodingAgent` has two implementations and `CodingAgentRegistry` picks one. Do not
  reach for a model SDK — §1 of the plan is why, and it is the decision most likely to be re-derived
  wrongly.
- **The permission model is where it runs, not a tool list.** The sandbox has the site's files, node, git,
  the CLIs and one model key. No connection string, no Vercel token, no session cookie, no git remote.
  Publishing, domains, billing and deleting a site are not tools it lacks — they are unreachable from
  there. **And not the sandbox agent's own token either**: every command used to inherit the whole environment,
  which handed the workload the bearer token Webly authenticates to *that* service with. Nothing about the site
  was at risk — the agent has a shell in there — but speaking as the control plane on its own machine is one
  step it should not have towards speaking as the control plane anywhere else. `tools/e2e/run.mjs` asserts a
  command cannot see it.
- **The files Webly owns inside a site are refreshed before a turn, each as its own version.** It ships inside each site's repository —
  which is what makes a site self-contained and what makes its rules as old as the site. The forms rule
  ("never write a server for one; this site is a static export") would have reached no existing site at all,
  and a stale rule is the agent confidently doing the thing the rule exists to prevent. `SyncSiteInstructions`
  compares the site's copy with the template's before the workspace is acquired and commits the difference as
  **"Updated the editing instructions"**, origin `Template`. Its own version rather than folded into the
  turn's, because a person's version has to say what they asked for — the same lesson as the `npm install`
  that put eighty-four lines of lockfile into somebody's headline change. Two `cat-file`s decide the usual
  case, so the whole tree is only read when something really changed; and `CommitSiteVersion` now updates
  `Site.HeadVersion` as well as the id, or the workspace would be seeded with the tree that commit replaced.
  **`next.config.ts` travels the same way**, with its own summary ("Updated this site's build settings"). It
  is the build contract, and what makes rewriting it safe is that the template's own rules put it on the list
  the agent must not touch — so the only copy that can exist is ours. Without it a fix there reaches new sites
  only, for ever: turning off Next's floating dev badge, which `next dev` drew on top of every customer's
  preview of their own website, would have left every existing site with it.
- **`AGENTS.md` in the site's repository is the other half of it.** A tool list cannot say "never write a
  testimonial nobody gave you", and a plausible invention published on a real business's website is the
  worst thing this product can do. The agent asks in its reply and the turn ends; the answer is the
  person's next message. (The old blocking `AskUser` tool is gone — a CLI in a sandbox cannot wait on this
  app. The MCP bridge that would bring it back is in the plan, §1.3.)
- **A turn is one version.** `AgentTurnService` runs the agent, then commits once from the tree the sandbox
  hands back: atomic, readable in the history, and free to cancel.
- **Two turns on one site at the same moment is a real case**, and it broke three things, each found by
  starting two turns a millisecond apart against the running app. Both turns see no open thread and both
  insert one — the partial unique index refuses the loser, so `FindOrCreateActiveAsync` returns the thread the
  winner just made. Both compute the same next message sequence — `AppendMessageAsync` holds a Postgres
  **advisory lock** on the conversation across the read and the insert, rather than retrying against the
  index, because retries degrade exactly when contention rises. And both build a tree from the same parent —
  `update-ref` is given the expected old value, so git refuses the second atomically instead of moving the
  branch to a commit that does not contain the first's work and leaving that turn's version row naming a
  commit nobody can reach. Alongside those: the workspace seeds from the **branch** rather than from the site
  entity a turn read minutes ago. Two simultaneous turns now both succeed, in order.
- **And that guard had never refused anything**, which is a different lesson from the one above: it was given the
  wrong expected value, so it could not. A turn re-read its site row just before committing and handed git *that*
  head — equal to the branch by construction — while the tree it was writing had come out of a sandbox seeded
  minutes earlier. So anything that committed in between was quietly overwritten: upload a photograph mid-turn
  and the turn's commit put the older tree back, deleting the file, with "Added probe.png" still in the history
  directly beneath it. Driven in the running app; nothing on screen admitted a version had been undone.
  `CommitSiteVersion` takes an optional **`treeBaseSha`** and `AgentTurnService` passes the workspace's own
  commit; every other caller passes nothing and means the head it just read, which is true of all of them — an
  upload, a delete, a rename, a restore and the owned-file sync each build their tree from the head in the same
  breath. That distinction is the whole fix, and `SiteVersionConflictTests` pins both halves of it.
  **The clean failure then needed an answer**: the first real refusal reached the upload endpoint as an unhandled
  exception with the repository store's stack trace in the body. Every operation that changes a site can lose
  this race, so the mapping lives in `ExceptionHandlingMiddleware` rather than in six error enums — 409,
  `site_changed`, and `RepositoryConflictException`'s own sentence, which was written to be read by whoever
  pressed the button. The upload deliberately does not retry, though it safely could; the window is a
  millisecond and the sentence names the remedy.
- **And two turns on a *cold* site never queued at all**, which is the same defect one layer up. Starting a
  sandbox is seconds of awaiting, so "there is no workspace for this site" and "here is one" were far enough
  apart for the second turn to read the same absence: both started a sandbox, the second overwrote the first in
  the dictionary, and neither ever met the semaphore that exists to make them take turns — one paid machine left
  running with nothing pointing at it, and the loser refused. Invisible until now because the warm path, which is
  every turn after the first, queues correctly. `SiteWorkspaceRegistry` holds a **per-site start lock** across
  find-or-start, and `AcquireAsync` resolves the branch **again once the lease is held**: the resolve before the
  queue is by definition stale for whoever waited, and re-seeding to it would copy the winner's replaced tree
  back into the sandbox. Driven both ways against a freshly restarted backend — two agents for one site and a
  failed turn before, one agent and two commits in order after.
- **Build errors are surfaced, not swallowed.** After a turn the dev server's log is read from an offset
  recorded before the turn started, and a `BuildFailed` event puts the compiler's own words on screen —
  because the person's next message is what fixes it. `CompilerOutput` owns both halves and both were
  wrong until they were run against a real dev server: the markers it looks for begin with `⨯ ./`, which is
  what a syntax error produces, and the three obvious phrases do not appear for one; and what reaches the chat
  has the terminal's ANSI codes, SWC's seventeen-frame Rust backtrace, the workspace's absolute path and every
  line before the complaint stripped out of it — a block that opens with npm's banner and `Ready in 1269ms` buries
  the one line somebody can act on. The publish path does not depend on any of this: `vercel build` runs there and
  a failure blocks the deployment.
- **The check is only as good as the request that provokes it, and that is where it was broken.** `next dev`
  compiles on demand, so the turn asks for a page before it reads the log — and on a cold workspace that request
  arrived before anything was listening, the agent answered its own 502, nothing ever compiled, and the empty log
  read as a healthy site. A site with a syntax error in its home page reported a clean turn and served a 500 in the
  preview pane. `TouchPreviewAsync` now retries for fifteen seconds while the sandbox says the dev server is not
  answering yet, and only that answer is retried: a 500 from the dev server is a compiled page that threw, which is
  the thing being looked for. Ask for the mock agent to "break the build" to see the whole path without a key.
- **A dev server can die on its own, and something has to notice.** It is a child process in the sandbox — the
  out-of-memory killer takes it first on a machine running several — and nothing did: the workspace stayed warm,
  `workspaceReady` stayed true, and the preview answered 502 for the rest of the session while every turn reported
  success. Three changes, each in the layer that knows: the sandbox agent keeps the log **past the child's exit**
  (it used to hang off the child object, so a dev server that died took the reason with it) and reports `running`
  beside it; `SiteWorkspaceRegistry.EnsureDevServerAsync` starts a new one on the next turn, which is the cheapest
  place to ask; and the preview answers "your site's preview has stopped" without a retry loop instead of "your
  site is compiling" with one, because a page that reloads for ever in front of a dead dev server is how somebody
  concludes the product is broken.
- **One warm workspace per site**, shared by the chat and the preview. `SiteWorkspaceRegistry` leases it
  with a semaphore so two turns queue rather than interleave, re-seeds it when the head has moved under it
  (clearing the agent's session id, because a resumed session would remember a different tree), and
  `WorkspaceReaper` closes it when idle — a warm sandbox bills by the second.
- **And closes all of them when the process stops**, which nothing did: stopping the app left one sandbox
  running per open site. Found by counting processes — fourteen local sandbox agents still listening, the
  oldest five hours old, after six restarts of the backend that started them, with their workspaces already
  deleted from under them by the next start's sweep. `WorkspaceReaper.StopAsync` is where it belongs, because
  that class already owns "a warm sandbox costs money per second". `ReleaseAllAsync` is deliberately not
  `ReleaseAsync` in a loop: that one waits up to thirty seconds for a running turn, and at shutdown the turn
  dies with the process either way — while a shutdown that overruns is abandoned by the host, and what it
  would abandon is the only code that stops a sandbox. The other half is `LocalSandboxProvider`, which now
  records **the agent's own pid** in a `.agentpid` beside the workspace so the first-use sweep can kill what a
  `kill -9` left: the dev server inside it had been recorded and swept since sixteen orphans wedged a machine,
  and the process that spawned it was recorded nowhere.
- **The preview is the site's own `next dev`, proxied.** `PreviewController` forwards with YARP's
  `IHttpForwarder`: ownership re-checked per request, WebSockets forwarded (hot reload is one), our cookie
  stripped on the way out. A cold site answers 503 with a sentence rather than starting a workspace on a GET,
  because tens of seconds of a hanging iframe looks broken.
- **The preview frame is sandboxed, and being same-origin was the bug rather than the safeguard.** It was
  written down here that same-origin is what made the preview safe. It is what made it dangerous: the page in
  that frame is the customer's own website, written by a coding agent, and running on Webly's origin it could
  call `/api/sites`, read the site's inbound messages or `DELETE /api/auth/account` with the owner's session,
  indistinguishably from the app doing it. The tokens are `HttpOnly`, so it could not read them — it did not
  need to. Confirmed by putting a `fetch('/api/sites')` in a site's home page and watching the preview print
  the owner's sites back; refused, from the same page, once the frame was sandboxed.
  `sandbox="allow-scripts allow-forms allow-popups"` — never `allow-same-origin`, which undoes all of it in
  one word. What that costs is the session cookie: an opaque origin is cross-site to everything, so
  `PreviewAccess` mints a **signed, per-site, path-scoped `SameSite=None` cookie** and the preview route is the
  one `[AllowAnonymous]` under a site. It is not an authorization — the same `FindForOwnerLightAsync` check
  runs on the user the token names.
  **A font is the one exception**, and it has to be: a font is always fetched with CORS in credentials mode
  `same-origin`, and nothing is same-origin to an opaque origin, so no cookie can ever ride with it. Scripts,
  styles, images and the hot-reload socket are fetched in modes that do send one, which is why only the
  typeface broke — the preview rendered in a fallback while the published site rendered in Inter. Font files
  under `_next/static/media/` are served on the nanoid alone with `Access-Control-Allow-Origin: *`: a copy of a
  public typeface, to somebody who already knows an unguessable id.
  **The proper fix is a separate origin** (`{id}.preview.webly.site`), where the frame's own origin serves its
  own assets and none of this arises. It needs a wildcard record and a certificate.
- **The dev server is told where it is, and that is load-bearing.** Next.js writes absolute URLs for its
  stylesheets, its chunks and its hot-reload socket, so a dev server that thinks it is at `/` asks the browser
  for `/_next/...` at the root of Webly's origin — which is Webly's app. `SiteWorkspaceRegistry` passes
  `/api/sites/{nanoid}/preview` to `StartDevServerAsync`; the sandbox agent sets `WEBLY_PREVIEW_BASE` and
  rewrites everything under its own `/preview` onto it; the site template turns that variable into Next's
  `basePath`. Three files agree on one string, and the day they disagree the preview renders as unstyled text
  — which is exactly how it shipped, because the test read the HTML and the HTML was perfect.
- **`next dev` catches less than it looks like, so the turn also runs the site's own typecheck.** It compiles
  with SWC, which strips types rather than checking them: a type error never reaches its log — the page serves a
  200, which the harness asserts — and neither does an *unused* broken import, which is elided as
  possibly-a-type before anything resolves it. So after the log check `AgentTurnService` runs
  `npm run typecheck` in the workspace and reports what it says as the same `BuildFailed` event. Three
  conditions, each with a reason: only when the turn **committed** something (`tsc` costs seconds and a question
  cannot have broken anything), only when the dev server said **nothing** (a file the compiler cannot parse is
  one `tsc` cannot check, and two blocks about one mistake reads as two mistakes), and only when the output
  **names a type error** — a missing `node_modules` or an absent script also exits non-zero, and telling a
  customer their site is broken when our sandbox is what is broken is worse than silence. `AGENTS.md` still asks
  the agent to run it, and that is not redundant: its run is what fixes the error before finishing, ours is what
  makes the report true when it did not. Ask the mock agent to "break the types" to see the path without a key.
- **`tsc --incremental` writes a cache, and it must not reach a commit.** It defaults to sitting beside
  `tsconfig.json`, so every turn that ran the typecheck — which `AGENTS.md` has asked for all along — would have
  committed a machine-readable dump of the project into the customer's history and shown it in their diff. Two
  things stop it: `tsBuildInfoFile` points it into `.next`, which the workspace keeps across re-seeds and which
  is why a warm turn only pays about a second for the check; and `*.tsbuildinfo` is in the sandbox agent's
  `IGNORED`, which is what actually decides what a commit can contain. Belt and braces deliberately — that list
  must not depend on one setting in one file the agent is asked not to edit.
- **`DockerSandboxProvider` had two defects that only running it could show.** It read the child's stdout to
  the end and *then* its stderr, which is a deadlock as soon as the other pipe fills — and `docker run` is the
  command that fills it, because the first run on any machine pulls the image and a pull writes megabytes of
  progress to stderr. `GitSiteRepositoryStore` has always drained both at once, because it has always been
  run. And it had no first-use sweep, so every restart of the API left one container per open site running
  with a `next dev` inside it, for ever; `LocalSandboxProvider` has had that sweep since sixteen orphaned dev
  servers wedged a machine.
- **`next/font/google` needs egress at build time**, which the sandbox is exactly the place not to have. Seen
  in a container with no route out: `next dev` logs "Failed to download Inter from Google Fonts. Using
  fallback font instead" and carries on, so the site builds and publishes in a font nobody chose. The
  template's comment — "self-hosted at build time by next/font, so a published page makes no request to a font
  CDN" — is true of the *published page* and not of the build. Either the sandbox gets that one host, or the
  template moves to `next/font/local` with the file committed, which is the stronger answer and needs the
  `.woff2` in the repository.
- **The sandbox contract is ours** (`tools/sandbox-agent`, zero dependencies, baked into the image). A
  provider's job is "start this image, give me a URL"; files, exec and preview all go through one HTTP
  contract we can test. That is what makes E2B → Fly → Daytona a class nobody else has to know about.
- **Run lifetime lives in `RunRegistry`, not on an `HttpContext`.** A closed tab must not cancel a turn
  halfway through rewriting somebody's home page. Only an explicit `Cancel` stops a run — which is why
  `OrphanRunReaper` is mandatory rather than nice to have (two minutes unwatched, thirty minutes absolute,
  five minutes to keep a finished handle for a late reconnect).
- **Events are appended to the run's log first, then published.** Publishing first loses anything emitted
  between a late subscriber's replay and its group join, which is the reconnect case. Every envelope carries
  a `Seq`, which is both the resume point and the duplicate filter, and that is what makes the hub's
  "join the group, *then* replay" order safe.
- **Exactly one terminal event per run, emitted only by `ChatRunLauncher`.** A client that never sees one
  waits forever, which is indistinguishable from the product being broken. **It carries what the app has to
  say about the ending**, in `Error` when the turn failed and in `Detail` when somebody stopped it — both the
  same sentence `AgentTurnService` writes into the thread, so the live screen and a reload agree. The failure
  half had that from the start and the stop half did not, so pressing Stop ended with the spinner gone and a
  status line frozen mid-sentence: the "Stopped. Nothing was changed." note was in the database and only
  appeared if the person reloaded the page.
- **The log is in memory, not a table.** A chat run cannot outlive its process and the log exists only to
  serve a reconnect. `IRunEventSink` is the seam if that changes.
- **`RunWriter` flushes before any non-text event**, or a file chip arrives before the sentence that
  introduced it.
- **`RunKind.Deploy` is the durable one**: its state is the `Deployment` row and its run id *is* the
  deployment's nanoid, so a page that reloads mid-publish can re-attach to something that outlived the
  process. **`PublishSite` registers the run, not the runner** — a deployment is a row three seconds before it is
  work, and the client's start-then-watch pair means `Subscribe` was asked for a run the runner had not reached
  yet. It answered "that run could not be found" and the editor showed an error over a publish that went on to
  succeed: **every publish through the UI looked like a failure.** `RunRegistry.Register` hands back an existing
  unfinished handle rather than replacing it, so the runner asking for the same id joins the one the client is
  already watching. **And the editor re-attaches on load**, which it did not: reloading mid-publish drew a
  Publish button over a publish that was already going, because nothing on load knew one was in flight — the
  durability that `RunKind.Deploy` exists for was described in a comment and used by nobody.
  `SiteDetailResponse.ActiveDeploymentNanoid` is one seek over the partial unique index that already enforces
  one live publish per site.
- **`AgentBudget`, not `[EnableRateLimiting]`.** The rate-limiting middleware only sees HTTP endpoints, so
  an attribute on a hub would look like a fence and be none. It matters more here than in the reference
  project: a turn costs a model call *and* a machine.
- **No agent key means the agent is absent, not broken**: `/api/sites/{nanoid}/chat/status` reports
  `enabled: false`, the client hides the chat, and the tests need no secrets. Same discipline as Google
  sign-in and publishing.

### Publishing

- **Webly builds; the provider serves.** `vercel build` then `vercel deploy --prebuilt`, both in a sandbox,
  rather than pushing source for the provider to build. The build failure is then *ours*: a site that does
  not compile is never published, and the reason is a log we can show rather than a page on somebody else's
  dashboard. It also needs no git remote, which is what lets Webly own the repositories.
- **A publish uses a fresh sandbox**, not the warm editing one, seeded from the version being published and
  `npm ci`'d against its lockfile. A build must not inherit whatever an editing session left behind, and
  that is also what makes a retry mean something.
- **A site publishes one thing at a time, and the index says so.** `PublishSite` read "is one in flight" and
  then inserted, which two clicks a millisecond apart walk straight through: both ran, which is two sandboxes,
  two `npm ci`s, two real builds and two "your site is live" emails for one press of one button. A partial
  unique index on `(SiteId)` over the live statuses ends it, `PublishSite` answers the loser with the
  deployment that is really running — so the editor watches the right run — and the supersede path saves its
  cancellation before inserting, because a partial index is checked per statement and the order of two changes
  inside one `SaveChanges` is EF's business rather than ours.
- **`Site.PublishedVersionId` moves in exactly one place**, in `DeploymentJobRunner` after the provider
  reports success. A failed publish leaves the previous version live, and the email says so in its first
  line. The build log goes to `Deployment.ErrorDetail`, which the settings screen shows folded away — through
  `CompilerOutput.Readable` and **then** trimmed to a tail, in that order: it reached the screen raw until a failed
  publish was watched in a browser, and trimming first sliced the middle out of SWC's backtrace, which left the
  frames in and the marker that identifies them out.
- **Deleting a site takes it off the internet, which it did not.** `DeleteSite` removed the rows, the
  repository and the custom domains, and left the published site answering at its Webly subdomain and at the
  provider's own URL — the one action somebody takes to get a page down did not get it down. Found by
  deleting a published site in the running app and asking for it again. `IDeploymentTarget.DeleteProjectAsync`
  is the missing half: the filesystem target removes the published directory, the Vercel one deletes the
  project. Best-effort and logged, like the domain detach — a provider outage must not leave somebody unable
  to delete their own site — and idempotent, because a caller cannot tell an already-gone project from a
  refusal and neither can the customer.
- **Next writes its generated metadata images with no extension**, so a static export contains a file called
  `opengraph-image` that is a PNG — and a static file middleware decides the type from the extension and so
  declined to serve it at all: the share card 404ed while the page's own `<meta property="og:image">` pointed
  at it. `NextMetadataImageContentTypeProvider` names the two files Next produces rather than turning on
  `ServeUnknownFileTypes`, which would serve *everything* unrecognised as an image. Development only, like the
  rest of the filesystem target; a real host reads the build's own manifest.
  One local-only oddity to not go hunting: because that target also sets a base path, the `og:image` URL in a
  locally published page is the site's address with `/published/{nanoid}` in the middle of it. Nothing resolves
  that, and nothing needs to — on a provider there is no base path, and the canonical link beside it is right
  in both.
- **A mistyped address on a published site gets that site's 404 page**, which took a middleware after the
  static files: without it the request fell through to `MapReverseProxy`, so somebody who typed one character
  wrong on a customer's shop website landed on **Webly's dashboard**, or on Webly's login page if they were
  not signed in. The template has had a `404.html` in its export the whole time. Development only, like the
  rest of the filesystem target — a real host serves the export's own 404. Nothing under `/published/` falls
  through to the app at all now: an id that is not there is a 404 rather than Webly's login page, which is
  what a **deleted** site has to answer.
- **A locally published site is served under a path, and the build has to be told.** Next.js writes absolute
  URLs for its stylesheets and chunks, so `FileSystemDeploymentTarget` passes `WEBLY_PREVIEW_BASE` =
  `/published/{nanoid}` — without it every published site asked for `/_next/…` at the root of Webly's own
  origin and rendered as unstyled HTML. That was true from the first publish and nobody saw it, because every
  check until now read the HTML and the HTML was perfect; it took opening one in a browser. The same mistake,
  in the same product, for the same reason as the preview's. The Vercel target passes nothing, because a site
  on its own domain is served from the root. The variable is named for the preview because the template and
  the sandbox agent have called it that since it was only the preview's, and renaming it would break the
  preview of every site created before the rename — each one carries its own `next.config.ts`.
- **The CLIs are pinned in the image**, and the deployment target calls `vercel` rather than `npx vercel`:
  a publish that works on Tuesday and not on Wednesday, with no diff to blame, is the failure that costs
  the most to diagnose.
- **One platform-owned Vercel account.** "Self-service" cannot begin with "create a Vercel account". The
  bring-your-own upgrade is a nullable token on `Site` and nothing else — see `docs/deploy-plan.md` §3.
- **A site's address is arranged, not assumed.** A provider serves a hostname only once that hostname has been
  attached to the project, so `{slug}.{BaseDomain}` — the address every screen prints — resolves nowhere until
  something attaches it. `DeploymentJobRunner.EnsureAddressAsync` does, after the first successful publish (the
  earliest moment there is a project to attach it to), stamps `Site.AddressReadyAt` when the provider confirms it,
  and tries again on the next publish while it has not: a retry loop with no timer and no state machine. It is
  best-effort by design — a publish that worked must not be reported as failed because a domain call did.
- **So a site has three URLs, and `SiteMapper` is the one place all of them are decided.** `WeblyUrl` is the
  subdomain the platform gives it, which never changes — the domains screen read the address for that and so
  relabelled a customer's own domain as ours the moment one was promoted. `Url` is its **address**, what
  the settings screen prints and somebody reads out over the phone — its verified primary domain, or `WeblyUrl`; `LiveUrl` is where the published version can
  **actually be opened** — the address once `AddressReadyAt` says the provider serves it, the deployment's own
  provider URL until then, and null for a site nobody has published. The header, the site cards and the publish
  email all link `LiveUrl`, because a link that 404s immediately after "published" reads as the publish having
  lied. In development `AddressReadyAt` stays null for ever, which is honest: nothing local resolves a subdomain
  of the production zone, and the filesystem target's `/published/{nanoid}/` is where the site really is.
- **A `Domain` row mirrors the provider**, which owns verification and the certificate. A local "verified"
  flag it disagrees with is a site that is live according to us and 404 according to the internet. Checking
  is a button, never a timer. Exactly one primary hostname per site, by partial unique index; only a
  verified domain may be promoted.
- **And the main address can be disconnected**, which reverses an earlier rule. Removing it used to be
  refused with "make another domain the main one first" — impossible advice for somebody with one domain,
  which is everybody who has ever connected one, and unreachable advice as well, because the client hid the
  button on that row. A domain somebody added could never be taken away. Nothing is left pointing at
  nothing: a site always has its Webly subdomain, and `SiteMapper.UrlFor` falls back to it the moment there
  is no verified primary. The client asks first and names the address it goes back to.
- **The DNS record is shown in full, and each half has its own copy button.** It used to truncate both the
  name and the value, and only the value could be copied — on the one screen whose own class comment says
  its whole job is the copying, into a registrar in another tab. Pressing copy also does something visible
  now; before, the clipboard changed and the screen did not.
- **`VercelDeploymentTarget` has never run**, in either half — the REST calls or the CLI in the sandbox.
  Treat its endpoints, payloads and output parsing as the plan; `docs/deploy-plan.md` §6 says what to
  reconcile first.

### Images

**An image is a version, not an asset.** There is no blob store and no asset table: an upload commits the
files into the site's repository under `public/images/`, which is where Next.js serves static files from, and
`UploadSiteImages` goes through `CommitSiteVersion` like everything else that changes a site.

- Four things follow from that, and together they are the argument for it. The published site serves its own
  photographs **from its own domain**, so nothing of Webly's has to exist for them to load. They travel with
  the **git bundle**, so a customer who leaves takes their pictures as well as their code. They are in the
  **history**, so a restore takes them back and a version cannot reference one that is missing. And
  `CommitSiteVersion` stays the **only writer** of history.
- The cost is binary in git, which is what the caps are for: 5 MB an image, ten an upload, and the tree cap
  underneath. The browser shrinks first (`client/src/app/models/image-file.ts`, a canvas re-encode to 2000 px)
  — which is also where the resizing has to happen, because a static export has no image optimizer behind it
  (`next.config.ts` turns Next's off, since it needs a running server) so **what is uploaded is what every
  visitor downloads**. The server re-checks; the browser is an affordance, not an authority.
- **The extension comes from the bytes, never from the name.** `ImageKind.Of` sniffs the magic numbers and the
  file is stored with that extension, because the extension is the only thing a static host uses to decide how
  to serve a file and the name comes from whoever uploaded it. A PNG called `.jpg` is stored as a `.png`; an
  HTML document called `.jpg` is refused. **SVG is deliberately not on the list** — it can carry script, and the
  preview serves it from Webly's own origin, where the person's session is.
- **A name collision gets a number, never an overwrite.** Two phones both calling it `image.jpg` is a normal
  afternoon, and replacing the first would remove it from a page already using it.
- **The upload lives in the chat**, not on a screen of its own: nobody wants to upload a picture, they want it
  *on* a page, and the sentence saying which page is the next thing they type. So the paths land in the
  composer with the caret after them. The upload re-seeds the warm workspace for the same reason a restore
  does — the next thing that happens is an agent turn that has to be able to see the file.
- **Tidying up lives in Settings**, because it happens at a different moment: a week later, looking for the
  wrong photograph. Thumbnails, since a list of file names is not how anybody knows which picture is which —
  which needs a route for the bytes, and that route is **the editor's, never the site's**. A published page
  serves its own photographs from its own domain, and one that fetched them through Webly would stop working
  for a visitor who is not signed in; what this is for is the picture in a site nobody has published yet, or
  whose sandbox is asleep. Its content type comes from the bytes, never from the name.
- **A delete is refused while a page still uses it**, and the answer names the pages — a delete that leaves an
  `<img>` pointing at nothing is a broken page produced by a button that said nothing about it.
  `ImageReferences` is the rule, with its own test, because the first version searched every non-image file
  and refused immediately: `AGENTS.md` uses `/images/shopfront.jpg` as its example, so every site would have
  refused to delete a photograph with that name. Prose about a site is not a page of it. The delete is a
  version, so the picture is still in the history — deleting the wrong one is undoable, like everything else.

### Forms

A published site is a **static export**, so it has no server of its own — which is the whole reason this
exists and the answer to the question `MASTER_PLAN.md` P4 left open. A contact form posts to **Webly**.

- **`POST /api/public/forms/{siteNanoid}` is the product's only inbound path**, and the only row a stranger can
  create. `PublicFormController` is `[AllowAnonymous]`, takes an ordinary form body rather than JSON — so it
  needs no CORS entry and works with JavaScript off — and is `[ApiExplorerSettings(IgnoreApi = true)]`, because
  the Angular client is the one caller it is not for.
- **It answers 303 back where the visitor came from**, resolving the form's relative `_next` against the
  `Referer`. A scheme, a leading slash or a `..` is refused rather than cleaned up. It is not a useful open
  redirect: the target can only be reached by *posting* from a page that already had the visitor, and a link in
  an email cannot produce a POST. With no usable Referer the answer is Webly's own small thank-you page.
- **`ISiteRepository.FindForSubmissionAsync` is the one lookup in the repository with no ownership check.** It
  says so at length, because the rule everywhere else is that a use case starts from `FindForOwnerAsync`. What
  makes it safe is that the operation cannot read anything back: it appends a row and sends one email.
- **Four things stand between it and abuse**, and each covers what the others cannot. The honeypot (`_ignore`)
  catches the bots, and answers success — telling one it was caught is telling it what to change. The size caps
  stop it being a way to write megabytes into somebody's database. The rate limit is per IP **and per site**, so
  an office filling in one shop's form does not spend the budget of everyone behind that address writing to
  every other shop. And `SubmitForm`'s per-site caps are the half a thousand hosts sending one submission each
  cannot get past — a day's cap for our disk, an hour's for the owner's inbox, the second of which stops the
  mail while still recording the messages.
- **The notification's reply-to is the visitor's own address**, guessed from a field whose name contains
  "mail" and then checked for being one. That is the point of the whole email: a lead the owner cannot answer
  from their phone is a lead that waits a day. `EmailMessage.ReplyTo` exists for this one message, and
  `EmailLayout.Escape` exists because this is the one template carrying words Webly did not write.
- **The inbox is three verbs and no more**: read them, say they have been read, throw one away. There is no
  reply — an enquiry is answered from the owner's own email, where the notification already is with the
  visitor's address in its reply-to. **Opening the Messages screen is what marks them read**, because the
  screen shows every message in full with nothing to click through, so having the list in front of you *is*
  having read them; a per-message button would ask somebody to confirm what they just did. It is a `POST`
  beside the `GET` rather than a side effect of it: a `GET` that clears somebody's unread messages is one a
  prefetch or a second tab can spend. `ReadAt` is a timestamp rather than a flag, and the count of the
  nulls — over a partial index, because the usual answer is zero — is the badge on the Messages tab, which
  exists so that somebody who never opens that tab still knows an enquiry is sitting in it. **A delete really
  deletes**: a message is a few hundred bytes, the reason to remove one is that it is spam, and a second list
  nobody empties is somewhere for the same messages to pile up out of sight. The copy in the owner's inbox is
  the backup, and the confirm dialog says so.
- **`Fields` is one jsonb column**, not a table and not a fixed set of columns. The agent writes the form, so
  Webly cannot know whether this site asks for a postcode — and a column per question is a migration per
  question. The editor's Messages tab lists the labels the visitor's own form used.
- **`App:BaseUrl` is where the endpoint's address comes from**, the same setting the links in mail are built
  from: a form action and a verification link are one fact about one host. `AppOptions.FormEndpointFor` is the
  one place it is composed, and both the dev server and the publish read it — a preview whose form posts
  somewhere other than the published page's is a difference found after launch.

### Frontend (`client/`, Angular 22 with SSR)

Zoneless, signals, standalone components, Tailwind 4 + daisyUI 5 (`src/styles.css`). Routes are centralized
in `src/app/app.routes.paths.ts` as `{ path, build() }` pairs — always build URLs through `AppRoutes`, never
with raw strings. Folder layout under `src/app/`: `api/` (generated), `pages/`, `components/`, `services/`,
`guards/`, `interceptors/`, `models/`, `shared/`.

- **`yarn typecheck` does not check templates.** It is `tsc --noEmit`, which never sees an HTML file, so a
  property that does not exist on a component — `routes.sites` where the object has `home` — compiles clean
  and fails in `ng build`. Run `client_build` (or `yarn build`) before believing a template change.
- **`client/src/app/api/` is generated. Never hand-edit it.** After any controller or DTO change run
  `regen_api` (MCP) or `./regen-api.ps1`; it is committed so CI needs no backend. Two things about the
  document it is generated from are load-bearing and were both found by generating it: `AddSwaggerGen` must
  keep `SupportNonNullableReferenceTypes()`, or every `string` in all 29 models comes out as `string | null`
  and the client's types stop meaning anything; and an action returning `IActionResult` describes **no type at
  all**, so it generates as `void` — declare `ActionResult<T>` even where the body is written by hand.
- SSR is on: `src/server.ts` is the Express host, `main.server.ts` the server entry. Anything touching
  `window`/`document` must be guarded (`isPlatformBrowser`/`afterNextRender`), and **the realtime
  connection is browser-only** — an unanswerable HTTP call during prerendering does not error, it hangs
  until the build times out.
- **Auth is resolved in the browser only.** `AuthService.refresh()`/`loadProviders()` no-op on the server and
  `app.routes.server.ts` renders anything auth-dependent with `RenderMode.Client` (login, register and
  forgot-password are prerendered).
- **The sidebar is the site switcher.** A person's sites are the app's top level; everything about one site
  is a tab inside that site's own screen, because those links cannot be built without knowing which site
  they mean. There is no "more" tab — the reference project needs one for the screens its bottom bar cannot
  hold, and Webly has two destinations.
- **`min-w-0` on the editor's child pane is load-bearing**, as it is on the History screen's diff. A flex
  item's default minimum width is its content's, so a long version summary made that pane wider than the room
  beside the chat and an ancestor clipped it: the title was cut off mid-word, the diff ran off the edge, and
  the page's own `scrollWidth` never changed, because clipping is exactly what hides that. It read as a bug in
  the diff renderer. `tools/e2e/screens.mjs` now fails on it.
- **The editor shell owns the site.** One load, one signal (`SiteService.current`), so the header, the chat,
  the preview and whichever child route is showing cannot disagree about what is open. History, domains and
  settings render **in place of the preview**, not over the whole page, so the chat stays available.
- **History and Code are two panes from `lg` up and one at a time below it.** Stacked, the list grew to its
  own height — twenty files is about 1200px — and pushed the thing somebody had just picked off the bottom of
  a phone screen, so choosing a file or a version appeared to do nothing at all. They are master/detail
  below `lg`, with a way back. The pair that goes with that: those screens **open on something** — the home
  page's source, the newest version — because an empty pane beside a list asks a question instead of
  answering one, and that is now conditional on there *being* a pane beside the list (`shared/wide-screen.ts`,
  which is the one place the `1024px` those templates switch on is written in TypeScript). A `?version=` deep
  link from the chat still opens its diff at any width: somebody following that link asked for it.
- **The preview updates itself.** It is an iframe over `/api/sites/{nanoid}/preview/`, which is the site's
  own dev server, so hot reload puts the agent's edits on screen with nothing on this side asking. Bumping
  `previewKey` reloads the frame and is for the case where the whole tree moved — a commit or a restore;
  it is a counter rather than a timestamp so unrelated renders do not make it flicker.
- **A cold preview is a sentence and a button, not a frame.** The pane shows "your preview is asleep" until
  `SiteDetailResponse.workspaceReady` or a `WorkspaceProgress` event says otherwise — and that flag is now asked
  of the sandbox (`ISiteWorkspaceRegistry.IsPreviewReadyAsync`: it answers, and it has a dev server) rather than
  read off the registry's dictionary, which only records what was true when the workspace started. A machine the
  provider has reclaimed and a dev server the out-of-memory killer took both leave the entry looking healthy, and
  the editor put an iframe over a 502. The preview proxy deliberately does *not* make that call — it runs per
  chunk and per socket frame, and its own failure tells it the same thing — an iframe pointed at the 503
  would render the browser's own error page. **"Wake it up" starts the workspace without changing anything**
  (`POST /api/sites/{nanoid}/workspace`, 202, then the client polls `workspaceReady`): before it, the only way to
  see a preview was to send a message, which costs a model call and writes a version — so looking at your own site
  and editing it were the same button.
- **The chat's hard part is disagreement between the page and the run.** A turn is started, then watched, as
  two steps, so a reload re-attaches by the same path; `GetChat` reports `activeRunId` for exactly that;
  `lastSeq` per run is the resume point and the duplicate filter; every watched run is re-subscribed on
  reconnect.
- **Switching sites has to let go of the run**, and it did not. The editor keeps one `SiteChat` alive across
  the switch — an effect reloads it — so a turn running on the site being left went on writing into the new
  site's transcript: its "waking up your site" line appeared under somebody else's history, and the composer
  offered a **Stop button that would have cancelled a turn on a site no longer on screen**. `detach()` ends
  the subscription and unwatches before the new thread loads; the run itself is untouched, which is the point,
  and coming back re-attaches through `activeRunId` exactly as a reload does. The subscription is now *held*
  rather than dropped on the floor, which matters for a second reason: `RealtimeService.watch` hands back the
  **same** stream for a run it is already watching, so subscribing twice is not a second stream, it is every
  event applied twice — which is how returning to a site mid-turn drew the tail of its transcript in
  duplicate. Found by switching between two sites while one was mid-turn.
- **The hub's DTOs are generated too**, which took a document filter. Swagger describes HTTP and a hub is not
  HTTP, so `ng-openapi-gen` deleted anything only the hub used and `realtime.service.ts` re-declared it by hand —
  including `RunEventType`, a union the client switches on, which meant adding a value on the server changed
  nothing here until somebody remembered. `HubContractDocumentFilter` puts those types into the document;
  `realtime.service.ts` re-exports them, because everything that watches a run already imports that service.
- **And it says when the site's name is not the dashboard's.** The two can differ for three reasons now —
  the rename's checkbox unticked, an older version brought back with the name it had, or the agent rewording
  it — and until the screen said so the dashboard quietly disagreed with the website and neither admitted it.
  Read through the same file endpoint the facts panel uses, and parsed in the client, deliberately: the
  comparison is one sentence on one screen, while `SiteDetailResponse` is fetched on every editor load *and
  polled every two seconds while a workspace starts, so a `cat-file` behind it would be sixty process spawns
  for a sentence nobody is looking at. The server stays the authority on writing that constant
  (`SiteIdentity`); this only reads it, and says nothing at all when the line is missing.
- **The settings screen shows what the assistant believes.** `content/brand.md` is the agent's memory — its
  session does not outlive a turn, so what it learns goes in that file and every later turn starts from it —
  which makes it the most consequential text in the site and, until this panel, the only one nobody could
  see. A fact recorded wrongly shapes every page written afterwards, and the person it belongs to had no way
  of knowing. Read from the head commit through the existing file endpoint, folded away, and read-only for
  the Code tab's reason: correcting it is a sentence in the chat, which is how it got there.
- **Enter sends; Shift+Enter writes a second line.** Bound explicitly, because a `<textarea>` does not submit
  its form on Enter and an `<input>` does — so the composer's growing to fit a message silently took away the
  only way to send one with the keyboard, in a product whose entire interface is a box you type a sentence
  into. Nothing caught it: the template type-checks, the page renders, the screenshot is right, and the send
  button beside it works. Angular's `keydown.enter` already excludes the modifiers, so the newline case needs
  no code; `isComposing` does, because Enter accepts an input method's candidate.
- **The composer grows with the message, up to about eight lines.** It was one line and `resize-none`,
  which is right for "make the headline bigger" and wrong for the two cases that are not that: adding
  photographs writes their paths into the box, one per line, and the second one was cut off by the bottom of
  the window. `shared/auto-grow.ts` is bound to the **value** as well as listening for input, because that
  case is a programmatic change and `ngModel` writing into the box fires no `input` event — a directive that
  only listened for typing would have missed the one thing it was written for. The cap matters as much as the
  growth: without one a long paragraph pushes the transcript off the top of the screen.
- **The chat's entries are one shape**, including the ones that are not messages: `activity` chips, a
  single growing `files` entry per turn (a chip per write buries the sentence explaining them), a `waking`
  line that is replaced rather than appended while the workspace starts, and a `build` block carrying the
  compiler's own words.
- **The app's own colours are quiet on purpose.** This app is a frame around somebody else's website, and
  the accents on screen should be the preview's.
- **A field's border is the one place a faint line is not a style choice**, and daisyUI's default is 1.5:1
  against the panel — under WCAG 1.4.11's 3:1 for the boundary of a control. A card can be outlined faintly
  because what is in it says what it is; a field is empty by definition, so its border is the only thing
  saying where to click, and login and register are made of nothing else. `styles.css` repoints
  `--input-color` (the variable daisyUI derives both the border and its inset shadow from) at 59% lightness,
  which is the single value that clears 3:1 on **both** themes — 4.0:1 light, 3.8:1 dark, measured in a
  browser rather than reasoned about; it lives in `@theme` as `--color-control-edge`, because two rules need it
  and the same literal written twice is the same value until somebody changes one. **The checkbox is the second
  of those rules and needed both halves**: unchecked it was a 1.49:1 outline, which is the whole of what a
  checkbox is; checked, unmodified daisyUI draws the same faint outline with a grey mark in it, so on and off
  looked alike on the control that decides whether renaming a business renames its website. The fix is
  `--input-color` again — daisyUI writes `border: … solid var(--input-color, color-mix(…20%…))`, so the faint
  colour is a *fallback* and a `border-color` of our own loses to the shorthand — excluding `:checked` and
  `:focus-visible`, where that same variable is the fill and the outline. Ticked is `checkbox-primary`, which is
  not decoration. Found by the sweep's contrast rule, on a screen it had photographed a dozen times. The `:not(:focus, :focus-within)` on that rule is load-bearing: an
  unlayered declaration beats anything in a cascade layer whatever its specificity, so without it the rule
  would also win against daisyUI's `:focus`, which repoints the same variable — and silently delete the focus
  ring on every field in the app. The site template has the same rule for the same reason, as its own
  `--color-field-edge`, and deliberately its own number: the two are different palettes.
- **`shared/icon.ts` writes its whole `<svg>` into the host element's `innerHTML`**, rather than binding the
  paths inside an `<svg>` in its own template. That looks like the long way round and is the only way that
  prerenders: SSR's DOM has no `innerHTML` setter on an `SVGElement`, so the shorter version throws
  `NotYetImplemented` during the build and the prerendered login, register and forgot-password pages come
  out unrendered — with an exit code of 0. The reference project has the shorter version and the same
  defect.
- `models/problem-details.ts` holds the one `messageOf`. The generated client asks for
  `responseType: 'text'` on endpoints that answer 204, so a failure from one of those hands back the problem
  body as a *string* — reading only `error.title` there silently shows the generic message.

## Deployment

Images on Docker Hub under one repository, told apart by tag — `byc0/margareta:webly_dev_api` and
`webly_dev_frontend` from master, `<branch>_webly_api` / `_webly_frontend` from `release/**`, plus
**`webly_sandbox`**, which has one tag and no dev/prod split because it contains nothing of Webly's.
`.github/workflows/webly-dev-deploy.yaml` builds all three, pushes, and then POSTs to a Portainer stack
webhook; the prod workflow builds and pushes the two app images only, so cutting a release branch and
restarting production stay two decisions. `ci.yml` is the only thing that runs tests, and the deploy
workflows deliberately do not gate on it.

`deploy/portainer-stack.yml` is the stack: Postgres, the API, the SSR client and a Caddy container that
terminates TLS with `caddy reverse-proxy` rather than a Caddyfile, so the whole deployment is one pasteable
file. Only Caddy publishes ports. Its header comment lists every environment variable.

**The sandbox image is not a service in that file**, and that is not an omission: the sandbox provider starts
one per open site, so compose never sees it. `deploy/sandbox/Dockerfile` explains what is in it and why —
node, git, tar, the two agent CLIs and the Vercel CLI all pinned, the sandbox agent, and the site template's
dependencies pre-installed so a cold workspace is a tree copy rather than an `npm install`.

**`webly-repositories` is the volume whose loss cannot be recovered from anywhere else.** Every site's
content and history is a bare git repository on it; the database holds the index of the commits, not the
commits, and a published site on Vercel is built output rather than source. Back it up at least as often as
Postgres. Its path is agreed by three files: `Repositories:Root` in `appsettings.Production.json`, the
`mkdir`/`chown` in `Webly.Api/Dockerfile`, and the mount in the stack.

**Three things about this stack are load-bearing and easy to break:**

- **`webly-client` is a name three files agree on.** Angular 22's SSR engine answers **400 to any Host it
  does not recognise**, and YARP rewrites Host to the destination — so what the SSR server sees is the
  compose service name, never the public domain. `NG_ALLOWED_HOSTS` in `client/Dockerfile` lists it (plus
  `127.0.0.1`, without which the container's own healthcheck reports it unhealthy forever while it serves
  pages perfectly), and `appsettings.Production.json` points the YARP cluster at it.
- **The route turns `X-Forwarded` off toward the SSR server.** Left on, `@angular/ssr` logs three warnings
  per request. Turning that trust *on* instead is worse: YARP **appends** to an inbound `X-Forwarded-Host`
  and the engine reads the first value, so a visitor could choose the host the server renders for.
- **The API reads `X-Forwarded-*` and must.** `UseForwardedHeaders` runs in non-Development only. Without it
  `UseHttpsRedirection` sees plain HTTP from Caddy, and — the quieter bug — the `mail` rate limiter
  partitions on `RemoteIpAddress`, which would be Caddy for every visitor alike, collapsing a per-IP budget
  into one global budget that the first password reset spends.

Smaller notes: the API image copies a static `busybox` purely so the healthcheck can reach `/health` (the
runtime image ships neither curl nor wget, and a healthcheck that cannot run reports failure, not
"unknown"), and it `apt-get install`s **git**, which is not optional — the repository store drives the real
binary, so an image without it boots, serves the login page, and fails the moment anybody creates a site. It
also carries `templates/` beside the published app, because `dotnet publish` does not: the template is not
part of any project, and `CreateSite` reads it to write a site's first commit. It runs as non-root `app`,
and both the Data Protection key directory and the repository root are created and chowned in the image
because Docker seeds a named volume from the path it covers. The SSR runtime stage carries **no
`node_modules`**: the Angular builder bundles the server output down to a few `node:` built-ins — recheck
that before adding a dependency that resists bundling, because a missing one is a crash loop at container
start, not a build error.

## Conventions and gotchas

- **A constructor with more than one parameter puts each parameter on its own line**, indented four spaces,
  with the closing paren and any base type on the last parameter's line. This applies to primary
  constructors everywhere in the backend — controllers, services, use cases, middleware, repositories —
  however short the parameters happen to be today:

  ```csharp
  public class AuthSessionService(
      ITokenService tokenService,
      IRefreshTokenRepository refreshTokenRepository,
      IAdminPolicy adminPolicy,
      WeblyDbContext dbContext) : IAuthSessionService
  ```

- **Enums cross the API as names, not numbers** (`JsonStringEnumConverter` in `Program.cs`, `.AddJsonProtocol`
  on SignalR, and `HasConversion<string>()` in the context). `RunEventType` is the load-bearing one: it is a
  generated TypeScript union the client switches on *and* a value in the hub's payloads, so a numbered enum
  would make inserting a value in the middle a silent breaking change in two places at once.
- **Administrators are configuration, not a column.** `IAdminPolicy` reads `Administrators:Emails` and
  `AuthSessionService.Describe` folds the role into the profile, so the client keeps one source. Deliberately
  not a token claim: a claim goes stale exactly like `email_verified` did, and recovering from that needed
  the whole blacklist-invalidation mechanism. Promotion takes a config change and a restart.
- **An unconfigured feature is absent, not broken.** Google sign-in, the editor agent and publishing each
  check their own configuration and answer honestly. That is what lets a fresh clone run, and the tests run,
  with no secrets at all.
- **`Program.cs` calls `app.UseRouting()` explicitly, and moving or removing it breaks things silently.**
  `WebApplication` inserts routing at the *front* of the pipeline when nothing has called it, which puts
  endpoint selection before every middleware in the file — and `MapReverseProxy` is a catch-all, so an
  endpoint is then selected for every request. `StaticFileMiddleware` stands down when one already is, so the
  locally published sites 502ed through the proxy while their `index.html` sat on disk, with nothing logged
  anywhere. Anything that serves files or defers to endpoints has to be registered **above** that call.
- **A proxying action gets an `HttpMessageInvoker`, never an `HttpClient`.** YARP's `IHttpForwarder` throws on
  the latter, because `HttpClient` adds a total-request timeout, redirect following and buffering, and all
  three are wrong for a response that is streamed and a WebSocket that is held open. `PreviewForwarder` is the
  one in this codebase and says so at length.
- New endpoints are protected by default. Add `[AllowAnonymous]` deliberately, never reflexively.
- Add the `[Route("api/...")]` prefix to every real controller. `HealthController` deliberately sits at
  `/health` (no prefix) so it is a proxy-bypass canary — leave it there.
- Per-site routes live **under** the site (`/api/sites/{nanoid}/versions/...`, `/files`, `/domains`,
  `/deployments`, `/chat`, `/preview`). A flat route would invite a lookup by the child's nanoid alone, which
  is exactly the shape that lets somebody read another account's data by guessing.
- Debug/dev-only scaffolding (the Testcontainers fallback, swagger UI) is gated on
  `Environment.IsDevelopment()`; keep it out of Release behaviour.
- `dotnet ef` uses `WeblyDbContextDesignTimeFactory`, not the API host — scaffolding a migration needs the
  provider, not a running server. Migrations are applied by the API at startup (`MigrateAsync`).
- **Anything that shells out passes its arguments as arguments.** `ProcessStartInfo.ArgumentList`, never a
  composed command line — that goes for git, for docker, and for a command sent into a sandbox. Customer
  input reaches all three.
- **Four things are per-process and commented as such**: `IAccessTokenBlacklist`, `RunRegistry`,
  `AgentBudget` and `SiteWorkspaceRegistry`. The last one is the newest and the sharpest: two instances
  would each hold a warm sandbox for the same site, editing two working trees and committing over each
  other. Plus `DeploymentJobRunner`, which polls rather than leasing. Scaling out means addressing all five,
  and each one says so where it is.
