# Publishing, domains and hosting

## 1. The shape

```
PublishSite (endpoint)          writes a Deployment row, status Queued, and returns 202
        │
DeploymentJobRunner (hosted)    leases it, and:
        ├── Rendering           ISiteRenderer turns the version's document into bytes
        ├── Uploading           IDeploymentTarget hands the bytes to the provider
        ├── Ready               Site.PublishedVersionId := the deployed version   ← the only place
        └── Failed              Deployment.Error, previous version still live
```

Progress is published on the realtime hub as a `Deploy` run **and** written to the row. The stream is for
the person watching; the row is for the one who closed the tab, which is the case that makes a deployment
a job rather than a request.

## 2. Why Webly renders

The alternative is pushing a repository and letting the provider build it. Rejected because:

- A build on somebody else's machine can fail for reasons the site's owner cannot see, and there is no
  code for them to fix it in. "Without touching a single line of code" has to mean there is no build log.
- A render that is a pure function of (document, context) always produces the same bytes, which is what
  makes a failed deployment safe to retry.
- There is no repository to own, so there is no git account, no branch, no merge conflict and no
  `.gitignore` in the product.

So: `projectSettings.framework = null`, no build command, no install command, files uploaded inline with
the deployment. Vercel serves exactly the bytes we sent.

## 3. Why one platform-owned Vercel account

"Self-service" cannot begin with "create a Vercel account and generate an API token". So Webly owns the
account, creates one project per site (`webly-{siteNanoid}`), and the customer never hears the provider's
name.

The costs, written down because they are real:

- **Our quota is shared.** The `deploy` rate-limit policy (per user, 20/hour) exists so that one stuck
  client cannot get every other customer's publishing rate-limited.
- **Our bill.** Metering is MASTER_PLAN P7.
- **Our account is the blast radius.** The token is a single secret with rights over every customer's
  hosting, which is an argument for the upgrade below rather than against the decision.

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

`VercelDeploymentTarget` follows Vercel's documented v9/v10/v11/v13 REST endpoints, and **has never been
run against the real service** — no token existed in the environment where it was written. The endpoints
and payload shapes are the plan, not tested code. The first task with a real token is:

1. Create a site, publish it, and reconcile `EnsureProjectAsync` / `DeployAsync` with what comes back.
2. Add a domain, and reconcile `DomainAttachment` with the real `verification` array.
3. Check what a rate-limited response actually looks like, and whether `Translate` reads its code.

Until then, treat a green build as saying nothing about whether publishing works. See `whats_next.md`.

## 7. Local development

Publishing is absent locally unless `Deployment:Vercel:Token` is in user secrets, and that is deliberate:
a fresh clone has to run without a hosting account. `PublishSite` answers
`DeployError.PublishingUnavailable` → 503 with a sentence, and the client disables the button.

To try it for real, point a dev stack at a throwaway Vercel project:

```
dotnet user-secrets set "Deployment:Vercel:Token" "<token>" --project Webly.Api
dotnet user-secrets set "Sites:BaseDomain" "webly-dev.example.com" --project Webly.Api
```

The subdomain zone has to be one the token can add domains to, or the site's own `{slug}` address will not
resolve — the site will publish and the provider URL will work, which is enough to exercise the pipeline.
