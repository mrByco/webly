using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace Webly.Services.Services.Repositories;

/// <summary>
/// The repository store, over the <c>git</c> binary.
///
/// The binary rather than a library (libgit2sharp): every operation here is one plumbing command, the
/// behaviour is then exactly what a developer gets on the same repository by hand, and the two places that
/// matter — <c>commit-tree</c> and <c>update-ref</c> — are the parts a library wraps most thickly. The cost
/// is a process per operation, which is microseconds beside the sandbox and the model.
///
/// <b>Nothing here ever checks out a working copy.</b> Trees are written with <c>hash-object</c> +
/// <c>update-index</c> against a temporary index, and read with <c>cat-file</c>. A bare repository with no
/// worktree cannot be left in a half-checked-out state by a crash, cannot be raced by two concurrent
/// sessions through the filesystem, and cannot be filled with build output.
/// </summary>
public class GitSiteRepositoryStore(
    IOptions<RepositoryOptions> options,
    ILogger<GitSiteRepositoryStore> logger) : ISiteRepositoryStore
{
    private readonly RepositoryOptions _options = options.Value;

    public async Task<CommitResult> InitializeAsync(
        string siteNanoid,
        string branch,
        WorkspaceTree template,
        CommitAuthor author,
        string summary,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(siteNanoid);

        if (Directory.Exists(path))
            throw new RepositoryException($"Site '{siteNanoid}' already has a repository.");

        Directory.CreateDirectory(path);
        await RunAsync(path, cancellationToken, "init", "--bare", $"--initial-branch={branch}");

        var commit = await WriteTreeAndCommitAsync(
            path, branch, parentSha: null, template, author, summary, details: null, cancellationToken);

        return commit ?? throw new RepositoryException("The starter template produced an empty commit.");
    }

    public async Task<WorkspaceTree> ReadTreeAsync(
        string siteNanoid,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(siteNanoid);
        var files = new List<WorkspaceFile>();

        foreach (var entry in await ListEntriesAsync(path, commitSha, cancellationToken))
        {
            var content = await RunBinaryAsync(path, cancellationToken, "cat-file", "blob", entry.Sha);
            files.Add(new WorkspaceFile(entry.Path, content));
        }

        return new WorkspaceTree(files);
    }

    public async Task<WorkspaceFile?> ReadFileAsync(
        string siteNanoid,
        string commitSha,
        string path,
        CancellationToken cancellationToken = default)
    {
        var repository = PathFor(siteNanoid);
        var entry = (await ListEntriesAsync(repository, commitSha, cancellationToken))
            .FirstOrDefault(x => x.Path == path);

        if (entry is null) return null;

        return new WorkspaceFile(entry.Path, await RunBinaryAsync(repository, cancellationToken, "cat-file", "blob", entry.Sha));
    }

    public async Task<IReadOnlyList<RepositoryEntry>> ListAsync(
        string siteNanoid,
        string commitSha,
        CancellationToken cancellationToken = default) =>
        [.. (await ListEntriesAsync(PathFor(siteNanoid), commitSha, cancellationToken))
            .Select(x => new RepositoryEntry(x.Path, x.Size))];

    public Task<CommitResult?> CommitAsync(
        string siteNanoid,
        string branch,
        string parentSha,
        WorkspaceTree tree,
        CommitAuthor author,
        string summary,
        string? details,
        CancellationToken cancellationToken = default)
    {
        if (tree.Files.Count > _options.MaxTreeFiles)
            throw new RepositoryException($"That change has {tree.Files.Count} files; at most {_options.MaxTreeFiles} are allowed.");

        if (tree.TotalBytes > _options.MaxTreeBytes)
            throw new RepositoryException($"That change is {tree.TotalBytes / 1024 / 1024} MB; at most {_options.MaxTreeBytes / 1024 / 1024} MB are allowed.");

        return WriteTreeAndCommitAsync(
            PathFor(siteNanoid), branch, parentSha, tree, author, summary, details, cancellationToken);
    }

    public async Task<CommitResult?> RestoreAsync(
        string siteNanoid,
        string branch,
        string parentSha,
        string restoreFromSha,
        CommitAuthor author,
        string summary,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(siteNanoid);

        // The old commit's tree, committed forward with the branch tip as its parent. One commit-tree call,
        // no checkout and no file copying: the tree object already exists, so "restore" is git's own cheapest
        // operation rather than a rewrite of every file.
        var tree = (await RunAsync(path, cancellationToken, "rev-parse", $"{restoreFromSha}^{{tree}}")).Trim();
        var parentTree = (await RunAsync(path, cancellationToken, "rev-parse", $"{parentSha}^{{tree}}")).Trim();

        if (tree == parentTree) return null;

        var sha = await CommitTreeAsync(path, tree, parentSha, author, summary, details: null, cancellationToken);

        await MoveBranchAsync(path, branch, sha, parentSha, cancellationToken);

        return new CommitResult(sha, await CountChangedFilesAsync(path, sha, cancellationToken));
    }

    /// <summary>
    /// Moves the branch, <b>but only if it is still where the caller thought it was</b>.
    ///
    /// <c>update-ref</c> takes an expected old value and git checks it atomically, which turns the one race
    /// this design has into a clean failure. Two turns on one site can each build a tree from the same parent —
    /// a second browser tab, or a turn that started while another was committing — and without the old value
    /// the second <c>update-ref</c> silently moves the branch to a commit that does not contain the first's
    /// work. The first turn's <c>SiteVersion</c> row is then still in the history, naming a commit no longer
    /// reachable from the branch: a change the person watched happen, with an entry in their history, simply
    /// gone from their site.
    ///
    /// An empty expected value means "this ref must not exist yet", which is exactly right for a site's first
    /// commit: a second initialize for the same site is refused by git rather than by a check-then-act.
    ///
    /// <b>The expected value has to be the commit the tree was built from</b>, not the branch as it is now — and
    /// that distinction was the whole defect this guard was supposed to prevent. A turn read the branch again
    /// just before committing, so the two were the same by construction and git had nothing to refuse; the tree
    /// it wrote had been seeded minutes earlier. Upload a photograph while a turn is running and the turn's
    /// commit, built on the tree from before it, silently deleted the file — with "Added probe.png" still in the
    /// history above it. See <c>AgentTurnService.CommitAsync</c>.
    /// </summary>
    private async Task MoveBranchAsync(
        string repository,
        string branch,
        string commitSha,
        string? expectedSha,
        CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(
                repository,
                cancellationToken,
                "update-ref",
                $"refs/heads/{branch}",
                commitSha,
                expectedSha ?? string.Empty);
        }
        catch (RepositoryException exception)
        {
            throw new RepositoryConflictException(
                "Your site changed while this was being saved, so nothing was written. Please try again.",
                exception.Detail ?? exception.Message);
        }
    }

    /// <summary>
    /// What one commit changed, as a unified diff.
    ///
    /// <c>diff-tree --root</c> rather than <c>diff {sha}~1 {sha}</c>, and that is not a stylistic preference:
    /// a site's first commit has no parent, so <c>~1</c> is not a revision and git exits 128. Which meant the
    /// History tab of every newly created site answered 500 on the only version it had — the first thing a new
    /// customer would look at. <c>--root</c> diffs a parentless commit against the empty tree and behaves
    /// exactly like the old command for every other one, so it is one call rather than a branch.
    ///
    /// <c>--no-commit-id</c> because <c>diff-tree</c> otherwise prints the sha as a first line, and this text
    /// goes straight into a diff viewer. No colour, because a client reads it and not a terminal.
    /// </summary>
    public Task<string> DiffAsync(string siteNanoid, string commitSha, CancellationToken cancellationToken = default) =>
        RunAsync(PathFor(siteNanoid), cancellationToken,
            "diff-tree", "--no-color", "--no-commit-id", "--unified=3", "-p", "--root", commitSha);

    /// <summary>
    /// <c>git bundle create - HEAD &lt;branch&gt;</c>: the whole repository on stdout.
    ///
    /// The literal <c>-</c> is how git spells stdout here, and <see cref="RunBinaryAsync"/> is why that is safe
    /// — a bundle is a packfile, and reading it as text would corrupt it exactly the way reading a blob as text
    /// would. That is the same reason the read path for files is binary.
    ///
    /// <b><c>HEAD</c> is not redundant.</b> A bundle of the branch alone carries the commits and the ref, and
    /// <c>git bundle verify</c> is perfectly happy with it — but <c>git clone</c> of it produces a repository
    /// with <i>no files checked out</i>, because there is no HEAD to say which branch to check out. Naming HEAD
    /// as well is the difference between a customer who leaves getting their website and getting an empty
    /// directory, which is the whole point of the endpoint. Measured, not assumed; the harness runs the same
    /// arguments and clones the result.
    /// </summary>
    public Task<byte[]> CreateBundleAsync(
        string siteNanoid,
        string branch,
        CancellationToken cancellationToken = default) =>
        RunBinaryAsync(PathFor(siteNanoid), cancellationToken, "bundle", "create", "-", "HEAD", branch);

    public async Task<string?> ResolveHeadAsync(
        string siteNanoid,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(siteNanoid);

        if (!Directory.Exists(path)) return null;

        try
        {
            return (await RunAsync(path, cancellationToken, "rev-parse", $"refs/heads/{branch}")).Trim();
        }
        catch (RepositoryException)
        {
            // A repository with no commits on that branch. Not an error to the caller: it is the state a
            // half-finished creation leaves behind, and CreateSite's own transaction is what resolves it.
            return null;
        }
    }

    public Task DeleteAsync(string siteNanoid, CancellationToken cancellationToken = default)
    {
        var path = PathFor(siteNanoid);

        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);

        return Task.CompletedTask;
    }

    // ── git plumbing ───────────────────────────────────────────────────────────────────────────

    private record TreeEntry(string Path, string Sha, long Size);

    private async Task<IReadOnlyList<TreeEntry>> ListEntriesAsync(
        string repository,
        string commitSha,
        CancellationToken cancellationToken)
    {
        // -r recurses into subtrees, -l gives the blob size, -z separates with NULs so a filename with a
        // newline in it cannot forge a row. The format is "<mode> <type> <sha> <size>\t<path>".
        var output = await RunAsync(repository, cancellationToken, "ls-tree", "-r", "-l", "-z", commitSha);
        var entries = new List<TreeEntry>();

        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0) continue;

            var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || fields[1] != "blob") continue;

            entries.Add(new TreeEntry(record[(tab + 1)..], fields[2], long.TryParse(fields[3], out var size) ? size : 0));
        }

        return entries;
    }

    /// <summary>
    /// Writes every file as a blob into a temporary index, turns that into a tree, and commits it — the
    /// bare-repository equivalent of <c>git add -A &amp;&amp; git commit</c>, without a worktree.
    ///
    /// The temporary index file is what makes it safe for two sessions on one repository to overlap: each
    /// has its own <c>GIT_INDEX_FILE</c>, so neither can see the other's staged state.
    /// </summary>
    private async Task<CommitResult?> WriteTreeAndCommitAsync(
        string repository,
        string branch,
        string? parentSha,
        WorkspaceTree tree,
        CommitAuthor author,
        string summary,
        string? details,
        CancellationToken cancellationToken)
    {
        foreach (var file in tree.Files) RejectUnsafePath(file.Path);

        var indexFile = Path.Combine(Path.GetTempPath(), $"webly-index-{Guid.NewGuid():N}");
        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = indexFile };

        try
        {
            foreach (var file in tree.Files)
            {
                var sha = (await RunAsync(repository, cancellationToken, environment, file.Content,
                    "hash-object", "-w", "--stdin")).Trim();

                // 100644 for everything: a Next.js source tree has no executables, and letting a mode come
                // from a sandbox would let an agent commit something the build runs.
                await RunAsync(repository, cancellationToken, environment, null,
                    "update-index", "--add", "--cacheinfo", $"100644,{sha},{file.Path}");
            }

            var treeSha = (await RunAsync(repository, cancellationToken, environment, null, "write-tree")).Trim();

            if (parentSha is not null)
            {
                var parentTree = (await RunAsync(repository, cancellationToken, "rev-parse", $"{parentSha}^{{tree}}")).Trim();

                // Identical tree, no commit. This is what makes "the agent answered a question without
                // changing anything" leave no trace in the history.
                if (treeSha == parentTree) return null;
            }

            var commitSha = await CommitTreeAsync(repository, treeSha, parentSha, author, summary, details, cancellationToken);

            await MoveBranchAsync(repository, branch, commitSha, parentSha, cancellationToken);

            return new CommitResult(commitSha, parentSha is null
                ? tree.Files.Count
                : await CountChangedFilesAsync(repository, commitSha, cancellationToken));
        }
        finally
        {
            if (File.Exists(indexFile)) File.Delete(indexFile);
        }
    }

    private async Task<string> CommitTreeAsync(
        string repository,
        string treeSha,
        string? parentSha,
        CommitAuthor author,
        string summary,
        string? details,
        CancellationToken cancellationToken)
    {
        var message = details is { Length: > 0 } ? $"{summary}\n\n{details}" : summary;

        var arguments = new List<string> { "commit-tree", treeSha };

        if (parentSha is not null)
        {
            arguments.Add("-p");
            arguments.Add(parentSha);
        }

        // Author and committer are both the person who asked. The agent is how the change was made, not who
        // made it — and a commit attributed to a robot is a history nobody can answer questions about.
        var environment = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = author.Name,
            ["GIT_AUTHOR_EMAIL"] = author.Email,
            ["GIT_COMMITTER_NAME"] = author.Name,
            ["GIT_COMMITTER_EMAIL"] = author.Email
        };

        var sha = await RunAsync(
            repository, cancellationToken, environment, Encoding.UTF8.GetBytes(message), [.. arguments]);

        return sha.Trim();
    }

    private async Task<int> CountChangedFilesAsync(string repository, string commitSha, CancellationToken cancellationToken)
    {
        var output = await RunAsync(repository, cancellationToken,
            "diff-tree", "--no-commit-id", "--name-only", "-r", "-z", commitSha);

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Refuses a path a tree from a sandbox has no business containing.
    ///
    /// <b>git already refuses every one of these</b> — it rejects an absolute path, a <c>..</c> segment, a
    /// <c>.git</c> component in any case, a doubled slash and an empty name, and that was the only thing
    /// standing between an agent and the rest of the disk. This exists for two reasons anyway. The first is
    /// that the rule belongs to us: the tree was assembled by a language model on a machine we do not own, and
    /// "it happens to be safe because of what the tool we shell out to does" is the kind of protection that
    /// disappears in a refactor nobody connects to it. The second is the sentence somebody reads — git's is
    /// <c>fatal: git update-index: --cacheinfo cannot add ../evil.txt</c>, which reaches the chat as a failed
    /// turn nobody can act on.
    ///
    /// Note what is <i>not</i> refused: a leading dot. `.gitignore`, `.env.example` and `.eslintrc.json` are
    /// ordinary files in a Next.js project, and a rule that swallowed them would break real sites.
    /// </summary>
    private static void RejectUnsafePath(string path)
    {
        var segments = path.Split('/');

        var unsafeSegments = path.Length == 0
            || path.StartsWith('/')
            || path.Contains('\\')
            || path.Any(char.IsControl)
            || segments.Any(segment =>
                segment.Length == 0
                || segment == "."
                || segment == ".."
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase));

        if (unsafeSegments)
            throw new RepositoryException(
                $"'{path}' is not a path this site can hold. Files live under the project, and nothing may "
                + "write outside it or into its git directory.");
    }

    private string PathFor(string siteNanoid)
    {
        // A nanoid is URL-safe by construction, but this path is built from a value that arrived over HTTP,
        // so it is checked rather than trusted: one directory traversal here would reach every other site's
        // source.
        if (siteNanoid.Length is 0 or > 40 || !siteNanoid.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_'))
            throw new RepositoryException($"'{siteNanoid}' is not a site id.");

        return Path.Combine(_options.Root, $"{siteNanoid}.git");
    }

    private Task<string> RunAsync(string repository, CancellationToken cancellationToken, params string[] arguments) =>
        RunAsync(repository, cancellationToken, null, null, arguments);

    private async Task<string> RunAsync(
        string repository,
        CancellationToken cancellationToken,
        IDictionary<string, string>? environment,
        byte[]? input,
        params string[] arguments) =>
        Encoding.UTF8.GetString(await RunBinaryAsync(repository, cancellationToken, environment, input, arguments));

    private Task<byte[]> RunBinaryAsync(string repository, CancellationToken cancellationToken, params string[] arguments) =>
        RunBinaryAsync(repository, cancellationToken, null, null, arguments);

    /// <summary>
    /// One git invocation. Output is read as bytes, because <c>cat-file blob</c> returns whatever the file
    /// holds — decoding it as text would corrupt every image in a site.
    /// </summary>
    private async Task<byte[]> RunBinaryAsync(
        string repository,
        CancellationToken cancellationToken,
        IDictionary<string, string>? environment,
        byte[]? input,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Arguments as a list, never a command string: a file path in a site's tree is attacker-supplied, and
        // a quoted command line is where that becomes a shell injection.
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new RepositoryException("git could not be started. Is it on PATH?");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CommandTimeout);

        using var output = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

        if (input is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
            process.StandardInput.Close();
        }

        await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            logger.LogError("git {Arguments} in {Repository} exited {Code}: {Error}",
                string.Join(' ', arguments), repository, process.ExitCode, error);

            throw new RepositoryException($"git {arguments[0]} failed.", error);
        }

        return output.ToArray();
    }
}
