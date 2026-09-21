# The editing agent and the run substrate

How the chat works, and why the machinery under it looks like this. The short version lives in `CLAUDE.md`;
this is the reasoning.

## 1. The agent is a coding agent, in a sandbox

Webly does not call a model API. It runs **a coding agent's own CLI** — `claude -p`, or `opencode run` —
inside a machine that has the site's source on it, and lets the agent read files, run commands, watch the
dev server compile and fix its own mistakes. `ICodingAgent` is the interface; `ClaudeCodeAgent` and
`OpenCodeAgent` are the two implementations; `CodingAgentRegistry` decides which one serves a turn.

The alternative — a model SDK in `Webly.Services`, a tool list of file operations, a loop — was the obvious
build and it is the wrong one. It is rebuilding, worse, the harness that is the product we would be paying
for: the planning, the context management, the file editing, the "run the thing and read the error"
discipline. All of it exists, is better than a reimplementation, and is a CLI away. **There is no
`Microsoft.Extensions.AI`, no Anthropic SDK and no OpenAI SDK in this solution**, and the package entries
were removed with a comment saying why.

Two implementations, not one, because the choice is real: Claude Code is the strongest at long-horizon tool
use on an actual codebase, OpenCode is open source and provider-agnostic. The second one is the answer to
"what if the provider, the price or the terms change", and keeping it working costs one class. It is
deliberately the plainer implementation — its headless output is prose rather than a typed event stream, so
the person sees the agent's words and a coarse activity line instead of per-tool chips. That is the honest
consequence of the choice rather than something to paper over with guesses about its log format.

There is a third, `MockCodingAgent`, which has no model behind it and makes one deterministic edit to the home
page's headline. It is not a stub: everything around a turn — the workspace starting, the dev server compiling,
the tree coming back, the commit, the version row, the preview refreshing, the publish — is Webly's code and
none of it is the model's, and without a keyless agent the most expensive credential in the system is a
prerequisite for testing the parts that do not use it. It edits a real file for the same reason: a turn that
changed nothing would commit nothing, and the interesting half would never run. Enabled by
`Agent:Mock:Enabled`, off in production, and the registry prefers a configured real agent over it.

### 1.1 The model

`AgentModels.Default` is `claude-opus-5`, and the cheaper `claude-sonnet-5` is opt-in. That ordering is
deliberate and it is not a default anybody should quietly flip: this agent edits somebody's business
website in one pass with no review step, and a weaker model's failure mode here is not a worse sentence, it
is a broken build or an invented fact. Cost is a decision to make through a plan, out loud, not silently in
a config file.

### 1.2 The permission model is the sandbox, plus `AGENTS.md`

There is no tool list to curate any more, because the agent brings its own tools. So the fence moved, and it
is now two things:

- **What is in the sandbox.** The site's files, node, git, the agent CLIs, and one model API key passed per
  run as an environment variable on the command. No database connection string, no Vercel token, no session
  cookie, no git remote, no credential that could write to the site's real repository. Publishing, domains,
  billing and deleting a site are not "tools the agent does not have" — they are *not reachable from where
  it runs*, which is a much stronger statement.
- **`AGENTS.md`, in the site's own repository.** A tool list cannot express "never write a testimonial
  nobody gave you", and that is the rule this product most needs. It is committed with the site so both
  CLIs read it as part of the workspace, and so a rule added next month applies to every existing site
  without a migration. `docs/domain-plan.md` §2 has the file's contents.

### 1.3 Asking rather than inventing

A plausible invention published on a real business's website is the worst thing this product can do, so the
rule is written first in `AGENTS.md`: if a fact is not in `content/brand.md` or the conversation, ask for it
in the reply and leave the section out.

**It is a reply, not a blocking tool.** The earlier in-process agent had an `AskUser` tool that suspended
the run until the person answered — the reason the transport had to be bidirectional at all. A CLI in a
sandbox cannot do that: the tool would have to reach back out of the sandbox, into this app, and wait there.
So the agent asks in prose and the turn ends; the answer is the person's next message, which is also what
the transcript reads like afterwards.

