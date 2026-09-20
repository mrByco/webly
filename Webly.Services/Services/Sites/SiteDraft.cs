using NanoidDotNet;
using System.Text.Json.Nodes;
using Webly.Data.Models.Sites.Document;

namespace Webly.Services.Services.Sites;

/// <summary>
/// A site being edited: one document, a list of what changed, and the operations that may change it.
/// Every edit in Webly — from the agent, from the property editor, from a restore — goes through a
/// draft, so there is exactly one implementation of "add a section" and one place where the rules are
/// checked.
///
/// <b>Two invariants make the rest of the system simple.</b>
///
/// First, <b>a draft is always valid</b>. Each operation works on a clone, validates the whole document,
/// and either commits the clone or throws the clone away and reports the problems — so a failed edit
/// changes nothing. The alternative, validating once at commit time, means a turn that made nine good
/// edits and one bad one loses all ten, and the model finds out at the end with no way to tell which
/// call was wrong.
///
/// Second, <b>a draft is never the persisted document</b>. It starts as a clone (see
/// <see cref="SiteDocument.Clone"/> for why that matters with <c>JsonNode</c>), so an abandoned turn, a
/// cancelled run or a failed commit leaves the stored version exactly as it was. Nothing rolls back
/// because nothing was ever written.
///
/// A turn's accumulated <see cref="Changes"/> become the version's <c>Summary</c>, which is what makes
/// the history readable — see <see cref="Summarize"/>.
/// </summary>
public sealed class SiteDraft
{
    private SiteDocument _document;
    private readonly List<string> _changes = [];

    private SiteDraft(SiteDocument document) => _document = document;

    /// <summary>
    /// Starts from a stored version's document. The argument is cloned, never captured.
    /// </summary>
    public static SiteDraft From(SiteDocument baseDocument) => new(baseDocument.Clone());

    /// <summary>The document as it stands. Read it; change it through the operations below.</summary>
    public SiteDocument Document => _document;

    /// <summary>What happened, in the order it happened, one sentence per edit.</summary>
    public IReadOnlyList<string> Changes => _changes;

    public bool IsDirty => _changes.Count > 0;

    // ── Pages ──────────────────────────────────────────────────────────────────────────────

    public DraftResult AddPage(string path, string title)
    {
        var pageId = NewId();

        return Apply(
            document => document.Pages.Add(new SitePage
            {
                Id = pageId,
                Path = NormalizePath(path),
                Title = title,
                Sections = []
            }),
            $"Added the page {NormalizePath(path)} ({title})",
            pageId);
    }

    public DraftResult UpdatePage(string pageId, string? path, string? title, PageSeo? seo)
    {
        if (_document.FindPage(pageId) is null)
            return NoSuchPage(pageId);

        return Apply(
            document =>
            {
                var page = document.FindPage(pageId)!;
                if (path is not null) page.Path = NormalizePath(path);
                if (title is not null) page.Title = title;
                if (seo is not null) page.Seo = seo;
            },
            $"Updated the page {_document.FindPage(pageId)!.Path}");
    }

    public DraftResult RemovePage(string pageId)
    {
        var page = _document.FindPage(pageId);
        if (page is null) return NoSuchPage(pageId);

        if (page.Path == "/")
            return DraftResult.Fail("The home page cannot be deleted. Change what is on it instead.");

        return Apply(
            document => document.Pages.RemoveAll(x => x.Id == pageId),
            $"Deleted the page {page.Path}");
    }

    // ── Sections ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a section. <paramref name="props"/> may be partial or null: what is missing is taken from
    /// <see cref="SectionCatalogue.DefaultProps"/>, so "put a FAQ at the bottom of the home page" is one
    /// call and the fields can be filled afterwards.
    /// </summary>
    public DraftResult AddSection(string pageId, SectionType type, JsonObject? props = null, int? index = null)
    {
        if (_document.FindPage(pageId) is null) return NoSuchPage(pageId);

        var sectionId = NewId();
        var merged = SectionCatalogue.DefaultProps(type);

        foreach (var (key, value) in props ?? new JsonObject())
            merged[key] = value?.DeepClone();

        return Apply(
            document =>
            {
                var page = document.FindPage(pageId)!;
                var section = new SiteSection { Id = sectionId, Type = type, Props = merged };

                var at = Math.Clamp(index ?? page.Sections.Count, 0, page.Sections.Count);
                page.Sections.Insert(at, section);
            },
            $"Added a {type} section to {_document.FindPage(pageId)!.Path}",
            sectionId);
    }

    /// <summary>
    /// Merges <paramref name="patch"/> into a section's props: keys present are replaced, keys absent are
    /// left alone, and a key whose value is JSON null is removed. A patch rather than a whole-props write,
    /// because "change the headline" should not require the caller to resend the other six fields — and a
    /// caller that resends them from a stale read is how an edit silently reverts another one.
    /// </summary>
    public DraftResult UpdateSection(string sectionId, JsonObject patch)
    {
        if (FindSection(sectionId) is not { } found) return NoSuchSection(sectionId);

        var (page, section) = found;

        return Apply(
            document =>
            {
                var target = document.FindPage(page.Id)!.FindSection(sectionId)!;

                foreach (var (key, value) in patch)
                {
                    if (value is null)
                        target.Props.Remove(key);
                    else
                        target.Props[key] = value.DeepClone();
                }
            },
            $"Edited the {section.Type} section on {page.Path}");
    }

