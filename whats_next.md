# Where this stopped

Written at the end of the session that initialized the repository — and then pivoted it. `MASTER_PLAN.md` is
the map; this is the handoff note.

## What happened

Webly was designed and written in one pass, from the two reference projects: the layering, the
authentication and the dev stack from **cookta-rework**, the run substrate from **auto-grader**.

**Then the model of a site changed, deliberately and completely.** The first pass made a site a structured
jsonb document over a closed catalogue of section types, rendered to HTML by C# we own. The second — this
one — makes a site **a real Next.js project in a bare git repository, edited by a coding agent's CLI running
in a sandbox**. `docs/domain-plan.md` §1 argues it and is honest about what the document model was better
at. The catalogue, the validator, `SiteDraft`, the renderer and its six section renderers were **deleted**
rather than left in place, so nothing in this tree half-supports the old design.

What exists now: the whole backend (auth; the site/version domain as a commit index; the git repository
layer; sandboxes and the contract they speak; warm workspaces; the coding-agent interface with two
implementations; the run substrate; the preview proxy; publishing and domains), the Angular client (auth
screens, site list, first-site onboarding, the chat editor over a proxied dev server, history with diffs
and restore, domains, settings), the site template, three Dockerfiles, the dev tooling, CI and the
production stack.

## What has *not* happened, exactly

**No .NET SDK existed in the environment this was written in.** So:

1. **Nothing in the backend has been compiled.** Expect real compile errors on the first `dotnet build`.
2. **There is no EF migration.** `Webly.Data/Migrations/` is empty, so the app cannot start until one is
   generated. **And the generated one is not enough by itself** — see §1 below.
3. **`client/src/app/api/` does not exist**, because generating it needs the backend's live swagger. The
   client therefore does not type-check against real DTOs, and CI's client job skips itself while the
   directory is missing (that check deletes itself on the first `regen_api`).
4. **Three integrations have never run**: the two agent CLIs and Vercel (both its REST calls and its CLI).
   §3 and §5.
5. **The sandbox image has never been built**, because there is no Docker here either.

What *was* verified by hand, because these are the parts where being wrong is expensive and none of them
needs .NET:

- **The git plumbing sequence**, against a real temporary bare repository: `hash-object`, `update-index
  --cacheinfo`, `write-tree`, `commit-tree`, `update-ref`, `ls-tree -r -l -z`, `diff-tree`, and a restore by
  writing an old tree forward. `GitSiteRepositoryStoreTests` is the same ground, as tests.
- **The sandbox agent contract** (`tools/sandbox-agent/index.js`) end to end against real node: the bearer
  check, a tar in and a tar out with the excludes applied, `/exec` streaming NDJSON with an exit line, the
  HTTP proxy, and a WebSocket upgrade relayed with an echo back.
- **The site template builds** (`npm run build` in `templates/next-site`).
- **The Angular client builds and prerenders** — browser and SSR bundles, three prerendered routes, all
  templates type-checked — against throwaway stand-ins for the generated client, which were then deleted.

So the component layer, the git layer and the sandbox contract are sound. The service layer's call shapes
and every external CLI are not yet.

## The first six things, in order

### 1. Build, migrate, and hand-write the deferred constraints

```
dotnet build Webly.slnx
dotnet ef migrations add InitialCreate --project Webly.Data --startup-project Webly.Data
```

Then **edit that migration by hand**, because EF cannot express what the model needs. Every foreign key that
points into the version chain is `NoAction` and has to become deferrable:

