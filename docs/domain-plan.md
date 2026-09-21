# The domain: sites, source, versions

The long form of the decisions `CLAUDE.md` states in a paragraph each. Read this before changing the shape
of a site, a version or the repository layer.

## 1. Why real source rather than a structured document

There are two ways to build "an AI that makes websites", and Webly has been both.

The first is a **structured document**: a closed catalogue of section types, a jsonb document validated
against it, a renderer that turns it into HTML. Everything is validatable, nothing can fail to build, and a
property editor can be generated from the same schema the agent writes against. It was built here first,
and the argument for it is still a good one.

The second — what Webly is now — is **a real Next.js project per site**, edited by a coding agent. The
trade is deliberate and it is worth stating both halves.

What the document model gave up, and why it mattered more than the safety:

- **The catalogue is the ceiling.** "Can you put the menu in two columns with the prices right-aligned" is
  either a section type somebody designs and ships, or a no. Every customer request that is not in the
  catalogue is a feature request against Webly, and a product whose extension point is our sprint is not
  self-service.
- **The agent's best skill was unused.** A coding agent that can write a React component was reduced to
  filling in props on a hero. The model that makes this product possible is a model that writes code, and
  the document model was there to stop it doing that.
- **Nobody could leave.** A site expressed only in our jsonb is a site that exists only while Webly does.
  A git repository full of Next.js is a thing a customer can be handed.
- **It was a renderer to maintain for ever.** Six section renderers, a theme-to-CSS compiler, an escaping
  layer, and a rule that the renderer may not read a clock. All of it our code, all of it in the path of
  every page anybody ever sees.

What real source costs, and how each cost is paid:

| Cost | How it is handled |
|---|---|
| The model can break the build | The dev server compiles while the agent works and the errors go into the turn; `vercel build` runs before a publish and a failure blocks it (`DeploymentJobRunner`). A broken site cannot go live. |
| Nobody can edit it in a form | Nobody is asked to. The chat is the editor; the source is readable (`GET /api/sites/{id}/files`) but never something the customer has to touch. |
| A diff is not a history a shop owner reads | The history shows the agent's own sentence per version, with the file count. The diff is there for the person who wants it. |
| The agent needs a filesystem, node and a network | One sandbox per open site, warm while somebody is editing. This is the real price: a warm machine per active editor, billed by the second. `docs/agent-plan.md` §3. |
| An agent could write anything anywhere | It writes in a sandbox with the site's files and a model key, and nothing else — no database, no provider token, no git credential. The commit is made on our side from the tree that comes back. |

**The section catalogue, the renderer, `SiteDraft` and the document model are gone from the repository,
not deprecated in it.** If the argument in this section is ever reversed, the reversal starts from git
history rather than from dead code, and the pivot commit is where to look.

## 2. The shape

A site is three things: a row, a bare git repository, and — while somebody is editing — a sandbox.

```
Site (row)                          .run/repositories/{nanoid}.git      sandbox (ephemeral)
├── Nanoid, Name, Slug              refs/heads/main ──► commit ──► …    /workspace  (the tree)
├── DefaultBranch  "main"                                               next dev    (the preview)
├── HeadVersionId ─────► SiteVersion (CommitSha, Summary, Origin, …)     claude/opencode (the turn)
├── PublishedVersionId ► SiteVersion
└── ProviderProjectId
```

The repository's contents are the template's shape, and the agent works inside it:

```
package.json, next.config.ts, tsconfig.json, postcss.config.mjs
AGENTS.md          the standing rules: never invent a fact, keep the build working, stay in the stack
CLAUDE.md          points at AGENTS.md, so both CLIs read the same instructions
content/brand.md   facts about this business that somebody actually confirmed
src/app/           layout.tsx, page.tsx, globals.css
src/components/    header, hero, features, cta, footer
```

Two of those files are the product, not scaffolding:

