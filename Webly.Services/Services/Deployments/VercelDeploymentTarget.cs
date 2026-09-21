using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Deployments;

/// <summary>
/// Vercel: its REST API for projects and domains, its CLI in the sandbox for builds and uploads.
///
/// The split is the design. Projects and domains belong to Webly's account, so they are REST calls from this
/// process with Webly's token. A build belongs to the site, so it happens in the sandbox the site was edited in
/// — see <see cref="BuildAndDeployAsync"/>.
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

    public bool IsConfigured => _vercel.IsConfigured;

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
            // Named, although we build it ourselves: the project *is* a Next.js app, and a provider whose
            // settings say otherwise is what makes a later support conversation — or a "deploy from git"
            // migration — confusing for no reason.
            ["framework"] = "nextjs"
        }, cancellationToken);

        return created?["id"]?.GetValue<string>()
            ?? throw new DeploymentFailedException(
                "Webly could not create the hosting project for this site. Please try again in a minute.",
                created?.ToJsonString());
    }

    /// <summary>
    /// Builds in the sandbox and uploads the output, both through the provider's CLI.
    ///
    /// <c>vercel build</c> then <c>vercel deploy --prebuilt</c>, rather than pushing source for the provider to
    /// build. Three reasons, in order of how much they matter: the build failure is <b>ours</b>, so a site that
    /// does not compile is never published and the log is something we can show; it builds in the same sandbox
    /// the editing happened in, with the same dependencies, so a deploy cannot fail for an environment reason
    /// the preview did not have; and it needs no git remote, which is the whole reason Webly can own the
    /// repositories.
    ///
    /// The project link is written to <c>.vercel/project.json</c> rather than passed as flags: the CLI wants it
    /// there, it is not secret (two ids), and it keeps the token out of an argument list that ends up in a
    /// process table.
    /// </summary>
    public async Task<DeploymentHandle> BuildAndDeployAsync(
        ISandbox sandbox,
        string projectId,
        SiteBuildSettings settings,
        Func<string, Task>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var environment = new Dictionary<string, string>
        {
            ["VERCEL_TOKEN"] = _vercel.Token,

            // Read by the site's own `next build` for its canonical link and its sitemap. Passed in the
            // environment rather than configured at the provider, because it is this build's input and a
            // project setting is one more thing that can disagree with the site row.
            ["NEXT_PUBLIC_SITE_URL"] = settings.SiteUrl,

            // Where the published site's forms post: this app, because a static export cannot receive one.
            ["NEXT_PUBLIC_FORM_ENDPOINT"] = settings.FormEndpoint
        };

        if (_vercel.TeamId.Length > 0) environment["VERCEL_ORG_ID"] = _vercel.TeamId;

        environment["VERCEL_PROJECT_ID"] = projectId;

        var link = await sandbox.RunAsync(
            new SandboxCommand("sh", ["-c", "mkdir -p .vercel && printf '%s' \"$LINK\" > .vercel/project.json"],
                TimeSpan.FromSeconds(30),
                new Dictionary<string, string>
                {
                    ["LINK"] = $$"""{"projectId":"{{projectId}}","orgId":"{{_vercel.TeamId}}"}"""
                }),
            cancellationToken: cancellationToken);

        if (!link.Succeeded)
            throw new DeploymentFailedException("Publishing could not be prepared. Please try again.", link.Output);

        // `vercel`, not `npx vercel`: the sandbox image installs the CLI at a pinned version
        // (deploy/sandbox/Dockerfile), and npx would either find that same binary on PATH or quietly fetch
        // whatever is newest — a publish that works on Tuesday and not on Wednesday, with no diff to blame.
        var build = await sandbox.RunAsync(
            new SandboxCommand("vercel", ["build", "--prod", "--yes", "--token", _vercel.Token],
                TimeSpan.FromMinutes(10), environment),
            output => onOutput?.Invoke(output.Text) ?? Task.CompletedTask,
            cancellationToken);

        if (!build.Succeeded)
            // The tail, not the whole log: the last lines are where the error is, and the whole thing is a
            // megabyte of webpack. It reaches the deployment row, which is what the person is shown.
            throw new DeploymentFailedException(
                "Your site did not build, so nothing was published. The error is below — ask the assistant to fix it.",
                Tail(build.Output));

        var deploy = await sandbox.RunAsync(
            new SandboxCommand("vercel",
                ["deploy", "--prebuilt", "--prod", "--yes", "--token", _vercel.Token],
                TimeSpan.FromMinutes(10), environment),
            output => onOutput?.Invoke(output.Text) ?? Task.CompletedTask,
            cancellationToken);

        if (!deploy.Succeeded)
            throw new DeploymentFailedException(
                "Publishing failed on our hosting provider. Your live site is unchanged — please try again.",
                Tail(deploy.Output));

        // The CLI prints the deployment URL on its own line; the last URL it printed is the one it made.
        var url = deploy.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .LastOrDefault(line => line.StartsWith("https://", StringComparison.Ordinal))
            ?? throw new DeploymentFailedException(
                "The site was uploaded but the provider did not say where. Publishing again is safe.",
                Tail(deploy.Output));

        // There is no deployment id in the CLI's output, and the URL identifies it well enough for everything
        // Webly does with it: it is what "preview this old version" opens.
        return new DeploymentHandle(url, url);
    }

    private static string Tail(string output) => output.Length <= 4000 ? output : output[^4000..];

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
