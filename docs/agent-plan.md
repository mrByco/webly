# The editing agent and the run substrate

How the chat works, and why the machinery under it looks like this. The short version lives in
`CLAUDE.md`; this is the reasoning, ported from the reference projects rather than re-derived.

## 1. The agent

One agent, `site-editor`, built by a static `Create(IServiceProvider, key)` factory and registered keyed,
so the runner only ever holds an `AIAgent` and never knows which one it has. That shape is taken from
auto-grader, and it is what makes a second agent — or a multi-agent workflow behind `.AsAIAgent()` — one
registration and no change to anything below it.

Routing is polymorphic on the request body: `BaseAgentArgs` carries a `__type` discriminator,
`SiteEditorAgentArgs` carries the site nanoid, and `SetupScope` writes it into the scoped
`AgentRunContext` the toolkits read. A string parameter would have to be reinterpreted by every future
agent; an args class cannot be handed the wrong fields.

The model is a constant in the factory (`AiName.Sonnet_4_6`), not a setting. Changing which model edits
people's websites is a behaviour change and should be a commit somebody can read. A per-request override
exists (`args.Model` → `site-editor-{model}`) for the cheap variant, and falls back to the default when
the variant is not registered.

### 1.1 The tool list is the permission model

There is no second check. `SiteToolkit` can read the site, add and edit and move and remove pages and
sections, set the theme and set the menu. `QuestionToolkit` can ask the person something.

Absent, deliberately: publishing (a person decides when their site goes live, and a deploy spends
provider quota), domains, billing, and deleting the site. Adding one of those is a method plus a review,
and the diff shows it.

Every tool returns prose, including its failures — a tool result is the model's only feedback channel, so
"page path '/about' is already used" is worth more than a status code. That is also why `SiteDraft`
validates per operation rather than at commit: a model that made nine good edits and one bad one fixes
the bad one, instead of losing all ten at the end with no way to tell which was wrong.

### 1.2 Asking rather than inventing

`AskUser` is modelled on Claude Code's own `AskUserQuestion`: the model asks, the run blocks, the person
picks (or types), the answer comes back as the tool result. It is the reason the transport had to be
bidirectional at all.

It is also the safety property that makes write tools acceptable. When the agent does not know whether
the bakery closes at 5 or at 6, the alternative to asking is inventing, and a plausible invention
published on somebody's real website is the worst thing this product can do. The instructions say so in
those words, and the catalogue's testimonial section says it again in its own description.

A question times out after ten minutes and returns *"The person did not answer"* as the tool result — a
result, not an exception, so the model can wrap up gracefully instead of the turn dying and losing
everything it staged.

### 1.3 A turn is one version

`SiteEditSession` holds the turn's `SiteDraft`. Tools mutate it; `AgentTurnService` commits once, when the
model has stopped talking. Three properties follow, and they are the reason it works this way:

- **A turn is atomic.** A model that adds a section, edits it, then fails leaves the site as it was.
- **The history stays readable.** One turn is one version with one summary.
- **Cancelling costs nothing.** Stop is honest: the draft is discarded and nothing was written.

## 2. The run substrate

Ported from auto-grader's MASTER_PLAN Phase 3. Its own write-up of why NDJSON-over-POST was abandoned is
adopted rather than re-derived: cancellation was welded to the HTTP connection, so the server could not
tell "user pressed Stop" from "wifi blipped"; idle proxy timeouts killed long responses; there was no
resume; and it was one-directional, so no tool could ask a question.

What Webly runs:

- **`RunRegistry`** holds live runs as `RunHandle`s, each with its own `CancellationTokenSource`,
  subscriber count, unwatched clock, pending questions and **its own event log**. Run lifetime lives here,
  not on an `HttpContext` — that is the whole point. Only an explicit `Cancel` stops a run.
- **`ChatRunLauncher`** (singleton) starts a run on **its own DI scope** and `Task.Run`, returns the run id
  immediately, and emits **exactly one terminal event on every exit path**. Nothing else emits a terminal.
- **`IRunEventSink`** appends to the handle's log with a monotonic `Seq` **first**, then publishes. The
  other order loses any event emitted between a late subscriber's replay and its group join — the
  reconnect case, i.e. the case the log exists for.
- **`RunWriter`** (scoped, one bound run per scope) coalesces text deltas (200 chars / 100 ms) and
  **flushes before any non-text event**, so a tool chip cannot arrive before the sentence introducing it.
