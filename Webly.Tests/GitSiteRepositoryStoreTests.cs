using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webly.Services.Services.Repositories;

namespace Webly.Tests;

/// <summary>
/// The repository store, against real git in a temporary directory.
///
/// No Postgres and no Docker: these are the fastest tests in the suite and they cover the layer everything else
/// trusts — if a commit does not contain what was handed to it, or a restore loses history, nothing above notices
/// until a customer does.
/// </summary>
public class GitSiteRepositoryStoreTests
{
    private string _root = null!;
    private GitSiteRepositoryStore _store = null!;

    private static readonly CommitAuthor Author = new("Anna Kovacs", "anna@example.com");

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"webly-git-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _store = new GitSiteRepositoryStore(
            Options.Create(new RepositoryOptions { Root = _root }),
            NullLogger<GitSiteRepositoryStore>.Instance);
    }

    [TearDown]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static WorkspaceTree Tree(params (string Path, string Content)[] files) =>
        new([.. files.Select(x => WorkspaceFile.Text(x.Path, x.Content))]);

    [Test]
    public async Task Initializing_commits_the_template_and_it_reads_back()
    {
        var commit = await _store.InitializeAsync(
            "site1", "main", Tree(("package.json", "{}\n"), ("src/app/page.tsx", "export default () => null;\n")),
            Author, "Created from the Webly starter template");

        var tree = await _store.ReadTreeAsync("site1", commit.Sha);

        Assert.Multiple(() =>
        {
            Assert.That(commit.Sha, Has.Length.EqualTo(40));
            Assert.That(commit.ChangedFileCount, Is.EqualTo(2));
            Assert.That(tree.Files.Select(x => x.Path), Is.EquivalentTo(new[] { "package.json", "src/app/page.tsx" }));
            Assert.That(tree.Find("package.json")!.AsText(), Is.EqualTo("{}\n"));
        });
    }

    [Test]
    public async Task A_second_repository_for_the_same_site_is_refused()
    {
        await _store.InitializeAsync("site1", "main", Tree(("a.txt", "a")), Author, "First");

        Assert.That(
            async () => await _store.InitializeAsync("site1", "main", Tree(("a.txt", "b")), Author, "Again"),
            Throws.TypeOf<RepositoryException>());
    }

    /// <summary>
    /// The whole tree is what a sandbox hands back, so a file that is simply absent from it is a deletion. If this
    /// were a patch instead, a deleted file would need the agent to say so explicitly — and it would not.
    /// </summary>
    [Test]
    public async Task Committing_a_tree_adds_changes_and_deletes_by_absence()
    {
        var first = await _store.InitializeAsync(
            "site1", "main", Tree(("keep.txt", "keep"), ("gone.txt", "gone")), Author, "First");

        var second = await _store.CommitAsync(
            "site1", "main", first.Sha,
            Tree(("keep.txt", "kept"), ("new.txt", "new")),
            Author, "Changed things", null);

        var tree = await _store.ReadTreeAsync("site1", second!.Sha);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Files.Select(x => x.Path), Is.EquivalentTo(new[] { "keep.txt", "new.txt" }));
            Assert.That(tree.Find("keep.txt")!.AsText(), Is.EqualTo("kept"));
            // keep.txt changed, gone.txt vanished, new.txt arrived.
            Assert.That(second.ChangedFileCount, Is.EqualTo(3));
        });
    }

    /// <summary>
    /// A turn that answered a question without editing anything must leave no commit — otherwise the history fills
    /// with entries that changed nothing, which is exactly what makes a history unreadable.
    /// </summary>
    [Test]
    public async Task Committing_an_identical_tree_produces_nothing()
    {
        var first = await _store.InitializeAsync("site1", "main", Tree(("a.txt", "a")), Author, "First");

        var second = await _store.CommitAsync(
            "site1", "main", first.Sha, Tree(("a.txt", "a")), Author, "Nothing changed", null);

        Assert.That(second, Is.Null);
    }

    /// <summary>
    /// The heart of the versioning promise: going back adds to history rather than rewinding it, so the version
    /// that was skipped over is still reachable and the restore itself can be undone.
    /// </summary>
    [Test]
    public async Task Restoring_writes_an_old_tree_forward_and_keeps_the_history()
    {
        var first = await _store.InitializeAsync("site1", "main", Tree(("page.tsx", "one")), Author, "First");
        var second = await _store.CommitAsync(
            "site1", "main", first.Sha, Tree(("page.tsx", "two")), Author, "Second", null);

        var restored = await _store.RestoreAsync(
            "site1", "main", second!.Sha, first.Sha, Author, "Restored the first version");

        var head = await _store.ResolveHeadAsync("site1", "main");
        var restoredTree = await _store.ReadTreeAsync("site1", restored!.Sha);
        var skipped = await _store.ReadTreeAsync("site1", second.Sha);

        Assert.Multiple(() =>
        {
            Assert.That(head, Is.EqualTo(restored.Sha), "the branch moved forward, not back");
            Assert.That(restored.Sha, Is.Not.EqualTo(first.Sha), "a restore is its own commit");
            Assert.That(restoredTree.Find("page.tsx")!.AsText(), Is.EqualTo("one"));
            Assert.That(skipped.Find("page.tsx")!.AsText(), Is.EqualTo("two"), "the skipped version is still there");
        });
    }

    [Test]
    public async Task Restoring_the_current_version_produces_nothing()
    {
        var first = await _store.InitializeAsync("site1", "main", Tree(("a.txt", "a")), Author, "First");

        var restored = await _store.RestoreAsync("site1", "main", first.Sha, first.Sha, Author, "Restored");

        Assert.That(restored, Is.Null);
    }

    [Test]
    public async Task Reading_a_file_and_a_diff_answers_what_changed()
    {
        var first = await _store.InitializeAsync("site1", "main", Tree(("page.tsx", "one\n")), Author, "First");
        var second = await _store.CommitAsync(
            "site1", "main", first.Sha, Tree(("page.tsx", "two\n")), Author, "Second", null);

        var file = await _store.ReadFileAsync("site1", second!.Sha, "page.tsx");
        var missing = await _store.ReadFileAsync("site1", second.Sha, "nope.tsx");
        var diff = await _store.DiffAsync("site1", second.Sha);

        Assert.Multiple(() =>
        {
            Assert.That(file!.AsText(), Is.EqualTo("two\n"));
            Assert.That(missing, Is.Null);
            Assert.That(diff, Does.Contain("-one"));
            Assert.That(diff, Does.Contain("+two"));
        });
    }

    /// <summary>
    /// Binary content has to survive the round trip byte for byte, or every image a site uses is corrupted the
    /// first time it is committed — which is exactly the bug a text-based store would hide until somebody looked.
    /// </summary>
    [Test]
    public async Task Binary_content_round_trips()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0xFF, 0xFE };

        var commit = await _store.InitializeAsync(
            "site1", "main", new WorkspaceTree([new WorkspaceFile("logo.png", bytes)]), Author, "First");

        var file = await _store.ReadFileAsync("site1", commit.Sha, "logo.png");

        Assert.That(file!.Content, Is.EqualTo(bytes));
    }

    /// <summary>
    /// The site id becomes a path, and it arrives over HTTP. One traversal here would reach every other site's
    /// source, so it is checked rather than trusted.
    /// </summary>
    [Test]
    public void A_site_id_that_is_a_path_is_refused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(async () => await _store.ResolveHeadAsync("../etc", "main"), Throws.TypeOf<RepositoryException>());
            Assert.That(async () => await _store.ResolveHeadAsync("a/b", "main"), Throws.TypeOf<RepositoryException>());
        });
    }

    [Test]
    public async Task A_tree_beyond_the_limits_is_refused()
    {
        var store = new GitSiteRepositoryStore(
            Options.Create(new RepositoryOptions { Root = _root, MaxTreeFiles = 2 }),
            NullLogger<GitSiteRepositoryStore>.Instance);

        var first = await store.InitializeAsync("site1", "main", Tree(("a.txt", "a")), Author, "First");

        Assert.That(
            async () => await store.CommitAsync("site1", "main", first.Sha,
                Tree(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c")), Author, "Too much", null),
            Throws.TypeOf<RepositoryException>());
    }

    [Test]
    public async Task The_head_of_an_unknown_site_is_null_rather_than_an_error()
    {
        Assert.That(await _store.ResolveHeadAsync("nope", "main"), Is.Null);
    }
}