- **`AGENTS.md` is the permission model's other half.** The tool list cannot express "do not write a
  testimonial nobody gave you", and that is the rule this product most needs. It lives in the site's own
  repository so both CLIs read it as part of the workspace, and so a rule added next month applies to
  every existing site without a migration.
- **`content/brand.md` is the memory.** A coding agent's session does not outlive a workspace, so a fact
  the person mentioned in March has to be written down somewhere the next session reads. Rule 7 in
  `AGENTS.md` is what makes that happen, and it is why the file is committed rather than kept in a table:
  the agent can read and edit it with the same tools it uses for everything else.

## 3. Versioning: a version is a commit

```
Site ──HeadVersionId───────► SiteVersion(sha=b) ──ParentVersionId──► SiteVersion(sha=a) ──► (first)
     └─PublishedVersionId──► SiteVersion(sha=a)
```

Git already does immutable snapshots, parents, deduplicated storage and diffs better than a table can, so
**nothing in the database duplicates the repository**: no file contents, no tree, no diff. The
`SiteVersion` row is the index — a nanoid the API can address, which chat message asked for it, whether it
was a restore, how many files it touched, and whether it is the head or the published one.

- **Every accepted change appends a commit.** `CommitSiteVersion` is the only writer, and a second write
  path would be a second definition of history.
- **A turn that changed nothing commits nothing.** `GitSiteRepositoryStore.WriteTreeAndCommitAsync`
  compares the tree it built against the parent's and returns null when they are identical, so a turn that
  answered a question in prose leaves no version behind. A history of "no changes" entries is not a
  history.
- **The agent never sees git.** The sandbox gets a working tree, not a clone. So it cannot rewrite history,
  cannot force-push, cannot commit something Webly did not see, and needs no credential that could do any
  of those. What comes back is a tar of the tree; Webly writes it as a commit with the person as author.
- **Restore writes forward.** `RestoreSiteVersion` reads the old commit's tree and commits it as a new
  commit on the branch. Moving the branch back would orphan the intervening history, which is the one
  thing a version-control feature must never do; and undoing an undo is then the same operation again.
- **`HasUnpublishedChanges` is computed** from the two pointers. A boolean beside them is a third fact that
  can disagree with both.
- **`PublishedVersionId` moves in exactly one place**, in `DeploymentJobRunner` after the provider reports
  success. A failed publish leaves the previous version live and the email says so in its first line.
- **A version links to the message that produced it** (`SourceMessageId`), and the message links back
  (`ProducedVersionId`). That pair is what makes the history read as the conversation that caused it.

### The repository layer

`ISiteRepositoryStore` is the only thing that touches git, and it drives the real binary — `hash-object`,
`update-index`, `write-tree`, `commit-tree`, `update-ref`, `ls-tree`, `diff-tree` — rather than a managed
reimplementation of the object format. Three things about it are deliberate:

- **Plumbing, not porcelain.** There is no working copy on the API's side at all: a commit is built by
  hashing blobs into a temporary index (`GIT_INDEX_FILE` per call) and writing a tree from it. So two
  turns on two sites cannot collide over a checkout, and the API needs no disk beyond the bare repository.
- **Arguments, never a shell.** Every call goes through `ProcessStartInfo.ArgumentList`. A site's nanoid
  reaches a path, and a path that is concatenated into a command line is an injection waiting for the
  first customer who tries.
- **Paths are validated, not trusted.** `PathFor` refuses anything that is not `[A-Za-z0-9-_]{1,40}`, and
  tree writes refuse absolute paths, `..` segments, and trees over the configured file-count and byte
  limits. The tar arriving from a sandbox was assembled by a language model on a machine we do not own.

`GitSiteRepositoryStoreTests` covers all of that against real git in a temporary directory — no Postgres,
no Docker — because it is the one layer where being wrong loses somebody's website.

### What is deliberately absent

- **Branches and merges.** A solo owner does not branch. `Site.DefaultBranch` exists so that the day a
  staging branch is wanted, the column is not a migration.
