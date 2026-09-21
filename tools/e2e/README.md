# tools/e2e — the product loop, without the backend

```
node tools/e2e/run.mjs --agent mock              # no credentials needed
node tools/e2e/run.mjs --agent claude            # the real CLI; also re-records the parser's fixture
node tools/e2e/run.mjs --agent mock --keep       # leave the workspace and repository to poke at
node tools/e2e/run.mjs --agent mock --out ./site # keep the published site, to open in a browser
```

Needs node and git. Does **not** need .NET, Docker, Postgres, a model key or a hosting account — except
`--agent claude`, which needs the `claude` CLI on PATH and credentials for it.

## Why this exists

Webly's orchestration is C#, and for the repository's first several commits there was no .NET SDK to compile
it with. That left a real question unanswered: *does the thing actually work?* Not "does the code look right"
— does a site get created, edited, committed, previewed, restored and published.

The C# runs now, and `turn.mjs` next door drives a turn through it. This harness is still the cheaper of the
two and still the only one that needs nothing at all: no .NET, no database, no credentials. It is also where
most of the expensive bugs have been found, which is the argument for keeping it.

This script answers it by standing in for the one layer that cannot run, and using the real thing everywhere
else:

| Layer | Here | In the product |
|---|---|---|
| Orchestration | `run.mjs` | `AgentTurnService`, `CommitSiteVersion`, `DeploymentJobRunner` |
| Versioning | `git-store.mjs`, same plumbing sequence | `GitSiteRepositoryStore` |
| Sandbox | `sandbox.mjs`, spawning the agent locally | `LocalSandboxProvider` / `DockerSandboxProvider` / `E2bSandboxProvider` |
| Sandbox contract | **the real `tools/sandbox-agent`** | the same file, baked into the image |
| Coding agent | **the real `claude` CLI**, or the mock's algorithm mirrored | `ClaudeCodeAgent` / `MockCodingAgent` |
| Dev server, build | **the real Next.js**, on the real template | the same |

So the parts most likely to be wrong — git plumbing, an HTTP contract, another product's CLI, a framework's
build — are all exercised for real. What is mocked is the part written in the language that cannot run.

## What it asserts, in order

1. A new site is the template, committed to its own bare repository — with `AGENTS.md` and
   `content/brand.md` — and that first commit's diff, which has no parent to diff against.
2. A turn that changes nothing commits nothing.
3. A sandbox starts and answers `/health`.
4. The commit's tree lands in the workspace, and `node_modules` did not travel through git.
5. `npm ls --depth=0` passes without an install — the prebaked-dependencies bet the sandbox image makes.
6. `next dev` starts and `/preview/` serves the page **through the agent's proxy** — the document *and* its
   stylesheet, under the base path the dev server was started with.
7. A turn runs the agent in the workspace.
8. The stream carries what `ClaudeStreamJsonParser` reads, and the transcript is recorded as its fixture.
   *(`--agent claude` only, so a mock run has seventeen steps rather than eighteen.)*
9. The tree comes back and becomes exactly one commit, authored by the person, with a diff.
10. Hot reload puts the change on screen with nothing on Webly's side asking for it.
11. Re-seeding a warm workspace **removes** files the new tree does not have, and keeps `node_modules`.
12. Restoring an earlier version writes it forward and keeps the history reachable.
13. The publish gate: the site really builds.
14. Publishing copies the export out and **the published page says what the person typed**.
15. The site exports as a git bundle that **clones into a working project** with its history.
16. A broken build is caught rather than published, and the log says why.
17. A compile error is reported when it happens and **stops being reported once it is fixed**.
18. A type error **serves a page happily** and only `npm run typecheck` catches it — and the check leaves no
    `.tsbuildinfo` in the tree that becomes a commit.

## Things it has already caught

- **The preview proxy was broken.** `tools/sandbox-agent` relayed `/preview` by writing a raw HTTP response
  into `response.socket` while the server's own response object still owned it; every fetch failed with "other
  side closed". It now uses `http.request`. This is the bug the whole harness paid for: the preview is the
  product's main surface and it did not work at all.
- **The mock agent edited the wrong file.** Rewriting the hero component's `<h1>` left its `headline` prop
  unused, which the template's `strict` TypeScript rejects. It edits the page's `headline="…"` instead —
  found because step 12 actually builds the result.
- **`base64 -w 0` is not portable.** `FileSystemDeploymentTarget` used it; with the local sandbox provider the
  "sandbox" is the developer's own machine, and that flag is GNU's.
