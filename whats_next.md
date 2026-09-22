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

27. **Adding two photographs wrote the second one off the bottom of the screen.** The chat's composer was one
    line high and could not grow, so the paths it writes in — one per line, which is the point of writing them
    in at all — were cut by the window. It grows now, to a cap. Found by uploading two pictures and looking,
    which nothing had done: the upload path had been exercised over HTTP and never with a pointer.

    Beside it, on the settings screen, a failed publish showed "Broke…" where the version's summary should be:
    the error message shared the row and took all of it, so the *why* pushed out the *what*.

28. **On a phone, picking a file or a version did nothing you could see.** History and Code stack their two
    panes below `lg`, and the list grew to its own height — about 1200px for twenty files — so the diff or
    the file was a thousand pixels below the fold. Both are master/detail on a phone now, and they no longer
    open on a detail there: opening on the newest version is right beside a list and wrong instead of one.

    The browser sweep had walked both screens clean many times, because opening a screen is not using it. It
    presses one thing per screen now — the thing the default selection does not reach.

29. **The second site was a room with no door.** "New site" in the sidebar leads to a full-screen page —
    right for the first one, which is the last step of signing up, and wrong for every one after it: there
    was no way back but the browser's own button. It has one now, for somebody who has somewhere to go back
    to. Its field also offered "Kovács Bakery" as the example name, the last of the reference project's
    fingerprints on a screen a customer sees.

30. **The preview let a customer's site act as the customer.** The frame was same-origin with the app — which
    `PreviewController` stated as the thing that made it safe — so a script in the page it rendered could call
    `/api/sites`, read the site's inbound messages or close the account, with the owner's session, and look
    exactly like the app doing it. The page in that frame is written by a coding agent. The sandbox's whole
    argument is that the agent is confined by *where it runs*; the preview brought its output back into the
    owner's browser on Webly's origin and undid a good part of that.

    Found by putting a `fetch('/api/sites')` into a site's home page and reading what the preview printed: the
    owner's sites. The frame is sandboxed now and the same page is refused. The cost is that an opaque origin
    sends no session cookie, so the preview has its own signed, per-site, path-scoped credential — and that a
    font, which can never carry a credential in any origin-less frame, is served on the nanoid alone.

    The proper fix is a separate origin for previews. It needs a wildcard DNS record and a certificate, which
    is why it is written down rather than done.

31. **The Docker sandbox provider ran for the first time, and had two defects.** It read a child's stdout to
    the end before its stderr — a deadlock the moment the other pipe fills, which `docker run` does on the
    first run of any machine, because a pull writes its progress to stderr. And it had no first-use sweep, so
    every restart of the API left a container per open site running with a `next dev` inside it; the local
    provider has had that sweep since sixteen orphaned dev servers wedged a machine. Both fixed, both watched
    working against a real daemon.

    Getting there is written down in CLAUDE.md: a daemon starts fine here, the registry is what is blocked, and
    a rootfs assembled on the host and `docker import`ed is enough to exercise the provider.

    One thing that run showed and nothing else would have: **`next/font/google` fetches at build time**. In a
    sandbox with no egress `next dev` says "Failed to download Inter… Using fallback font instead" and carries
    on, so a site builds and publishes in a font nobody chose. The fix is `next/font/local` with the file
    committed, which needs the `.woff2` in the repository.

32. **Every form field in the product had a border nobody could see.** daisyUI draws one at 20% of the content
    colour, which is 1.5:1 against the panel it sits on, and the site template used the same token for a
    field's boundary as for a card's outline — 1.3:1. WCAG 1.4.11 asks for 3:1 on exactly this, and the reason
    it asks is the reason a card can get away with less: a card is identified by what is in it, and a field is
    empty by definition, so its border is the only thing on screen saying where to click. Login and register
    are made of nothing else.

    It was invisible to every check this repository has, including the screenshot sweep: a screenshot of a
    faint border looks like a design, and nobody compares it against a number. So the number is the fix —
    `screens.mjs` now measures each field's border against what is behind it and fails under 3:1, resolving
    the colour through a canvas, because `oklch()`, `color-mix()` and a translucent border are all things a
    computed style hands back unresolved and only the browser can composite.

    Two edits, both measured in a browser afterwards rather than reasoned about: `--input-color` at 59%
    lightness in the app, which is the one value that clears 3:1 on **both** themes (4.0:1 light, 3.8:1 dark)
    — and `--color-field-edge` at 65% in the template, which clears it across all five looks. The app's rule
    is scoped `:not(:focus, :focus-within)` because an unlayered declaration beats anything in a cascade layer
    whatever its specificity: without that exclusion it would have quietly deleted the focus ring on every
    field in the app, which is a worse version of the same defect.

    Beside it, in the same file: 28 lines of `cat-tint` / `cat-chip` / `cat-ink` / `cat-edge`, the reference
    project's twelve-ingredient-category recognition layer, carried over wholesale and referenced by nothing
    here. Dead CSS is cheap; a comment in Webly's stylesheet explaining how Webly colours ingredient
    categories is not.

