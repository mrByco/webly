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
    PublishingUnavailable

    // There is no "does not build": the build is the gate and it takes minutes, so it is found by the runner and
    // reported through a failed `Deployment` row and its run's `Failed` event, never as the answer to this request.
    // A `BuildFailed` value sat here for that with a sentence mapped to it, and nothing could ever produce it.
}
