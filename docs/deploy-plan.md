# Publishing, domains and hosting

## 1. The shape

```
PublishSite (endpoint)          writes a Deployment row, status Queued, and returns 202
        │
DeploymentJobRunner (hosted)    picks it up, and:
        ├── Preparing           starts a FRESH sandbox, seeds the version's tree, `npm ci`
        ├── Building            `vercel build --prod`, then `vercel deploy --prebuilt --prod`
        ├── Ready               Site.PublishedVersionId := the deployed version   ← the only place
        └── Failed              Deployment.Error + ErrorDetail (the log's tail), previous version still live
```

Progress is published on the realtime hub as a `Deploy` run **and** written to the row. The stream is for
the person watching; the row is for the one who closed the tab, which is the case that makes a deployment a
job rather than a request.

## 2. Why Webly builds, in its own sandbox

There are three places the build could happen, and the choice matters more than it looks.

**On the provider's side, from a pushed repository** — rejected. A build on somebody else's machine fails
for reasons the site's owner cannot see, in a log we did not produce, on a page we do not control. It also
means a git remote the provider can read, which is the one thing that would force Webly to hand a
repository to a third party.

**In the warm editing sandbox** — rejected, though it is tempting. A build must not depend on whatever the
editing session left behind: a package installed and then removed, a stale `.next`, a file the agent wrote
after the commit. Building from the committed tree with `npm ci` against the committed lockfile is what
makes a failed deployment safe to retry and a successful one reproducible.

**In a fresh sandbox, seeded from the version being published** — what happens. It costs a machine start
per publish, which is a minute somebody is watching a progress line for, and it buys the property the whole
product rests on:

> **A site that does not compile is never published.** The build failure is ours, before anything reaches
> the provider, so the previous version keeps serving and the person is shown the error in words plus the
> log's tail — and the next thing they do is ask the assistant to fix it, which is a conversation, not a
> support ticket.

`vercel build` then `vercel deploy --prebuilt` rather than uploading source, both through the CLI the image
pins. The project link is written to `.vercel/project.json` rather than passed as flags, which keeps the
token out of an argument list that ends up in a process table.

### The development target

`FileSystemDeploymentTarget` (`Deployment:Provider=filesystem`, the default in development, refused outside it)
runs **the same real `next build` in the same sandbox** and then writes the static export to
`.run/published/{slug}`, which the dev host serves at `/published/{slug}/`.

So what is simulated is only the upload. Everything that decides whether a publish is safe is real: the fresh
sandbox, `npm ci` against the committed lockfile, the build, the failure blocking the publish, the log's tail
reaching `Deployment.ErrorDetail`, and `PublishedVersionId` moving only on success. That matters because the
publish path is the most consequential one in the product and it should not be testable only by people who have
a hosting account.

It fetches the build output with a base64'd `tar` over `/exec` rather than through the tree-reading contract,
deliberately: that contract excludes build output on the way out, and it should — an artefact must never be able
to travel back into somebody's git history.

Its domain calls are a **simulation and say so**: attach answers pending with a record that names itself, check
then answers verified. That exercises the two-step "add it, go to your registrar, come back and check" flow the
UI is built around, without claiming anything resolves. Nothing there touches DNS.

## 3. Why one platform-owned Vercel account

"Self-service" cannot begin with "create a Vercel account and generate an API token". So Webly owns the
account, creates one project per site (`webly-{siteNanoid}`), and the customer never hears the provider's
name.

The costs, written down because they are real:

- **Our quota is shared.** The `deploy` rate-limit policy (per user, 20/hour) exists so that one stuck
  client cannot get every other customer's publishing rate-limited.
- **Our bill.** Metering is a later slice, and it now has two lines rather than one: provider hosting and
  sandbox seconds.
- **Our account is the blast radius.** The token is a single secret with rights over every customer's
  hosting, which is an argument for the upgrade below rather than against the decision. Note it never
  reaches an editing sandbox — only the publishing one, and only as the CLI's argument for that one run.

**Bring-your-own-account** is the additive upgrade when somebody asks: a nullable `ProviderToken` on
`Site`, read by `VercelDeploymentTarget` in place of the configured one. `IDeploymentTarget` does not
change, and neither does anything above it.

