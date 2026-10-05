using Webly.Services.Services.Sandboxes;

namespace Webly.Tests;

/// <summary>
/// The two defaults that keep a local agent inside its workspace. Where the workspace goes: it used to be
/// <c>.run/workspaces</c>, inside this checkout, and a coding agent walking up from its workspace for a
/// <c>.git</c> found Webly's — so its first turn through the app went looking through the other sites'
/// repositories before it found the one it had been given. And that it is confined there.
/// </summary>
public class LocalSandboxOptionsTests
{
    /// <summary>
    /// An agent must not reach anything outside its working directory, and the switch for that is one string
    /// in configuration. A default that drifted to "none" would turn it off for every fresh clone, silently.
    /// </summary>
    [Test]
    public void By_default_the_agent_is_confined_to_its_workspace() =>
        Assert.That(new LocalSandboxOptions().Confinement, Is.EqualTo(LocalSandboxOptions.BubblewrapConfinement));

    private static readonly string AgentScript =
        Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "tools/sandbox-agent/index.js"));

    [Test]
    public void By_default_a_workspace_is_in_no_git_repository()
    {
        var root = new LocalSandboxOptions().ResolveWorkspaceRoot(AgentScript);

        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
        {
            // A file as well as a directory: in a git worktree `.git` is a file, and the walk stops there too.
            Assert.That(
                Path.Combine(directory.FullName, ".git"),
                Does.Not.Exist,
                $"{root} sits inside the repository at {directory.FullName}");
        }
    }

    [Test]
    public void Two_checkouts_do_not_share_a_root()
    {
        var options = new LocalSandboxOptions();

        Assert.That(
            options.ResolveWorkspaceRoot("/home/a/webly/tools/sandbox-agent/index.js"),
            Is.Not.EqualTo(options.ResolveWorkspaceRoot("/home/b/webly/tools/sandbox-agent/index.js")),
            "the first-use sweep of one would take down the other's sandboxes");
    }

    [Test]
    public void A_configured_root_is_used_as_it_is()
    {
        var root = Path.Combine(Path.GetTempPath(), "somewhere-chosen");

        Assert.That(new LocalSandboxOptions { WorkspaceRoot = root }.ResolveWorkspaceRoot(AgentScript), Is.EqualTo(root));
    }
}
