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
3. **Edits by hand** where they want to: every section the agent wrote is editable in a property editor
   generated from the same schema the agent writes against.
4. **Publishes.** One button. The site is live at `{slug}.webly.site` from the first publish, before
   anybody has bought a domain.
5. **Connects a domain** when they have one: add the hostname, copy one DNS record, come back and check.
6. **Goes back** whenever they want. Every change is a version with a sentence describing it, and
   restoring one is a click that adds to the history rather than rewinding it.

### 1.2 What Webly deliberately is not

- **Not a page builder with a canvas.** Drag-and-drop is the thing people cannot do on a phone, cannot do
  well without design skill, and cannot ask for in words. The chat is the interface; the property editor
  is the fallback for the person who knows exactly which word they want changed.
- **Not a code generator.** The agent does not write HTML, CSS, JSX or Astro. It edits a structured
  document against a closed catalogue of sections — see `docs/domain-plan.md` for why, at length.
- **Not a host.** Hosting is Vercel's, behind one interface, and the customer never hears its name.
- **Not a CMS with plugins, themes or a marketplace.** One theme system, twelve-ish section types, and
  the extension point is a reviewed commit.

## 2. Where the architecture comes from

Webly is the third project in a line, and the point of naming them is that their mistakes are already
paid for:

- **zumiq** — ASP.NET + Angular, by the same author. The original layering and the cookie-JWT pattern.
- **cookta-rework** (`sample-projects/`, gitignored) — the household meal planner. Its authentication,
  its layering rules, its dev tooling and its "write the decision down next to the code" habit are
  carried over almost verbatim: `Webly.Api` → `Webly.Services` → `Webly.Data`, thin controllers, use
  cases with one `Execute`, nanoids across the API surface, one place per rule. Where Webly's code has a
  comment about "the reference project", this is usually it.
- **auto-grader** ("ador") — the AI course builder. Its agent framework (`Microsoft.Agents.AI`, keyed
  agents, toolkits over the same use cases the controllers call) and its **run substrate** (a run
  outliving the connection that started it, an append-only event log with a sequence number, replay on
  reconnect, an orphan reaper) are what Webly's chat is built on. cookta's `concepts/agent-chat.md` is
  the write-up of that port; Webly is where it actually shipped.

Ported deliberately from cookta:

| Piece | Why it transferred unchanged |
|---|---|
| Cookie-JWT auth, no refresh endpoint, silent rotation in middleware | The client never learns access tokens expire. Solved once, worth nothing to re-derive. |
| Default-deny authorization, verification gate in the accessors | A new endpoint is protected because the default accessor is, not because somebody remembered an attribute. |
| Email verification + password reset on one `UserSecurityToken` shape | Purpose mixed into the hash, single use, attempt counter. |
| The dev stack (`run-app.ps1`, the `webly-dev` MCP server, hybrid Postgres, YARP single origin) | Tedium already removed. Same ports and pidfile contract. |
| The layering rules and comment style | The reason both codebases are still readable. |

Not ported: households, memberships and roles (a site has one owner), the ingredient/unit domain
obviously, and ador's credit metering, provider zoo and durable job table — see `docs/agent-plan.md` §5
for each decision.

## 3. Decisions already made (don't relitigate)

1. **Latest stable everything**: .NET 10, Angular 22, Tailwind 4 + daisyUI 5, Postgres 17.
2. **A site is a structured document, not source files.** `docs/domain-plan.md`.
3. **Versions are whole-document snapshots in an append-only chain.** Restore copies forward.
4. **The section catalogue is the single registration point** for validation, the agent's tool
   description, the property editor and the renderer.
5. **Webly renders; the provider serves.** No build step on the provider's side, ever.
6. **One platform-owned Vercel account.** The customer never creates one. Bring-your-own is a later,
   additive option.
7. **One owner per site.** Collaboration is a later feature with a known shape, not a role enum now.
8. **English throughout.** Unlike the reference projects, whose UI is Hungarian.
9. **The agent may not publish, buy domains, or delete a site.** The tool list is the permission model.
10. **The preview is the renderer**, in an iframe. There is no second implementation of what a site
    looks like.
