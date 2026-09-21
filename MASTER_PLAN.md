# Webly — plan of record

What is built, what is next, in the order it makes sense to build it. `whats_next.md` is the handoff note
saying where work actually stopped; this file is the map.

Status: **P0 is running and P1 is most of the way through.** The whole loop has been driven in the running app
and every screen of it pressed in a browser; what is left in P1 needs credentials or a Docker daemon, which is
the only reason it is left. `whats_next.md` is where to start.

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
constraints, 84 tests pass, and `client/src/app/api/` is generated and committed.

**And every screen has been pressed by hand**, which is where most of the defects below came from: a turn
reloaded mid-flight and stopped; publishing, succeeding and failing, with its progress and its build log;
restore; domains end to end against the simulated provider; the code view and the git-bundle download, cloned
and checked; the forgotten-password round trip including a replayed link; closing an account; waking a preview;
and the app at 390 px and in dark mode.

**Known gaps inside P0**, each with a note in the code:

| Gap | Where |
|---|---|
| `OpenCodeAgent` has never run, and its output is prose rather than a typed stream | `OpenCodeAgent` class comment |
| `DockerSandboxProvider` and `E2bSandboxProvider` have never run; only `local` has | their class comments |
| `VercelDeploymentTarget` is unverified in both halves — REST and CLI | `docs/deploy-plan.md` §6 |
| The real agent has never run *inside the app* — every turn so far has been the mock | `whats_next.md` §1 |
| Google sign-in and Resend are absent without their configuration, by design | `CLAUDE.md` "An unconfigured feature is absent" |

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
2. ~~`GitSiteRepositoryStoreTests` and `ClaudeStreamJsonParserTests`~~, then the rest: 70 tests, green, and
   `WEBLY_TEST_POSTGRES` lets the Postgres-backed ones run without Docker.
3. ~~`regen_api`, commit `client/src/app/api/`, get the client green~~ — and keep
   `SupportNonNullableReferenceTypes()` in `AddSwaggerGen`, without which every string in the generated client
   is nullable and its types stop meaning anything.
4. ~~**One real turn through the app**~~ with the defaults and no credentials: register, verify, create a site,
   send a message, watch the version commit, fetch the preview, publish, open the published page. That is what
   `tools/e2e/turn.mjs` does, and it found four bugs that reading had not.
5. ~~**A reload mid-turn and Stop**~~ — the two paths that only exist because a run outlives its connection,
   driven in a browser against a cold workspace. The reload found nothing until the run's correlation id
   stopped being assigned from a value that arrives when the turn is over. **Still to do: the same turn with
   `Agent:ClaudeCode:ApiKey` set**, which is the next thing, and the only part of the chat path the mock cannot
   stand in for.
6. **Reconcile `VercelDeploymentTarget`** against a real token: publish, add a domain, verify it.
7. **Build the sandbox image** and run the same turn with `Sandbox:Provider=docker`. The prebaked-dependencies
   bet — `npm ls --depth=0` passing without an install — is the one thing the local provider cannot tell you.

## P2 — The editor people can actually use

- ~~**The code view**~~: done. A Code tab in the editor, one level of grouping by directory, a read-only
  viewer with line numbers. Read-only deliberately — a save button there would be a second definition of what
  a version is.
- ~~**Better diffs** in the history: per-file collapse~~ — done: parsed into files, each a collapsible block
  with its own `+`/`−` counts and a badge for a file that was added, deleted or renamed, expanded by default
  only when the version touched three files or fewer. **Side-by-side** for the file somebody clicks is what is
  left, and it is worth less than it sounds while a turn's diff is usually one line.
- **Agent choice on screen**, if the second agent turns out to be worth offering rather than only worth
  having.
- ~~The hub-DTO OpenAPI document filter~~: done. `HubContractDocumentFilter` puts `RunEvent` and friends into
  the document, so the client stops declaring the realtime contract by hand and a value added to `RunEventType`
  is a compile error rather than a silent divergence.
- **The cost dial.** A warm sandbox per open editor is the product's real unit cost; `Sandbox:IdleTimeout` is
  still a guess. One side is measured — a cold turn is 16 s and a warm one 3.8 s with the local provider — and
  the part that decides the timeout is what a real container adds to that. Measure it before P7 prices
  anything.

## P3 — Make the agent good at this

