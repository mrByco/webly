using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Deployments;

/// <summary>What the provider gave back, in Webly's own words.</summary>
public record DeploymentHandle(string ProviderDeploymentId, string ProviderUrl);

/// <summary>
/// A failure the site's owner can read. The provider's own message — or the build log — is kept in
/// <see cref="ProviderDetail"/> for the record; <see cref="Message"/> is what reaches the screen and the email,
/// and translating once, here, is what stops a raw API body or 400 lines of webpack output from doing that.
/// </summary>
public class DeploymentFailedException(string message, string? providerDetail = null) : Exception(message)
{
    public string? ProviderDetail { get; } = providerDetail;
}

/// <summary>
/// Somewhere a site can be built and put on the internet, and the hostnames that point at it.
///
/// Two halves, deliberately different in where they run. Project and domain management are REST calls from
/// this process, because they are Webly's account and Webly's token. <see cref="BuildAndDeployAsync"/> runs
/// inside a sandbox, because building a Next.js project needs a filesystem, node and the provider's CLI — and
/// because that is the same place the editing happened, which is what makes "what I previewed is what I
/// published" true rather than hopeful.
///
/// Two implementations: Vercel, and <see cref="FileSystemDeploymentTarget"/> for development, which runs the
/// same real build and then writes the output to a directory. The interface exists for both reasons — the thing
/// behind it is the part a business decision can change (pricing, a region requirement, an outage), and the
/// publish path is too consequential to be testable only by people who have a hosting account.
/// </summary>
/// <summary>
/// The facts a site's build cannot work out for itself, both of them absolute URLs.
///
/// A record rather than two parameters because there will be a third: a static export decides at build time
/// everything a server would answer at request time, so anything about "where this site lives" has to arrive
/// here. Both are read by <c>src/site.ts</c> in the template.
/// </summary>
/// <param name="SiteUrl">
/// The site's own <b>address</b>, as <c>NEXT_PUBLIC_SITE_URL</c>. Every absolute URL a published page contains —
/// its canonical link, its sitemap, what a social network reads when somebody shares it — comes from it. The
/// address rather than the deployment's own URL, because a canonical that changed with every publish is not a
/// canonical.
/// </param>
/// <param name="FormEndpoint">
/// Where the site's forms post, as <c>NEXT_PUBLIC_FORM_ENDPOINT</c>. It is Webly's, not the site's: a static
/// export has nothing of its own that can receive a POST, which is what decided the question P4 left open.
/// </param>
public record SiteBuildSettings(string SiteUrl, string FormEndpoint);

public interface IDeploymentTarget
{
    /// <summary>
    /// Whether this deployment can actually publish. Asked of the target rather than read off a configuration
    /// key, because the answer differs per target and only the target knows it: Vercel needs a token, the
    /// development one needs nothing. A caller that checked <c>Deployment:Vercel:Token</c> itself would report
    /// publishing unavailable in a deployment where it works fine.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Makes sure a provider-side project exists for this site and returns its id. Idempotent: called on every
    /// publish, cheap when the project is already there, and the reason <c>Site.ProviderProjectId</c> is stored
    /// rather than derived.
    /// </summary>
    Task<string> EnsureProjectAsync(string siteNanoid, string siteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the site in the sandbox and uploads the result. The build happens first and its failure is the
    /// gate: a site that does not compile is never published, and the log comes back in the exception so the
    /// person — and the next agent turn — can see why.
    ///
    /// <paramref name="onOutput"/> is the build log as it happens, for the deployment's event stream.
    ///
    /// <paramref name="settings"/> is what the build has to be told about the world, because a static export
    /// has no server to ask afterwards.
    /// </summary>
    Task<DeploymentHandle> BuildAndDeployAsync(
        ISandbox sandbox,
        string projectId,
        SiteBuildSettings settings,
        Func<string, Task>? onOutput = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a hostname to the project and returns what has to go in DNS. The provider owns verification and
    /// the certificate; Webly only mirrors what it says — see <c>Domain</c>.
    /// </summary>
    Task<DomainAttachment> AttachDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider whether a hostname is verified now. Called when the person clicks "check again", never
    /// on a timer: a poll per pending domain per minute is a rate limit waiting to happen, and the person
    /// watching the screen is a better trigger than a clock.
    /// </summary>
    Task<DomainAttachment> CheckDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default);

    Task RemoveDomainAsync(string projectId, string hostname, CancellationToken cancellationToken = default);
}

/// <summary>
/// A hostname's state at the provider, plus the DNS record it wants. Mirrored onto a <c>Domain</c> row verbatim,
/// because the person reading it has their registrar's panel open in the next tab and anything we paraphrase is
/// something they will type wrong.
/// </summary>
public record DomainAttachment(
    string? ProviderDomainId,
    bool Verified,
    string? RecordType,
    string? RecordName,
    string? RecordValue,
    string? Error);
