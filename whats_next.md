# Where this stopped

Written at the end of the session that initialized the repository. `MASTER_PLAN.md` is the map; this is the
handoff note.

## What happened

Webly was designed and written in one pass, from the two reference projects: the layering, the
authentication and the dev stack from **cookta-rework**, the agent framework and the run substrate from
**auto-grader**. See `PROJECT.md` for what came from where and `CLAUDE.md` for the rules that came with it.

What exists: the whole backend (auth, the site/version domain, the section catalogue and its validator,
the renderer, the agent and its run substrate, publishing and domains), the Angular client (auth screens,
site list, first-site onboarding, the chat editor with a live preview, history with restore, domains,
settings), the dev tooling, CI, and the production stack.

## What has *not* happened, exactly

**No .NET SDK existed in the environment this was written in.** So:

1. **Nothing in the backend has been compiled.** Expect real compile errors on the first `dotnet build` —
   most likely around the `Microsoft.Agents.AI` surface (§2 below) and the odd missing `using`.
2. **There is no EF migration.** `Webly.Data/Migrations/` is empty, so the app cannot start until one is
   generated. **And the generated one is not enough by itself** — see §1.
3. **`client/src/app/api/` does not exist**, because generating it needs the backend's live swagger. The
   client therefore does not type-check against real DTOs, and CI's client job skips itself while the
   directory is missing (that check deletes itself on the first `regen_api`).
4. **`VercelDeploymentTarget` has never spoken to Vercel.** `docs/deploy-plan.md` §6 lists what to
   reconcile.

What *was* verified: the Angular client **builds** — browser and SSR bundles, three prerendered routes, all
templates type-checked — against hand-written stand-ins for the generated client, which were then deleted.
So the component and template layer is sound; the service layer's call shapes are not yet.

## The first five things, in order

### 1. Build, migrate, and hand-write the deferred constraints

```
dotnet build Webly.slnx
dotnet ef migrations add InitialCreate --project Webly.Data --startup-project Webly.Data
```

Then **edit that migration by hand**, because EF cannot express what the model needs. Every foreign key
that points into the version chain is `NoAction` and has to become deferrable:

```csharp
migrationBuilder.Sql("""
    ALTER TABLE "Sites"
        ALTER CONSTRAINT "FK_Sites_SiteVersions_DraftVersionId" DEFERRABLE INITIALLY DEFERRED,
        ALTER CONSTRAINT "FK_Sites_SiteVersions_PublishedVersionId" DEFERRABLE INITIALLY DEFERRED;
    ALTER TABLE "SiteVersions"
        ALTER CONSTRAINT "FK_SiteVersions_SiteVersions_ParentVersionId" DEFERRABLE INITIALLY DEFERRED,
        ALTER CONSTRAINT "FK_SiteVersions_SiteVersions_RestoredFromVersionId" DEFERRABLE INITIALLY DEFERRED,
        ALTER CONSTRAINT "FK_SiteVersions_ConversationMessages_SourceMessageId" DEFERRABLE INITIALLY DEFERRED;
    ALTER TABLE "ConversationMessages"
        ALTER CONSTRAINT "FK_ConversationMessages_SiteVersions_ProducedVersionId" DEFERRABLE INITIALLY DEFERRED;
    ALTER TABLE "Deployments"
        ALTER CONSTRAINT "FK_Deployments_SiteVersions_SiteVersionId" DEFERRABLE INITIALLY DEFERRED;
    """);
```

(Check the generated constraint names against the migration — EF's naming is predictable but this is
copied from the model, not from a run.)

Then run the canary, which is the whole reason for the above:

```
dotnet test Webly.Tests/Webly.Tests.csproj --filter Deleting_a_user_removes_their_sites
```

If it fails with a foreign-key violation, a constraint name above is wrong. `CLAUDE.md` "Deferred foreign
keys" explains what breaks without it.

### 2. Reconcile the agent's streaming surface

`AgentTurnService.StreamAsync` and `SiteEditorAgent.Create` are written against `Microsoft.Agents.AI`
1.9.0 / `Microsoft.Extensions.AI` 10.6.0 — the versions the reference project pins — but from its source
rather than from a compiler. Expect to adjust:

- `agent.RunStreamingAsync(messages, …)`'s exact signature and update type.
- Whether `TextContent` / `FunctionCallContent` / `FunctionResultContent` arrive on `update.Contents` the
  way this assumes.
- `ChatClientAgent` + `ChatClientAgentOptions`, and whether tools belong on `ChatOptions.Tools` there.

Everything around it — the writer, the sink, the log, the replay, the hub, the budget, the reaper — is
independent of that surface. Fix this one method and the substrate is untouched.

### 3. Generate the API client and get the client green

```
./run-app.ps1 start     # backend first: the generator reads its live swagger
regen_api               # or ./regen-api.ps1
cd client && yarn typecheck && yarn build
```

The client's services import names like `apiSitesNanoidVersionsVersionNanoidRestorePost$Json`, which were
written to match `ng-openapi-gen`'s convention rather than read off a real run. Expect to fix a handful of
them, and commit the generated directory.

### 4. Drive the real app

The product is a chat, so the only meaningful verification is a conversation. In one continuous run:
register → verify (read the mail from `.run/mail/`) → create a site → ask for a real change → answer the
question it asks → reload the page mid-turn and confirm the answer still arrives → press Stop on a later
turn → open History and restore. Screenshots as evidence.

Two things to watch specifically, because they are where the design is load-bearing rather than obvious:
the reload mid-turn (it proves the run outlived the socket) and the preview refreshing when a version
commits.

### 5. Reconcile Vercel

With a real token in user secrets: publish a starter site, add a domain, check it. `docs/deploy-plan.md` §6
and §7.

## Things deliberately left undone, so nobody hunts for them

- **Rich text is escaped, not sanitized** (`SectionMarkup.RichTextHtml`), so a `body` field renders its
  markup visibly. The allowlist lands with the rich-text editor in P2, using a library.
- **No property editor yet.** `GET /api/sites/catalogue` and `POST /api/sites/{nanoid}/sections` are both
  there and unused by the client; the form generated from them is the first half of P2.
- **No page switcher in the preview.** `SitePreview` takes a `page` input that nothing sets yet.
- **Six section types**, not fourteen. The other eight are listed in `SectionType`'s own comment, in the
  order they are worth adding.
- **Hub DTOs are declared by hand** in `realtime.service.ts`, with a comment saying why.
- **No forms, no uploads, no billing.** P4, P5, P7.
