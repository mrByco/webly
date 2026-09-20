# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repository.

## What this is

**Webly**: a self-service SaaS where somebody describes their website in a chat and gets one — versioned,
on their own domain, published to the internet, without touching a line of code. Product UI and generated
sites are English; code and comments are English.

`PROJECT.md` is the product and the lineage: what Webly is, which two projects its architecture comes from
(**cookta-rework** for the layering, the auth and the dev stack; **auto-grader** for the agent framework
and the run substrate), and the decisions that are settled. `MASTER_PLAN.md` is the plan of record,
`whats_next.md` says where work actually stopped, and `docs/` holds the long-form reasoning:
`domain-plan.md` (sites, documents, versions), `agent-plan.md` (the chat and its substrate),
`deploy-plan.md` (publishing, domains, Vercel). Read the relevant one before designing in that area — the
"why" is written down there rather than re-derived.

## Status: written, not run

**The repository was initialized in an environment with no .NET SDK.** Nothing in the backend has been
compiled, no migration has been generated, and the generated Angular API client
(`client/src/app/api/`) does not exist yet — so the client does not type-check against real DTOs either,
although it builds and its templates type-check against the shapes the services declare.

Do not treat a green anything as evidence yet. `MASTER_PLAN.md` P1 and `whats_next.md` list the exact
first steps, in order. Until they are done, the honest summary of this repository is "a complete design,
expressed as code".

## Work philosophy

Hobby project; **clean, maintainable code is the point**. No deadline pressure to trade against it. Prefer
the clear solution over the clever one and the small diff that fits the surrounding code over a rewrite.
**Every feature gets validated in the running app** (`https://localhost:5000`), not just in tests — and for
this product that means looking at a real published page, not only the editor.

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
| Backend tests (needs Docker) | `app_test` (optional `filter`) | `dotnet test Webly.Tests/Webly.Tests.csproj` |
| Tail logs | `app_logs` | `./run-app.ps1 logs -Only backend` |
| Persistent dev DB up/down | `db_compose_up`, `db_compose_down` | `docker compose -f docker-compose.dev.yml up -d` |
| Inspect the DB | `db_status`, `db_query` (read-only SQL) | `docker exec webly-postgres-dev psql -U webly -d webly` |
| Regenerate the Angular API client | `regen_api` | `./regen-api.ps1` or `yarn --cwd client regen-api` |
| Angular typecheck / build | `client_typecheck`, `client_build` | `yarn typecheck`, `yarn build` in `client/` |
| Migrations | — | `dotnet ef migrations add <Name> --project Webly.Data --startup-project Webly.Data` |
| Read a sent email | — | open the newest file in `.run/mail/` (dev sends nothing; it logs and saves) |

### Running-the-stack facts that cost time if unknown

- **Single origin.** The browser only ever talks to the backend at `https://localhost:5000`. It serves
  `/api/*`, `/health`, `/hubs/realtime` and `/swagger` itself and **reverse-proxies everything else**
  (YARP, `Program.cs`) to the Angular dev server on plain `http://localhost:4200`. In production the same
  proxy points at the built Angular SSR node server (`node dist/client/server/server.mjs`, :4000).
  **There is no CORS and there must not be** — same-origin is by construction. Do not point the browser at
  :4200.
- **Hybrid dev database.** On startup in Development the backend tries the docker-compose Postgres
  (`docker-compose.dev.yml`, **`localhost:5434`**). Not 5432, which a locally installed Postgres commonly
  owns, and not 5433, which is the reference project's dev DB — connecting to *that* by accident is worse
  than not connecting at all, because its schema is close enough to look plausible and wrong. If
  unreachable it falls back to an ephemeral Testcontainers Postgres (needs Docker) — **that data is gone on
  the next restart.** `GET /health` → `{ status, dbSource }` says which is active; so does `app_status`.
- **Start order: backend first.** The frontend's `prestart` (`ng-openapi-gen`) reads the backend's live
  swagger. `run-app.ps1` and `app_start` both do this in the right order.