33. **Stopping the app left a sandbox running per open site, and nothing owned the shutdown.** Found by
    counting processes rather than by reading code: fourteen sandbox agents still listening, the oldest five
    hours old, after six restarts of a backend that had started them — idle, nine hundred megabytes between
    them, with their workspaces already deleted from under them by the next start's sweep. Locally that is a
    node process each; on a provider that bills by the second it is a sandbox per site, per deploy, for ever.

    The dev server inside each one has been recorded in a `.devpid` and swept since sixteen orphans wedged a
    machine. The *agent that spawned it* was recorded nowhere, so nothing could find it — the sweep was
    deleting the directories of processes it had no way to name.

    Two halves, each in the layer that knows. `WorkspaceReaper` gains a `StopAsync`, because it already owns
    "a warm sandbox costs money per second", and `ISiteWorkspaceRegistry.ReleaseAllAsync` is deliberately not
    `ReleaseAsync` in a loop: that one waits up to thirty seconds for a turn to finish, and at shutdown the
    turn has no future either way, while a shutdown that overruns is abandoned by the host — and what it would
    abandon here is the only code that stops a sandbox. And `LocalSandboxProvider` now writes the agent's own
    pid beside the workspace, so the sweep can kill what a `kill -9` left.

    Both watched: a `SIGTERM` takes the sandbox down and deletes the workspace; a `kill -9` leaves it, and the
    next start logs "Killed the sandbox agent 20753, left by an earlier run."

34. **The chat could not be sent with the keyboard.** The composer became a `<textarea>` when it had to grow
    with the message — and a textarea does not submit its form on Enter, which an `<input>` does. So the whole
    interface of this product, a box you type a sentence into, answered Enter with a blank second line and
    nothing else. Every check passed: the template type-checks, the page renders, the screenshot is right, and
    the send button beside it works.

    Found by accident, which is the point: a script that was testing something else pressed Enter and waited
    for a turn that never started. Enter sends now and Shift+Enter writes a second line, which is what Angular's
    `keydown.enter` already distinguishes; `isComposing` is checked, because Enter accepts an input method's
    candidate and a message sent mid-word is worse than an Enter that does nothing.

35. **Pressing Stop said nothing.** The turn's own note — "Stopped. Nothing was changed." — was written into
    the thread and the thread is only re-read on a page load, so what the person who pressed the button saw was
    the spinner disappear under a status line frozen mid-sentence: *Waking up your site*. Reload and the note
    was there, which is the worst version of it, because the app knew.

    The failure path had had the answer all along: it puts the same sentence on the terminal event so that "the
    live screen and a reload agree". The cancel path set the error to null and carried nothing in its place.
    It now carries the note as the terminal's `Detail`, and the chat draws a `Detail` on a `Completed` as a
    notice — not an assistant bubble, because the agent did not say it.

    Found by pressing Stop during a cold workspace start, which is the one moment a turn is slow enough to
    interrupt: with a warm workspace the mock agent finishes before a person could reach the button.

36. **Renaming a site renamed a label and nothing else.** The dashboard said "Ridgeway Cycles" and the
    website said "My Shop" — in the header, in the footer, in the title of every page and in the card a shared
    link draws — because the name lives in `src/site.ts` and only creation ever wrote it. The settings screen
    made it worse by answering the question next to the one being asked: "the name is yours to change; the web
    address is not." Found by renaming a site and reading its published home page.

    The rename now offers to write it into the site as well, ticked by default, because renaming a business
    and wanting its website to say so is one thought. It is a version like everything else that changes a site
    — `SiteIdentity`'s own rewrite applied to the head commit, through `CommitSiteVersion` — and it is safe by
    construction rather than by care: a file the agent has reshaped is left alone, and a tree identical to its
    parent commits nothing. Both no-op cases are tests, as is the apostrophe: "Joe's Kitchens" reaches a
    TypeScript literal that has to still compile, and the whole chain was driven — rename, version, publish —
    to a published page whose title says Joe's Kitchens.

    The web address still does not move, and that stays right: it may be published, linked, indexed and
    printed on a van.

37. **One site's turn wrote into another site's chat.** The editor keeps a single `SiteChat` alive across a
    switch between sites, and the effect that reloads it reset the transcript without letting go of the run it
    was watching. So with a turn running on site A, opening site B showed B's history with A's "waking up your
    site" line appended to it — and a Stop button, which would have cancelled a turn on a site that was no
    longer on screen.

    Coming back was the other half: `RealtimeService.watch` hands back the *same* stream for a run it is
    already watching, so the second subscribe was not a second stream but every event applied twice, and the
    tail of the transcript appeared in duplicate.

    `detach()` fixes both — unsubscribe, unwatch, and only then load the new thread. The run is untouched by
    design: a turn outlives the page looking at it, and returning re-attaches through `activeRunId` exactly as
    a reload does. Found by driving it in a browser: send a message on one site, click the other in the
    sidebar, come back.

