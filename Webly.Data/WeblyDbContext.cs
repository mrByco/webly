using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NanoidDotNet;
using System.Text.Json.Nodes;
using Webly.Data.Models.Authentication;
using Webly.Data.Models.Chat;
using Webly.Data.Models.Deployments;
using Webly.Data.Models.Forms;
using Webly.Data.Models.Interfaces;
using Webly.Data.Models.Sites;

namespace Webly.Data;

public class WeblyDbContext(DbContextOptions<WeblyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<UserSecurityToken> UserSecurityTokens => Set<UserSecurityToken>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<SiteVersion> SiteVersions => Set<SiteVersion>();
    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<FormSubmission> FormSubmissions => Set<FormSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(user =>
        {
            user.HasIndex(x => x.Nanoid).IsUnique();
            user.HasIndex(x => x.Email).IsUnique();

            // Deleting a site must not delete its owner — it only clears the pointer, and the next
            // request picks another of their sites, or offers to create one.
            user.HasOne(x => x.CurrentSite)
                .WithMany()
                .HasForeignKey(x => x.CurrentSiteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RefreshToken>(token =>
        {
            // Every refresh is a lookup by hash, and rotation makes these rows accumulate.
            token.HasIndex(x => x.TokenHash);

            token.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSecurityToken>(token =>
        {
            // Every verification and reset is a lookup by hash.
            token.HasIndex(x => x.TokenHash);

            token.Property(x => x.Purpose).HasConversion<string>();

            token.HasOne(x => x.User)
                .WithMany(x => x.SecurityTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLogin>(login =>
        {
            // One account per provider identity. This is the index that makes "sign in with Google"
            // idempotent rather than a second account per visit.
            login.HasIndex(x => new { x.Provider, x.ProviderKey }).IsUnique();

            login.Property(x => x.Provider).HasConversion<string>();

            login.HasOne(x => x.User)
                .WithMany(x => x.ExternalLogins)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Site>(site =>
        {
            site.HasIndex(x => x.Nanoid).IsUnique();

            // The subdomain every site is reachable at, so it has to be unique across the platform.
            site.HasIndex(x => x.Slug).IsUnique();

            site.HasOne(x => x.Owner)
                .WithMany(x => x.Sites)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // The two pointers into the commit index. NoAction, and made DEFERRABLE INITIALLY DEFERRED
            // by raw SQL in the migration — EF cannot express that.
            //
            // They have to refuse a version that is still pointed at, and they do. But deleting a site
            // cascades its versions away *and* deletes the row holding these pointers, and Postgres
            // checks a constraint after each triggered action rather than after all of them: undeferred,
            // whichever half ran second lost, and no site that had ever been published could be
            // deleted. WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them
            // is the canary for this; do not change these behaviours without running it.
            site.HasOne(x => x.HeadVersion)
                .WithMany()
                .HasForeignKey(x => x.HeadVersionId)
                .OnDelete(DeleteBehavior.NoAction);

            site.HasOne(x => x.PublishedVersion)
                .WithMany()
                .HasForeignKey(x => x.PublishedVersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SiteVersion>(version =>
        {
            version.HasIndex(x => x.Nanoid).IsUnique();

            // The history list: a site's versions, newest first.
            version.HasIndex(x => new { x.SiteId, x.CreatedAt });

            // One row per commit. Git guarantees the sha globally; this says so per site, which is the
            // scope every query has — and it is what makes "record this commit" idempotent if a turn is
            // retried after its commit landed but before its row did.
            version.HasIndex(x => new { x.SiteId, x.CommitSha }).IsUnique();

            version.Property(x => x.CommitSha).HasMaxLength(40);

            version.Property(x => x.Origin).HasConversion<string>();

            version.HasOne(x => x.Site)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deferred for the same reason as the site's pointers: the whole index is deleted in one
            // statement when a site goes, and a self-reference is checked per row.
            version.HasOne(x => x.ParentVersion)
                .WithMany()
                .HasForeignKey(x => x.ParentVersionId)
                .OnDelete(DeleteBehavior.NoAction);

            version.HasOne(x => x.RestoredFromVersion)
                .WithMany()
                .HasForeignKey(x => x.RestoredFromVersionId)
                .OnDelete(DeleteBehavior.NoAction);

            // Not Cascade: the account that asked for a change is not what the change belongs to. Deleting a
            // user deletes their sites, which takes the versions with them.
            //
            // ClientNoAction rather than NoAction, which is the difference between this compiling and this
            // working. Both produce ON DELETE NO ACTION, which is what the deferred constraint in the migration
            // needs. But NoAction still leaves EF's own change tracker in charge of the loaded graph, and its
            // default for a *required* relationship whose principal is being removed is to sever it — which for
            // a non-nullable foreign key means throwing "the association has been severed" before a single
            // statement is sent. ClientNoAction tells it to leave the graph alone and let the database keep its
            // own promises. The three relationships in this file that point a required key at a row a cascade
            // will remove anyway all say it.
            version.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.ClientNoAction);

            // A version outlives the chat that produced it — see SiteVersion.SourceMessageId.
            version.HasOne(x => x.SourceMessage)
                .WithMany()
                .HasForeignKey(x => x.SourceMessageId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Domain>(domain =>
        {
            domain.HasIndex(x => x.Nanoid).IsUnique();

            // One site per hostname, platform-wide.
            domain.HasIndex(x => x.Hostname).IsUnique();

            domain.Property(x => x.VerificationState).HasConversion<string>();

            domain.HasOne(x => x.Site)
                .WithMany(x => x.Domains)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            // At most one primary hostname per site. A partial unique index rather than a check in the
            // use case: two requests promoting different domains at once would both pass a read-then-write
            // check, and a site with two canonical hostnames splits its own search ranking.
            domain.HasIndex(x => x.SiteId)
                .IsUnique()
                .HasFilter("\"IsPrimary\"")
                .HasDatabaseName("IX_Domains_OnePrimaryPerSite");
        });

        modelBuilder.Entity<Deployment>(deployment =>
        {
            deployment.HasIndex(x => x.Nanoid).IsUnique();

            // The deployment list, and the "is one already running for this site" question.
            deployment.HasIndex(x => new { x.SiteId, x.CreatedAt });

            deployment.Property(x => x.Status).HasConversion<string>();

            deployment.HasOne(x => x.Site)
                .WithMany(x => x.Deployments)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            // A version that has been deployed cannot be deleted out from under the record of it.
            // Deferred, like every other reference into the version chain.
            deployment.HasOne(x => x.SiteVersion)
                .WithMany()
                .HasForeignKey(x => x.SiteVersionId)
                .OnDelete(DeleteBehavior.NoAction);

            // ClientNoAction, for the reason SiteVersion.CreatedBy explains.
            deployment.HasOne(x => x.TriggeredBy)
                .WithMany()
                .HasForeignKey(x => x.TriggeredByUserId)
                .OnDelete(DeleteBehavior.ClientNoAction);
        });

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.HasIndex(x => x.Nanoid).IsUnique();

            conversation.Property(x => x.Status).HasConversion<string>();

            conversation.HasOne(x => x.Site)
                .WithMany(x => x.Conversations)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            // ClientNoAction, for the reason SiteVersion.CreatedBy explains.
            conversation.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.ClientNoAction);

            // One active thread per site (see Conversation). Partial, so archived threads accumulate
            // freely — the index states the invariant the editor relies on instead of a lookup that two
            // concurrent "New chat" clicks could both pass.
            conversation.HasIndex(x => x.SiteId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'")
                .HasDatabaseName("IX_Conversations_OneActivePerSite");
        });

        modelBuilder.Entity<ConversationMessage>(message =>
        {
            message.HasIndex(x => x.Nanoid).IsUnique();

            // Reading a thread is this index, and the writer's next sequence number is its last row.
            message.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();

            message.Property(x => x.Role).HasConversion<string>();

            message.Property(x => x.Parts)
                .HasColumnType("jsonb")
                .HasConversion(PartsConverter, PartsComparer);

            message.HasOne(x => x.Conversation)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            message.HasOne(x => x.ProducedVersion)
                .WithMany()
                .HasForeignKey(x => x.ProducedVersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FormSubmission>(submission =>
        {
            submission.HasIndex(x => x.Nanoid).IsUnique();

            // The only query there is: this site's submissions, newest first. Also what the per-site flood cap
            // counts, which is why the date is in the index rather than beside it.
            submission.HasIndex(x => new { x.SiteId, x.CreatedAt });

            submission.HasOne(x => x.Site)
                .WithMany(x => x.FormSubmissions)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.Cascade);

            // One jsonb column rather than a table of fields. There is no query that looks inside it — the
            // screen lists pairs and the email prints them — and a child table would buy an index nobody uses
            // in exchange for a join on every read.
            submission.OwnsMany(x => x.Fields, fields => fields.ToJson());
        });
    }

    private static readonly ValueConverter<JsonArray?, string?> PartsConverter = new(
        parts => parts == null ? null : parts.ToJsonString(),
        json => json == null ? null : JsonNode.Parse(json)!.AsArray());

    private static readonly ValueComparer<JsonArray?> PartsComparer = new(
        (left, right) => left == null ? right == null : right != null && left.ToJsonString() == right.ToJsonString(),
        parts => parts == null ? 0 : parts.ToJsonString().GetHashCode(),
        parts => parts == null ? null : JsonNode.Parse(parts.ToJsonString())!.AsArray());

    /// <summary>
    /// Fills in <see cref="IHasNanoid.Nanoid"/> on insert and stamps <see cref="IHasTimestamps"/> on
    /// insert and update.
    ///
    /// Central on purpose: a public id and a last-write time that depend on every use case, seeder and
    /// test builder remembering are values nothing downstream can trust. A caller that wants a specific
    /// nanoid (an import, a fixture) may still set one — only empty values are filled.
    ///
    /// <see cref="IHasCreatedAt"/> is the other half: a version, a message and a form submission happen once
    /// and are never edited, so an <c>UpdatedAt</c> beside them could only ever mislead. They are stamped
    /// here too, by the same clock, so a version and the message that produced it agree exactly — which is
    /// what a loop per entity type used to do three times over.
    /// </summary>
    private void StampEntities()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IHasNanoid>())
        {
            if (entry.State is EntityState.Added && string.IsNullOrEmpty(entry.Entity.Nanoid))
                entry.Entity.Nanoid = Nanoid.Generate();
        }

        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State is EntityState.Added)
                entry.Entity.CreatedAt = now;

            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<IHasCreatedAt>())
        {
            if (entry.State is EntityState.Added && entry.Entity.CreatedAt == default)
                entry.Entity.CreatedAt = now;
        }
    }

    // Only the `acceptAllChangesOnSuccess` overloads need overriding: the parameterless ones
    // delegate to these.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