- **Every stopped sandbox leaked a dev server.** `next dev` is a grandchild of the sandbox agent, so killing
  the agent left it holding its port and a few hundred megabytes. Eight had accumulated before anything
  noticed. Both sides now kill the process group, and the agent takes the dev server down on `SIGTERM`.
- **Writing a tree overlaid it instead of replacing it.** `tar -x` only adds and overwrites, so a file the
  incoming tree did not contain survived — meaning a restore that *deleted* a page would re-seed the warm
  workspace with the page still there and commit it straight back. Step 10 exists for this.
- **A hung request stalled the run silently.** `waitFor` bounded the retry loop but not each attempt, so a dev
  server compiling under load held one `fetch` open past every deadline and the harness simply stopped rather
  than failing. Attempts are bounded now.
- **The preview served the HTML and nothing else.** Next.js writes absolute URLs for its stylesheets, its
  chunks and its hot-reload socket, so a dev server proxied under a path asked for `/_next/...` at the root of
  whatever origin was serving it — Webly's own app — and every one of those requests 502ed. The page rendered
  as unstyled text with no JavaScript, and this harness had been asserting on the document since the day it
  was written. The document was perfect. Step 6 reads an asset now.
- **The first commit's diff answered 500.** `git diff {sha}~1 {sha}` is not a revision for a parentless
  commit, so the History tab of every newly created site was broken on the only version it had. What kept it
  hidden is worth more than the fix: `git-store.mjs` had a comment claiming the C# returned an empty string
  there. It did not — a stand-in that *describes* the behaviour it stands in for is not standing in for
  anything. It runs the same `diff-tree --root` now.
- **The build-error report could not fire, and then could not stop firing.** Two bugs in one check.
  `next dev` compiles *on demand*, so right after a turn it has never looked at the agent's edits and the log is
  empty — which reads as "no errors"; the turn now requests the preview first. And the log is *cumulative*, so
  reading all of it re-reports an error from three turns ago for ever; the turn now records an offset before it
  starts. Step 17 covers both, and writing it turned up a third thing worth knowing: `next dev` does not
  typecheck at all, which is why a turn runs `npm run typecheck` as well (step 18). A third attempt at breaking
  a file taught the rest of it: an *unused* broken import is elided as possibly-a-type before anything resolves
  it, so the dev server recompiles it happily. What the compile check can see is syntax errors and imports that
  are used — `CompilerOutput` has the full list and the markers to match.
- **Every turn would have committed the typecheck's cache.** `AGENTS.md` asks the agent to run
  `npm run typecheck`, and `tsc --incremental` writes its cache next to `tsconfig.json` — so a machine-readable
  dump of the project would have travelled out in the tree, into the customer's history, and shown up in their
  diff. It had not happened yet only because every turn driven through the app so far was the mock agent's,
  which does not run it. Step 18 asserts the tree that becomes a commit has no `.tsbuildinfo` in it.
- **The export bundle cloned into an empty directory.** `git bundle create - <branch>` records the commits and
  the ref but no `HEAD`, so `git bundle verify` says "complete history" and `git clone` checks out *nothing*.
  The export endpoint — the feature whose whole point is that a customer can leave with their site — shipped
  that way for about ten minutes. Naming `HEAD` as well fixes it, and step 14 asserts on the cloned working
  tree rather than on the bundle, because only the clone shows the difference.
- **Building in the warm sandbox exports a broken site.** The harness used to publish from the editing
  sandbox. After a re-seed — the source directory deleted and rewritten under a running dev server — `next
  build` reported *success* and produced an `out/` containing only `404.html` and `_next`: no home page at
  all. A publish that reports Ready and serves nothing is about the worst failure this product has.
  `DeploymentJobRunner` already used a fresh sandbox for reasons that were reasoning rather than evidence;
  this is the evidence, and step 12 now does the same thing.

## The fixture

`fixtures/claude-stream-json.ndjson` is a real recorded turn, sanitised of the recorder's session id, tool
catalogue and cost data (`sanitise` in `run.mjs`). `Webly.Tests/ClaudeStreamJsonParserTests.cs` drives the
parser with it. Re-record it by running with `--agent claude`; the diff should be small and readable, and if it
is not, the CLI's output shape changed and the parser needs looking at.

## When to delete this

When `dotnet test` runs, most of it is redundant: `GitSiteRepositoryStoreTests` covers the versioning from the
side that ships, and the real orchestration can be driven through the running app. Keep steps 3–6 and 13–15 in
some form even then — they cover the sandbox contract and the build, which no C# test touches — and delete
`git-store.mjs`, which is the only file here that duplicates logic and can therefore drift.