The phase that is now the product rather than a catalogue expansion:

- **Iterate on `AGENTS.md` against real turns.** It is the closest thing to a prompt this product has, and a
  change to it now reaches existing sites: `SyncSiteInstructions` refreshes a site's copy before a turn and
  commits it as its own version. That question — raised here when the forms rule landed and would have reached
  nobody — is answered; what is left is the iterating, which needs a model key and real turns to judge.
- ~~**Give the agent the build's own feedback loop**~~ — done, and it is a check rather than an instruction.
  The turn runs `npm run typecheck` in the workspace after the dev-server log check and reports what it says as
  the same `BuildFailed` event, because the dev server compiles with SWC and cannot see a type error at all.
  `AGENTS.md` still asks the agent to run it — its run is what fixes the error before finishing, ours is what
  makes the report true when it did not. Ask the mock agent to "break the types" to see it. It also found the
  bug that asking had already introduced: `tsc --incremental` writes its cache beside `tsconfig.json`, so every
  turn that obeyed would have committed it into the customer's history.
- ~~**Starter variety.**~~ Done, and not as this line imagined it. A handful of templates would be a handful
  of copies of every convention — the contact form, the images rule, the SEO files — to keep in step the next
  time one changes, which is a cost paid on every future rule rather than once. So it is **one template and
  five looks**: `SiteLooks` writes a hue, a chroma and a card radius into `src/app/look.css` at creation, and
  the palette including the neutrals derives from them. Two new sites no longer look alike, and "make it
  green" is an edit to one file rather than a theme system. What this does not give is a different *shape* of
  site — a restaurant's menu page, a tradesperson's service list — and that is the agent's job on the first
  turn, which is the thing to judge once there is a model key.
- **Regression tests for turns**, which means recorded transcripts and assertions about the tree, not about
  the prose.

## P4 — Forms

~~A contact page needs somewhere for submissions to go~~ — done, and the decision this section left open
answered itself the moment it was looked at: **a published site is a static export, so it has no server of its
own**. There is nowhere in it for a form to post to, and a route handler the agent wrote would compile and then
404 in front of a customer. So the endpoint is Webly's.

What landed: `FormSubmission` with its fields as one jsonb column (the agent writes the form, so a fixed set of
columns would need a migration per question somebody adds), `POST /api/public/forms/{siteNanoid}`, a honeypot,
a per-IP-and-site rate limit and per-site caps for the day and the hour, an email to the owner with the
visitor's address as its reply-to, a Messages tab in the editor, and a working contact page in the starter
template.

Three decisions worth not re-deriving:

- **It takes an ordinary form post, not JSON**, so it needs no CORS entry and no JavaScript. A cross-origin
  `<form method="post">` is something browsers have always allowed, and a contact form is the last thing on a
  small business's site that should stop working because a script did not load.
- **It answers 303 back to the page the form was on**, resolving the form's own relative `_next` against the
  `Referer`. Not an open redirect worth the name: the target can only be reached by *posting* from a page that
  already had the visitor, and a link in an email cannot produce a POST.
- **`ISiteRepository.FindForSubmissionAsync` is the one lookup here with no ownership check**, and it says so
  at length. What keeps it safe is that the operation cannot read anything back.

Still open, and deliberately: no read state and no delete on a submission (both need a decision about what the
editor does with the state), no attachments (P5), and no spam scoring beyond the honeypot — which is what
actually catches the traffic this endpoint will see.

## P5 — Assets

~~Uploads, a blob store, image conversion, and a URL a sandbox can fetch~~ — done, and three of those four
turned out to be the wrong shape. There is **no blob store**: an image is committed into the site's own
repository under `public/images/`, so the published site serves its own photographs from its own domain, they
travel with the export, and they are in the history like every other change. There is **no URL for the sandbox
to fetch**, because the files are simply in the tree it is seeded with. And the **conversion happens in the
browser** — a canvas re-encode before the upload — which keeps an image library out of the backend and means
the megabytes never cross the network.

What is left for a later round: a way to delete an image from the editor (an agent turn can do it today, and a
delete that left a page pointing at nothing would need to say so), and thumbnails in the chat instead of
paths. Video is deliberately not on that list — a git repository is the wrong place for it, and that is the
point at which a blob store becomes the right answer after all.

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
