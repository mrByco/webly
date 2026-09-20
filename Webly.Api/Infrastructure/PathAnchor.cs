namespace Webly.Api.Infrastructure;

/// <summary>
/// Makes the configured paths absolute before anything reads them.
///
/// Three settings name something on disk — <c>Templates:SitePath</c>, <c>Sandbox:Local:AgentPath</c> and
/// <c>Repositories:Root</c> — and all three are written relative, because <c>templates/next-site</c> is what
/// they mean and an absolute path in a committed file means nothing on another machine. A relative path in
/// .NET resolves against the process's working directory, and that is the problem: the working directory is
/// decided by whoever launched the app, and the two ways this repository launches it disagree. <c>dotnet
/// run</c> uses the project's directory whatever directory it was invoked from; the dev MCP server runs the
/// built exe from the repository root on purpose. Both were "correct" and each broke the other.
///
/// So the paths are anchored here instead, against <b>the repository root if there is one, and the content
/// root otherwise</b>:
///
/// <list type="bullet">
/// <item>In development the binary is under <c>Webly.Api/bin/…</c>, the walk up finds <c>Webly.slnx</c>, and
/// <c>templates/next-site</c> means the one in this checkout — from any working directory, under any
/// launcher, including <c>dotnet run</c> typed by hand.</item>
/// <item>In the container there is no solution file. The walk finds nothing and falls back to the content
/// root, which is <c>/app</c> — where the Dockerfile copies <c>templates/</c> and where the volume is
/// mounted. The same configuration means the right thing without a second set of values.</item>
/// </list>
///
/// A path that is already absolute is left exactly as it is, which is what makes
/// <c>appsettings.Production.json</c>'s <c>/var/lib/webly/repositories</c> and an override in a test host
/// both work without knowing this class exists.
/// </summary>
public static class PathAnchor
{
    /// <summary>
    /// The file whose presence means "this is a checkout, not a deployment". The solution file rather than
    /// <c>.git</c>: a published app could plausibly sit inside somebody's repository, and a directory that
    /// holds <c>Webly.slnx</c> is unambiguously this project's source.
    /// </summary>
    private const string SolutionFile = "Webly.slnx";

    public static void Resolve(IConfiguration configuration, IHostEnvironment environment, params string[] keys)
    {
        var anchor = RepositoryRoot() ?? environment.ContentRootPath;

        foreach (var key in keys)
        {
            var value = configuration[key];

            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)) continue;

            // Written back into configuration rather than handed to each consumer, so that the options
            // classes, the boot-time checks and anything added later all see one already-resolved value and
            // none of them has to remember to resolve it.
            configuration[key] = Path.GetFullPath(Path.Combine(anchor, value));
        }
    }

    private static string? RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile))) return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
