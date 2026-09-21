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
site (a bare repository with the template in one commit) → turns over the hub, each committing one version →
the preview served through Webly's own origin → publish → a page at `/published/{nanoid}/` that says what the
person typed. `tools/e2e/turn.mjs` is what starts a turn, because `StartChat` is a hub method and a SignalR
client is the only way to press that button.

A customer's own photographs are in it as well: a JPEG uploaded from the chat was committed as a version
(`Added shopfront.jpg`, one binary file), served byte for byte by the preview's dev server, and then by the
published site at `/published/{nanoid}/images/shopfront.jpg` after a publish — no blob store anywhere in that
sentence.

A visitor who is not a customer can reach it too: the starter template ships a working contact page, and a
form posted from the published site at `/published/{nanoid}/contact/` answered 303 back to that page, put the
message in the owner's Messages tab with the visitor's own labels on it, and wrote an email whose reply-to was
the address they left.

**And every screen of it has been pressed in a browser**, which is where most of the defects below came from:
a turn reloaded mid-flight and stopped; publish (succeeding and failing) with its progress and its build log;
restore; the domains flow end to end against the simulated provider; the code view and the git-bundle download,
cloned and checked; the whole forgotten-password round trip including a replayed link; a fourth site refused;
"wake it up"; and the app at 390 px and in dark mode. `SiteIsolationTests` drives every route under a site as a
second account, which is the rule that most wants a test rather than a screenshot.

**The backend compiles, migrates and tests.** `dotnet build Webly.slnx` is clean, `InitialSchema` is applied
to a real Postgres with the ten deferrable constraints written into it by hand, and all 121 tests pass.
`PostgresTestBase` will use an existing server (`WEBLY_TEST_POSTGRES`) instead of Testcontainers, so the
suite runs where there is a Postgres and no Docker.

**The client is real.** `client/src/app/api/` is generated from the live swagger — 37 models, 8 services —
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
12. **A site whose address was printed on every screen resolved nowhere.** Nothing ever asked the provider to
    serve `{slug}.{BaseDomain}`, so the header's link — offered the moment a publish succeeded — was to a 404.
    Publishing attaches it now, and until it takes, what the product links is the deployment's own URL.
13. **Every publish pressed in the UI looked like it had failed.** A deployment is a row before it is work, and
    the page watches the run as a second step — so `Subscribe` was called three seconds before the runner
    registered anything and threw "that run could not be found", which the client showed as its generic "that
    could not be saved". The publish itself was fine the whole time. Two fixes: the run is registered when the
    row is, and a `HubException`'s own sentence now reaches the screen instead of the fallback (every hub
    message did that — including the rate limit's).
14. **Build errors were never reported, for two independent reasons.** The turn asks the dev server for a page in
    order to make it compile, and on a cold workspace that request landed before anything was listening; and a dev
    server killed by the out-of-memory killer took the only record of itself with it, because the log hung off the
    child object. Both were invisible: the log read as empty and empty reads as healthy. Asking the mock agent to
    "break the build" is now how to see the whole path in one turn.
15. **A type error was invisible until the publish email, and every turn that looked for one would have
    committed a build artifact.** `next dev` compiles with SWC, so a page with a type error serves a 200 and its
    log says nothing — asserted now, not assumed. `AGENTS.md` had asked the agent to run `npm run typecheck`
    since the beginning, which covers the case where the agent remembers; nothing covered the case where it does
    not, and that is the one the person pays for. The turn runs it now. Finding that also found the other half:
    `tsc --incremental` writes its cache next to `tsconfig.json`, so the instruction Webly had been giving would
    have put a machine-readable dump of the project in every commit. It had never happened only because every
    turn through the app so far was the mock agent's.
16. **Every locally published site had been unstyled since the first one.** A static export writes absolute
    URLs for its stylesheet and its chunks, and the development target serves each site under
    `/published/{nanoid}/` — so every one of them asked for `/_next/…` at the root of Webly's own origin and
    rendered as black-on-white HTML. Nothing caught it because every check ever made read the page's HTML,
    and the HTML was perfect; it took opening one in a browser. This is the second time this exact mistake has
    happened in this product, the first being the preview, which is why the fix is now asserted by a test that
    names the environment variable rather than by a comment.

    The same afternoon's browser sweep found two more of the same shape: a mistyped address on a published
    site fell past the static files to the catch-all proxy and served **Webly's own dashboard** instead of the
    site's 404 page, and every page of a published site had the site's name as its whole title, so a shared
    link to a contact page said "Contact" and nothing about whose contact page it was.

17. **Two turns at once lost, three times over.** Sending two messages on one site a millisecond apart — a
    second tab, a double-click — failed with "something went wrong" on a message that was fine: both turns
    inserted a conversation and the index refused one; then, fixed, both took the same message sequence and the
    index refused one again; and underneath both, `update-ref` had no expected old value, so a second commit
    built on the same parent would have moved the branch to a commit missing the first turn's work while that
    turn's version row stayed in the history pointing at an unreachable commit. Nothing had ever run two turns
    at once. Both now succeed, in order, and the third case fails cleanly if it ever arises.
18. **And pressing Publish twice published twice.** The same check-then-act, on the product's most expensive
    operation: two sandboxes, two `npm ci`s, two real builds and two "your site is live" emails for one press.
    A partial unique index over the live statuses now says a site publishes one thing at a time, and the second
    request is answered with the deployment that is really running.

