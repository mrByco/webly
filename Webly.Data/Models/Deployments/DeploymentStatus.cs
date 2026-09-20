namespace Webly.Data.Models.Deployments;

/// <summary>
/// The life of a deploy. Four of these are the provider's own states, mirrored; <see cref="Queued"/>
/// is ours, and it exists because the row is written before the job runner picks it up — a publish
/// that is waiting for a worker has to be distinguishable from one that is waiting for Vercel, or the
/// screen cannot say anything true while it waits.
/// </summary>
public enum DeploymentStatus
{
    /// <summary>Written, not yet leased by the job runner.</summary>
    Queued,

    /// <summary>The document is being rendered to files, before anything is uploaded.</summary>
    Rendering,

    /// <summary>Files handed to the provider; the provider is finishing the deployment.</summary>
    Uploading,

    /// <summary>Live. This is the only status that moves <c>Site.PublishedVersionId</c>.</summary>
    Ready,

    /// <summary>Gave up. <c>Deployment.Error</c> says why; the previous live version is untouched.</summary>
    Failed,

    /// <summary>
    /// Cancelled by the person, or superseded by a newer publish of the same site. Superseding is not
    /// its own status: from the owner's point of view the older deploy stopped because they asked for a
    /// newer one, and one word covers both.
    /// </summary>
    Cancelled
}
