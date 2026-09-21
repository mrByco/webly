# Webly — plan of record

What is built, what is next, in the order it makes sense to build it. `whats_next.md` is the handoff note
saying where work actually stopped; this file is the map.

Status: **P0 landed as a skeleton, unbuilt.** The repository was initialized in an environment with no .NET
SDK, so no phase below has been compiled, run or seen working. That is the first task — see `whats_next.md`.

Note that P0 was built twice. The first pass expressed a site as a structured jsonb document over a closed
section catalogue, with a C# renderer; it was then replaced by real Next.js source edited by a coding agent.
`docs/domain-plan.md` §1 is the argument, with both halves of the trade. The catalogue, the renderer and
`SiteDraft` were deleted rather than left in place, so the earlier design is in git history and nothing in
this tree half-supports it.

---

## P0 — Skeleton, auth, source-backed sites, the agent, publishing *(shipped and running)*

Everything in this phase exists in the repository:

- **Scaffolding**: `Webly.slnx`, central package management, the dev stack (`run-app.ps1`, the `webly-dev`
  MCP server, hybrid dev Postgres on 5434, YARP single origin at `https://localhost:5000`), CI, the
  Portainer stack, three Dockerfiles.
- **Authentication**, ported from the reference project: cookie-JWT with silent rotation, default-deny
  authorization, blocking email verification with a link *and* a code, password reset, optional Google
  sign-in, an access-token blacklist, per-IP mail rate limiting.
- **The domain**: `Site` with two pointers into a commit index, `SiteVersion` as that index, `Domain`,
  `Deployment`, `Conversation`.
- **The repository layer**: `ISiteRepositoryStore` over real git plumbing — init from the template, write a
  tree and commit it, read a tree, read a file, diff, restore by writing an old tree forward.
  `GitSiteRepositoryStoreTests` covers it against a temporary repository.
