# Webly

**Upload nothing, write no code — describe your website and publish it.**

A self-service SaaS: somebody signs up, says what their business does, and gets a real website they can
edit by talking to it, version like source code, point their own domain at, and publish to the internet
in one click. The product UI and the generated sites are in English; code and comments are English too.

This file is the *why*: what Webly is, where its architecture comes from, and which questions were
already settled. `CLAUDE.md` is the working guide for changing the code; `MASTER_PLAN.md` is the plan of
record; `whats_next.md` says where work actually stopped.

## 1. The product

### 1.1 What a customer does

1. **Signs up**, proves their email address (blocking — a site publishes to the public internet under our
   infrastructure, so an unowned mailbox never gets that far), and names their first site.
2. **Talks to it.** "We're a bike shop in Utrecht, open since 2009, we do repairs and sell second-hand
   city bikes." The agent rewrites the page, and asks when it needs a fact it cannot know — opening
   hours, prices, whether a photo is theirs. It never invents one.
3. **Watches it happen.** The preview is their site's own `next dev`, so the page updates as the agent
   saves files. They can read the source if they want to — "you never have to touch the code" is not
   "you are not allowed to see it" — and a version's diff is in the history.
4. **Publishes.** One button. Webly builds the site and uploads the result; the site is live at
   `{slug}.webly.site` from the first publish, before anybody has bought a domain.
5. **Connects a domain** when they have one: add the hostname, copy one DNS record, come back and check.
6. **Goes back** whenever they want. Every change is a version with a sentence describing it, and
   restoring one is a click that adds to the history rather than rewinding it.

### 1.2 What Webly deliberately is not

- **Not a page builder with a canvas.** Drag-and-drop is the thing people cannot do on a phone, cannot do
  well without design skill, and cannot ask for in words. The chat is the interface.
- **Not an IDE.** The customer is not shown a file tree to work in, is never asked to fix a build error
  themselves, and never sees the word "commit". A real Next.js project is what makes the product good;
  making somebody manage one is what it exists to avoid.
- **Not a host.** Hosting is Vercel's, behind one interface, and the customer never hears its name.
- **Not a CMS with plugins, themes or a marketplace.** The extension point is asking for something in
  the chat.

## 2. Where the architecture comes from

Webly is the third project in a line, and the point of naming them is that their mistakes are already
paid for:

- **zumiq** — ASP.NET + Angular, by the same author. The original layering and the cookie-JWT pattern.
- **cookta-rework** (`sample-projects/`, gitignored) — the household meal planner. Its authentication,
  its layering rules, its dev tooling and its "write the decision down next to the code" habit are
  carried over almost verbatim: `Webly.Api` → `Webly.Services` → `Webly.Data`, thin controllers, use
  cases with one `Execute`, nanoids across the API surface, one place per rule. Where Webly's code has a
  comment about "the reference project", this is usually it.
- **auto-grader** ("ador") — the AI course builder. Its **run substrate** is what Webly's chat is built
  on: a run outliving the connection that started it, an append-only event log with a sequence number,
  replay on reconnect, an orphan reaper, exactly one terminal event. Its *agent* framework
  (`Microsoft.Agents.AI`, keyed agents, in-process toolkits) is deliberately **not** ported — Webly's
  agent is a coding-agent CLI running in a sandbox, so the substrate survives and the framework does
  not. `docs/agent-plan.md` §5 is that comparison in full.

Ported deliberately from cookta:

| Piece | Why it transferred unchanged |
|---|---|
| Cookie-JWT auth, no refresh endpoint, silent rotation in middleware | The client never learns access tokens expire. Solved once, worth nothing to re-derive. |
| Default-deny authorization, verification gate in the accessors | A new endpoint is protected because the default accessor is, not because somebody remembered an attribute. |
| Email verification + password reset on one `UserSecurityToken` shape | Purpose mixed into the hash, single use, attempt counter. |
| The dev stack (`run-app.ps1`, the `webly-dev` MCP server, hybrid Postgres, YARP single origin) | Tedium already removed. Same ports and pidfile contract. |
| The layering rules and comment style | The reason both codebases are still readable. |

Not ported: households, memberships and roles (a site has one owner), the ingredient/unit domain
obviously, and ador's credit metering, provider zoo, in-process agent framework and durable job table —
see `docs/agent-plan.md` §5 for each decision.

## 3. Decisions already made (don't relitigate)

1. **Latest stable everything**: .NET 10, Angular 22, Tailwind 4 + daisyUI 5, Postgres 17.
2. **A site is a real Next.js project.** Source files, in git, that a developer could clone and run.
   `docs/domain-plan.md` is the argument and what it costs.
3. **A version is a commit.** One bare git repository per site; the `SiteVersion` row is the index into
   it, not a copy of it. Restore writes an old tree forward as a new commit, never moves the branch back.
4. **The agent is a coding-agent CLI in a sandbox**, behind one interface, with Claude Code as the
   default and OpenCode as the second implementation. No model SDK in the solution. `docs/agent-plan.md`.
5. **The agent never sees git.** It gets a working tree; Webly commits what comes back. So it cannot
   rewrite history and needs no credential that could.
6. **Webly builds; the provider serves.** `vercel build` runs in the sandbox and a failed build is the
   publish gate, so a broken site cannot go live and the reason is a log the person can read.
7. **The preview is the site's own dev server**, proxied from this origin. There is no second
   implementation of what a site looks like, and hot reload is why an edit appears without a refresh.
8. **One platform-owned Vercel account.** The customer never creates one. Bring-your-own is a later,
   additive option.
9. **One owner per site.** Collaboration is a later feature with a known shape, not a role enum now.
10. **English throughout.** Unlike the reference projects, whose UI is Hungarian.
11. **The agent may not publish, buy domains, or delete a site.** Its sandbox has no route to any of
    them: it is a machine with the site's files on it and a model key, and nothing else.
12. **Every external dependency has a development substitute, and each one refuses to run in production.**
    A sandbox provider that needs no Docker, an agent that needs no model key, a deployment target that
    needs no hosting account — so a clone runs with node, git and a Postgres, and the paths that matter most
    (a turn, a commit, a build, a publish) are testable by anyone. The substitutes do the real work wherever
    the real work is ours: the local sandbox speaks the same contract, the mock agent makes a real edit, and
    the filesystem target runs the real build and lets a failure block the publish. `CLAUDE.md` "Running it
    with nothing installed" is the table; `tools/e2e/run.mjs` drives the whole loop without the backend.