The upgrade, if the round trip turns out to matter, is an MCP server Webly exposes to the sandbox with one
`ask_user` tool on it — the CLIs both speak MCP, the run substrate already has the pieces (a run outlives
its request, and the hub can carry an answer inbound), and it would be additive. It is not built because
one extra message is a small cost and a blocking question mid-turn is a real complication.

### 1.4 A turn is one version

`AgentTurnService` runs the turn and commits **once**, at the end, from the tree the sandbox hands back:

- **A turn is atomic.** A model that edits four files and then fails leaves the site's history as it was —
  the workspace has changes in it, but no commit was written.
- **The history stays readable.** One turn is one version with one summary, which is the commit subject.
- **Cancelling is honest.** Stop kills the run; nothing was committed.
- **A turn that changed nothing records nothing.** The tree is compared against the head's and an identical
  one commits nothing, so answering a question in prose does not add a version.

The summary comes from the agent: both prompts ask for a final `SUMMARY:` line, because Webly needs a commit
subject and a history of twenty-nine entries called "Update site" is not a history. When the agent does not
give one, the person's own message is used — a worse subject, but never a fabricated one.

### 1.5 Build errors are surfaced, not swallowed

After a turn, `AgentTurnService` reads the dev server's log and looks for the three things Next.js says when
it cannot compile — `Failed to compile`, `Module not found`, `Type error:`. If it finds one, it emits a
`BuildFailed` event and the editor shows it.

That is the opposite of hiding it, and it is on purpose. `AGENTS.md` rule 3 tells the agent not to finish a
turn with a compile error, and most of the time it does not. When it does, the person's next message is what
fixes it, and they can only write that message if they can see what broke. The publish path does not rely on
this at all: `vercel build` runs there and a failure blocks the deployment.

Two things about that check are not obvious, and both were wrong in the first version:

- **`next dev` compiles on demand.** Straight after a turn it has not looked at the agent's edits at all, so a
  log read at that moment is empty — which reads as success. `ReportBuildErrorsAsync` therefore requests the
  preview first (`ISandbox.TouchPreviewAsync`) and only then asks what happened. Without that, `BuildFailed`
  would essentially never fire and the person would find out at publish time.
- **The log is cumulative.** Reading all of it finds the error a turn three messages ago left behind and reports
  it again — telling somebody their site is broken every time they speak to it, however many times they have it
  fixed. So the turn records the log's offset before it starts and reads only what followed.

And one thing the dev server cannot do for us: **it does not typecheck.** `next dev` compiles with SWC, which
strips types without checking them, so a type error never reaches its output. What it reports is syntax errors
and unresolvable imports. That is the division of labour: `BuildFailed` catches those, `AGENTS.md` tells the
agent to run `npm run typecheck` as its last step — which the recorded turn did not do unprompted — and
`next build` at publish time is the backstop for both. A type error costs seconds to find in the turn and a
failed publish plus a wait to find later.

## 2. Where the agent runs: workspaces and sandboxes

The one part of this product that makes somebody wait, so it is worth the machinery.

### 2.1 The sandbox contract is ours

Every provider — E2B, Daytona, Fly Machines, a local Docker container — has a different API for uploading
files, running commands and exposing ports, and none of them has a .NET SDK. So the provider's job is
reduced to **"start this image and give me a URL"**, and everything else goes through one HTTP contract we
own and can test: `tools/sandbox-agent/index.js`, a zero-dependency node service baked into the image.

```
GET  /health                 → { ok, devServer, node }
POST /files      (tar body)  → replace the workspace with this tree
GET  /files                  → tar of the workspace, minus node_modules, .next, .env, .git…
POST /exec       (json)      → run a command, streaming NDJSON back as it goes
POST /dev/start  (json)      → start the dev server, once
ANY  /preview/*              → proxy to the dev server, WebSockets included
```

Zero dependencies on purpose: an `npm install` inside the image would be a supply chain and a build step for
300 lines of `node:http`, and `tar` is already in every image that can run Next.js. `SandboxAgentClient` is
the .NET side, and it is the same class for every provider — which is what makes a provider swap a class
nobody else has to know about. `deploy/sandbox/Dockerfile` is the image.