38. **Restoring a version works, and it can move the site's name back.** Driven through the UI for the first
    time — "Bring this back" on a site's first commit — and it does what it says: a new version whose origin is
    Restore, the head moved, the list reloading with it marked Current, no confirmation dialog by design (the
    class comment makes that argument, and it holds: a restore is itself a version and undoing it is the same
    operation).

    What it exposed is a state the rename slice made reachable: the restored tree carries the name that
    version had, so the dashboard said "Joe's Kitchens" and every page said "Fieldline Joinery". The settings
    screen now says which name the visitors see whenever the two differ, and stops saying it once a save with
    the box ticked puts them back in step. Both halves watched in a browser.

    Its own note: the client reads `src/site.ts` through the file endpoint and parses the constant, rather than
    the API reporting it. The site detail is fetched on every editor load and polled every two seconds while a
    workspace starts — a git read behind it would be sixty process spawns for a sentence on a screen nobody has
    open.

39. **Deleting the open site handed the editor the wrong successor.** `DeleteSite` repointed
    `User.CurrentSiteId` at `remaining[^1]`, with a comment calling it "their oldest remaining site" —
    but `ListForOwnerAsync` orders by `UpdatedAt` **descending**, so the last of that list is the site its owner
    has touched least recently. Delete the one you are working on and the app opens next time on the one you
    care about least. One character, and it took reading the repository's ordering rather than the comment.

    `SiteDeletionTests` pins all three cases — the successor, the last site leaving the account with none, and
    deleting a site that is not the open one leaving the pointer alone — and the first is red on the old line,
    which is how the discrimination was checked rather than assumed.

40. **Signing out left the hub connection signed in.** A hub decides who its caller is during the handshake
    and never looks again, so the socket a page had already opened went on being that person's after their
    session was revoked. Demonstrated with a script rather than argued: connect with a signed-in jar, log out
    over HTTP until `/api/sites` answers 401, then invoke `StartChat` on the connection that is still open — and
    a real turn started, on their site, against their budget. On a shared machine that is the next person's
    turn.

    Two halves, both wanted. `AuthService.logout` now calls `RealtimeService.disconnect()` before the HTTP call,
    which fixes the ordinary case and is the honest behaviour: nothing should stay connected as somebody who
    signed out. And `RealtimeHub.CallerId()` asks `IAccessTokenBlacklist` on **every** invocation — the same
    mechanism that ends an HTTP session immediately, and an `IMemoryCache` lookup, which is why it can sit on
    the path of every call. Both watched in the running app: the socket closes on sign-out, and the hub answers
    "You are not signed in." to an invocation on a connection deliberately kept open.

    Writing that check also produced the session's best argument for driving things: a blanket rename turned
    `CallerId`'s own first line into a call to itself, the backend died of a stack overflow, and the probe
    reported a socket closing with 1006 instead of a refusal. A compile-clean infinite recursion, caught in
    seconds because something actually pressed the button.

    **The gap that left** — a socket whose access token had been rotated away before the logout, so its `jti`
    was not the one revoked — is finding 41.

41. **Sessions have an identity now, because nothing else could name one.** The access token's `jti` is replaced
    every fifteen minutes and the refresh token's hash every use, so a socket opened five hours ago could not be
    matched to the session that opened it. `RefreshToken.SessionId` is minted at sign-in, carried across every
    rotation, and rides in the access token as `sid`; signing out ends that session's live connections through
    `IRealtimeSessions`. Proven with the adversarial case rather than the easy one: connect, force a rotation so
    the socket's token is stale, log out with the *new* token, and the socket dies.

    **Per session and not per user, and that distinction cost a run to learn.** The first version ended a
    *user's* connections, on the argument that their other devices would simply reconnect. They do not:
    `Context.Abort` closes cleanly enough that the SignalR client treats it as a deliberate close and never
    reconnects at all. Watched with two clients — logging out of one left the other's socket dead and silent,
    which is a worse thing than the gap it was closing. With the session named, the other device is not touched:
    still `Connected`, still answering.

    A connection whose token predates the claim carries no `sid`, and is ended whenever its user signs out
    anywhere. Being unable to name something is not a reason to leave it running as somebody who has signed out.

42. **And the preview cookie now ends with its session**, which is what having a session id was for. It carried
    a user and a site and twelve hours, so a sign-out left it working: on a shared machine, whoever had the
    browser could read that site's preview until the evening — which matters most for a site nobody has
    published, because that is the one nothing else will show them.

    It carries the `sid` it was minted under and is refused once that session is revoked; `IAccessTokenBlacklist`
    gained `RevokeSession` / `IsSessionRevoked` for it, cheap enough for a path that runs per chunk of every
    page. A token minted before the change has three fields rather than four and is honoured until it expires,
    deliberately: refusing it would break every open editor's preview the moment a deployment lands, to close a
    window that closes itself overnight.

    Two details worth keeping. The revocation is remembered for a **day** rather than the refresh token's sixty,
    because the longest-lived credential a session can mint is that twelve-hour cookie and the rest is paying
    memory for nothing. And the session being ended is read from the **access token**, not from the refresh row:
    the first version read the row, and the test that signs out with only an access cookie — which is what the
    client's own sign-out does when the access token is still live — went straight through it and answered 503
    instead of 404. Found by writing the test before believing the code.