```csharp
migrationBuilder.Sql("""
    ALTER TABLE "Sites"
        ALTER CONSTRAINT "FK_Sites_SiteVersions_HeadVersionId" DEFERRABLE INITIALLY DEFERRED,
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

(Check the generated constraint names against the migration — EF's naming is predictable but this is copied
from the model, not from a run.)

Then run the canary, which is the whole reason for the above:

```
dotnet test Webly.Tests/Webly.Tests.csproj --filter Deleting_a_user_removes_their_sites
```

If it fails with a foreign-key violation, a constraint name above is wrong. `CLAUDE.md` "Deferred foreign
keys" explains what breaks without it.

### 2. Run the git tests, which need nothing

```
dotnet test Webly.Tests/Webly.Tests.csproj --filter GitSiteRepositoryStore
```

No Postgres, no Docker, no keys — just git and a temp directory. It is therefore the first suite that can be
green, and it covers the layer where being wrong loses somebody's website: the template round trip, a second
init refused, deletion by absence, an identical tree committing nothing, a restore that keeps history, a
file read, a diff, a binary round trip, path traversal refused, and the tree limits.

### 3. The sandbox, end to end

```
docker build -f deploy/sandbox/Dockerfile -t byc0/margareta:webly_sandbox .
```

Then, from the running backend: create a site, and make the workspace start. The chain to watch, in order —
`E2bSandboxProvider` is not involved locally, `DockerSandboxProvider` is:

1. The container starts and `GET /health` answers.
2. `WriteTreeAsync` puts the template's tree in `/workspace` (`docker exec` in and look).
3. `npm ls --depth=0` succeeds, so no install runs — that is the prebaked-dependencies bet, and if it fails
   the first message pays a full `npm install` and the bet needs re-examining.
4. `next dev` starts and `/api/sites/{nanoid}/preview/` renders the starter page **through the proxy**.
5. Edit a file inside the container by hand and confirm the browser updates — that is the WebSocket forward
   in `PreviewController`, and it is the one part of the proxy that fails silently rather than loudly.

### 4. Generate the API client and get the client green

```
./run-app.ps1 start     # backend first: the generator reads its live swagger
regen_api               # or ./regen-api.ps1
cd client && yarn typecheck && yarn build
```

The client's services import names like `apiSitesNanoidVersionsVersionNanoidDiffGet$Plain`, written to match
`ng-openapi-gen`'s convention rather than read off a real run. Expect to fix a handful, and commit the
generated directory. The diff endpoint is the one to check first: it is the only `text/plain` response in the
API, so whether it is `$Plain` or unsuffixed depends on what Swashbuckle emitted for it.

### 5. One real turn

The product is a chat, so the only meaningful verification is a conversation. With
`Agent:ClaudeCode:ApiKey` in user secrets, in one continuous run: register → verify (read the mail from
`.run/mail/`) → create a site → ask for a real change ("we're a bike shop in Utrecht, we do repairs") →
watch the files land and the preview update by itself → open History and read the diff → restore.

Then **reconcile `ClaudeCodeAgent` with what the CLI actually emitted.** The flags
(`-p --output-format stream-json --verbose --permission-mode acceptEdits --add-dir`) and the event shapes
`StreamJsonParser` reads (`assistant` text blocks, `tool_use` names, `session_id`, `result`) come from the
documented interface, not from a run. The `SUMMARY:` convention is ours and the agent may simply not follow
it — if it does not, that is a prompt problem in `BuildPrompt`, not a parser problem.

Two things to watch specifically, because they are where the design is load-bearing rather than obvious: a
**reload mid-turn** (it proves the run outlived the socket, and that the workspace lease did not break), and
**Stop** (it proves a cancelled turn leaves no commit).

### 6. Reconcile Vercel

With a real token in user secrets: publish a starter site, add a domain, check it. Both halves are
unverified — the REST calls from this process and the CLI inside the publishing sandbox.
`docs/deploy-plan.md` §6 lists what to look at, in order.

## Things deliberately left undone, so nobody hunts for them

- **No code view in the client.** `GET /api/sites/{nanoid}/files` and `/file?path=` exist and nothing calls
  them; the file tree over them is the first half of P2.
- **The agent cannot ask a blocking question.** It asks in its reply and the turn ends; the answer is the
  person's next message. The MCP bridge that would make it a tool again is described in
  `docs/agent-plan.md` §1.3 and is not built.
- **One template**, so every site starts the same shape. P3.
- **Hub DTOs are declared by hand** in `realtime.service.ts`, with a comment saying why.
- **`Sandbox:IdleTimeout` is a guess** (ten minutes). It is the product's real unit cost and nobody has
  measured it.
- **No export.** "It is your code" has no button yet — P6, and it is first in that phase for a reason.
- **No forms, no uploads, no billing.** P4, P5, P7.
