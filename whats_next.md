# Where this stopped

`MASTER_PLAN.md` is the map. This is the handoff note: what is now evidence, what is still intent, and what
to do first.

## What happened

Webly was designed and written in one pass from the two reference projects — the layering, the authentication
and the dev stack from **cookta-rework**, the run substrate from **auto-grader** — and then the model of a
site changed completely. The first pass made a site a structured jsonb document over a closed catalogue of
section types, rendered by C# we own. It is now **a real Next.js project in a bare git repository, edited by
a coding agent's CLI running in a sandbox**. `docs/domain-plan.md` §1 argues that and is honest about what
the document model was better at; the catalogue, the validator, `SiteDraft` and the six section renderers
were deleted rather than left half-supported.

Then it was made to run. That is the part worth reading, because almost everything below was found by
running it and none of it by reading.

## What is now evidence

**The product loop works in the running app.** Register → verify from the mail in `.run/mail/` → create a
site (a bare repository with the template in one commit) → three turns over the hub, each committing one
version → the preview served through Webly's own origin → publish → a page at `/published/{nanoid}/` that
says what the person typed. `tools/e2e/turn.mjs` is what starts a turn, because `StartChat` is a hub method
and a SignalR client is the only way to press that button.

**The backend compiles, migrates and tests.** `dotnet build Webly.slnx` is clean, `InitialSchema` is applied
to a real Postgres with the ten deferrable constraints written into it by hand, and all 70 tests pass.
`PostgresTestBase` will use an existing server (`WEBLY_TEST_POSTGRES`) instead of Testcontainers, so the
suite runs where there is a Postgres and no Docker.

**The client is real.** `client/src/app/api/` is generated from the live swagger — 29 models, 8 services —
and the Angular app type-checks and prerenders against it.

**The lower layers were already evidence** before any of that, from `tools/e2e/run.mjs`: the git plumbing
sequence, the sandbox agent's HTTP contract, the real `claude` CLI (including that it follows the `SUMMARY:`
convention and edits `content/brand.md` unasked, because `AGENTS.md` tells it to), the real `next dev`, the
real build. `tools/e2e/README.md` lists the bugs it caught. Hot reload is evidence too: an external write to
`page.tsx` reaches the served HTML in about four seconds, which is what "the preview updates itself" rests
on.

### The ones the running app caught

Kept here because each is a shape of mistake that will recur, not because the fix is interesting.

1. **The preview proxy answered 500 to every request.** YARP's `IHttpForwarder` throws on an `HttpClient` and
   wants a bare `HttpMessageInvoker`. Nothing about the code looked wrong.
2. **A published site 502ed with its `index.html` on disk.** `WebApplication` inserts `UseRouting()` at the
   front of the pipeline unless you call it yourself, `MapReverseProxy` is a catch-all, and static files stand
   down when an endpoint is already selected. Nothing logged anything.
3. **Seeding a workspace rewrote the site's source.** `npm install` writes `package-lock.json` back, so a
   turn's diff was the headline asked for *plus* eighty-four deleted lines of lockfile — and the publish path
   `npm ci`s against that lockfile. It is `npm ci` first now.
4. **A publish reported Ready and served nothing**, because one of five relative paths was not anchored.
5. **`InvariantGlobalization` was on**, which makes `String.Normalize` a silent no-op, so "Kovács Bicikli"
   was published at `kov-cs-bicikli`. A site's address is permanent.
6. **Account deletion cannot go through EF at all** — a message points at the version it produced and that
   version points back at the message, so the client-side cascade reports a circular dependency and sends
   nothing. The deferred constraints are what make the cycle harmless in Postgres, and the delete is one
   statement.
7. **Neither launcher could start the app**, and each was correct: `dotnet run` ignores the working directory
   a launcher sets. `PathAnchor` ends that argument.
8. **The OpenAPI document threw away every nullable annotation** without
   `SupportNonNullableReferenceTypes()`, and an `IActionResult` describes no type at all.
9. **The preview served the HTML and nothing else.** Next.js writes absolute asset URLs, so a dev server
   proxied under a path asked for `/_next/...` at the root of Webly's origin and every one of those 502ed.
   The product's main surface had never rendered with a stylesheet. The harness had been reading the
   document, and the document was perfect.
