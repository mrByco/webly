using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Sites;
using Webly.Data.Repositories.Sites;
using Webly.Services.Services.Repositories;
using Webly.Services.UseCases.Sites;

namespace Webly.Tests;

/// <summary>
/// The files Webly owns inside a site, kept current in one created before they changed.
///
/// This is the test for a problem the product made for itself. <c>AGENTS.md</c> and <c>next.config.ts</c>
/// ship inside each site's repository, which is what makes a site self-contained — and what makes both as old
/// as the site. The first rule added after that ("never write a server for a form; this site is a static
/// export") would have reached no existing site at all, and the agent would have gone on confidently doing
/// the thing the rule exists to prevent. The same is true of a fix to the build contract.
/// </summary>
public class WeblyOwnedFileTests : PostgresTestBase
{
    /// <summary>A template whose contents the test can change, standing in for a release of Webly.</summary>
    private sealed class FakeTemplate(WorkspaceTree tree) : ISiteTemplateSource
    {
        public WorkspaceTree Tree { get; set; } = tree;

        public Task<WorkspaceTree> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Tree);
    }

    private static readonly CommitAuthor Author = new("Anna Kovacs", "anna@example.com");

    private static WorkspaceTree TemplateWith(string rules, string config = "export default { output: 'export' };\n") =>
        new(
        [
            WorkspaceFile.Text("AGENTS.md", rules),
            WorkspaceFile.Text("CLAUDE.md", "See AGENTS.md.\n"),
            WorkspaceFile.Text("next.config.ts", config),
            WorkspaceFile.Text("src/app/page.tsx", "export default () => null;\n"),
        ]);

    private string _root = null!;
    private GitSiteRepositoryStore _store = null!;

    [SetUp]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), $"webly-instructions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _store = new GitSiteRepositoryStore(
            Options.Create(new RepositoryOptions { Root = _root }), NullLogger<GitSiteRepositoryStore>.Instance);
    }

    [TearDown]
    public void DeleteRoot()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task A_site_created_before_a_rule_changed_gets_it_in_a_version_of_its_own()
    {
        await using var db = CreateContext();

        var user = new User { Email = "anna@example.com", DisplayName = "Anna Kovacs", EmailVerifiedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var site = new Site { Name = "Koopman Cycles", Slug = "koopman-cycles", OwnerId = user.Id };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var template = new FakeTemplate(TemplateWith("1. The old rule.\n"));
        var first = await _store.InitializeAsync(
            site.Nanoid, site.DefaultBranch, template.Tree, Author, "Created from the Webly starter template");

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

        var commitSiteVersion = new CommitSiteVersion(
            _store, new SiteVersionRepository(db), db, NullLogger<CommitSiteVersion>.Instance);

        var sync = new SyncWeblyOwnedFiles(
            template, _store, commitSiteVersion, NullLogger<SyncWeblyOwnedFiles>.Instance);

        Assert.That(await sync.ExecuteAsync(site, Author, user.Id), Is.Null,
            "a site whose instructions are current is left alone, so no turn writes a version nobody asked for");

        // A release of Webly changes the rules.
        template.Tree = TemplateWith("1. The old rule.\n2. The new rule.\n");

        var version = await sync.ExecuteAsync(site, Author, user.Id);

        Assert.That(version, Is.Not.Null);

        Assert.Multiple(() =>
        {
            // Its own version, saying what it is. Folding it into the person's next turn is the package-lock
            // mistake again: their diff would be the change they asked for plus one they did not.
            Assert.That(version!.Summary, Is.EqualTo("Updated the editing instructions"));
            Assert.That(version.Origin, Is.EqualTo(SiteVersionOrigin.Template));
            Assert.That(version.ChangedFileCount, Is.EqualTo(1), "only the file that changed");
        });

        var tree = await _store.ReadTreeAsync(site.Nanoid, version!.CommitSha);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Find("AGENTS.md")!.AsText(), Does.Contain("The new rule"));
            Assert.That(tree.Find("src/app/page.tsx"), Is.Not.Null, "and the site itself is untouched");

            // The navigation property, not just the key: the workspace registry seeds from it, so a site left
            // pointing at the previous version would copy the tree this commit just replaced into the sandbox.
            Assert.That(site.HeadVersion!.CommitSha, Is.EqualTo(version.CommitSha));
        });

        Assert.That(await sync.ExecuteAsync(site, Author, user.Id), Is.Null, "and it is current again");

        // And the build contract travels the same way, with a summary that says what it is rather than
        // claiming a rule changed. A site whose `next.config.ts` is a release old renders its preview with
        // Next's dev badge on top of it, for ever, because nothing else would ever rewrite that file.
        template.Tree = TemplateWith(
            "1. The old rule.\n2. The new rule.\n", "export default { output: 'export', devIndicators: false };\n");

        var settings = await sync.ExecuteAsync(site, Author, user.Id);

        Assert.That(settings, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(settings!.Summary, Is.EqualTo("Updated this site's build settings"));
            Assert.That(settings.ChangedFileCount, Is.EqualTo(1));
        });

        var updated = await _store.ReadTreeAsync(site.Nanoid, settings!.CommitSha);

        Assert.That(updated.Find("next.config.ts")!.AsText(), Does.Contain("devIndicators"));
    }
}
