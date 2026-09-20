using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Services.Services.Rendering;

namespace Webly.Services.Services.Deployments;

/// <summary>
/// Vercel, over its REST API.
///
/// Files are uploaded <b>inline with the deployment</b> (base64 in the create call) rather than through the
/// separate upload-and-reference flow. A rendered Webly site is a handful of HTML files and a stylesheet —
/// tens of kilobytes — and the two-step flow exists for large build outputs. One call is one thing that can
/// fail, and it makes a retry genuinely idempotent.
///
/// No framework and no build command: <c>projectSettings.framework = null</c> with an empty build step, so
/// Vercel serves exactly the bytes we sent. That is the whole reason the renderer is ours — see
/// <see cref="ISiteRenderer"/>. If a build ever runs on the provider's side, a deployment can fail for a
/// reason the site's owner cannot see, and there is no code for them to fix it in.
///
/// <b>Unverified against the live API.</b> The endpoints and payload shapes below follow Vercel's documented
/// v13/v10/v9 REST API, but nothing here has been run against the real service yet — see whats_next.md. The
/// first thing to do with a real token is publish a starter site and reconcile this class with what comes
/// back; treat the shapes as the plan rather than as tested code.
/// </summary>
public class VercelDeploymentTarget(
    HttpClient httpClient,
    IOptions<DeploymentOptions> options,
    ILogger<VercelDeploymentTarget> logger) : IDeploymentTarget
{
    private readonly DeploymentOptions.VercelOptions _vercel = options.Value.Vercel;

    public async Task<string> EnsureProjectAsync(
        string siteNanoid,
        string siteName,
        CancellationToken cancellationToken = default)
    {
        var name = ProjectName(siteNanoid);

        var existing = await SendAsync(HttpMethod.Get, $"/v9/projects/{name}", null, cancellationToken);
        if (existing is not null && existing["id"]?.GetValue<string>() is { } id)
            return id;

        var created = await SendAsync(HttpMethod.Post, "/v11/projects", new JsonObject
        {
            ["name"] = name,
            // Explicitly null: with a framework set, Vercel infers a build, and there is nothing to build.
            ["framework"] = null
        }, cancellationToken);

        return created?["id"]?.GetValue<string>()
            ?? throw new DeploymentFailedException(
                "Webly could not create the hosting project for this site. Please try again in a minute.",
                created?.ToJsonString());
    }

    public async Task<DeploymentHandle> DeployAsync(
        string projectId,
        RenderedSite site,
        CancellationToken cancellationToken = default)
    {
        var files = new JsonArray();

        foreach (var file in site.Files)
        {
            files.Add(new JsonObject
            {
                ["file"] = file.Path,
                ["data"] = Convert.ToBase64String(file.Content),
                ["encoding"] = "base64"
            });
        }

        var payload = new JsonObject
        {
            ["name"] = projectId,
            ["project"] = projectId,
            ["target"] = "production",
            ["files"] = files,
            ["projectSettings"] = new JsonObject
            {
                ["framework"] = null,
                ["buildCommand"] = null,
                ["installCommand"] = null,
                // The rendered files are already the output, so the output directory is the root.
                ["outputDirectory"] = null
            }
        };

        var response = await SendAsync(HttpMethod.Post, "/v13/deployments", payload, cancellationToken)
            ?? throw new DeploymentFailedException("Publishing failed before it started. Please try again.");

        var id = response["id"]?.GetValue<string>();
        var url = response["url"]?.GetValue<string>();

        if (id is null || url is null)
            throw new DeploymentFailedException(
                "The hosting provider accepted the site but did not say where it is. Publishing again is safe.",
                response.ToJsonString());

        // The API returns a bare hostname; everything downstream — the email, the history, the "open your
        // site" link — wants something clickable.
        return new DeploymentHandle(id, url.StartsWith("http") ? url : $"https://{url}");
    }

    public Task<DomainAttachment> AttachDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default) =>
        DomainCallAsync(HttpMethod.Post, $"/v10/projects/{projectId}/domains", new JsonObject
        {
            ["name"] = hostname
        }, hostname, cancellationToken);

    public Task<DomainAttachment> CheckDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default) =>
        DomainCallAsync(HttpMethod.Get, $"/v9/projects/{projectId}/domains/{hostname}", null, hostname, cancellationToken);

    public async Task RemoveDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default) =>
        await SendAsync(HttpMethod.Delete, $"/v9/projects/{projectId}/domains/{hostname}", null, cancellationToken);

    /// <summary>
    /// Attach and check answer with the same shape, so they translate the same way. A domain that the
    /// provider says is not verified comes back as an <see cref="DomainAttachment"/> with its records rather
    /// than as an exception: "your DNS is not pointing here yet" is the normal state of a domain somebody
    /// added thirty seconds ago, not a failure.
    /// </summary>
    private async Task<DomainAttachment> DomainCallAsync(
        HttpMethod method,
        string path,
        JsonObject? payload,
        string hostname,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(method, path, payload, cancellationToken)
            ?? throw new DeploymentFailedException($"Webly could not reach the hosting provider about {hostname}.");

        var verified = response["verified"]?.GetValue<bool>() ?? false;
        var challenge = response["verification"]?.AsArray().FirstOrDefault();

        return new DomainAttachment(
            ProviderDomainId: response["name"]?.GetValue<string>(),
            Verified: verified,
            // An apex domain gets an A record and a subdomain a CNAME. The provider states which, and
            // repeating that decision here is how a screen ends up telling somebody to add the wrong one.
            RecordType: challenge?["type"]?.GetValue<string>() ?? (IsApex(hostname) ? "A" : "CNAME"),
            RecordName: challenge?["domain"]?.GetValue<string>() ?? (IsApex(hostname) ? "@" : hostname.Split('.')[0]),
            RecordValue: challenge?["value"]?.GetValue<string>() ?? (IsApex(hostname) ? "76.76.21.21" : "cname.vercel-dns.com"),
            Error: response["error"]?["message"]?.GetValue<string>());
    }

    private async Task<JsonObject?> SendAsync(
        HttpMethod method,
        string path,
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        // Every call carries the team id when there is one. A missing teamId against a team token is a 403
        // that reads like a bad token, which is a confusing hour the first time.
        var url = _vercel.TeamId.Length > 0
            ? $"{path}{(path.Contains('?') ? '&' : '?')}teamId={_vercel.TeamId}"
            : path;

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _vercel.Token);

        if (payload is not null)
            request.Content = JsonContent.Create(payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Vercel {Method} {Path} answered {Status}: {Body}", method, path, (int)response.StatusCode, body);

            throw new DeploymentFailedException(Translate(response.StatusCode, body), body);
        }

        return body.Length == 0 ? null : JsonNode.Parse(body)?.AsObject();
    }

    /// <summary>
    /// The one place a provider error becomes a sentence for the site's owner. Three of these are worth
    /// naming because the owner can act on them; everything else is ours to fix, and saying so is more
    /// honest than a status code they cannot use.
    /// </summary>
    private static string Translate(System.Net.HttpStatusCode status, string body)
    {
        var code = TryReadCode(body);

        return code switch
        {
            "domain_already_in_use" => "That domain is already connected to another site. Remove it there first, then add it here.",
            "invalid_domain" => "That does not look like a domain name. Enter it without https:// and without a path, like example.com.",
            "forbidden" or "rate_limited" when status is System.Net.HttpStatusCode.TooManyRequests =>
                "Publishing is busy right now. Wait a minute and try again.",
            _ => "Publishing failed on our hosting provider. Your live site is unchanged — please try again, and tell us if it keeps happening."
        };
    }

    private static string? TryReadCode(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["code"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a hostname is the zone itself rather than a subdomain of it — which decides A record versus
    /// CNAME. Counting labels is wrong for a public suffix like <c>co.uk</c>, and deliberately so: the
    /// provider's own answer is used when it gives one, and this is only the fallback wording.
    /// </summary>
    private static bool IsApex(string hostname) => hostname.Count(x => x == '.') == 1;

    private string ProjectName(string siteNanoid) => $"{_vercel.ProjectPrefix}-{siteNanoid.ToLowerInvariant()}";
}
