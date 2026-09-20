using Webly.Data.Models.Authentication;
using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;
using System.ComponentModel.DataAnnotations;

namespace Webly.Data.Models.Deployments;

/// <summary>
/// One attempt to put one version of a site on the internet. Durable, unlike a chat run: a deploy
/// outlives the request that asked for it, the tab that asked for it and — because it is a row rather
/// than an in-memory handle — a restart of the API. That is the difference that makes it a job
/// (<c>DeploymentJobRunner</c>) rather than a run held in <c>RunRegistry</c>, and the reason its
/// progress log is this row's status rather than an in-memory event list.
///
/// Attempts are kept, including the failed ones. "It failed twice with the same error at 11pm" is the
/// only evidence anybody has when a provider misbehaves, and deleting failures is how a product ends
/// up unable to answer that.
/// </summary>
public class Deployment : IHasNanoid, IHasTimestamps
{
    [Key]
    public int Id { get; set; }

    public string Nanoid { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;

    /// <summary>
    /// Exactly which version was deployed. Not "the draft at the time": a deploy that referred to a
    /// moving pointer could not be replayed, and the question "what is actually live" would have no
    /// answer once the draft moved on. <c>NoAction</c> on delete — a version that has been deployed
    /// cannot be removed from history.
    /// </summary>
    public int SiteVersionId { get; set; }
    public SiteVersion SiteVersion { get; set; } = null!;

    public DeploymentStatus Status { get; set; } = DeploymentStatus.Queued;

    /// <summary>Who pressed publish. A person, always — the agent may not deploy. See CLAUDE.md.</summary>
    public int TriggeredByUserId { get; set; }
    public User TriggeredBy { get; set; } = null!;

    /// <summary>The provider's id for this deployment, used to poll it and to read its logs.</summary>
    public string? ProviderDeploymentId { get; set; }

    /// <summary>
    /// The provider's own immutable URL for this deployment, which resolves even after a later deploy
    /// replaces it. This is what makes "preview the version I published on Tuesday" possible without
    /// re-rendering anything.
    /// </summary>
    public string? ProviderUrl { get; set; }

    /// <summary>
    /// Why it failed, in language meant for the site's owner rather than for us. Translated once, in
    /// <c>VercelDeploymentTarget</c> and the runner; a raw API body or a webpack trace reaching this column
    /// reaches the screen.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// The build log's tail, when there is one. Kept because it is the one thing that makes a failed publish
    /// actionable: the person pastes it back into the chat and the agent fixes what it says. Shown behind a
    /// disclosure rather than in the message, since it is four hundred lines of somebody else's output.
    /// </summary>
    public string? ErrorDetail { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