Two details of that contract are load-bearing and were both wrong at first, which is the argument for
`tools/e2e` existing:

- **`POST /files` replaces the source, it does not overlay it.** `tar -x` only adds and overwrites, so the
  workspace's source is cleared first (keeping `node_modules` and `.next`, which are in no commit and cost
  minutes). Without that, re-seeding a warm workspace after a restore that *deleted* a page leaves the page
  there and the next turn commits it back — a restore that silently did not remove anything.
- **`GET /preview/*` proxies through `http.request`, not a raw socket relay.** The first version wrote a
  complete HTTP response into the server's own socket, which ends the connection without a parseable reply;
  every preview fetch failed. The WebSocket upgrade is the one case that genuinely is a raw relay, and it is
  handled separately.

### Three providers, and only one of them for production

| Provider | Starts | For |
|---|---|---|
| `local` | `tools/sandbox-agent` as a child process, workspace in `.run/workspaces` | development, and the default — it needs nothing but node |
| `docker` | a container from the sandbox image | development with real isolation |
| `e2b` | a managed sandbox | production |

`local` is the one that makes "clone it and try it" true, and it is also the one where every security property
in `ISandbox`'s comment is absent: the workspace is a directory on the host and the agent runs as the API's own
user. So selecting it outside Development throws at startup rather than warning. The same rule applies to
`FileSystemDeploymentTarget` — see `docs/deploy-plan.md`.

### 2.2 A workspace is warm, and shared

`SiteWorkspaceRegistry` keeps at most one workspace per site, and the chat and the preview both use it:

- **Seeding is a tree copy, usually.** The image already has the template's dependencies installed, so
  `SeedAsync` writes the tree and then runs `npm ls --depth=0`; only a site whose agent added a package pays
  for an install, and it pays once.
- **A lease is a semaphore.** `AcquireAsync` hands out an `IWorkspaceLease`, so two turns on one site queue
  instead of interleaving file writes. Releasing touches the idle clock.
- **Health is re-checked, and a stale workspace is re-seeded.** If the site's head has moved since the
  workspace was made — a restore, or a turn from another tab — the tree is rewritten and the agent's session
  id is cleared, because a resumed session's memory of the codebase would be a memory of a different one.
- **`WorkspaceReaper` closes idle ones.** A warm sandbox bills by the second, so `Sandbox:IdleTimeout`
  (ten minutes) is the number that decides what an open editor tab costs, and `Sandbox:MaxLifetime` is the
  backstop for one that never goes idle.

### 2.3 The preview is the dev server

`PreviewController` forwards `/api/sites/{nanoid}/preview/{**path}` into the workspace's sandbox with YARP's
`IHttpForwarder`. Not a route in the reverse-proxy configuration, because the destination is per site and
per session and there is no static cluster to configure — this is the case the forwarder API exists for.

Four things make it safe and make it work:

- **Same origin.** The browser talks to Webly; Webly talks to the sandbox. So the session cookie is enough,
  there is no CORS, and the sandbox's URL and bearer token stay server-side.
- **Ownership per request**, through `FindForOwnerLightAsync`. A preview is not a public URL with a
  guessable id.
- **WebSockets are forwarded.** Hot reload is one. Without it the preview loads once and then silently
  stops updating, which looks exactly like the agent not working.
- **Our cookie is stripped on the way out.** The sandbox has no use for it, and forwarding a credential to
  another machine because it happened to be on the request is how one leaks.

A cold site answers **503 with a sentence** rather than starting a workspace on a GET: a workspace takes tens
of seconds and an `<iframe>` that hangs for that long looks broken. The editor shows "your preview is
asleep" until a message wakes it, and `SiteDetailResponse.WorkspaceReady` is how it knows.

## 3. The run substrate

Ported from auto-grader's MASTER_PLAN Phase 3, and the one piece of its agent work that survived the pivot
unchanged — because it is about runs, not about models. Its own write-up of why NDJSON-over-POST was
abandoned is adopted rather than re-derived: cancellation was welded to the HTTP connection, so the server
could not tell "user pressed Stop" from "wifi blipped"; idle proxy timeouts killed long responses; and there
was no resume.

