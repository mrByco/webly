using Webly.Data.Repositories.Domains;
using Webly.Data.Repositories.Sites;
using Webly.Services.DTO.Common;
using Webly.Services.DTO.Sites;
using Webly.Services.Services.Rendering;
using Webly.Services.Services.Sites;

namespace Webly.Services.UseCases.Sites;

/// <summary>
/// Renders one page of a version for the editor's preview pane.
///
/// <b>The preview is the renderer.</b> The pane is an iframe over this endpoint, not a second implementation of what a
/// site looks like in TypeScript — so what somebody approves in the editor is byte-for-byte what publishing uploads.
/// The reference projects both record the cost of the alternative: a viewer and a renderer that drift are two
/// descriptions of one thing, and the one that is wrong is always the one the customer saw.
///
/// It also means a section type needs no client-side template. One template, in C#, is what "adding a section type is
/// four edits" depends on.
/// </summary>
public class PreviewSite(
    ISiteRepository siteRepository,
    ISiteVersionRepository versionRepository,
    IDomainRepository domainRepository,
    ISiteRenderer renderer,
    SiteMapper mapper)
{
    /// <summary>
    /// The stylesheet for a version, served beside the preview. Separate from the page so the browser caches it once
    /// per theme change instead of re-parsing it inside every page load.
    /// </summary>
    public async Task<Result<SiteError, string>> StylesheetAsync(
        int userId,
        string siteNanoid,
        string? versionNanoid = null,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, string>.Fail(SiteError.NotFound);

        var version = versionNanoid is null
            ? site.DraftVersion
            : await versionRepository.FindForSiteAsync(versionNanoid, site.Id, cancellationToken);

        return version is null
            ? Result<SiteError, string>.Fail(SiteError.VersionNotFound)
            : Result<SiteError, string>.Ok(ThemeCss.For(version.Document.Theme));
    }

    /// <summary>
    /// The HTML of one page. <paramref name="versionNanoid"/> null means the draft, which is what the editor shows;
    /// naming a version is how the history previews the past.
    /// </summary>
    public async Task<Result<SiteError, string>> ExecuteAsync(
        int userId,
        string siteNanoid,
        string? versionNanoid = null,
        string path = "/",
        string? stylesheetUrl = null,
        CancellationToken cancellationToken = default)
    {
        var site = await siteRepository.FindForOwnerAsync(siteNanoid, userId, cancellationToken);

        if (site is null) return Result<SiteError, string>.Fail(SiteError.NotFound);

        var version = versionNanoid is null
            ? site.DraftVersion
            : await versionRepository.FindForSiteAsync(versionNanoid, site.Id, cancellationToken);

        if (version is null) return Result<SiteError, string>.Fail(SiteError.VersionNotFound);

        var page = version.Document.FindPageByPath(path) ?? version.Document.Pages.FirstOrDefault();

        if (page is null) return Result<SiteError, string>.Fail(SiteError.InvalidDocument, "This version has no pages.");

        var primary = await domainRepository.FindPrimaryAsync(site.Id, cancellationToken);
        var rendered = renderer.Render(
            version.Document,
            new RenderContext(site.Name, mapper.UrlFor(site, primary)) { StylesheetUrl = stylesheetUrl });

        // The preview renders the whole document and then picks one page's file, rather than rendering that page alone:
        // it is the same call the deployment makes, so the pane cannot be looking at output produced a different way.
        var file = rendered.Files.FirstOrDefault(x => x.Path == PathFor(page.Path));

        return file is null
            ? Result<SiteError, string>.Fail(SiteError.InvalidDocument, "That page could not be rendered.")
            : Result<SiteError, string>.Ok(System.Text.Encoding.UTF8.GetString(file.Content));
    }

    private static string PathFor(string pagePath) =>
        pagePath == "/" ? "index.html" : $"{pagePath.Trim('/')}/index.html";
}
