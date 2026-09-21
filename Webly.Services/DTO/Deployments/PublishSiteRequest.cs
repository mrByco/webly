namespace Webly.Services.DTO.Deployments;

/// <summary>
/// What a publish is asked for. One flag, and it exists because "publish my changes" and "publish this again"
/// are different intentions that would otherwise be the same request.
/// </summary>
public record PublishSiteRequest
{
    /// <summary>
    /// Publish the current version even though it is already the live one.
    ///
    /// Ordinarily that is refused, and rightly: a publish costs a sandbox and a build, and a button that
    /// silently rebuilds the same commit invites people to press it when something looks wrong. But there is a
    /// case the refusal leaves nowhere to go — a deployment whose output the provider lost, or one this app
    /// recorded as Ready and wrote somewhere it could not serve from, which is a thing that has happened here.
    /// Then the only honest answer is to build it again, and this is how somebody says so.
    /// </summary>
    public bool Republish { get; init; }
}