- **`RunRegistry`** holds live runs as `RunHandle`s, each with its own `CancellationTokenSource`, subscriber
  count, unwatched clock and **its own event log**. Run lifetime lives here, not on an `HttpContext` — a
  closed tab must not cancel a turn halfway through rewriting somebody's home page. Only an explicit
  `Cancel` stops a run.
- **`ChatRunLauncher`** (singleton) starts a run on **its own DI scope** and `Task.Run`, returns the run id
  immediately, and emits **exactly one terminal event on every exit path**. Nothing else emits a terminal: a
  client that never sees one waits for ever, which is indistinguishable from the product being broken.
- **`IRunEventSink`** appends to the handle's log with a monotonic `Seq` **first**, then publishes. The other
  order loses any event emitted between a late subscriber's replay and its group join — the reconnect case,
  i.e. the case the log exists for.
- **`RunWriter`** (scoped, one bound run per scope) coalesces text deltas and **flushes before any non-text
  event**, so a file chip cannot arrive before the sentence introducing it.
- **`RealtimeHub`** — `StartChat`, `Subscribe(kind, runId, fromSeq)` (join the group, *then* replay),
  `Unsubscribe`, `Cancel`, `FindRun`. Auth rides on the cookie, because a browser cannot set an
  `Authorization` header on a WebSocket upgrade; `CloseOnAuthenticationExpiration` forces the reconnect that
  re-runs the cookie middleware, so a long connection cannot outlive its own token.
- **`OrphanRunReaper`** — the mandatory counterweight to "a disconnect no longer cancels". Two minutes
  unwatched cancels a chat run; thirty minutes cancels anything, which is generous because a turn is a coding
  agent installing a package and waiting for a compile; a finished handle is evicted after five minutes,
  which is the only thing keeping the in-memory log from being a memory leak with a nice name.
- **`AgentBudget`** — turns per user per hour, checked in the hub. Not an `[EnableRateLimiting]` attribute:
  the rate-limiting middleware only sees HTTP endpoints, so an attribute on a hub would look like a fence and
  be none. It matters more than it did in the reference project — a turn now costs a model call *and* a
  machine.

### 3.1 The event types, and what each one is for

| Event | What it is |
|---|---|
| `TextDelta` / `MessageCompleted` | the agent's own prose, streamed then replaced by the whole of it |
| `WorkspaceProgress` | a slow step before work can start: starting a sandbox, installing, waiting for the dev server |
| `Activity` | the agent used a tool, phrased in the person's language — an internal tool name on screen makes the product look like a debugger |
| `FileChanged` | one path, collected by the client into a single list per turn |
| `VersionCommitted` | carries the version nanoid, so the editor refreshes the header and the history without polling |
| `BuildFailed` | §1.5 |
| `DeploymentProgress` | the other run kind |
| `Completed` / `Failed` | terminal, exactly once, from the launcher only |

### 3.2 What is *not* ported, and why

- **The agent framework.** `Microsoft.Agents.AI`, keyed agent factories, args-class routing, in-process
  toolkits: all of it is about running a model in this process, which Webly no longer does. §1.
- **A `RunEvents` table.** ador needs a durable log because its jobs share it and survive restarts. A Webly
  chat run exists only inside one process and its log exists only to serve a reconnect, so the log lives on
  the handle: no entity, no migration, no sweep. `IRunEventSink` is the seam if that changes.
- **Credit metering and the provider zoo.** One `AgentBudget`. Billing is a later slice and will meter at the
  turn, not per token — and will have to include sandbox seconds, which is new.
- **Durable jobs as a general mechanism.** Deployments are the one durable run Webly has, and their state is
  the `Deployment` row — see §4.
- **Langfuse/OTLP tracing.** The agent runs as a process in a sandbox, so the trace that would matter is its
  own; worth revisiting when there is a quality problem to measure.

### 3.3 Two defects the reference project only found live

Both are designed for here rather than rediscovered:

1. **A new conversation's run must become findable the moment its nanoid exists**, not when the run ends —
   otherwise a client that reloads mid-turn on a brand-new thread can never re-attach.
   `ChatRunLauncher` sets `handle.CorrelationId` from the turn's return value, and `GetChat` reports
   `activeRunId` so a reload re-subscribes.