43. **A photograph uploaded during a turn was silently deleted by that turn.** The guard against two writers had
    been in place since two simultaneous turns were driven against the running app — `update-ref` takes an
    expected old value, and git refuses atomically rather than moving the branch to a commit that does not
    contain the other's work. It had never refused anything, because of who was answering "what was this tree
    built from?": the turn re-read its site row just before committing and handed git *that* head, so the two
    were equal by construction. The tree it was writing had been seeded from the sandbox minutes earlier.

    Driven: send a message, upload an image three seconds later, and the turn reported success while
    `ls-tree HEAD public/images/` no longer had the file — with "Added probe.png" still sitting in the history
    directly under the turn's own commit. Nothing anywhere said a version had been undone. The comment on the
    guard asserted the invariant it did not have.

    `CommitSiteVersion` now takes an optional `treeBaseSha`, and `AgentTurnService` passes the workspace's own
    commit. Every other caller passes nothing and means the head it just read, which is true of all of them: an
    upload, a delete, a rename, a restore and the owned-file sync each build their tree from the head in the
    same breath. That distinction is the whole fix, and it is what the two tests in `SiteVersionConflictTests`
    pin — the first is red on the old line.

    **And the clean failure had no answer.** The first real refusal came back from the upload endpoint as an
    unhandled `RepositoryConflictException` with the repository store's stack trace in the response body. Every
    operation that changes a site can lose this race, so the mapping belongs in one place rather than in six
    error enums: `ExceptionHandlingMiddleware` answers 409 with `site_changed` and the exception's own sentence,
    which was written to be read by whoever pressed the button — "Your site changed while this was being saved,
    so nothing was written. Please try again." The client already shows `messageOf`, so nothing there changed.

    What is deliberately *not* done: the upload does not retry. It could — its tree is derived from the head it
    read a millisecond earlier, so re-reading and re-adding is always safe — and the window is small enough that
    a sentence naming the remedy is an honest answer for now. Worth doing the day somebody hits it twice.


44. **Two turns on a *cold* site never queued at all**, which the fix above is what made visible. Starting a
    sandbox is several seconds of awaiting, so "there is no workspace for this site" and "here is the workspace
    for this site" were separated by long enough for the second turn to read the same absence: both started their
    own sandbox, the second overwrote the first in the dictionary, and neither ever met the semaphore whose whole
    job is to make two turns take turns. One paid machine was left running with nothing pointing at it, and the
    loser's commit was refused.

    It had always been the case and it had always been invisible, because the warm path — which is every turn
    after the first — does queue correctly. Driven by starting two turns a millisecond apart against a
    freshly restarted backend: one turn failed with the conflict sentence and `pgrep sandbox-agent` found two
    agents for one site. With a per-site start lock, the same run leaves **one** agent and both turns complete
    in order.

    The other half of the same finding: `AcquireAsync` resolved the branch *before* the queue, so a turn that
    waited behind another read the head as it was before the winner committed — and would have re-seeded the
    sandbox backwards to the tree the winner replaced. It asks again once the lease is held, which is the only
    moment the answer cannot change.


45. **The one checkbox in the app was invisible in both of its states**, and the contrast rule in
    `tools/e2e/screens.mjs` is what said so — on a screen it had already photographed a dozen times, because a
    border drawn too faintly reads as a design choice in every screenshot ever taken of it.

    Two separate things were wrong. The earlier contrast fix repointed `--input-color` and named the four field
    classes, which left the checkbox at daisyUI's 1.49:1: an *unchecked* box is nothing but its border, so a line
    nobody can see is a control nobody can find. And an unmodified daisyUI checkbox that is *ticked* is the same
    faint outline with a grey mark in it, so "on" and "off" looked nearly the same — on the control that decides
    whether renaming a business renames its website.

    The border goes through `--input-color` after all: daisyUI writes `border: … solid var(--input-color,
    color-mix(…20%…))`, so the faint colour is its fallback rather than its definition, and a `border-color` of
    our own loses to the shorthand. Read out of the served stylesheet after the `border-color` version measured
    exactly as faint as before — which is the lesson worth keeping: with daisyUI, find the variable rather than
    fight the rule. The checked half is `checkbox-primary`, which is not decoration: it fills the box, clears 3:1
    on its own edge, and makes the state readable at a glance. Photographed ticked and unticked, light and dark.

    The value both rules use is now one `--color-control-edge` token in `@theme` rather than the same literal
    written twice, and the bundle budget — which had been warning on every single build for longer than anyone
    can date, at 503 kB against 500 — is 600 kB, so a clean build is once again how a new warning gets noticed.


46. **Typing the sixth digit of the confirmation code did nothing.** The code field held all six, the Confirm
    button lit up, and the page waited. Every one-time-code field anybody has used advances by itself — the
    length is known and there is exactly one thing that can happen next — and this is the worst screen in the
    product to make somebody look for a button on: the code is on a phone, the box is on a laptop, and the
    moment they look up from one to the other is the moment they have to find the other. The page's own last
    line already promised "this page moves on by itself", of the link, which left the code path contradicting
    the sentence printed under it. One `if` in `onCodeInput`; `submitCode` already refused to re-enter while a
    request was in flight, and a wrong code still clears the box and says so. Walked both: right code advances
    on the sixth keystroke, wrong code errors and stays.