- **The site template** (`templates/next-site`): a Next.js 15 App Router project that builds, with
  `AGENTS.md` (the standing rules) and `content/brand.md` (the agent's memory) committed into every site.
- **Sandboxes**: `tools/sandbox-agent` (the HTTP contract, zero dependencies), `SandboxAgentClient`, a Docker
  provider for development and an E2B provider for production, and `deploy/sandbox/Dockerfile`.
- **Workspaces**: one warm sandbox per open site, leased, re-seeded when the head moves, reaped when idle.
- **The agent**: `ICodingAgent` with `ClaudeCodeAgent` and `OpenCodeAgent`, a registry, and a turn that
  commits exactly one version from the tree that comes back.
- **The run substrate**: registry, in-memory replay log, sink, coalescing writer, launcher with one terminal
  event, orphan reaper, SignalR hub, per-user turn budget.
- **Publishing**: `PublishSite` → `Deployment` row → `DeploymentJobRunner` → a fresh sandbox → `vercel build`
  → `vercel deploy --prebuilt` → the published pointer moves. Domains: add, mirror the DNS record, check,
  promote, remove.
- **The client**: Angular 22 SSR, auth screens, site list, first-site onboarding, the editor (chat + the
  proxied preview), history with diffs and restore, domains, settings with the build log.

Plus the substitutes that make it runnable with nothing installed — a local sandbox provider, a mock agent and
a filesystem deployment target, each refused outside Development. `CLAUDE.md` "Running it with nothing
installed" is the table.

**The loop has been driven end to end twice over**, and the two runs answer different questions.
`tools/e2e/run.mjs` drives everything under the C# for real — git plumbing, the sandbox agent, the `claude`
CLI, `next dev`, the build — in sixteen asserted steps. `tools/e2e/turn.mjs` drives the C# itself, through the
running backend's own hub. Between them: a verified account, a site that is a bare repository, turns that
commit one version each, a preview through Webly's origin, and a published page that says what the person
typed. The backend compiles, `InitialSchema` is applied to a real Postgres with its ten deferrable
constraints, 68 tests pass, and `client/src/app/api/` is generated and committed.

**Known gaps inside P0**, each with a note in the code:

| Gap | Where |
|---|---|
| `OpenCodeAgent` has never run, and its output is prose rather than a typed stream | `OpenCodeAgent` class comment |
| `DockerSandboxProvider` and `E2bSandboxProvider` have never run; only `local` has | their class comments |
| `VercelDeploymentTarget` is unverified in both halves — REST and CLI | `docs/deploy-plan.md` §6 |
| The real agent has never run *inside the app* — every turn so far has been the mock | `whats_next.md` §1 |
| Hub DTOs are declared by hand in the client | `docs/agent-plan.md` §3.3 |
| A published version cannot be republished: 409, and no way to retry a lost deployment | `whats_next.md` |

Closed by running things rather than reading them, which is the only reason any of it is closed:
`Directory.Build.props` was missing entirely, so nothing could have built; the preview proxy was broken twice
over, first in the sandbox agent and then in the controller that handed YARP an `HttpClient`; the sandbox
agent leaked a dev server per stop; `InvariantGlobalization` silently disabled accent folding, so a site named
"Kovács Bicikli" was published at `kov-cs-bicikli`; seeding a workspace rewrote the site's lockfile and
committed it; a publish reported Ready and served nothing; static files never ran because `WebApplication`
inserts `UseRouting` in front of them; and account deletion turned out to be impossible through EF's change
tracker. `whats_next.md` is the list with the reasons.

---

## P1 — Make it real

The phase that turns the skeleton into a running product. No new features. **Steps 0–4 are done**; the
paragraphs are kept because each one names a thing to check again after a change of that kind.

0. ~~`node tools/e2e/run.mjs --agent mock`~~, because it needs nothing and it is the fastest way to find out
   whether the machine can do the things the product needs at all. Sixteen steps, all green.
1. ~~`dotnet build`, then the migration, then the deferred-constraint SQL by hand~~ — `InitialSchema` carries
   ten `ALTER CONSTRAINT … DEFERRABLE INITIALLY DEFERRED` statements, and the canary that justifies them
   passes. It also turned out that the delete cannot go through EF's change tracker at all; the test says why.
2. ~~`GitSiteRepositoryStoreTests` and `ClaudeStreamJsonParserTests`~~, then the rest: 68 tests, green, and
   `WEBLY_TEST_POSTGRES` lets the Postgres-backed ones run without Docker.
3. ~~`regen_api`, commit `client/src/app/api/`, get the client green~~ — and keep
   `SupportNonNullableReferenceTypes()` in `AddSwaggerGen`, without which every string in the generated client
   is nullable and its types stop meaning anything.
4. ~~**One real turn through the app**~~ with the defaults and no credentials: register, verify, create a site,
   send a message, watch the version commit, fetch the preview, publish, open the published page. That is what
   `tools/e2e/turn.mjs` does, and it found four bugs that reading had not.
5. **Then the same with `Agent:ClaudeCode:ApiKey` set**, and a reload mid-turn and Stop — the two paths that
   only exist because a run outlives its connection. This is the next thing to do.
6. **Reconcile `VercelDeploymentTarget`** against a real token: publish, add a domain, verify it.
7. **Build the sandbox image** and run the same turn with `Sandbox:Provider=docker`. The prebaked-dependencies
   bet — `npm ls --depth=0` passing without an install — is the one thing the local provider cannot tell you.

## P2 — The editor people can actually use

- ~~**The code view**~~: done. A Code tab in the editor, one level of grouping by directory, a read-only
  viewer with line numbers. Read-only deliberately — a save button there would be a second definition of what
  a version is.
- **Better diffs** in the history: per-file collapse, and a side-by-side for the file somebody clicks.
- **Agent choice on screen**, if the second agent turns out to be worth offering rather than only worth
  having.
- The hub-DTO OpenAPI document filter, so the client stops declaring them by hand.
- **The cost dial.** A warm sandbox per open editor is the product's real unit cost; `Sandbox:IdleTimeout`
  is currently a guess. Measure it before P7 prices anything.

## P3 — Make the agent good at this

The phase that is now the product rather than a catalogue expansion:

- **Iterate on `AGENTS.md` against real turns.** It is the closest thing to a prompt this product has, and
  it ships in every site's repository — so a change applies to existing sites immediately.
- **Give the agent the build's own feedback loop**: today it reads a dev server log; a `npm run typecheck`
  it is told to run before finishing is probably cheaper than a failed publish.
- **Starter variety.** One template produces one shape of website. A handful of templates, chosen by what
  the person says in their first message, is the smallest honest answer.
- **Regression tests for turns**, which means recorded transcripts and assertions about the tree, not about
  the prose.

## P4 — Forms

A contact page needs somewhere for submissions to go: `FormSubmission`, an email to the owner, a honeypot
and a rate limit, a submissions list in the editor. With real source this is no longer a section type — it is
a route handler the agent can write — so the decision to make first is whether the endpoint is the site's or
Webly's.

## P5 — Assets

Uploads, a blob store, image conversion, and a URL a sandbox can fetch. The agent writes `<Image>` tags
today against URLs somebody pasted; a customer photographing their shop front is the next step.

## P6 — Export, then growth surface

- ~~**Export**~~: done, and done as a **git bundle** rather than a tarball, so `git clone` on it gives a
  working project with every version. `GET /api/sites/{nanoid}/export`, linked from the settings screen. What
  remains is the half that needs the customer's credentials: a push to their own GitHub.
- The public marketing site, a template gallery, and an example site anybody can look at without signing up.
  Webly's own marketing site should be a Webly site, which is the more honest demonstration — and the first
  real test of the product's own limits.

## P7 — Money

Plans, Stripe, the site limit enforced by a plan rather than a constant, and metering that now has two lines
rather than one: model usage per turn and sandbox seconds. The "you have reached your plan's limit" paths are
currently one conflict response.

---

## Further out, deliberately unscheduled

- **Collaboration**: a membership table plus one clause in `FindForOwnerAsync`.
- **Locales**: the App Router's `[locale]` segment, which is much less of a decision than it was under the
  document model. Still not worth guessing early.
- **An MCP bridge into the sandbox**, which is what would let the agent ask a blocking question again —
  `docs/agent-plan.md` §1.3.
- **A staging branch per site.** `Site.DefaultBranch` exists so that the day this is wanted, it is not a
  migration.
- **Scale-out**: the four per-process pieces — the access-token blacklist, `RunRegistry`, `AgentBudget` and
  `SiteWorkspaceRegistry` — move to shared storage, and `DeploymentJobRunner` gets a lease column. The
  workspace registry is the sharpest of them: two instances would each hold a warm sandbox for the same site
  and commit over each other. Each one is commented where it is.
