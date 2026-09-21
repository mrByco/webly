using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webly.Services.Services.Deployments;
using Webly.Services.Services.Repositories;
using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// What the publish tells the build about the world.
///
/// A static export decides at build time everything a server would answer at request time, so the three
/// environment variables below are the whole of what the published site knows about where it lives. Each one
/// has already been wrong once in a way nobody could see by reading the HTML:
///
/// <list type="bullet">
/// <item><c>WEBLY_PREVIEW_BASE</c> was not passed at all, so every locally published site asked for
/// <c>/_next/…</c> at the root of Webly's own origin and rendered as unstyled text. It was found by opening
/// one in a browser, months after the first publish.</item>
/// <item><c>NEXT_PUBLIC_SITE_URL</c> is the canonical, and it must stay the site's address rather than the
/// path this copy happens to be served from — a canonical that moved with the deployment would tell search
/// engines the site moves every time somebody fixes a headline.</item>
/// <item><c>NEXT_PUBLIC_FORM_ENDPOINT</c> is where the contact form posts. A published site with the wrong one
/// loses enquiries silently, which is the worst failure this product has.</item>
/// </list>
/// </summary>
public class PublishBuildTests
{
    /// <summary>A sandbox that runs nothing and remembers what it was asked to run.</summary>
    private sealed class RecordingSandbox : ISandbox
    {
        public List<SandboxCommand> Commands { get; } = [];

        public string Id => "recording";
        public Uri AgentUrl => new("http://localhost");
        public string AgentToken => "none";

        public Task<SandboxCommandResult> RunAsync(
            SandboxCommand command,
            Func<SandboxOutput, Task>? onOutput = null,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);

            // The build succeeds and the copy-out produces an empty archive, which is as far as this test
            // needs to go: what it is about is the command, not the bytes.
            return Task.FromResult(new SandboxCommandResult(0, command.Command == "npm" ? "ok" : string.Empty));
        }

        public Task WriteTreeAsync(WorkspaceTree tree, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<WorkspaceTree> ReadTreeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceTree([]));

        public Task StartDevServerAsync(
            string basePath,
            IReadOnlyDictionary<string, string>? environment = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TouchPreviewAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DevServerLog> ReadDevServerLogAsync(long since = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(DevServerLog.Empty);

        public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<SandboxHealth> ReadHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SandboxHealth(true, false));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Test]
    public async Task A_local_publish_tells_the_build_where_it_is_served_from()
    {
        var sandbox = new RecordingSandbox();

        var target = new FileSystemDeploymentTarget(
            Options.Create(new DeploymentOptions
            {
                FileSystem = new DeploymentOptions.FileSystemOptions
                {
                    Root = Path.Combine(Path.GetTempPath(), $"webly-publish-{Guid.NewGuid():N}"),
                    BaseUrl = "https://localhost:5000"
                }
            }),
            NullLogger<FileSystemDeploymentTarget>.Instance);

        var settings = new SiteBuildSettings(
            "https://koopman-cycles.webly.site",
            "https://localhost:5000/api/public/forms/abc123");

        // The copy-out finds nothing, which is where this stops. The build command is already recorded.
        Assert.That(
            async () => await target.BuildAndDeployAsync(sandbox, "local-abc123", settings),
            Throws.InstanceOf<DeploymentFailedException>());

        var build = sandbox.Commands.FirstOrDefault(x => x.Command == "npm" && x.Arguments.Contains("build"));

        Assert.That(build?.Environment, Is.Not.Null, "the build ran");

        Assert.Multiple(() =>
        {
            Assert.That(build!.Environment!["WEBLY_PREVIEW_BASE"], Is.EqualTo("/published/abc123"),
                "or the published site asks for its stylesheet at the root of Webly's origin");

            Assert.That(build.Environment["NEXT_PUBLIC_SITE_URL"], Is.EqualTo(settings.SiteUrl),
                "the address, not the path this copy is served at");

            Assert.That(build.Environment["NEXT_PUBLIC_FORM_ENDPOINT"], Is.EqualTo(settings.FormEndpoint));
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task Deleting_a_site_takes_its_published_files_with_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"webly-publish-{Guid.NewGuid():N}");
        var target = new FileSystemDeploymentTarget(
            Options.Create(new DeploymentOptions
            {
                FileSystem = new DeploymentOptions.FileSystemOptions { Root = root, BaseUrl = "https://localhost:5000" }
            }),
            NullLogger<FileSystemDeploymentTarget>.Instance);

        var published = Path.Combine(root, "abc123");
        Directory.CreateDirectory(published);
        await File.WriteAllTextAsync(Path.Combine(published, "index.html"), "<h1>Koopman Cycles</h1>");

        await target.DeleteProjectAsync("local-abc123");

        Assert.Multiple(() =>
        {
            // The bug this is for: the rows went, the repository went, and the site carried on being served.
            Assert.That(Directory.Exists(published), Is.False, "the published files are gone");
            Assert.That(Directory.Exists(root), Is.True, "and nothing else is");
        });

        // Idempotent: a caller that deletes twice, or whose site was never published, is not an error.
        Assert.That(async () => await target.DeleteProjectAsync("local-abc123"), Throws.Nothing);

        // And a project id that tries to climb out of the root does nothing at all.
        var outside = Path.Combine(root, "..", "webly-should-survive");
        Directory.CreateDirectory(outside);

        await target.DeleteProjectAsync("local-../webly-should-survive");

        Assert.That(Directory.Exists(outside), Is.True, "a path that leaves the root is refused, not followed");

        Directory.Delete(outside, recursive: true);
        Directory.Delete(root, recursive: true);
    }
}