- Trust the dev cert once: `dotnet dev-certs https --trust`.
- Logs and pidfiles live in `.run/` (gitignored). `Webly.Api` locks its build output while running —
  `app_build` handles stop/build/start.
- The solution file is `Webly.slnx` (the new .NET 10 format).
- Package manager for `client/` is **yarn** (classic). Central Package Management for .NET: versions live
  in `Directory.Packages.props`, csproj files carry bare `<PackageReference>`s, and transitive pinning is
  on (Npgsql asks for an older EF Core Relational than the version we pin).

## Architecture

### Backend layering

`Webly.Api` (controllers, the hub, middleware, DI, the reverse proxy) → `Webly.Services` (all business
logic) → `Webly.Data` (EF Core `WeblyDbContext`, entities, repositories, migrations). Strict and
one-directional: `Webly.Api` never references `Webly.Data` directly, and EF entities never leak above
`Webly.Data` — with one deliberate exception, `SiteDocument`, which is the *content* of a jsonb column
rather than a row and crosses the API surface as itself. `Webly.Tests` is NUnit + Testcontainers.

Controllers are thin: no business logic, no `object` in return types. `Webly.Services` splits into
`Services/` (stateful/infrastructure), `UseCases/` (single-operation classes with an `Execute`) and
`Agent/` (the agent, its args and its toolkits). DTOs live one-per-file under `Webly.Services/DTO/`.
Entities are addressed by nanoid strings across the API surface; integer `Id`s stay internal to
`Webly.Data`.

**`Webly.Data/Models/` is organized by domain**, one folder per area, plus `Interfaces/` for the
cross-cutting entity contracts: `Authentication/`, `Sites/` (with `Sites/Document/` for the document
model), `Deployments/`, `Chat/`. Namespaces follow the folders.

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
- **Refresh tokens are single-use.** Replaying a spent one is treated as theft and revokes the whole chain.
  Only the HMAC hash is stored.
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
  returns, so none of them can express the check wrongly or forget it. Its light variant skips the two
  jsonb documents for the operations that only touch the site row.
- **Not yours reads as 404, never 403.** A 403 would confirm that a guessed nanoid names a real site.
  `SiteController.Failure` is the one place that mapping lives.
- **A site never exists without a version.** `CreateSite` writes the row, the starter document and the
  draft pointer together, so no code anywhere else has to handle a site with nothing to render.
- **`User.CurrentSiteId` is which site the editor opens on**, the reference project's `CurrentFamilyId` in
  the same role. Read it **before** deleting anything: the FK nulls it on cascade, so a check afterwards
  can no longer tell "was looking at this site" from "was looking at nothing".
- **The slug does not follow the name.** A rename leaves the address alone, because the address may already
  be published, linked to and indexed.

### The site document

The whole of a site's content is one `SiteDocument` — theme, navigation, pages, sections — stored as jsonb
inside a `SiteVersion`. There is no HTML, JSX or template source anywhere that a person or the agent edits.
`docs/domain-plan.md` §1 is the argument; the short version is that generated source cannot be validated,
cannot be edited in a form, and turns every deploy into "did the model break the build".

- **`SectionCatalogue` is the single registration point.** One declaration per section type drives
  validation, the agent's tool description (generated, never written out in the prompt), the client's
  property editor, and the renderer's template. The reference project uses typed payload classes and
  records the result in its own notes: five registration points, where missing one is a blank space on a
  page. Adding a section type here is **four edits in one place each** — the `SectionType` enum, the schema,
  a renderer, a preview thumbnail — and `SectionCatalogueTests` fails until three of them are done.
- **Nothing in the document is duplicated.** The home page is the one whose path is `/` (no `isHome` flag);
  a section's position is its index (no sort field); a navigation link names a page id **or** a URL, with
  the validator enforcing exactly one. A link naming a *path* is refused outright, which is what makes
  renaming a page's URL safe.
