namespace Webly.Services.DTO.Deployments;

public enum DeployError
{
    SiteNotFound,

    /// <summary>The draft is already the published version. Not an error the client shows as a failure — the
    /// publish button is simply disabled, and this is the race where it was not.</summary>
    NothingToPublish,

    /// <summary>
    /// This deployment of Webly has no hosting credentials, so nothing can be published from it. A configuration state,
    /// not a failure of the request — the same "absent rather than broken" rule as the editor agent.
    /// </summary>
    PublishingUnavailable,

    /// <summary>
    /// The site does not build. Discovered by the runner rather than here — the build is the gate, and it takes
    /// minutes — so a publish reaches this only through a failed <c>Deployment</c> row, never as the answer to
    /// the request.
    /// </summary>
    BuildFailed
}