- **Giving the customer the git URL.** It is their code and they should be able to have it, but a
  read-write remote means somebody pushes a commit Webly never validated and the next agent turn starts
  from it. An export (a tarball, or a push to their own GitHub) is the shape that lands first.
- **A version limit or pruning.** Git deduplicates; a thousand versions of a text project is nothing.
- **Tags or labels beyond `Summary` and `Origin`.** The history is read by scrolling, not by querying.

## 4. Ownership

A `Site` has one `OwnerId`. There are no memberships, no roles and no owner column beside a role enum —
the reference project spent real effort keeping `ownerSub` in step with an owner role and found families
with neither. `ISiteRepository.FindForOwnerAsync` is the single place the ownership check lives, so a use
case starts from an already-authorised site and cannot express the check wrongly. Not yours reads as
**404**, never 403: a 403 would confirm that a guessed nanoid names a real site.

That rule reaches further now than it did: the preview is a proxy into a running machine, so
`PreviewController` re-checks ownership **per request** through the same method. A preview is not a public
URL with a guessable id, and the sandbox's own address and token never leave the server.

When collaboration lands, it is a membership table plus one clause inside `FindForOwnerAsync`, and nothing
above it changes. That is the whole reason the check is in one query.

## 5. Publishing and hosting

- **Webly builds; the provider serves.** `vercel build` runs in a sandbox and `vercel deploy --prebuilt`
  uploads the result, so the build happens where we can read its log and hand it to the person — rather
  than on the provider's side, where a failure is a page the site's owner cannot act on. It also means the
  publish gate is real: a site that does not compile does not go live.
- **A publish uses a fresh sandbox**, not the warm editing one. A build must not depend on whatever the
  editing session left in `node_modules`, and `npm ci` against the committed lockfile is what makes a
  failed deployment safe to retry.
- **A deployment is a durable job**, unlike an agent turn: it changes the outside world and must survive a
  restart, so its state is a row (`Deployment`) and its run id *is* that row's nanoid, which is what lets
  a page reloaded mid-publish re-attach to something that outlived the process.
- **Domains are mirrored, never decided locally.** The provider owns DNS verification and the certificate;
  a local "verified" flag it disagrees with is a site that is live according to us and 404 according to the
  internet.
- **One primary hostname per site**, kept by a partial unique index. Two canonical hostnames split a
  site's search ranking in half.

## 6. The next domain questions, in the order they will be asked

1. ~~**Export.**~~ Done: `GET /api/sites/{nanoid}/export` returns a **git bundle**, and the settings screen
   links to it. A bundle rather than a tarball because a tarball is a snapshot and a bundle is the repository —
   `git clone site.bundle` gives a working project with every version and every commit message. What is still
   open is the half that needs the customer's credentials: a push to their own GitHub.
2. ~~**Forms.**~~ Done, and the question this line said to decide first answered itself: a published site is a
   **static export**, so it has no server. A route handler the agent wrote would compile and then 404 in front
   of a customer, which is worse than not having one. The endpoint is Webly's —
   `POST /api/public/forms/{siteNanoid}`, an ordinary cross-origin form post so that it needs no CORS and no
   JavaScript — and the starter template ships the form that uses it. `CLAUDE.md`'s Forms section is the
   short version; the caps, the honeypot and the reply-to are the parts worth reading before changing it.
3. ~~**Assets.**~~ Done, and without the blob store this line assumed. An image is committed into the site's
   repository under `public/images/`, which makes it a version: the published site serves it from its own
   domain, it travels with the git bundle, a restore takes it back, and the sandbox gets it in the tree it is
   already seeded with. The seam that was going to be "a URL the sandbox can fetch" turned out not to be
   needed at all. The resizing happens in the browser, before the upload. `CLAUDE.md`'s Images section is the
   short version.
4. **A second locale per site.** With real source this is the App Router's `[locale]` segment rather than
   a second document tree, which is a much smaller decision than it was. Still do not guess it early.
5. **Collaboration**, as in §4.