47. **Every Webly email carried three paragraphs of our own notes to the customer.** Comments explaining a
    header colour, a dark-mode meta tag and a footer decision were written inside the layout's HTML rather than
    beside it, so they went out in the message — including the one about a bowl of stew being the reference
    project's. No client renders a comment, which is exactly why nobody saw them and why it took reading a sent
    email as *text* to notice. They are in the method's `<remarks>` now, and `EmailTemplateTests` fails on
    `<!--` in any of the seven messages, in either body. Red on a one-line canary before it was green.

    Both of these came out of the same walk: registering as somebody who had never seen the product and going
    through to a live site, reading every screen and every email on the way. Nothing was broken; what it found
    is the two places the product asks for one step more than it needs to, which is not something a harness that
    checks for errors can see.


48. **Six of the seven emails said "Sent by Webly because of something on your account".** The footer slice had
    decided that the footer belongs to the message — "if you did not ask for it, you can ignore it" is true
    under a sign-up code and a lie under a notice about somebody's own website — and then left a
    `DefaultFooter` in place, whose own comment claimed the exceptions passed their own. Only the sign-up code
    did. So the password reset, which is the *other* message that can reach somebody who did nothing at all,
    carried the vague line; so did "Ridgeway Joinery is live", which is where it was read during the walk.

    Each message now passes its own, and the parameter is **required**: a default is how a decision that has to
    be made per message stops being made, so there is nothing left to fall back to. `EmailTemplateTests` pins
    the half a signature cannot — the two unsolicited ones give the reader the way out, and the ones about
    somebody's own work do not offer to be ignored, which under a customer's enquiry would read as an insult.


49. **Reloading the editor during a publish showed a Publish button over a publish already running.** A
    deployment's run id *is* its nanoid, deliberately, which is the whole reason a publish is the one run in
    this product that can outlive the process that started it — and nothing used that, because nothing on load
    knew one was in flight. The comment beside the publish handler described the capability; the code it
    described did not exist.

    Pressing again was harmless: the partial unique index refuses a second and `PublishSite` answers the loser
    with the one that is running, so the second press would have quietly joined it. But until somebody pressed,
    the screen said nothing was happening — on a tab reopened, a second device, or a laptop woken up.
    `SiteDetailResponse.ActiveDeploymentNanoid` is one index seek over the partial unique index that already
    exists, the editor watches it on load, and the starting label is the caller's to name — "Queued" when the
    call is what created the row, "Publishing" when it is a reload joining something already building, rather
    than claiming a stage it cannot know.

    Driven: press Publish, reload, watch the button go Publishing → Building → Published on the re-attached
    subscription. The backend half is pinned by `SitePublishStateTests`, including that a *finished* deployment
    is not offered — which would leave the button on "Publishing" for ever.


50. **A visitor who sent an enquiry was told nothing at all.** The form posted, Webly stored the message and
    emailed the owner with the visitor's address in the reply-to — and the visitor was returned to an **empty
    contact form** with nothing on the page saying any of that had happened. Which is indistinguishable from a
    failed send, so the next thing they do is fill it in and send it again, or give up and ring somebody else.
    On the one path a small business's website exists for.

    It fell between two decisions that were each right on their own. The endpoint sends the visitor back to the
    page they came from, because a shop's customer should not land on Webly's website; and the contact page
    could not read `?sent=1` itself, because a static export has no server to read a query string at request
    time. The sentence that joined them — that the confirmation is the one Webly's endpoint shows on the way
    back — was only ever true in the *fallback* case, when there is no usable `Referer`, which is the case
    almost nobody takes. `returnTo = '?sent=1'` had been in the component all along with nothing reading it.

    The browser can read a query string even when no server can, so `SentNotice` is a client component that
    reads it **after mounting**: same markup on the server and the first paint, no hydration mismatch, and a
    visitor with scripts off gets the form exactly as before — which matters, because the form working without
    JavaScript is the whole reason it is an ordinary HTML post. Found by filling in the form on a published site
    as a stranger; `tools/e2e/run.mjs` step 14 is the guard, and it checks both halves — the form is in the
    HTML, and the confirmation is in the export's chunks, which is the part no amount of reading a page shows.

    **Existing sites keep the silent form**, and that is the honest note: `contact-form.tsx` and its page are
    the agent's to rewrite, so they are not on the `SyncWeblyOwnedFiles` list and a template fix reaches new
    sites only. Making them owned would undo an agent's restyling of a form it is explicitly allowed to
    restyle. Worth revisiting the day there are sites somebody would mind.


51. **The address on the Messages screen was not a link.** The screen has no reply of its own, deliberately —
    an enquiry is answered from the owner's own email, where the notification already is with the visitor's
    address in its reply-to. That is right, and it is exactly what made the plain text wrong: somebody reading
    the list on their phone and deciding to answer *this* one had to select, copy and switch app, for the thing
    the screen exists for. A phone number was the same.

    `models/contact-link.ts` judges the value rather than the label, because the agent writes the form and the
    field asking for an address might be called anything, while an address looks like an address whatever it is
    called. Whole-value only, so the address inside somebody's paragraph stays part of what they wrote — driven
    with a message carrying one, and it stayed text while the address field beside it became a link. `?` and
    `&` are refused, because that is how a `mailto:` grows a `bcc`. Six cases in `contact-link.spec.ts`.

    Not a fourth verb: the three the screen has, made usable.