2. **`ng-openapi-gen` deletes hub-only DTOs** from the generated client, because Swagger describes HTTP only.
   Until a Swashbuckle document filter pins `RunEventEnvelope` and `RunEvent` into the OpenAPI document, the
   client declares those shapes by hand in `realtime.service.ts` — a duplication with a comment on it, not
   an oversight.

## 4. Deployments as the other run kind

`RunKind` has two values and they are treated differently on purpose:

| | Chat | Deploy |
|---|---|---|
| Lifetime | seconds to minutes | seconds to minutes, but must survive a restart |
| State | `RunHandle` in memory | a `Deployment` row |
| Replay | the handle's event log | the row's status |
| Started by | the hub | an endpoint, picked up by `DeploymentJobRunner` |
| Sandbox | the warm workspace | a fresh one, `npm ci`, discarded after |
| Lost on restart | yes, and nothing was committed | no |

One hub, one envelope, one client service. The deployment's run id **is** its nanoid, so a page that reloads
mid-publish can re-attach to something that outlived the process.

`DeploymentJobRunner` polls a status column rather than leasing rows. Webly runs as one instance, and the
honest version of that is a runner that says so in its comment: a second instance would take the same row
twice, and the fix is a lease column plus a heartbeat (ador has both, and needed them for twenty-minute
jobs).

## 5. What is done and what is next

Done, as code that has never run — see `whats_next.md`:

1. ✅ The sandbox contract (`tools/sandbox-agent`), verified by hand against real node: auth, tar in and out
   with the excludes, NDJSON exec, the HTTP proxy, and a WebSocket upgrade.
2. ✅ `ISandbox` / `SandboxAgentClient`, the Docker provider (development) and the E2B provider (production,
   **unverified against the live API**).
3. ✅ `ISiteRepositoryStore` over real git, with `GitSiteRepositoryStoreTests` against a temporary repository.
4. ✅ `ICodingAgent` with both implementations, the registry, and the stream-json parser.
5. ✅ Workspaces: registry, lease, re-seed on a moved head, reaper.
6. ✅ The run substrate and the hub, minus the question mechanism.
7. ✅ `AgentTurnService`: thread, workspace, turn, commit, version, build-error report.
8. ✅ The client: chat, activity and file chips, workspace progress, build errors, the proxied preview, the
   history with diffs.

Next, in order:

1. ✅ **Compile it**, migrate it, and get the suite green. The .NET 10 SDK is in the Ubuntu archive, which is
   what unblocked this after several commits of "written, not run".
2. ✅ **The first real turn**, with the local provider and the mock agent: a message, a file written, one
   commit, the preview served through Webly's origin, a publish, a page. `tools/e2e/turn.mjs` drives it,
   because `StartChat` is a hub method and a SignalR client is the only way to start a turn.
3. ✅ **Reconcile `ClaudeCodeAgent`'s stream-json parsing with the CLI's actual output** — recorded as
   `tools/e2e/fixtures/claude-stream-json.ndjson` and covered by `ClaudeStreamJsonParserTests`, which now
   computes its expectations from the fixture rather than quoting one recording's prose. The `SUMMARY:`
   convention is ours and the CLI does follow it.
4. ⬜ **A turn with the real agent inside the app**, and then a reload mid-turn and Stop. Every turn through
   the running app so far has been the mock, so what is untested is narrow: whether `BuildPrompt` produces a
   turn worth having, and the two paths that exist only because a run outlives its connection.
5. ⬜ **Reconcile `DockerSandboxProvider`**, then `E2bSandboxProvider` with E2B's live API, which has never
   been called. They speak the contract `tools/e2e` already exercises, so what is unverified is the "start
   this image, give me a URL" half only.
6. ⬜ The OpenAPI document filter for the hub DTOs (§3.3).
7. ⬜ Tests worth writing next: `RunRegistry` (cancel ownership, subscriber accounting), `RunWriter`'s flush
   order, and `SiteWorkspaceRegistry`'s re-seed decision against a fake sandbox — which is also where the
   `npm ci` rule belongs, since its absence rewrote a site's lockfile and committed it.