10. **Both editor panes were the height of their own contents**, because a custom element is a plain block
    and the `h-full` inside resolved against nothing.
11. **Logging in lasted fifteen minutes.** A browser's parallel requests all present the same refresh cookie
    when the access token dies; one rotates it and the rest were read as theft, which revoked the chain.

## What is still intent

- **`VercelDeploymentTarget`** — both halves, the REST calls from this process and the CLI inside the
  publishing sandbox. `docs/deploy-plan.md` §6 lists what to reconcile, in order.
- **`OpenCodeAgent`** — written against the documented interface of `opencode run`, never executed.
- **`DockerSandboxProvider` and `E2bSandboxProvider`** — only `local` has run. They all speak the contract
  `tools/e2e` exercises, which is what makes that cheap to find out.
- **The real agent inside the app.** The `claude` CLI has run under `tools/e2e/run.mjs` and its transcript is
  the parser's fixture, but every turn through the running app has been the mock.
- **The sandbox image has never been built**, for want of a Docker daemon.
- **Google sign-in and Resend**, absent by design without their configuration.

## The first five things, in order

### 1. One real turn with the real agent

`Agent:ClaudeCode:ApiKey` in user secrets, then a conversation — not a message. Ask for something that needs
a fact the agent does not have ("we do repairs, we're in Utrecht") and watch it ask rather than invent; answer
it; watch the second turn build on the first.

The mock agent is a stand-in for the model, not for the plumbing, so what is genuinely untested here is
narrow: whether `BuildPrompt` produces a turn worth having, and whether the `SUMMARY:` line survives a long
answer. **A reload mid-turn and Stop are already done** — the two paths that exist only because a run
outlives its connection, driven in a browser against a cold workspace, which is what makes the window wide
enough to press a button in. The reload found nothing until the run's correlation id stopped being assigned
from a value that arrives when the turn is over; Stop leaves no commit and a "Stopped. Nothing was changed."
line in the thread.

### 2. The sandbox in a container

```
docker build -f deploy/sandbox/Dockerfile -t byc0/margareta:webly_sandbox .
```

Then set `Sandbox:Provider` to `docker` and watch the same chain the local provider already passes: health,
the tree in `/workspace`, `npm ls --depth=0` succeeding **without an install** — that is the prebaked
dependencies bet and the only thing the local provider cannot tell you — `next dev`, and the preview through
the proxy. Then edit a file inside the container by hand and confirm the browser updates by itself: the
WebSocket forward is the one part of the proxy that fails silently rather than loudly.

### 3. Vercel

A real token, then publish a starter site, add a domain, check it. The build already runs in a fresh sandbox
and fails honestly, so what is unverified is narrower than the whole path: the two provider calls and the
CLI's output parsing.

### 4. Measure `Sandbox:IdleTimeout`

Ten minutes is a guess, and it is the product's real unit cost. A warm sandbox bills by the second and a cold
one costs a person tens of seconds of staring at "your preview is asleep". Nobody has measured either side.

### 5. Reconcile the Vercel domain attach with what the API answers

Publishing now asks the provider to serve the site's own `{slug}.{BaseDomain}` address, because nothing did
and so that address — the one every screen prints — resolved nowhere. What is unknown is what Vercel answers
for a subdomain of a zone already pointed at it: if it comes back verified, `Site.AddressReadyAt` is stamped
and the header links the address; if it comes back pending with a challenge, the platform address needs the
same "come back and check" step a customer's domain has. Until then a published site is linked at its
deployment's own URL, which is what development does for ever.

*(The hub DTOs are no longer on this list: `HubContractDocumentFilter` puts them in the OpenAPI document and
the client re-exports the generated types.)*

## Things deliberately left undone, so nobody hunts for them

- **The code view is read-only.** A save button there would be a second way for a site to change, and so a
  second definition of what a version is. If hand editing lands it lands through `CommitSiteVersion`.
- **The agent cannot ask a blocking question.** It asks in its reply and the turn ends; the answer is the
  person's next message. The MCP bridge that would make it a tool again is `docs/agent-plan.md` §1.3.
- **One template**, so every site starts the same shape. P3.
- **No forms, no uploads, no billing.** P4, P5, P7.