52. **The history showed "a binary file" where a photograph belonged.** The Code tab had already learned this —
    a picture is shown, not described, because a file name is not how anybody knows which one it is — and the
    History screen had not, on the screen where it matters more: this is where somebody decides what to bring
    back, and "Added lathe.png" followed by "there is nothing to show line by line" is a question rather than an
    answer.

    The half that made it worth doing properly: the image route read the **head** and nothing else, so showing
    the picture would have been wrong the moment a name was reused and broken the moment one was deleted —
    which is precisely the version somebody is looking at when they want it back. Found by restoring a site to a
    version older than its photographs and then opening its history: at head that file now 404s, and the version
    that added it still serves the real PNG. So `GET …/images/{name}` takes an optional `?version=`, resolved
    through the version repository against *this* site, so another site's id answers 404 exactly as an invented
    one does. Three cases driven against the running app, and pinned in `SiteImageTests`.

    Deliberately not shown for a **removed** file: it is not in that version's tree — that is what removed
    means — so the bytes are the parent's, and a version's diff reaching into another version is a thread to
    pull when somebody asks rather than now.

    Two things checked on the way and found already right, which is worth recording so nobody re-checks them:
    **restore reaches the internet** (restored a site to a version predating its three photographs, published,
    and the images are gone from the live export while the page still serves 200 and the history keeps the
    commits — `FileSystemDeploymentTarget` deletes the target directory before copying, so a deleted file stops
    serving rather than lingering); and **closing an account takes its published sites down with it** (a
    throwaway account with one published site: repository gone, export gone, the page 404s, the credentials
    answer 401).


53. **A site called "Joe's Garage" could be named once and never renamed again.** The pattern that finds the
    constant in `src/site.ts` was `'[^']*'`, which cannot match a value this same class has already escaped a
    quote into: it stops at the `\'`, the `';` that has to follow is not there, and nothing matches. The
    escaping that makes the first write safe is exactly what makes every later one miss.

    What that looked like from the outside, driven in the running app: rename it to "Ridgeway Motors" with the
    checkbox ticked, get a **204**, a version in the history called *"Renamed the site to Ridgeway Motors"*, a
    dashboard saying Ridgeway Motors and `content/brand.md` saying it too — while the header, the footer, every
    page's title and the share card went on saying Joe's Garage. For ever, because the next rename would miss
    in the same way. The commit's own diff touched `brand.md` and nothing else. Nothing failed, nothing was
    logged, and the only thing that would ever have admitted it is the settings screen's "the site calls itself
    something else" notice — which says the two disagree, not that the rename did not work.

    It is the names most likely to have one — O'Brien, Joe's, Sainsbury's — so this was never an edge case; it
    is a large share of the small businesses this product is for.

    `'(?:[^'\\]|\\.)*'` is the whole fix. The existing apostrophe test renamed *into* one, which is the half
    that worked; it now renames out, back in and out again, and is red on the old pattern. Re-driven afterwards
    on the site that was already stuck — a second rename recovers it, so nothing is permanently wrong — and
    then all the way through a real `next build` and publish, because "customer input reaching a file that gets
    compiled" is only proved by compiling it: the published page's title is `O&#x27;Brien Plumbing`.


54. **A long business name cut off the two things beside it, in two different ways.** Driven by creating a site
    called "The Old Forge Blacksmith & Metalwork Restoration Company (Northumberland)" — which is not a stress
    test, it is how small businesses are called — and looking at the app on a 390px screen.

    In the editor's header, the address and the published-state badge share a flex row that carried `truncate`.
    `truncate` on a **row** sets `overflow:hidden` on the container and nothing on the children: the address
    would not give way, so the badge was sliced, and the header read **"not publis"** — the site's own status,
    clipped by its own address. `truncate` belongs on the text, `shrink-0` on what follows it.

    On the All sites screen it was worse, because the cards are a **grid**. A grid item's default `min-width` is
    `auto`, so it will not shrink below its content — and `truncate` inside sets `white-space: nowrap`, which
    makes the name's min-content width the whole name on one line. One long-named site dragged the *entire
    list* to 688px inside a 390px screen, pushing every card's "Open editor" off the right edge, **including
    the cards whose names were short**. The `min-w-0` already on the button could not help: the constraint is
    on the `<li>`. Caught by the sweep's existing sideways-scroll rule the first time it ran against an account
    whose sites have real names — which is the other lesson, and why it now runs against that account.

    **`tools/e2e/screens.mjs` gained a rule for the first one**, because nothing could see it. When a child
    overflows an ancestor that clips, the page's `scrollWidth` does not change — clipping is precisely what
    hides it — so neither the browser nor the sideways-scroll rule can tell. `clippedText` measures **leaf
    text** against the nearest clipping ancestor and skips anything wearing its own ellipsis, since an ellipsis
    is a decision. Red on every screen carrying that header before the fix, naming the element and the words
    lost; silent across all 26 screens at both widths otherwise. That is the third defect of this shape — the
    version summary, the badge, the card — so it is worth a rule rather than a third reading of the same
    lesson.


