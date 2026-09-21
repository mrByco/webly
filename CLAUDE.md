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
person typed. The backend compiles, the schema is a real migration applied to a real Postgres, the 70-test
suite is green, and `client/src/app/api/` is the real generated client that the Angular app type-checks and
prerenders against.

Two harnesses drive it, and they answer different questions:

- **`tools/e2e/run.mjs`** stands in for the C# and drives everything underneath it — real git plumbing, the
  real sandbox agent, the real `claude` CLI, the real Next.js dev server and build — in sixteen steps from "a
  new site is the template" to "a compile error stops being reported once it is fixed". No .NET, no Docker, no
  database, no credentials. `tools/e2e/README.md` lists what it has caught.
- **`tools/e2e/turn.mjs`** does the opposite: it talks to the running backend over the real hub, so what it
  exercises is the C# — `ChatRunLauncher`, `AgentTurnService`, `SiteWorkspaceRegistry`, the sandbox provider,
  `CommitSiteVersion`. It exists because a turn cannot be started over HTTP; `StartChat` is a hub method, so a
  SignalR client is the only way to press the button.

**What is still unrun**, and treat each as the plan rather than as working code:

- **`OpenCodeAgent` and `VercelDeploymentTarget`** — written against another product's documented interface,
  never executed. `docs/deploy-plan.md` §6 says what to reconcile in the Vercel one first.
- **`DockerSandboxProvider` and `E2bSandboxProvider`** — only `local` has run. The contract they all speak is
  the one `tools/e2e` exercises, which is the point of having it.
- **The real agent inside the app.** `ClaudeStreamJsonParser` is covered by a recorded transcript and the
  `claude` CLI has run under `tools/e2e/run.mjs`, but every turn through the running app so far has been the
  mock. That needs `Agent:ClaudeCode:ApiKey` in user secrets and nothing else.
- **Google sign-in and Resend**, which are configuration away and absent by design without it.

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
- **Start order: backend first.** The frontend's `prestart` (`ng-openapi-gen`) reads the backend's live
  swagger. `run-app.ps1` and `app_start` both do this in the right order.
- Trust the dev cert once: `dotnet dev-certs https --trust`.
- Logs and pidfiles live in `.run/` (gitignored). `Webly.Api` locks its build output while running —
  `app_build` handles stop/build/start.
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
- **Verification is a blocking onboarding step.** Registering lands on `/verify-email` and nothing else is
  reachable until the address is proven: a site publishes to the public internet under our infrastructure.
  Google-created accounts arrive verified and skip it.
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
inline styles, so a template engine buys nothing at six messages. Links are built from `Email:BaseUrl`,
never the request host (host-header poisoning).

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
  `SiteController.Failure` is the one place that mapping lives.
- **A site never exists without a version.** `CreateSite` writes the row, initializes the bare repository
  from the template, commits it, and points `HeadVersionId` at that commit — so no code anywhere else has to
  handle a site with no source. It saves the row first, because the repository is keyed by the nanoid the
  context stamps on insert.
- **`User.CurrentSiteId` is which site the editor opens on**, the reference project's `CurrentFamilyId` in
  the same role. Read it **before** deleting anything: the FK nulls it on cascade, so a check afterwards
  can no longer tell "was looking at this site" from "was looking at nothing".
- **The slug does not follow the name.** A rename leaves the address alone, because the address may already
  be published, linked to and indexed.

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
  `[A-Za-z0-9-_]{1,40}`; tree writes refuse absolute paths, `..` segments, and anything over the configured
  file-count and byte limits. It was assembled by a language model on a machine we do not own.
- **`templates/next-site` is what a new site starts as**, and it is a normal project somebody can open and
  `npm run build`. Two of its files are product rather than scaffolding: **`AGENTS.md`** carries the
  standing rules (never invent a fact, never write a testimonial nobody gave you, keep the build working,
  stay in the stack) and **`content/brand.md`** is where the agent records facts it learns, because its
  session does not outlive the workspace. `CLAUDE.md` in the template just points at `AGENTS.md`, so both
  CLIs read one file.
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
  same operation again.
- **A version links to the chat message that produced it** and the message links back, which is what makes
  the history read as the conversation that caused it.
- **`(SiteId, CommitSha)` is unique**, which is what stops a second row claiming the same commit and making
  the history show a change that is not one.
- **The history shows a diff, not a preview.** There is one dev server per site and it runs the working
  tree, so there is nothing to point an iframe at for a commit from last Tuesday.

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
  there.
- **`AGENTS.md` in the site's repository is the other half of it.** A tool list cannot say "never write a
  testimonial nobody gave you", and a plausible invention published on a real business's website is the
  worst thing this product can do. The agent asks in its reply and the turn ends; the answer is the
  person's next message. (The old blocking `AskUser` tool is gone — a CLI in a sandbox cannot wait on this
  app. The MCP bridge that would bring it back is in the plan, §1.3.)
