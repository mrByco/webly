using Webly.Services.Services.Rendering;

namespace Webly.Services.Services.Deployments;

/// <summary>What the provider gave back, in Webly's own words.</summary>
public record DeploymentHandle(string ProviderDeploymentId, string ProviderUrl);

/// <summary>
/// A failure the site's owner can read. The provider's own message is kept in
/// <see cref="ProviderDetail"/> for the log and for an administrator; <see cref="Message"/> is what reaches
/// the screen and the email, and translating once — here — is what stops a raw API body from doing that.
/// </summary>
public class DeploymentFailedException(string message, string? providerDetail = null)
    : Exception(message)
{
    public string? ProviderDetail { get; } = providerDetail;
}

/// <summary>
/// Somewhere a rendered site can be put. One implementation today (Vercel), and the interface exists
/// because the thing behind it is the one part of Webly a business decision can change: a provider's
/// pricing, a region requirement or an outage are all reasons to have a second, and none of them should
/// reach <c>PublishSite</c>.
///
/// Note what is <i>not</i> here: anything about building. A target receives finished bytes — see
/// <see cref="ISiteRenderer"/> for why Webly renders rather than pushing a repository.
/// </summary>
public interface IDeploymentTarget
{
    /// <summary>
    /// Makes sure a provider-side project exists for this site and returns its id. Idempotent: called on
    /// every publish, cheap when the project is already there, and the reason <c>Site.ProviderProjectId</c>
    /// is stored rather than derived.
    /// </summary>
    Task<string> EnsureProjectAsync(string siteNanoid, string siteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the files and returns once the provider has accepted them. Throws
    /// <see cref="DeploymentFailedException"/> for anything the owner should be told about.
    /// </summary>
    Task<DeploymentHandle> DeployAsync(
        string projectId,
        RenderedSite site,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a hostname to the project and returns what has to be put in DNS. The provider owns
    /// verification and the certificate; Webly only mirrors what it says — see <c>Domain</c>.
    /// </summary>
    Task<DomainAttachment> AttachDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the provider whether a hostname is verified now. Called when the person clicks
    /// "check again", never on a timer: a poll per pending domain per minute is a rate limit waiting to
    /// happen, and the person watching the screen is a better trigger than a clock.</summary>
    Task<DomainAttachment> CheckDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default);

    Task RemoveDomainAsync(string projectId, string hostname, CancellationToken cancellationToken = default);
}

/// <summary>
/// A hostname's state at the provider, plus the DNS record it wants. Mirrored onto a <c>Domain</c> row
/// verbatim, because the person reading it has their registrar's panel open in the next tab and anything we
/// paraphrase is something they will type wrong.
/// </summary>
public record DomainAttachment(
    string? ProviderDomainId,
    bool Verified,
    string? RecordType,
    string? RecordName,
    string? RecordValue,
    string? Error);