    public DraftResult MoveSection(string sectionId, int toIndex)
    {
        if (FindSection(sectionId) is not { } found) return NoSuchSection(sectionId);

        var (page, section) = found;

        return Apply(
            document =>
            {
                var sections = document.FindPage(page.Id)!.Sections;
                var current = sections.FindIndex(x => x.Id == sectionId);
                var moving = sections[current];

                sections.RemoveAt(current);
                sections.Insert(Math.Clamp(toIndex, 0, sections.Count), moving);
            },
            $"Moved the {section.Type} section on {page.Path}");
    }

    public DraftResult RemoveSection(string sectionId)
    {
        if (FindSection(sectionId) is not { } found) return NoSuchSection(sectionId);

        var (page, section) = found;

        return Apply(
            document => document.FindPage(page.Id)!.Sections.RemoveAll(x => x.Id == sectionId),
            $"Removed the {section.Type} section from {page.Path}");
    }

    public DraftResult SetSectionHidden(string sectionId, bool hidden)
    {
        if (FindSection(sectionId) is not { } found) return NoSuchSection(sectionId);

        var (page, section) = found;

        return Apply(
            document => document.FindPage(page.Id)!.FindSection(sectionId)!.Hidden = hidden,
            $"{(hidden ? "Hid" : "Unhid")} the {section.Type} section on {page.Path}");
    }

    // ── Theme and navigation ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Changes the parts of the theme the caller named and leaves the rest. Nullable parameters rather
    /// than a whole theme object, for the same reason section edits are patches.
    /// </summary>
    public DraftResult SetTheme(
        string? primaryColor = null,
        ThemeMode? mode = null,
        FontPairing? fonts = null,
        ThemeRadius? radius = null,
        ThemeDensity? density = null) =>
        Apply(
            document =>
            {
                var theme = document.Theme;
                if (primaryColor is not null) theme.PrimaryColor = primaryColor;
                if (mode is not null) theme.Mode = mode.Value;
                if (fonts is not null) theme.Fonts = fonts.Value;
                if (radius is not null) theme.Radius = radius.Value;
                if (density is not null) theme.Density = density.Value;
            },
            "Changed the site's look");

    public DraftResult SetNavigation(SiteNavigation navigation) =>
        Apply(document => document.Navigation = navigation, "Updated the menu");

    /// <summary>
    /// Replaces the whole document. Only <c>RestoreSiteVersion</c> uses this, and it goes through
    /// <see cref="Apply"/> like every other edit — so a document written under an older section catalogue is
    /// re-validated before it can be restored, rather than being trusted because it was valid once.
    /// </summary>
    public DraftResult Replace(SiteDocument document, string change) =>
        Apply(current =>
        {
            current.SchemaVersion = document.SchemaVersion;
            current.Theme = document.Theme;
            current.Navigation = document.Navigation;
            current.Pages = document.Pages;
        }, change);

    // ── Committing ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one-line summary this draft's version carries into the history. One change is described by
    /// itself; several are counted with the first named, because "12 changes" alone is not a history and
    /// a paragraph does not fit in a list.
    /// </summary>
    public string Summarize() => _changes.Count switch
    {
        0 => "No changes",
        1 => _changes[0],
        _ => $"{_changes[0]}, and {_changes.Count - 1} more change{(_changes.Count == 2 ? string.Empty : "s")}"
    };

    // ── Internals ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one edit against a clone, validates the result, and keeps it only if the document is still
    /// valid. This is where the "a draft is always valid" invariant is actually enforced; every operation
    /// above is a call to it.
    /// </summary>
    private DraftResult Apply(Action<SiteDocument> edit, string change, string? id = null)
    {
        var candidate = _document.Clone();

        edit(candidate);

        var problems = SiteDocumentValidator.Validate(candidate);
        if (problems.Count > 0) return DraftResult.Fail(problems);

        _document = candidate;
        _changes.Add(change);

        return DraftResult.Ok(id);
    }

    private (SitePage Page, SiteSection Section)? FindSection(string sectionId)
    {
        foreach (var page in _document.Pages)
        {
            var section = page.FindSection(sectionId);
            if (section is not null) return (page, section);
        }

        return null;
    }

    private DraftResult NoSuchPage(string pageId) =>
        DraftResult.Fail($"This site has no page with id '{pageId}'. Its pages are: {PageList()}.");

    private DraftResult NoSuchSection(string sectionId) =>
        DraftResult.Fail($"This site has no section with id '{sectionId}'. Read the page first to see its sections.");

    private string PageList() =>
        string.Join(", ", _document.Pages.Select(x => $"{x.Path} ({x.Id})"));

    /// <summary>
    /// Ids for pages and sections. Minted here rather than by the database, because the document is not a
    /// set of rows — there is nothing for <c>SaveChanges</c> to stamp. Shorter than an entity nanoid: these
    /// appear in tool calls and in the model's context, and every character of an id is a token.
    /// </summary>
    private static string NewId() => Nanoid.Generate(size: 10);

    /// <summary>
    /// Accepts what a person or a model is likely to type — "about", "/about/", "About" — and produces the
    /// one spelling the document stores. Normalizing here rather than rejecting is deliberate: the shape of
    /// a path is not something anybody should have to get right to add a page.
    /// </summary>
    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().Trim('/').ToLowerInvariant();

        return trimmed.Length == 0 ? "/" : $"/{trimmed}";
    }
}