19. **Deleting a site did not take it off the internet.** The rows went, the repository went, the custom
    domains were detached — and the published site carried on answering at its Webly subdomain and at the
    provider's own URL, because nothing ever deleted the provider-side project. The delete button's own words
    say "if it is published, it stops being reachable", and it did not. Found the way the others were: by
    doing it in the running app and then asking for the page again.

20. **Every published site had the browser's blank-page icon in its tab.** A small thing that is the first
    thing a visitor sees of a business, and it had been true of every site since the first one — the template
    simply had no `icon.svg`. It is a file rather than a token, so it is also the one place `look.css` cannot
    reach: `SiteLooks` rewrites the colour into it alongside the stylesheet's three numbers, or a terracotta
    site would have carried an indigo tile.

21. **A site did not know its own name.** The one fact Webly has about a business before anybody has said
    anything is what they typed on the screen that creates a site — and the site they got said "Your site" in
    its header, its footer and the title of every link they shared, until an agent turn changed it. Found by
    publishing a brand-new site and looking at the page. The name is now written into the source at creation
    and recorded as the first confirmed fact the agent reads.

    The browser sweep that found it was itself failing on the wrong things: it reported two problems for a
    site nobody had published, because it walked the published screens regardless, and it had never once
    checked the thing it exists for — a page that answers 200 while its stylesheet answers 404. It does both
    now, and the second was proved by hiding a stylesheet and watching it fail.

22. **A shared link to a Webly site showed nothing.** No card, no picture, no name — a grey rectangle with a
    URL under it, which is how a real business's website ends up looking like a broken link in somebody's
    chat. The template now generates one at build time: the business's name and its address on its own brand
    colour. Two things it taught: Satori has no CSS engine, so that one file carries a hex and `Oklch.ToHex`
    produces it (checked against colours sampled out of a browser); and Next writes the image as a file with
    **no extension**, which a static file middleware will not serve — so the card 404ed while the markup
    swore it was there, which is the same shape of defect as every other one on this list.

23. **Next's dev badge sat on top of every customer's preview.** A floating button in the corner of the
    preview pane, put there by `next dev`, offering route types and build activity to somebody who was told
    they would never have to touch code. `devIndicators: false` turns it off — and the interesting half is
    that `next.config.ts` lives in each site's own repository, so the fix would have reached new sites only.
    `SyncWeblyOwnedFiles` (was `SyncSiteInstructions`) now carries the build contract alongside `AGENTS.md`,
    which is safe precisely because the rules the agent reads put that file on the list it must not touch.

24. **Every email Webly sent was branded as another app.** A paprika header, a bowl-of-stew emoji beside the
    name and `lang="hu"` on the document — the reference project's shell, carried over and never opened. An
    email is the one part of this product a customer sees when they are not looking at it, which is why
    nothing caught it for months. Found by rendering all seven in a browser, which took ten minutes.

    Two more in the same pass: the failure notice said "your site did not build — the error is below" and had
    nothing below it, because the build log stayed on the settings screen; and every message carried the
    verification mail's footer, so a notice about somebody's own website ended with "if you did not ask for
    it, you can ignore it".

25. **The sign-in-with-Google button said "vagy".** Hungarian for "or", inherited from the reference project,
    on the two screens every new customer sees first. It survived because that button only renders when
    `Authentication:Google:ClientId` is configured and a fresh clone deliberately has none — so no
    development machine had ever drawn it, and neither had the browser sweep. The sweep now stubs the one
    endpoint that decides and walks the branch; it never presses the button, which is the only thing a real
    client id would buy. It walks register and forgotten-password now too, which it had never seen at all.

26. **A connected domain could never be disconnected.** Removing the site's main address was refused with
    "make another domain the main one first" — advice nobody with a single domain can take — and the client
    hid the button on that row anyway, so the message was unreachable as well as impossible. Found by
    driving the domains screen end to end in a browser, which nothing had ever done: the whole lifecycle is
    simulated by the development deployment target, so it was always there to look at.

    The same look found that the DNS record — the one thing on that screen somebody has to reproduce exactly,
    in their registrar, in another tab — was truncated in two of its three fields, with a copy button on only
    one of them, and that pressing copy did nothing anybody could see.

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

### 4. Measure `Sandbox:IdleTimeout` against a real provider

Ten minutes is a guess, and it is the product's real unit cost. A warm sandbox bills by the second and a cold
one costs a person tens of seconds of staring at "your preview is asleep".

**One side is now measured**, with the local provider and the mock agent, so it is the floor rather than the
number: a cold turn is **16 s** end to end (workspace, seed, `next dev`, the agent, the commit, and the first
compile) and a warm one **3.8 s**, repeatably. So a cold start costs about twelve seconds more than a warm one
before any container is involved — a Docker or E2B sandbox adds its own start to that, which is the part still
unmeasured and the part that decides the timeout.

Note where a third of the cold turn goes: the compile check asks the dev server for a page, and the *first*
compile of a cold dev server takes about nine seconds. The answer has already been written to the chat by then,
but the run's terminal event waits for it. Worth knowing before trying to make a cold turn feel faster — and
worth keeping, because that request is the only reason a compile error ever reaches the chat.

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
- **No billing.** P7. Uploads landed (P5) and are not what that section expected: an image is a commit in the
  site's own repository rather than a row in a blob store, so the published site serves its own photographs and
  a customer who exports takes them along. Removing one landed with it, thumbnails and all.
- Forms landed — see `MASTER_PLAN.md` P4 and the Forms section of `CLAUDE.md`. Read state and deleting a
  message landed after them, so what is deliberately missing there now is a reply: an enquiry is answered from
  the owner's own email, where the notification is, with the visitor's address already in its reply-to.
