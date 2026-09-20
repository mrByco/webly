namespace Webly.Services.Services.Repositories;

/// <summary>
/// One file of a site, as it travels between the repository and a sandbox. Bytes rather than a string,
/// because a site's tree holds images and fonts as well as source.
/// </summary>
/// <param name="Path">Repository-relative, forward slashes, no leading slash.</param>
public record WorkspaceFile(string Path, byte[] Content)
{
    public static WorkspaceFile Text(string path, string content) =>
        new(path, System.Text.Encoding.UTF8.GetBytes(content));

    public string AsText() => System.Text.Encoding.UTF8.GetString(Content);
}

/// <summary>
/// A whole working tree: what a commit contains, and what a sandbox is seeded with.
///
/// Deliberately a flat list rather than a directory structure. Every consumer either writes all of it or
/// looks a path up, and a tree of nodes would be a second shape to walk for no reader's benefit.
/// </summary>
public record WorkspaceTree(IReadOnlyList<WorkspaceFile> Files)
{
    public long TotalBytes => Files.Sum(x => (long)x.Content.Length);

    public WorkspaceFile? Find(string path) =>
        Files.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.Ordinal));
}

/// <summary>A file in a listing: path and size, without reading the bytes.</summary>
public record RepositoryEntry(string Path, long Size);