- **`SiteDocumentValidator` holds every rule Postgres cannot.** A jsonb column has no unique index and no
  check constraint over its contents. Its messages are written for the model to read, because a failed tool
  call hands them straight back.
- **`SiteDraft` is the only way a document changes** — the agent, the property editor and a restore all go
  through it. Two invariants: a draft is **always valid** (each operation validates the whole document and
  keeps it only if it still passes, so a failed edit changes nothing), and a draft is **never the persisted
  document** (it starts as a deep clone, so an abandoned or cancelled turn leaves the stored version
  untouched). `SiteDocument.Clone()` round-trips through JSON on purpose: a `JsonNode` remembers its parent,
  so a member-wise copy would either throw or hand two versions the same mutable props.

### Versions

**Every accepted change appends an immutable version.** `Site.DraftVersionId` is what the editor edits;
`Site.PublishedVersionId` is what the world sees. Whole-document snapshots, not diffs — a diff chain has to
be replayed before anything renders, and one bad entry poisons everything after it.

- **`CommitSiteVersion` is the only writer.** A second write path would be a second definition of history.
- **A draft with no changes commits nothing.** A history of "no changes" entries is not a history.
- **`HasUnpublishedChanges` is computed** from the two pointers. A boolean beside them is a third fact that
  can disagree with both.
- **Restore copies forward**, never repoints backwards: the intervening history stays reachable, the restore
  itself appears in the history, and undoing an undo is the same operation again. The restored document is
  **re-validated**, because a document written under an older catalogue can be invalid today.
- **A version links to the chat message that produced it** and the message links back, which is what makes
  the history read as the conversation that caused it.
- **One version per hand edit**, which is why the client debounces: a version per keystroke is a history
  nobody can read.

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

### The chat and the run substrate

`docs/agent-plan.md` is the full write-up. What matters when touching it:

- **One agent**, `site-editor`, keyed, built by a static `Create(sp, key)` factory. A second agent is one
  registration and one `ResolveAgentKey` case; nothing in the substrate changes.
- **The tool list is the permission model.** No publishing, no domains, no billing, no deleting a site.
  Adding one is a method plus a review, and the diff shows it.
- **The agent never invents a fact.** `AskUser` blocks the run until the person answers, and a timeout comes
  back as *"The person did not answer"* — a tool result, so the model wraps up instead of the turn dying. A
  plausible invention published on a real website is the worst thing this product can do.
- **A turn is one version.** `SiteEditSession` holds the draft, `AgentTurnService` commits once at the end:
  atomic, readable in the history, and free to cancel.
- **Run lifetime lives in `RunRegistry`, not on an `HttpContext`.** A closed tab must not cancel a turn
  halfway through rewriting somebody's home page. Only an explicit `Cancel` stops a run —
  which is why `OrphanRunReaper` is mandatory rather than nice to have (two minutes unwatched, thirty
  minutes absolute, five minutes to keep a finished handle for a late reconnect).
- **Events are appended to the run's log first, then published.** Publishing first loses anything emitted
  between a late subscriber's replay and its group join, which is the reconnect case. Every envelope carries
  a `Seq`, which is both the resume point and the duplicate filter, and that is what makes the hub's
  "join the group, *then* replay" order safe.
- **Exactly one terminal event per run, emitted only by `ChatRunLauncher`.** A client that never sees one
  waits forever, which is indistinguishable from the product being broken.
- **The log is in memory, not a table.** A chat run cannot outlive its process and the log exists only to
  serve a reconnect. `IRunEventSink` is the seam if that changes.
- **`RunWriter` flushes before any non-text event**, or a tool chip arrives before the sentence that
  introduced it.
- **`RunKind.Deploy` is the durable one**: its state is the `Deployment` row and its run id *is* the
  deployment's nanoid, so a page that reloads mid-publish can re-attach to something that outlived the
  process.
- **`AgentBudget`, not `[EnableRateLimiting]`.** The rate-limiting middleware only sees HTTP endpoints, so
  an attribute on a hub would look like a fence and be none.
