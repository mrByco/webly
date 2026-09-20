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

## P0 — Skeleton, auth, source-backed sites, the agent, publishing *(written, not run)*

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

**The loop has been driven end to end**, by `tools/e2e/run.mjs`: real git plumbing, the real sandbox agent, the
real `claude` CLI, the real Next.js dev server and build, fourteen asserted steps ending at a published page
that says what the person typed. So the parts owned by somebody else — git, the CLI's stream format, the
framework — are evidence. What remains is what needs a compiler.

**Known gaps inside P0**, each with a note in the code:

| Gap | Where |
|---|---|
| No C# has been compiled, so every file is unverified *as C#*; no EF migration has been generated | `whats_next.md` |
| `client/src/app/api/` (the generated client) does not exist yet, so the client does not type-check against real DTOs | `CLAUDE.md`, `whats_next.md` |
| `OpenCodeAgent` has never run, and its output is prose rather than a typed stream | `OpenCodeAgent` class comment |
| `E2bSandboxProvider` has never called E2B | `E2bSandboxProvider` class comment |
| `VercelDeploymentTarget` is unverified in both halves — REST and CLI | `docs/deploy-plan.md` §6 |
| Hub DTOs are declared by hand in the client | `docs/agent-plan.md` §3.3 |

Closed since the pivot, by running things rather than reading them: `ClaudeCodeAgent`'s stream-json format is
reconciled against a recorded turn and covered by `ClaudeStreamJsonParserTests`; the preview proxy was broken
and is fixed; the sandbox agent leaked a dev server per stop and does not; `Directory.Build.props` was missing
entirely, so nothing could have built at all.

---

## P1 — Make it real

The phase that turns the skeleton into a running product. No new features.

0. `node tools/e2e/run.mjs --agent mock` first, because it needs nothing and it is the fastest way to find out
   whether the machine can do the things the product needs at all.
1. `dotnet build`, then `dotnet ef migrations add InitialCreate`, then **hand-write the deferred-constraint
   SQL** into that migration (see `CLAUDE.md` "Deferred foreign keys") and run
   `WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them`.
2. Run `GitSiteRepositoryStoreTests` and `ClaudeStreamJsonParserTests`. Neither needs Postgres, Docker or a
   key, so they are the first suites that can be green — and between them they cover the layer where being
   wrong loses somebody's website and the one whose input format belongs to another product.
3. Start the stack, run `regen_api`, commit `client/src/app/api/`, fix whatever the generated names actually
   are, and get `client_typecheck` and `client_build` green.
4. **One real turn through the app**, with the defaults (local sandbox, mock agent, filesystem publish) and no
   credentials at all: register, verify, create a site, send a message, watch the preview change, publish, open
   the published page. Everything in that sentence has been proven to work from node; this is the step that
   proves the C# orchestrating it does.
5. Then the same with `Agent:ClaudeCode:ApiKey` set, and a reload mid-turn and Stop — the two paths that only
   exist because a run outlives its connection.
6. Reconcile `VercelDeploymentTarget` against a real token: publish, add a domain, verify it.

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
