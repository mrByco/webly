using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Webly.Services.Services.Sandboxes;

namespace Webly.Services.Services.Deployments;

/// <summary>
/// Publishing to a directory on this machine, for development.
///
/// The point is the gate, not the hosting. Everything that makes a publish either work or fail safely is
/// Webly's own code — a fresh sandbox, <c>npm ci</c> against the committed lockfile, a real
/// <c>next build</c>, the published pointer moving only on success, the build log reaching
/// <c>Deployment.ErrorDetail</c> — and none of it needs a hosting account to exercise. Without this class the
/// only way to run any of it is with a Vercel token, which means the most consequential path in the product
/// is also the one hardest to try.
///
/// <b>The build is real.</b> It runs the same <c>npm run build</c> in the same sandbox, so a site that does
/// not compile fails here exactly as it would in production, with the same log. What is fake is only the
/// upload: the static export is copied out of the sandbox and served from <c>/published/{nanoid}/</c> by the dev
/// host, so a published page is a page somebody can actually open.
///
/// <b>Domains are simulated, and say so.</b> <see cref="AttachDomainAsync"/> answers pending with a record
/// that names itself a simulation, and <see cref="CheckDomainAsync"/> then answers verified — which exercises
/// the two-step "add it, go to your registrar, come back and check" flow the UI is built around. It is not a
/// claim that anything resolves. Nothing here touches DNS, and a local deployment is reachable from this
/// machine only.
///
/// Selected by <c>Deployment:Provider=filesystem</c>, and refused outside Development.
/// </summary>
public class FileSystemDeploymentTarget(
    IOptions<DeploymentOptions> options,
    ILogger<FileSystemDeploymentTarget> logger) : IDeploymentTarget
{
    public const string ProviderName = "filesystem";

    private readonly DeploymentOptions.FileSystemOptions _local = options.Value.FileSystem;

    /// <summary>Always. A directory is the one dependency every machine has.</summary>
    public bool IsConfigured => true;

    /// <summary>
    /// A project id, which for this target is just the site. Returned rather than left null because
    /// <c>Site.ProviderProjectId</c> is what the runner stores and the domain calls take, and a target that
    /// answered null would make the caller special-case it.
    /// </summary>
    public Task<string> EnsureProjectAsync(
        string siteNanoid,
        string siteName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult($"local-{siteNanoid}");

    public async Task<DeploymentHandle> BuildAndDeployAsync(
        ISandbox sandbox,
        string projectId,
        Func<string, Task>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var build = await sandbox.RunAsync(
            new SandboxCommand("npm", ["run", "build"], TimeSpan.FromMinutes(10),
                new Dictionary<string, string> { ["NEXT_TELEMETRY_DISABLED"] = "1" }),
            output => onOutput?.Invoke(output.Text) ?? Task.CompletedTask,
            cancellationToken);

        if (!build.Succeeded)
            // The same exception, with the same tail, as the real target throws. The whole reason this class
            // exists is that this path can then be exercised without an account.
            // The whole output, not a tail of it: the runner makes it readable and *then* trims, and cutting first
            // sliced the middle out of SWC's stack backtrace — which left the frames in and the marker that
            // identifies them out, so the filter could not see what it was looking at.
            throw new DeploymentFailedException(
                "Your site did not build, so nothing was published. The error is below — ask the assistant to fix it.",
                build.Output);

        // The site's nanoid, which is what the project id is made of — not its slug, although the directory
        // reads like one. A slug can be reused by a later site once an old one is deleted; a nanoid never is,
        // and a published directory outliving the site that wrote it must not become a different site's.
        var directory = projectId.StartsWith("local-", StringComparison.Ordinal)
            ? projectId["local-".Length..]
            : projectId;

        var target = Path.Combine(Path.GetFullPath(_local.Root), directory);

        await CopyOutputAsync(sandbox, target, cancellationToken);

        logger.LogInformation("Published {Project} to {Target}.", projectId, target);

        return new DeploymentHandle(
            ProviderDeploymentId: $"{directory}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            ProviderUrl: $"{_local.BaseUrl.TrimEnd('/')}/published/{directory}/");
    }

    /// <summary>
    /// Copies the static export out of the sandbox.
    ///
    /// Through a base64'd tar over <c>/exec</c> rather than the tree-reading contract, for one reason: the
    /// sandbox agent's ignore list excludes build output on the way out, and it should — a built site must
    /// never be able to travel back into somebody's git history. So the build's own directory is fetched
    /// explicitly, which is also a reminder that these bytes are an artefact and not a version.
    ///
    /// Base64 because <c>/exec</c> streams JSON strings and raw archive bytes would not survive that. It costs
    /// a third more bytes over a loopback connection, in a development-only path.
    ///
    /// Plain <c>base64</c> with no <c>-w</c>: that flag is GNU coreutils', and the local provider's "sandbox" is
    /// whatever machine the developer is on. The wrapping it produces is stripped here instead.
    /// </summary>
    private async Task CopyOutputAsync(ISandbox sandbox, string target, CancellationToken cancellationToken)
    {
        var packed = await sandbox.RunAsync(
            new SandboxCommand("sh", ["-c", "test -d out && tar -c -z -C out . | base64"],
                TimeSpan.FromMinutes(2)),
            cancellationToken: cancellationToken);

        if (!packed.Succeeded || packed.Output.Trim().Length == 0)
            throw new DeploymentFailedException(
                "The build produced nothing to publish.",
                $"No out/ directory after a successful build. Is next.config.ts still output: 'export'? {Tail(packed.Output)}");

        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        var archive = Path.Combine(Path.GetTempPath(), $"webly-publish-{Guid.NewGuid():N}.tgz");

        try
        {
            var encoded = string.Concat(packed.Output.Where(character => !char.IsWhiteSpace(character)));

            await File.WriteAllBytesAsync(archive, Convert.FromBase64String(encoded), cancellationToken);
            await ExtractAsync(archive, target, cancellationToken);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    private static async Task ExtractAsync(string archive, string target, CancellationToken cancellationToken)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("tar")
        {
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in new[] { "-x", "-z", "-f", archive, "-C", target }) startInfo.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new DeploymentFailedException("Publishing could not unpack the build.");

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            throw new DeploymentFailedException("Publishing could not unpack the build.", error);
    }

    /// <summary>Pending, with a record that says what it is. See the class comment on simulated domains.</summary>
    public Task<DomainAttachment> AttachDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DomainAttachment(
            ProviderDomainId: $"local-{hostname}",
            Verified: false,
            RecordType: "TXT",
            RecordName: $"_webly-local.{hostname}",
            RecordValue: "simulated-by-the-filesystem-deployment-target",
            Error: null));

    /// <summary>Verified, because the simulation's second step is what the UI needs to be able to reach.</summary>
    public Task<DomainAttachment> CheckDomainAsync(
        string projectId,
        string hostname,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DomainAttachment(
            ProviderDomainId: $"local-{hostname}",
            Verified: true,
            RecordType: "TXT",
            RecordName: $"_webly-local.{hostname}",
            RecordValue: "simulated-by-the-filesystem-deployment-target",
            Error: null));

    public Task RemoveDomainAsync(string projectId, string hostname, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    private static string Tail(string output) => output.Length <= 2000 ? output : output[^2000..];
}