55. **Connecting your own domain told you it was the temporary one.** Driven by walking the whole domains flow
    for the first time — add a domain, see the DNS record, check it, promote it, then try to remove it. Every
    step works, and the removal's confirm is genuinely good: it names the address the site falls back to and
    warns that anyone holding the old one stops finding it.

    The defect is in the panel above. The Webly-address row asked `liveUrl === weblyUrl` to decide whether the
    platform subdomain was being served. That is a sound proxy right up to the moment somebody promotes their
    own domain — after which `liveUrl` *is* that domain and the two can never be equal again. So the row read
    **"Being set up"** for ever, however long ago the provider had confirmed the subdomain, and the line beneath
    it read: *"Your site is live at https://oldforge-blacksmith.co.uk **until this address is ready**."* The
    customer's own main address, framed as the stand-in for ours, on the screen where they had just finished
    connecting it. Exactly backwards, and at the worst possible moment.

    Two states that one comparison cannot tell apart, so the panel now asks the question it means:
    `SiteSummaryResponse.AddressReadyAt` for the badge, and `liveUrl === url` to choose between "which is its
    main address" and "until this address is ready". Both read correctly in the running app, on a site with a
    promoted domain and one without.

    (And the `@if` opens on the line the anchor closes on, deliberately: a line break between them renders as a
    space, and a space before a comma is how a sentence looks unfinished. It did, the first time.)


56. **The panel showing a customer "what Webly knows about your business" was showing them a note addressed to
    a coding agent.** `content/brand.md` shipped with an HTML comment under the Name fact — that it comes from
    what the owner typed, that it is also `siteName` in `src/site.ts`, and that a correction means changing
    both. All true, all useful, and all written for the wrong reader.

    The settings panel renders that file verbatim, which is its contract — it is "the assistant's memory, shown
    to the person it is about", and it says *"Tell it in the chat if something here is wrong."* So somebody
    reading their own facts, in a product whose promise is that they never touch code, was reading an
    instruction about a TypeScript constant in a file path. This is the same class as the HTML comments that
    were travelling inside every email, with one difference: those were invisible, and this one is rendered in
    full, in a monospaced block, on a screen built for them.

    The instruction moved to `AGENTS.md`, which is where instructions for the agent go and which the customer
    does not see — and it gained the sentence it was missing: **the owner reads `content/brand.md`, so write it
    for them.** Verified by creating a site and reading its first commit.

    Existing sites keep the comment, for the contact form's reason: `brand.md` is the agent's memory and the
    customer's facts, so it is deliberately not on the owned-file list — Webly only ever rewrites the one Name
    line. A turn that reworks the file will drop it.

    Also walked on the way and found clean, so nobody re-checks it: **forgetting a password**. The non-
    disclosing answer ("if there is an account with that address…"), the mail, the link, setting a new one,
    landing signed in, the old password answering 401 and the new one 200, the link refusing a second use with
    a sentence, a session open in another browser signed out, and a "your password changed" notice sent. Every
    step correct.


57. **"Your site is not compiling" said it three times.** Asked the mock agent to break the build — the path
    the documentation says exists for exactly this — and read the block it puts in the chat. One syntax error,
    written out **three times**, with `GET / 500 in 9212ms` underneath it.

    `next dev` compiles on demand and logs the failure again for **every request**, and the turn's own check
    asks for the page and then retries for fifteen seconds while the server warms up — so the slice of log a
    broken page produces holds the same twelve-line error two or three times over. Three copies read as three
    mistakes and bury the one line naming the file and the row, which is precisely what `CompilerOutput` exists
    to prevent: npm's banner above the error was the first version of the same failure, and this is the second.

    `Readable` now keeps one copy of each complaint, in the order they first appeared, and drops the dev
    server's own access log. Two details, each found by the test failing:

    - A block starts at `⨯ ./` or `x ./`, **not at any marker** — `Syntax Error` appears *inside* a block under
      `Caused by:`, and splitting there would cut every error in half and then call the halves distinct.
    - Blocks are compared **by their body, not by the glyph in front of them**: `next dev` writes the first
      occurrence with `⨯` and every repeat with `x`, so the two are never the same string. The first version of
      the fix collapsed the repeats and let the original through — two copies instead of three.

    Two genuinely different errors both survive, which is its own test: a page that will not compile for two
    reasons has to say both, or fixing the first reveals the second one message later. Re-driven afterwards on
    the running app — one copy, no access log — and then "fix the build", which brought the preview back to 200.


58. **"Your site is not compiling" was printed over a preview that plainly was.** The other half of the
    breakage check: `next dev` compiles with SWC, which strips types without checking them, so a **type error
    leaves the page rendering perfectly** — and the chat said the site was not compiling next to a pane showing
    it working. What a type error really costs is the *publish*, where `next build` runs `tsc` and refuses.
    Telling somebody their site is broken when they can see it working is how a product teaches people to
    disbelieve it — the same standard this codebase already applies to reporting a sandbox failure as a site
    failure.

    `RunEventType.TypesFailed` is its own value, which these enums are string unions precisely to allow: a new
    value is a case a client does not yet handle rather than a silent renumbering of the ones it does. The
    headings are now "Your site is not compiling" and "This will stop your site publishing", and both are true
    of what produced them. Driven both ways in the browser afterwards.

    **Three things in a row made this hard to see, and each is worth remembering:**

    - The mock agent breaking an already-broken site **commits nothing**, so the typecheck is skipped — which is
      correct ("a turn that changed nothing cannot have broken anything") and means a probe has to clean the
      site before breaking it. Two runs were spent re-breaking.
    - `tools/e2e/turn.mjs` printed **nothing** for the new event, because its switch did not know it. A harness
      silent about what it does not recognise reports a working feature as missing; it knows `TypesFailed` now.
    - And the browser showed nothing at all until `ng serve` was restarted — the **stale lazy chunk** this
      file and `CLAUDE.md` both warn about, costing its third diagnosis. The remedy is written down; following
      it sooner would have saved twenty minutes.

    What settled it was a probe that dumps **every** event type rather than the ones it expects. That is the
    lesson: an observer that filters by what it already knows cannot see something new arrive.