- **A turn is one version.** `AgentTurnService` runs the agent, then commits once from the tree the sandbox
  hands back: atomic, readable in the history, and free to cancel.
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
- **The preview is the site's own `next dev`, proxied.** `PreviewController` forwards with YARP's
  `IHttpForwarder`: same origin, ownership re-checked per request, WebSockets forwarded (hot reload is one),
  our cookie stripped on the way out. A cold site answers 503 with a sentence rather than starting a
  workspace on a GET, because tens of seconds of a hanging iframe looks broken.
- **The dev server is told where it is, and that is load-bearing.** Next.js writes absolute URLs for its
  stylesheets, its chunks and its hot-reload socket, so a dev server that thinks it is at `/` asks the browser
  for `/_next/...` at the root of Webly's origin — which is Webly's app. `SiteWorkspaceRegistry` passes
  `/api/sites/{nanoid}/preview` to `StartDevServerAsync`; the sandbox agent sets `WEBLY_PREVIEW_BASE` and
  rewrites everything under its own `/preview` onto it; the site template turns that variable into Next's
  `basePath`. Three files agree on one string, and the day they disagree the preview renders as unstyled text
  — which is exactly how it shipped, because the test read the HTML and the HTML was perfect.
- **`next dev` catches less than it looks like.** It compiles with SWC, which strips types rather than
  checking them: a type error never reaches its log, and neither does an *unused* broken import, which is
  elided as possibly-a-type before anything resolves it. What `BuildFailed` can see is syntax and imports that
  are used. Types are what `npm run typecheck` is for — `AGENTS.md` tells the agent to run it — and `next
  build` at publish time is the backstop for both.
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
  waits forever, which is indistinguishable from the product being broken.
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
  already watching.
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
- **`Site.PublishedVersionId` moves in exactly one place**, in `DeploymentJobRunner` after the provider
  reports success. A failed publish leaves the previous version live, and the email says so in its first
  line. The build log goes to `Deployment.ErrorDetail`, which the settings screen shows folded away — through
  `CompilerOutput.Readable` and **then** trimmed to a tail, in that order: it reached the screen raw until a failed
  publish was watched in a browser, and trimming first sliced the middle out of SWC's backtrace, which left the
  frames in and the marker that identifies them out.
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
- **`VercelDeploymentTarget` has never run**, in either half — the REST calls or the CLI in the sandbox.
  Treat its endpoints, payloads and output parsing as the plan; `docs/deploy-plan.md` §6 says what to
  reconcile first.

### Frontend (`client/`, Angular 22 with SSR)

Zoneless, signals, standalone components, Tailwind 4 + daisyUI 5 (`src/styles.css`). Routes are centralized
in `src/app/app.routes.paths.ts` as `{ path, build() }` pairs — always build URLs through `AppRoutes`, never
with raw strings. Folder layout under `src/app/`: `api/` (generated), `pages/`, `components/`, `services/`,
`guards/`, `interceptors/`, `models/`, `shared/`.

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
- **The editor shell owns the site.** One load, one signal (`SiteService.current`), so the header, the chat,
  the preview and whichever child route is showing cannot disagree about what is open. History, domains and
  settings render **in place of the preview**, not over the whole page, so the chat stays available.
- **The preview updates itself.** It is an iframe over `/api/sites/{nanoid}/preview/`, which is the site's
  own dev server, so hot reload puts the agent's edits on screen with nothing on this side asking. Bumping
  `previewKey` reloads the frame and is for the case where the whole tree moved — a commit or a restore;
  it is a counter rather than a timestamp so unrelated renders do not make it flicker.
- **A cold preview is a sentence, not a frame.** Only a turn starts a workspace, so the pane shows "your
  preview is asleep" until `SiteDetailResponse.workspaceReady` or a `WorkspaceProgress` event says
  otherwise. An iframe pointed at the 503 would render the browser's own error page.
- **The chat's hard part is disagreement between the page and the run.** A turn is started, then watched, as
  two steps, so a reload re-attaches by the same path; `GetChat` reports `activeRunId` for exactly that;
  `lastSeq` per run is the resume point and the duplicate filter; every watched run is re-subscribed on
  reconnect.
- **The hub's DTOs are generated too**, which took a document filter. Swagger describes HTTP and a hub is not
  HTTP, so `ng-openapi-gen` deleted anything only the hub used and `realtime.service.ts` re-declared it by hand —
  including `RunEventType`, a union the client switches on, which meant adding a value on the server changed
  nothing here until somebody remembered. `HubContractDocumentFilter` puts those types into the document;
  `realtime.service.ts` re-exports them, because everything that watches a run already imports that service.
- **The chat's entries are one shape**, including the ones that are not messages: `activity` chips, a
  single growing `files` entry per turn (a chip per write buries the sentence explaining them), a `waking`
  line that is replaced rather than appended while the workspace starts, and a `build` block carrying the
  compiler's own words.
- **The app's own colours are quiet on purpose.** This app is a frame around somebody else's website, and
  the accents on screen should be the preview's.
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
