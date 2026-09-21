namespace Webly.Services.DTO.Sites;

/// <summary>What waking a site's preview answers: it has started, or it was already running.</summary>
public record WakeWorkspaceResponse
{
    /// <summary>
    /// True when a workspace was already warm, so the client can show the frame immediately instead of a spinner
    /// that has nothing to wait for.
    /// </summary>
    public required bool AlreadyRunning { get; init; }
}
