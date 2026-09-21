using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.Services.Repositories;
using Webly.Services.UseCases.Sites;

namespace Webly.Tests;

/// <summary>
/// What happens when something commits to a site while a turn is running.
///
/// This is the test for the defect that the guard in <c>GitSiteRepositoryStore.MoveBranchAsync</c> was supposed
/// to prevent and did not, because of who was answering the question "what was this tree built from?". A turn
/// re-read its site row just before committing and handed git *that* head as the expected old value, so the two
/// were the same by construction and there was nothing for git to refuse — while the tree it was writing had
/// been seeded from the sandbox minutes earlier. Upload a photograph mid-turn and the turn's commit put the
/// older tree back, deleting the file, with "Added probe.png" still sitting in the history above it. Driven in
/// the running app before it was fixed: the image was gone from the head tree and nothing anywhere said so.
/// </summary>
public class SiteVersionConflictTests : PostgresTestBase
{
    private static readonly CommitAuthor Author = new("Anna Kovacs", "anna@example.com");

    private string _root = null!;
    private GitSiteRepositoryStore _store = null!;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"webly-conflict-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _store = new GitSiteRepositoryStore(
            Options.Create(new RepositoryOptions { Root = _root }), NullLogger<GitSiteRepositoryStore>.Instance);
    }

    [TearDown]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static WorkspaceTree Tree(params (string Path, string Content)[] files) =>
        new([.. files.Select(x => WorkspaceFile.Text(x.Path, x.Content))]);

    [Test]
    public async Task An_upload_during_a_turn_is_refused_rather_than_reverted()
    {
        await using var db = CreateContext();

        var user = new User { Email = "anna@example.com", DisplayName = "Anna Kovacs", EmailVerifiedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { Name = "Elkins Upholstery", Slug = "elkins-upholstery", OwnerId = user.Id };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var commitSiteVersion = new CommitSiteVersion(
            _store, new SiteVersionRepository(db), db, NullLogger<CommitSiteVersion>.Instance);

        var first = await _store.InitializeAsync(
            site.Nanoid, site.DefaultBranch,
            Tree(("src/app/page.tsx", "export default () => <h1>Elkins</h1>;\n")),
            Author, "Created from the Webly starter template");

        var head = new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = first.Sha,
            Summary = "Created from the Webly starter template",
            Origin = SiteVersionOrigin.Template,
            ChangedFileCount = first.ChangedFileCount,
            CreatedByUserId = user.Id
        };

        db.SiteVersions.Add(head);
        await db.SaveChangesAsync();

        site.HeadVersionId = head.Id;
        site.HeadVersion = head;
        await db.SaveChangesAsync();

        // The turn's sandbox is seeded here, so this is the commit its tree will have been built from whatever
        // happens next.
        var seededFrom = first.Sha;

        // While it runs, the person uploads a photograph. That is its own version, on top of the head, and it
        // has to stay: it is the file the next sentence they type is about.
        var upload = await commitSiteVersion.ExecuteAsync(
            site,
            Tree(
                ("src/app/page.tsx", "export default () => <h1>Elkins</h1>;\n"),
                ("public/images/shopfront.png", "not really a png")),
            Author, user.Id, SiteVersionOrigin.Upload, "Added shopfront.png");

        Assert.That(upload, Is.Not.Null);

        // Now the turn comes back with the tree it was given, which knows nothing about the photograph. Before
        // the fix this succeeded and the file was gone; the expected old value it handed git was the head it had
        // just re-read, which the upload had already moved.
        Assert.That(
            async () => await commitSiteVersion.ExecuteAsync(
                site,
                Tree(("src/app/page.tsx", "export default () => <h1>Elkins Upholstery</h1>;\n")),
                Author, user.Id, SiteVersionOrigin.Agent, "Set the headline",
                treeBaseSha: seededFrom),
            Throws.InstanceOf<RepositoryConflictException>(),
            "a turn carrying a tree from before the upload must be refused, not written");

        var tip = await _store.ResolveHeadAsync(site.Nanoid, site.DefaultBranch);
        var tree = await _store.ReadTreeAsync(site.Nanoid, tip!);

        Assert.Multiple(() =>
        {
            Assert.That(tip, Is.EqualTo(upload!.CommitSha), "the branch is where the upload left it");
            Assert.That(tree.Find("public/images/shopfront.png"), Is.Not.Null, "and the photograph is still there");
            Assert.That(site.HeadVersion!.CommitSha, Is.EqualTo(upload.CommitSha));
        });
    }

    /// <summary>
    /// And the ordinary caller — one that reads the head and commits in the same breath — is unaffected: it
    /// passes no base, so the head it read is the expected value. Without this the fix would have turned every
    /// upload, restore and rename into a conflict against itself.
    /// </summary>
    [Test]
    public async Task A_caller_that_reads_the_head_and_commits_needs_no_base()
    {
        await using var db = CreateContext();

        var user = new User { Email = "bo@example.com", DisplayName = "Bo Adeyemi", EmailVerifiedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { Name = "Joe's Kitchens", Slug = "joes-kitchens", OwnerId = user.Id };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var first = await _store.InitializeAsync(
            site.Nanoid, site.DefaultBranch, Tree(("a.txt", "one\n")), Author, "Created");

        var head = new SiteVersion
        {
            SiteId = site.Id,
            CommitSha = first.Sha,
            Summary = "Created",
            Origin = SiteVersionOrigin.Template,
            ChangedFileCount = first.ChangedFileCount,
            CreatedByUserId = user.Id
        };

        db.SiteVersions.Add(head);
        await db.SaveChangesAsync();

        site.HeadVersionId = head.Id;
        site.HeadVersion = head;
        await db.SaveChangesAsync();

        var commitSiteVersion = new CommitSiteVersion(
            _store, new SiteVersionRepository(db), db, NullLogger<CommitSiteVersion>.Instance);

        var second = await commitSiteVersion.ExecuteAsync(
            site, Tree(("a.txt", "two\n")), Author, user.Id, SiteVersionOrigin.Manual, "Second");

        var third = await commitSiteVersion.ExecuteAsync(
            site, Tree(("a.txt", "three\n")), Author, user.Id, SiteVersionOrigin.Manual, "Third");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.Null);
            Assert.That(third, Is.Not.Null);
            Assert.That(third!.ParentVersionId, Is.EqualTo(second!.Id));
        });
    }
}
