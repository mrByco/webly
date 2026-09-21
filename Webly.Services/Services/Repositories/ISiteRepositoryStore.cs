namespace Webly.Services.Services.Repositories;

/// <summary>What a commit is, to callers that do not care about git.</summary>
/// <param name="Sha">The commit id.</param>
/// <param name="ChangedFileCount">How many files it touched, for the history list.</param>
public record CommitResult(string Sha, int ChangedFileCount);

/// <summary>Raised for anything git refuses. Its message is for a log, never for a customer.</summary>
public class RepositoryException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// Every site's source code, as one bare git repository per site.
///
/// <b>Webly owns the history; the agent only ever sees a working tree.</b> A sandbox is seeded with the
/// files of one commit and nothing else — no <c>.git</c>, no remote, no credentials — and what it produces
/// comes back as a tree that this store commits. That is not a convenience: it means a coding agent
/// cannot rewrite, amend or force-push anybody's history, and it does not need a token that could. The
/// cost is that the agent cannot read its own <c>git log</c>; what it needs from the past is in the repo
/// as files (<c>AGENTS.md</c>, <c>content/</c>) rather than in commit messages.
///
/// Bare repositories on a volume rather than a repository per customer on GitHub: no account per customer,
/// no repo-count ceiling, no third-party rate limit in the middle of an edit, and the provider never sees
/// a customer's name. The trade is that Webly runs the storage and backs it up — and that "export to
/// GitHub" is a feature to write rather than one that came free.
/// </summary>
public interface ISiteRepositoryStore
{
    /// <summary>
    /// Creates a site's repository and commits the starter template into it. Returns the first commit.
    /// Idempotent per site only in the sense that it refuses to overwrite: a second call for the same
    /// site throws rather than resetting somebody's website to a template.
    /// </summary>
    Task<CommitResult> InitializeAsync(
        string siteNanoid,
        string branch,
        WorkspaceTree template,
        CommitAuthor author,
        string summary,
        CancellationToken cancellationToken = default);

    /// <summary>The whole tree at a commit, for seeding a sandbox or building a deployment.</summary>
    Task<WorkspaceTree> ReadTreeAsync(string siteNanoid, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>One file at a commit, for the editor's code view. Null when the path is not in the tree.</summary>
    Task<WorkspaceFile?> ReadFileAsync(string siteNanoid, string commitSha, string path, CancellationToken cancellationToken = default);

    /// <summary>Paths and sizes at a commit, without contents.</summary>
    Task<IReadOnlyList<RepositoryEntry>> ListAsync(string siteNanoid, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>
    /// The site's whole repository as a single git bundle.
    ///
    /// A bundle rather than a zip of the files, because a bundle is the repository: `git clone site.bundle`
    /// produces a working clone with every version and every commit message in it. That is what makes "it is
    /// your code" a true sentence rather than a slogan — a customer who leaves takes their history, not a
    /// snapshot, and they do not need Webly to read it.
    ///
    /// Returned as bytes rather than streamed. A text project's bundle is a few hundred kilobytes, and
    /// streaming it would mean holding a process open across a response for something that fits in a
    /// packet — worth revisiting the day a site carries video.
    /// </summary>
    Task<byte[]> CreateBundleAsync(string siteNanoid, string branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a tree on top of <paramref name="parentSha"/> and moves the branch to it.
    ///
    /// The whole tree, not a patch: what comes back from a sandbox is "these are the files now", and
    /// deriving a patch from it only to apply the patch would be two chances to get the same answer.
    /// Deletions therefore work by absence, which is exactly how the agent expresses them.
    ///
    /// Returns null when the tree is identical to the parent's — a turn that changed nothing must not
    /// leave an empty commit behind.
    /// </summary>
    Task<CommitResult?> CommitAsync(
        string siteNanoid,
        string branch,
        string parentSha,
        WorkspaceTree tree,
        CommitAuthor author,
        string summary,
        string? details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an older commit's tree forward as a new commit on the branch tip. This is what "go back"
    /// does — history stays reachable, and the restore is itself a commit somebody can undo.
    /// </summary>
    Task<CommitResult?> RestoreAsync(
        string siteNanoid,
        string branch,
        string parentSha,
        string restoreFromSha,
        CommitAuthor author,
        string summary,
        CancellationToken cancellationToken = default);

    /// <summary>The unified diff of one commit against its parent, for the history's detail view.</summary>
    Task<string> DiffAsync(string siteNanoid, string commitSha, CancellationToken cancellationToken = default);

    /// <summary>The branch tip, or null when the repository does not exist.</summary>
    Task<string?> ResolveHeadAsync(string siteNanoid, string branch, CancellationToken cancellationToken = default);

    /// <summary>Deletes the repository. Called after the rows are gone, and best-effort like the provider
    /// cleanup: a site that will not delete costs more than a directory left behind.</summary>
    Task DeleteAsync(string siteNanoid, CancellationToken cancellationToken = default);
}

/// <summary>Who a commit is attributed to. A real person, even when the agent did the typing.</summary>
public record CommitAuthor(string Name, string Email);

/// <summary>
/// The branch was not where the caller thought it was, so nothing was written.
///
/// Its own type because it is the one repository failure that is **expected**, explainable and the person's to
/// act on: something else changed the site while this change was being prepared. Everything else a repository
/// throws is ours — a path we should not have built, a git binary that is not there — and reaches the chat as
/// "something went wrong", which is all those deserve.
/// </summary>
public class RepositoryConflictException(string message, string? detail = null)
    : RepositoryException(message, detail);