- **No provider key means the agent is absent, not broken**: nothing is registered,
  `/api/sites/{nanoid}/chat/status` reports `enabled: false`, the client hides the chat, and the tests need
  no secrets. Same discipline as Google sign-in.

### Rendering and publishing

- **Webly renders; the provider serves.** `ISiteRenderer` turns a document into exact bytes with no build
  step anywhere. A build on the provider's side can fail for reasons the site's owner cannot see, and there
  is no code for them to fix it in. It also makes a render a pure function of (document, context), which is
  what makes a failed deployment safe to retry — so the renderer may not read a clock or a random number.
  `SiteRendererTests.Rendering_is_deterministic` is the guard.
- **The preview is the renderer.** The editor's pane is an iframe over `GET /api/sites/{nanoid}/preview`,
  which renders the same files publishing uploads. There is no client-side renderer and there must not be:
  two descriptions of what a site looks like means the wrong one is what the customer saw. The one
  concession is `RenderContext.StylesheetUrl`, because every page is previewed from one URL and a deployed
  page's relative `../styles.css` would not resolve there.
- **Section renderers emit class names, never inline styles.** `ThemeCss` derives the whole stylesheet from
  the theme, so "make it warmer" is one edit that changes every page, and a section added next month
  inherits a design rather than needing one. Colour variants are computed in OKLCH by the browser
  (`oklch(from var(--brand) …)`) rather than stored — four colours that can drift apart is four ways for a
  site to look wrong.
- **Everything from a document is escaped** through `SectionMarkup`. The values were written by a language
  model and typed by a member of the public, and neither is a reason to trust a string into markup. The one
  exception is `RichTextHtml`, which is **currently escaping rather than sanitizing** — the real allowlist
  lands with the rich-text editor, and sanitizing HTML properly is a library's job, not thirty lines here.
- **One page per directory** (`/about/index.html`), so a static host serves clean URLs with no rewrite
  rules — the part of static hosting that differs per provider, avoided.
- **`Site.PublishedVersionId` moves in exactly one place**, in `DeploymentJobRunner` after the provider
  reports success. A failed publish leaves the previous version live, and the email says so in its first
  line.
- **One platform-owned Vercel account.** "Self-service" cannot begin with "create a Vercel account". The
  bring-your-own upgrade is a nullable token on `Site` and nothing else — see `docs/deploy-plan.md` §3.
- **A `Domain` row mirrors the provider**, which owns verification and the certificate. A local "verified"
  flag it disagrees with is a site that is live according to us and 404 according to the internet. Checking
  is a button, never a timer. Exactly one primary hostname per site, by partial unique index; only a
  verified domain may be promoted.
- **`VercelDeploymentTarget` has never run against the live API.** Treat its endpoint and payload shapes as
  the plan — `docs/deploy-plan.md` §6 says what to reconcile first.

### Frontend (`client/`, Angular 22 with SSR)

Zoneless, signals, standalone components, Tailwind 4 + daisyUI 5 (`src/styles.css`). Routes are centralized
in `src/app/app.routes.paths.ts` as `{ path, build() }` pairs — always build URLs through `AppRoutes`, never
with raw strings. Folder layout under `src/app/`: `api/` (generated), `pages/`, `components/`, `services/`,
`guards/`, `interceptors/`, `models/`, `shared/`.

- **`client/src/app/api/` is generated. Never hand-edit it.** After any controller or DTO change run
  `regen_api` (MCP) or `./regen-api.ps1`; it is committed so CI needs no backend. **It does not exist yet**
  — see "Status" above — which is why CI's client job is currently skipped and why the client's services
  declare their own return types rather than inferring them.
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
- **A committed version reaches the screen by bumping `previewKey`.** Changing the iframe's URL is the only
  way to make a browser re-fetch a document it thinks it already has; the key is a counter rather than a
  timestamp, so unrelated renders do not make it flicker.