59. **A reload lost the warning, and the type-error case is where that is worst.** The build report was a run
    event and nothing else: it reached the live screen and existed nowhere afterwards. Reload the editor and
    the thread was the agent's cheerful reply with nothing under it. Measured exactly that way — "before
    reload: warning shown / after reload: NO warning".

    For a compile error this loses a sentence somebody could still get back by breaking the page again. For a
    **type error** it loses the only thing on the screen that was true: the preview renders, the reply says the
    change is done, and the site will refuse to publish. Every signal agrees the site is healthy and the one
    that disagreed was the one that did not survive a refresh.

    The fix follows this codebase's own precedent rather than inventing one. The stop note and the failure note
    are already written into the thread as `MessageRole.System` for exactly this reason — so that a reload
    tells the person what the live screen told them — and the build report is the third thing in that class.
    `ReportAsync` now writes a note carrying the headline plus the first non-empty line of the compiler's
    output: `This will stop your site publishing. src/app/page.tsx(43,7): error TS2322: Type 'number' is not
    assignable to type 'string'.` One line deliberately, not the block: the full text is in the run event, in
    the preview and in the publish's error detail, and a transcript is a conversation rather than a log.

    Verified both ways in the browser — the live screen says it, and the reloaded thread still says it.

60. **The chat opened at the oldest message.** Found while checking the one above, by measuring the transcript
    scroller instead of looking at it: `{"h":2984,"c":757,"top":0,"gap":2227}` on a desktop and `gap: 2641` on
    a phone. So every arrival at a site with any history put somebody two and a half screens above their own
    last exchange, with the composer beneath a conversation from an hour ago.

    `scrollToEnd()` existed. It was only ever called from `apply()`, the run-event handler — so the transcript
    jumped to the end the moment anybody typed, and never on the way in. That is the shape that hides a defect
    for weeks: it is right during the thing you are testing and wrong before you start.

    Two changes, and the second is the one that matters. `load()` calls it. And it runs inside
    `afterNextRender(…, { injector })`, because setting `entries` **schedules** a render rather than performing
    one: reading `scrollHeight` in the same tick measures the transcript as it was a moment ago, which on a
    first load is an empty one. Re-measured afterwards at both widths: `gap: 0`.


61. **The confirmation needed a script, on the one form built not to need one.** Found by driving the
    onboarding a real customer takes — register, verify, name a site, one turn, publish — and then sending the
    published contact form twice in a browser: once normally, and once with JavaScript switched off, which is
    the visitor `ContactForm` is an ordinary cross-origin HTML POST *for*.

    With scripts, "Thank you — your message has been sent." With scripts off, the enquiry arrived, the owner
    was emailed, and the page came back showing the empty form and nothing else. Exactly the defect finding 47
    fixed, still present for the one visitor whose form had been designed around it — because the fix put the
    acknowledgement in a `useEffect` reading `?sent=1`, and a `useEffect` is the one thing that visitor does
    not have. The comment I wrote there called the confirmation "an enhancement that can afford to be one".
    It cannot: for somebody who cannot run scripts, the acknowledgement is the whole of the feedback, and its
    absence is indistinguishable from a failed send.

    The answer is a **fragment and a CSS rule**. `_next` is `#sent`, the notice carries that id, and
    `.sent-notice:target` shows it while `.sent-notice:target ~ .sent-form` hides the form. No server, no
    script, no hydration, and "Send another message" is a link to `#form`, which un-targets the notice and
    brings the form back — a full round trip with JavaScript disabled, driven and screenshotted.

    A separate `/contact/sent/` page was the other candidate and is worse where it counts: `ContactForm` can
    be dropped on any page, so a `_next` pointing at a thank-you page nobody remembered to write is a 404 on a
    real business's website at the worst possible moment. A `:target` rule travels with the component.

    Two things are now pinned rather than trusted. `FormSubmissionTests` asserts the Location is
    `…/contact/#sent`, because the fragment has to survive .NET's relative-URI resolution against the
    `Referer` for any of this to work. And `tools/e2e/run.mjs` step 14 checks **both halves**: the
    acknowledgement is in the page's own HTML, and the rule that reveals it is in the export's stylesheet —
    without the second, the markup would be there and permanently hidden, and the page would look exactly as
    it did before the fix.

    **Existing sites keep the silent form**, the same honest note as last time and for the same reason:
    `contact-form.tsx`, `sent-notice.tsx` and the contact page are the agent's to rewrite, so they are not on
    the `SyncWeblyOwnedFiles` list. Verified on a site created after the template changed, published through
    the running app.


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