- **`RealtimeHub`** — `StartChat`, `Subscribe(kind, runId, fromSeq)` (join the group, *then* replay),
  `Unsubscribe`, `Cancel`, `Answer`, `FindRun`. Auth rides on the cookie, because a browser cannot set an
  `Authorization` header on a WebSocket upgrade; `CloseOnAuthenticationExpiration` forces the reconnect
  that re-runs the cookie middleware, so a long connection cannot outlive its own token.
- **`OrphanRunReaper`** — the mandatory counterweight to "a disconnect no longer cancels". Two minutes
  unwatched cancels a chat run; thirty minutes cancels anything; a finished handle is evicted after five,
  which is the only thing keeping the in-memory log from being a memory leak with a nice name.
- **`AgentBudget`** — 60 turns per user per hour, checked in the hub. Not an `[EnableRateLimiting]`
  attribute: the rate-limiting middleware only sees HTTP endpoints, so an attribute on a hub would look
  like a fence and be none.

### 2.1 What is *not* ported, and why

- **A `RunEvents` table.** ador needs a durable log because its jobs share it and survive restarts. A
  Webly chat run exists only inside one process and its log exists only to serve a reconnect, so the log
  lives on the handle: no entity, no migration, no sweep, one serializer path. `IRunEventSink` is the seam
  if that changes.
- **Credit metering and the provider zoo.** One `AgentBudget` and two provider registrations. Billing is
  MASTER_PLAN P7 and will meter at the turn, not per token, until there is a reason not to.
- **Durable jobs as a general mechanism.** Deployments are the one durable run Webly has, and their state
  is the `Deployment` row — see §3.
- **Langfuse/OTLP tracing and the DevUI.** Worth revisiting when there is a quality problem to measure.

### 2.2 Two defects the reference project only found live

Both are designed for here rather than rediscovered:

1. **A new conversation's run must become findable the moment its nanoid exists**, not when the run ends —
   otherwise a client that reloads mid-turn on a brand-new thread can never re-attach.
   `ChatRunLauncher` sets `handle.CorrelationId` from the turn's return value, and `GetChat` reports
   `activeRunId` so a reload re-subscribes.
2. **`ng-openapi-gen` deletes hub-only DTOs** from the generated client, because Swagger describes HTTP
   only. Until a Swashbuckle document filter pins `RunEventEnvelope`, `RunEvent` and the args classes into
   the OpenAPI document, the client declares those shapes by hand in `realtime.service.ts` — which is a
   duplication with a comment on it, not an oversight. Pinning them is a small task in P2.

## 3. Deployments as the other run kind

`RunKind` has two values and they are treated differently on purpose:

| | Chat | Deploy |
|---|---|---|
| Lifetime | seconds to minutes | seconds to minutes, but must survive a restart |
| State | `RunHandle` in memory | a `Deployment` row |
| Replay | the handle's event log | the row's status |
| Started by | the hub | an endpoint, picked up by `DeploymentJobRunner` |
| Lost on restart | yes, and nothing was committed | no |

One hub, one envelope, one client service. The deployment's run id **is** its nanoid, so a page that
reloads mid-publish can re-attach to something that outlived the process.

`DeploymentJobRunner` polls a status column rather than leasing rows. Webly runs as one instance, and the
honest version of that is a runner that says so in its comment: a second instance would take the same row
twice, and the fix is a lease column plus a heartbeat (ador has both, and needed them for twenty-minute
jobs).

## 4. Slice order

1. ✅ Packages, `AiName`, keyed clients registered only when configured, `AddWeblyAgent`.
2. ✅ `Conversation` / `ConversationMessage`, the repository, the migration.
3. ✅ Run substrate: registry, handle with log and pending questions, sink, writer, launcher, reaper.
4. ✅ `RealtimeHub` + publisher, the `ClaimsPrincipal` verified-user gate, hub JSON = the app's JSON.
5. ✅ `SiteEditorAgent` with `SiteToolkit` and `QuestionToolkit`; args and the args→key router.
6. ✅ `AgentTurnService`: thread resolution, history hydration, streaming, persistence, one commit.
7. ✅ `ChatController` (thread, archive, status) and the client's `RealtimeService` + chat panel.
8. ⬜ **Validate in the running app** — see whats_next.md. Nothing in 1–7 has been run: a real turn on a
   real site, a question answered mid-turn, a reload mid-turn, and Stop.
9. ⬜ The OpenAPI document filter for the hub DTOs (§2.2).
10. ⬜ Tests worth writing next: `RunRegistry` (cancel ownership, subscriber accounting, answer routing),
    `RunWriter` coalescing order, the question tool's timeout path, and one toolkit test proving a bad
    section write comes back as a correctable tool result.
