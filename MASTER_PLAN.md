# Webly — plan of record

What is built, what is next, in the order it makes sense to build it. `whats_next.md` is the handoff note
saying where work actually stopped; this file is the map.

Status: **P0 landed as a skeleton, unbuilt.** The repository was initialized in an environment with no
.NET SDK, so no phase below has been compiled, run or seen working. That is the first task — see
`whats_next.md`.

---

## P0 — Skeleton, auth, domain model, agent, publishing *(written, not run)*

Everything in this phase exists in the repository:

- **Scaffolding**: `Webly.slnx`, central package management, the dev stack (`run-app.ps1`, the `webly-dev`
  MCP server, hybrid dev Postgres on 5434, YARP single origin at `https://localhost:5000`), CI, the
  Portainer stack, both Dockerfiles.
- **Authentication**, ported from the reference project: cookie-JWT with silent rotation, default-deny
  authorization, blocking email verification with a link *and* a code, password reset, optional Google
  sign-in, an access-token blacklist, per-IP mail rate limiting.
- **The domain**: `Site`, `SiteVersion` (append-only, whole-document snapshots), the section catalogue and
  its validator, `SiteDraft` as the one editing path, `Domain`, `Deployment`, `Conversation`.
- **The renderer**: six section types, a theme-derived stylesheet, sitemap, robots, one page per
  directory. Also the editor's preview, which is the same renderer behind an iframe.
- **The agent**: one keyed `site-editor` agent on `Microsoft.Agents.AI`, a toolkit that is the permission
  model, a question tool that blocks the run, and a turn that commits exactly one version.
- **The run substrate**: registry, in-memory replay log, sink, coalescing writer, launcher with one
  terminal event, orphan reaper, SignalR hub, per-user turn budget.
- **Publishing**: `PublishSite` → `Deployment` row → `DeploymentJobRunner` → render → Vercel → the
  published pointer moves. Domains: add, mirror the DNS record, check, promote, remove.
- **The client**: Angular 22 SSR, auth screens, site list, first-site onboarding, the editor (chat +
  preview), history with restore, domains, settings. Builds; templates type-check.

**Known gaps inside P0**, each with a note in the code:

| Gap | Where |
|---|---|
| Nothing has been compiled or run; no EF migration has been generated yet | `whats_next.md` |
| `client/src/app/api/` (the generated client) does not exist yet, so the client does not type-check against real DTOs | `CLAUDE.md`, `whats_next.md` |
| `VercelDeploymentTarget` is unverified against the live API | `docs/deploy-plan.md` §6 |
| The `Microsoft.Agents.AI` streaming surface is unverified against the pinned package | `AgentTurnService.StreamAsync` |
| Rich text is escaped rather than sanitized, so it renders visibly | `SectionMarkup.RichTextHtml` |
| Hub DTOs are declared by hand in the client | `docs/agent-plan.md` §2.2 |

---

## P1 — Make it real

The phase that turns the skeleton into a running product. No new features.

1. `dotnet build`, then `dotnet ef migrations add InitialCreate`, then **hand-write the deferred-constraint
   SQL** into that migration (see `CLAUDE.md` "Deferred foreign keys") and run
   `WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them`.
2. Start the stack, run `regen_api`, commit `client/src/app/api/`, fix whatever the generated names
   actually are, and get `client_typecheck` and `client_build` green.
3. Reconcile `AgentTurnService.StreamAsync` with `Microsoft.Agents.AI` 1.9.0.
4. Drive the real app: register, verify, create a site, one agent turn, answer a question, reload
   mid-turn, press Stop. Screenshots as evidence.
5. Reconcile `VercelDeploymentTarget` against a real token: publish, add a domain, verify it.

## P2 — The editor people can actually use

- **The property editor**, generated from `SectionSchemaResponse`: a form per field kind, one version per
  committed edit (the client debounces; a version per keystroke is not a history).
- **Section reordering and adding by hand**, not only by asking.
- **A page switcher** in the preview, and a page list in the editor.
- The hub-DTO OpenAPI document filter, so the client stops declaring them by hand.
- Rich text done properly: an editor in the client, a sanitizer (AngleSharp or HtmlSanitizer) on the way in.

## P3 — The rest of the catalogue

Gallery, Pricing, LogoCloud, Stats, Steps, Team, LocationMap. Each is four edits: the enum, the schema, a
renderer, a preview thumbnail. Plus a section picker in the client that reads the catalogue.

## P4 — Forms

A contact section needs somewhere for submissions to go: `FormSubmission`, an email to the owner, a
honeypot and a rate limit, and a submissions list in the editor. A slice, not a section type — which is why
`ContactForm` is last on the catalogue's own list.

## P5 — Assets

Uploads, a blob store (`ISiteAssetStore`), image conversion, and a resolver that tells a blob name from an
external URL. Lands in two seams that already exist: `SectionFieldKind.Image` and the renderer's
`ResolveImage`. Deployments then carry the asset files alongside the HTML.

## P6 — Growth surface

The public marketing site, a template gallery seeded from real starter documents, and an example site
anybody can look at without signing up. Note the reference project (auto-grader) grew a whole third
top-level app for its public pages; Webly's own marketing site is a Webly site, which is the more honest
demonstration — and the first real test of the product's own limits.

## P7 — Money

Plans, Stripe, the site limit enforced by a plan rather than a constant, turn metering, and the "you have
reached your plan's limit" paths that are currently one conflict response.

---

## Further out, deliberately unscheduled

- **Collaboration**: a membership table plus one clause in `FindForOwnerAsync`.
- **Locales**: a second document tree per site, or per page. Do not guess this early.
- **A/B testing or analytics**: a reason to own the rendered page's script tag; none yet.
- **Scale-out**: the three per-process pieces — the access-token blacklist, `RunRegistry` and
  `AgentBudget` — move to shared storage, and `DeploymentJobRunner` gets a lease column. Each one is
  commented where it is.