- **The chat's hard part is disagreement between the page and the run.** A turn is started, then watched, as
  two steps, so a reload re-attaches by the same path; `GetChat` reports `activeRunId` for exactly that;
  `lastSeq` per run is the resume point and the duplicate filter; every watched run is re-subscribed on
  reconnect.
- **Hub DTOs are declared by hand in `realtime.service.ts`**, with a comment saying so: `ng-openapi-gen`
  deletes them, because Swagger describes HTTP only. Pinning them into the OpenAPI document with a
  Swashbuckle document filter is the fix, and it is a P2 task.
- **The app's own colours are quiet on purpose.** This app is a frame around somebody else's website, and
  the accents on screen should be the preview's. Its icons (`shared/icon.ts`) and the published sites'
  feature icons (`Icons` in the renderer) are two separate closed sets — the second has to be inlined into
  rendered HTML, where the component cannot reach.
- `models/problem-details.ts` holds the one `messageOf`. The generated client asks for
  `responseType: 'text'` on endpoints that answer 204, so a failure from one of those hands back the problem
  body as a *string* — reading only `error.title` there silently shows the generic message.

## Deployment

Three images on Docker Hub under one repository, told apart by tag — `byc0/margareta:webly_dev_api` and
`webly_dev_frontend` from master, `<branch>_webly_api` / `_webly_frontend` from `release/**`.
`.github/workflows/webly-dev-deploy.yaml` builds, pushes and then POSTs to a Portainer stack webhook; the
prod workflow builds and pushes only, so cutting a release branch and restarting production stay two
decisions. `ci.yml` is the only thing that runs tests, and the deploy workflows deliberately do not gate on
it.

`deploy/portainer-stack.yml` is the stack: Postgres, the API, the SSR client and a Caddy container that
terminates TLS with `caddy reverse-proxy` rather than a Caddyfile, so the whole deployment is one pasteable
file. Only Caddy publishes ports. Its header comment lists every environment variable.

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
"unknown"). It runs as non-root `app`, and the Data Protection key directory is created and chowned in the
image because Docker seeds a named volume from the path it covers. The SSR runtime stage carries **no
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
  on SignalR, and `HasConversion<string>()` in the context). `SectionType` is the load-bearing one: it is a
  document discriminator, a generated TypeScript union and the key the renderer switches on, so a numbered
  enum would make inserting a value in the middle a silent data migration.
- **Administrators are configuration, not a column.** `IAdminPolicy` reads `Administrators:Emails` and
  `AuthSessionService.Describe` folds the role into the profile, so the client keeps one source. Deliberately
  not a token claim: a claim goes stale exactly like `email_verified` did, and recovering from that needed
  the whole blacklist-invalidation mechanism. Promotion takes a config change and a restart.
- **An unconfigured feature is absent, not broken.** Google sign-in, the editor agent and publishing each
  check their own configuration and answer honestly. That is what lets a fresh clone run, and the tests run,
  with no secrets at all.
- New endpoints are protected by default. Add `[AllowAnonymous]` deliberately, never reflexively.
- Add the `[Route("api/...")]` prefix to every real controller. `HealthController` deliberately sits at
  `/health` (no prefix) so it is a proxy-bypass canary — leave it there.
- Per-site routes live **under** the site (`/api/sites/{nanoid}/versions/...`, `/domains`, `/deployments`,
  `/chat`). A flat route would invite a lookup by the child's nanoid alone, which is exactly the shape that
  lets somebody read another account's data by guessing.
- Debug/dev-only scaffolding (the Testcontainers fallback, swagger UI) is gated on
  `Environment.IsDevelopment()`; keep it out of Release behaviour.
- `dotnet ef` uses `WeblyDbContextDesignTimeFactory`, not the API host — scaffolding a migration needs the
  provider, not a running server. Migrations are applied by the API at startup (`MigrateAsync`).
- **Three things are per-process and commented as such**: `IAccessTokenBlacklist`, `RunRegistry` and
  `AgentBudget`. Plus `DeploymentJobRunner`, which polls rather than leasing. Scaling out means addressing
  all four, and each one says so where it is.