## 4. Domains

A `Domain` row is a **mirror** of what the provider knows, plus the link to our site. Webly runs no DNS
and issues no certificates.

- `AddDomain` calls the provider **first** and stores its answer. The other order would give us a domain
  the provider has never heard of, and a screen showing DNS instructions nobody can act on.
- The DNS record is stored rather than recomputed per request: it is what the person is looking at while
  their registrar's panel is open in another tab, and recomputing it would be one provider call per page
  load of a screen people leave open for a quarter of an hour.
- Verification is re-checked **when the person clicks**, never on a timer. A poll per pending domain per
  minute is a rate limit waiting to happen, and they know when they changed something.
- `Pending` and `Failed` are different states: pending is "the provider has not seen it yet", which is the
  normal state thirty seconds after an edit at a registrar; failed means there is something to fix, and
  `LastError` says what.
- Exactly one primary hostname per site, by partial unique index. Only a **verified** domain may be
  promoted: a canonical URL on a hostname that does not resolve tells every search engine the real site is
  somewhere unreachable.
- Removing the primary domain is refused — promote another first — because removing it would leave the
  site canonicalizing to a hostname it no longer serves.

Apex versus subdomain (an `A` record versus a `CNAME`) is the provider's answer, mirrored. Webly's
fallback wording exists only for the case where the provider returns no challenge, and its `IsApex` guess
is wrong for public suffixes like `co.uk` — which is exactly why the provider's answer wins when there is
one.

## 5. Failure, in the order it matters

1. **A failed deploy changes nothing.** `PublishedVersionId` moves only on success, so the previously live
   version keeps serving. The failure email says so in its first line, because that is the difference
   between an inconvenience and a panic.
2. **A superseded deploy is cancelled, not queued behind.** The newer draft is what the person wants live,
   and two uploads into one project race to decide which of them wins.
3. **Provider errors are translated once**, in `VercelDeploymentTarget`, into sentences for the site's
   owner. A raw API body reaching the `Deployment.Error` column reaches the screen.
4. **Deleting a site cleans up the provider last and best-effort.** An orphaned project costs a little
   money and is visible in the provider's dashboard; a site that will not delete costs trust.

## 6. Unverified against the live API

`VercelDeploymentTarget` is split in two, and both halves are unverified for different reasons.

The **REST half** (projects and domains, from this process with Webly's token) follows Vercel's documented
v9/v10/v11/v13 endpoints and **has never been run against the real service** — no token existed in the
environment where it was written. The **CLI half** (`vercel build`, `vercel deploy --prebuilt`, inside a
sandbox) has never been run either, and its failure modes are the CLI's rather than an API's: a missing
project link, an authentication prompt where a `--token` was expected, output that does not print the URL on
its own line.

The first tasks with a real token, in order:

1. Create a site, publish it, and reconcile `EnsureProjectAsync` with what comes back.
2. Watch a `vercel build` run in a sandbox and check that the output the runner streams is readable, that a
   failure's tail is the useful part, and that `deploy --prebuilt` prints a URL this code can find.
3. Add a domain, and reconcile `DomainAttachment` with the real `verification` array.
4. Check what a rate-limited response actually looks like, and whether `Translate` reads its code.

Until then, treat a green build as saying nothing about whether publishing works. See `whats_next.md`.

## 7. Local development

Publishing is absent locally unless `Deployment:Vercel:Token` is in user secrets, and that is deliberate:
a fresh clone has to run without a hosting account. It also needs a sandbox provider, which locally means
Docker — a publish starts its own container, so `Sandbox:Provider` has to be `docker` and Docker has to be
running. `PublishSite` answers
`DeployError.PublishingUnavailable` → 503 with a sentence, and the client disables the button.

To try it for real, point a dev stack at a throwaway Vercel project:

```
dotnet user-secrets set "Deployment:Vercel:Token" "<token>" --project Webly.Api
dotnet user-secrets set "Sites:BaseDomain" "webly-dev.example.com" --project Webly.Api
```

The subdomain zone has to be one the token can add domains to, or the site's own `{slug}` address will not
resolve — the site will publish and the provider URL will work, which is enough to exercise the pipeline.
