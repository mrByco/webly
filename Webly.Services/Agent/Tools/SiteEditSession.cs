using Microsoft.Extensions.Logging;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.Services.Sites;
using Webly.Services.UseCases.Sites;

namespace Webly.Services.Agent.Tools;

/// <summary>
/// The site a turn is editing, and the draft it is editing it on. Scoped, so one turn has exactly one.
///
/// <b>A turn stages its edits and commits once.</b> Every tool call mutates an in-memory
/// <see cref="SiteDraft"/>; the version is written when the turn ends, by <see cref="CommitAsync"/>. Three things
/// follow from that, and they are the reason it works this way:
///
/// <list type="bullet">
/// <item>A turn is atomic. A model that adds a section, edits it, then fails leaves the site exactly as it was —
/// rather than a history with three entries, one of them half an edit.</item>
/// <item>The history stays readable. One turn is one version with one summary, so scrolling back through a site's
/// past reads as the conversation that produced it instead of as a transaction log.</item>
/// <item>A cancelled run costs nothing. Stop is honest: the draft is discarded, and nothing was written to
/// discard.</item>
/// </list>
/// </summary>
public class SiteEditSession(
    ISiteRepository siteRepository,
    CommitSiteVersion commitSiteVersion,
    AgentRunContext context,
    ILogger<SiteEditSession> logger)
{
    private Site? _site;
    private SiteDraft? _draft;

    /// <summary>
    /// The site, loaded once per turn through the ownership check. Throws if the caller does not own it, which
    /// cannot happen — the hub authorized the site before the run started — and would be a bug rather than a
    /// request to answer.
    /// </summary>
    public async Task<Site> GetSiteAsync(CancellationToken cancellationToken = default) =>
        _site ??= await siteRepository.FindForOwnerAsync(context.SiteNanoid, context.UserId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Site '{context.SiteNanoid}' is not available to user {context.UserId} inside its own run.");

    public async Task<SiteDraft> GetDraftAsync(CancellationToken cancellationToken = default)
    {
        if (_draft is not null) return _draft;

        var site = await GetSiteAsync(cancellationToken);

        var document = site.DraftVersion?.Document
            ?? throw new InvalidOperationException($"Site '{site.Nanoid}' has no draft version.");

        return _draft = SiteDraft.From(document);
    }

    /// <summary>Whether this turn changed anything — what the launcher asks before committing.</summary>
    public bool HasChanges => _draft?.IsDirty ?? false;

    /// <summary>
    /// Writes the turn's version, or nothing if the turn made no changes. Called once, by the turn service, after
    /// the model has stopped talking.
    /// </summary>
    public async Task<SiteVersion?> CommitAsync(int? sourceMessageId, CancellationToken cancellationToken = default)
    {
        if (_draft is null || !_draft.IsDirty) return null;

        var site = await GetSiteAsync(cancellationToken);

        var version = await commitSiteVersion.ExecuteAsync(
            site, _draft, context.UserId, SiteVersionOrigin.Agent, sourceMessageId,
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Run {RunId} committed version {Version} to site {Site}: {Summary}",
            context.RunId, version?.Nanoid, site.Nanoid, version?.Summary);

        return version;
    }
}
