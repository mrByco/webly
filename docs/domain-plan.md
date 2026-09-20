# The domain: sites, documents, versions

The long form of the decisions `CLAUDE.md` states in a paragraph each. Read this before changing the
shape of a site, a version or a section.

## 1. Why a structured document rather than generated source

The obvious build of "an AI that makes websites" is an agent that writes HTML — or Astro, or JSX — into
files, and a provider that builds them. Webly does not, and this is the decision everything else hangs
off.

What a pile of model-written source costs:

- **Nobody can edit it but the model.** The product promises "without touching a single line of code",
  which means a person must be able to change a headline in a form. A form over arbitrary HTML is a
  rich-text editor that loses the layout, or a code editor with a different promise.
- **Nothing can be validated.** A missing closing tag, a broken class name, an image URL that 404s, a
  page that no longer fits a phone — all of them are discovered by a visitor. A schema-checked document
  is refused at the write.
- **Every deploy becomes "did the model break the build".** A build log is exactly the artefact this
  product exists to hide.
- **Diffs are unreadable.** "Version 12 changed 400 lines" is not a history a shop owner can use.

What the structured document costs instead: a closed catalogue. The agent can only build from sections
that exist. That is a real limit, and it is the right one — a catalogue entry is designed once, is
responsive, is accessible, renders in both themes, and is editable by hand afterwards. Nothing the model
produces can be less good than the catalogue.

## 2. The shape

```
SiteDocument
├── schemaVersion        so a future restructure has something to switch on
├── theme                one brand colour, mode, font pairing, radius, density
├── navigation           header, footer, one primary action, footer note
└── pages[]
    ├── id               stable across renames, so links survive them
    ├── path             "/", "/about" — unique per document
    ├── title, seo
    └── sections[]
        ├── id           stable, so a tool call can name it after its neighbours moved
        ├── type         SectionType, a closed enum
        ├── props        a JSON object, validated against the type's schema
        └── hidden
```

Three things to notice, each of which is a rule stated once in code:

- **Nothing is duplicated.** The home page is the one whose path is `/` (no `isHome` flag). A page's
  order in the array is its order (no sort field). A link names a page by id *or* a URL, never both.
- **Props are untyped C# and schema-checked.** `SectionCatalogue` declares the fields; the validator, the
  agent's instructions, the client's property editor and the renderer's template all read that one
  declaration. The reference project (auto-grader) has typed payload classes instead and records the
  result in its own notes: a block type spanning five registration points, where missing one is a blank
  space on a page.
- **Images are URLs in this slice.** `SectionFieldKind.Image` validates an https URL, and the renderer
  resolves every image through one method so that an owned asset store (MASTER_PLAN P5) is one method
  body rather than a change to six templates.

## 3. Versioning

```
Site ──DraftVersionId──────► SiteVersion ──ParentVersionId──► SiteVersion ──► … ──► (first)
     └─PublishedVersionId──► SiteVersion
```

- **Every accepted change appends a version.** There is no mutable working copy. One agent turn is one
  version; one hand edit is one version; a restore is one version.
- **A version holds the whole document**, not a diff. A diff chain has to be replayed before anything can
  be rendered, one bad entry poisons everything after it, and the thing a person wants ("what did my site
  look like on Tuesday") is the snapshot. A document is a few kilobytes of jsonb.
- **Two pointers, not a snapshot plus a flag.** `HasUnpublishedChanges` is `PublishedVersionId !=
  DraftVersionId`, computed in the mapper. A boolean beside the pointers is a third fact that can
  disagree with both.
- **`PublishedVersionId` moves only when a deployment succeeds.** A failed publish leaves the previous
  version live and says so.
- **Restore copies forward.** Repointing the draft at an older version would make the intervening history
  unreachable, which is the one thing a version-control feature must never do. Restoring is itself an
  edit: it appears in the history, and undoing an undo is the same operation again.
- **A version links to the message that produced it** (`SourceMessageId`), and the message links back
  (`ProducedVersionId`). That pair is what makes the history read as the conversation that caused it.

### What is deliberately absent

- **Branches and merges.** A solo owner does not branch, and merging two documents needs conflict UX
  nobody has asked for. If it ever lands, the chain is already a DAG in everything but enforcement.
- **A version limit or pruning.** A thousand versions is smaller than one hero image.
- **Tags or labels beyond `Summary` and `Origin`.** The history is read by scrolling, not by querying.

## 4. Editing: one path for the agent and the person

`SiteDraft` is the only way a document changes, whoever is changing it. It holds a clone, exposes the
operations, and validates the whole document after each one — keeping the result only if it is still
valid. Two invariants follow:

1. **A draft is always valid.** A failed edit changes nothing and reports why, so the agent can fix its
   own mistake on the next tool call rather than losing nine good edits at commit time.
2. **A draft is never the persisted document.** It starts as a clone (deep, via JSON — `JsonNode`
   remembers its parent, so a member-wise copy would either throw or alias the stored version's props),
   so an abandoned turn, a cancelled run or a failed commit leaves the stored version untouched. Nothing
   rolls back because nothing was written.

`CommitSiteVersion` is the only writer. `EditSection` (hand edit), `RestoreSiteVersion` and
`SiteEditSession` (the agent's turn) all end there, which is why a hand edit, an agent edit and a restore
are validated by the same rules and appear in the same history.

## 5. Ownership

A `Site` has one `OwnerId`. There are no memberships, no roles and no owner column beside a role enum —
the reference project spent real effort keeping `ownerSub` in step with an owner role and found families
with neither. `ISiteRepository.FindForOwnerAsync` is the single place the ownership check lives, so a use
case starts from an already-authorised site and cannot express the check wrongly. Not yours reads as
**404**, never 403: a 403 would confirm that a guessed nanoid names a real site.

When collaboration lands, it is a membership table plus one clause inside `FindForOwnerAsync`, and
nothing above it changes. That is the whole reason the check is in one query.

## 6. Publishing and hosting

- **Webly renders to bytes; the provider serves them.** No build step on the provider's side: a build can
  fail for reasons the site's owner cannot see, and there is no code for them to fix it in. It also makes
  a deployment reproducible, which is what makes a failed one safe to retry.
- **One page per directory** (`/about/index.html`), so a static host serves clean URLs with no rewrite
  rules — the part of static hosting that differs per provider, avoided.
- **A deployment is a durable job**, unlike an agent turn: it changes the outside world and must survive
  a restart, so its state is a row (`Deployment`) and that row is its own progress log.
- **Domains are mirrored, never decided locally.** The provider owns DNS verification and the
  certificate; a local "verified" flag it disagrees with is a site that is live according to us and 404
  according to the internet.
- **One primary hostname per site**, kept by a partial unique index. Two canonical hostnames split a
  site's search ranking in half.

## 7. The next domain questions, in the order they will be asked

1. **Forms.** A contact section needs somewhere for submissions to go: a `FormSubmission` table, an
   email, and spam handling. It is a slice, not a section type.
2. **Assets.** Uploads, a blob store, image conversion and a resolver that tells a blob name from a URL.
   `SectionFieldKind.Image` and the renderer's `ResolveImage` are the two seams it lands in.
3. **More section types.** Gallery, Pricing, LogoCloud, Stats, Steps, Team, LocationMap — each four
   edits, in one place each.
4. **A second locale per site.** The document is a tree of text; a locale is a second tree, which is a
   decision about whether pages or documents multiply. Do not guess it early.
5. **Collaboration**, as above.
