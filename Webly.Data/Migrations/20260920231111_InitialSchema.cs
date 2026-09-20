using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Parts = table.Column<string>(type: "jsonb", nullable: true),
                    ProducedVersionId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    SiteVersionId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TriggeredByUserId = table.Column<int>(type: "integer", nullable: false),
                    ProviderDeploymentId = table.Column<string>(type: "text", nullable: true),
                    ProviderUrl = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    ErrorDetail = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Domains",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    Hostname = table.Column<string>(type: "text", nullable: false),
                    VerificationState = table.Column<string>(type: "text", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderDomainId = table.Column<string>(type: "text", nullable: true),
                    DnsRecordType = table.Column<string>(type: "text", nullable: true),
                    DnsRecordName = table.Column<string>(type: "text", nullable: true),
                    DnsRecordValue = table.Column<string>(type: "text", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalLogins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalLogins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SiteId = table.Column<int>(type: "integer", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ParentVersionId = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    ChangedFileCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    SourceMessageId = table.Column<int>(type: "integer", nullable: true),
                    RestoredFromVersionId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteVersions_ConversationMessages_SourceMessageId",
                        column: x => x.SourceMessageId,
                        principalTable: "ConversationMessages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SiteVersions_SiteVersions_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalTable: "SiteVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SiteVersions_SiteVersions_RestoredFromVersionId",
                        column: x => x.RestoredFromVersionId,
                        principalTable: "SiteVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OwnerId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    DefaultBranch = table.Column<string>(type: "text", nullable: false),
                    HeadVersionId = table.Column<int>(type: "integer", nullable: true),
                    PublishedVersionId = table.Column<int>(type: "integer", nullable: true),
                    ProviderProjectId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sites_SiteVersions_HeadVersionId",
                        column: x => x.HeadVersionId,
                        principalTable: "SiteVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Sites_SiteVersions_PublishedVersionId",
                        column: x => x.PublishedVersionId,
                        principalTable: "SiteVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nanoid = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    ProfilePictureUrl = table.Column<string>(type: "text", nullable: true),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    CurrentSiteId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Sites_CurrentSiteId",
                        column: x => x.CurrentSiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserSecurityTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSecurityTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSecurityTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_Sequence",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_Nanoid",
                table: "ConversationMessages",
                column: "Nanoid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ProducedVersionId",
                table: "ConversationMessages",
                column: "ProducedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_Nanoid",
                table: "Conversations",
                column: "Nanoid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_OneActivePerSite",
                table: "Conversations",
                column: "SiteId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId",
                table: "Conversations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_Nanoid",
                table: "Deployments",
                column: "Nanoid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_SiteId_CreatedAt",
                table: "Deployments",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_SiteVersionId",
                table: "Deployments",
                column: "SiteVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_TriggeredByUserId",
                table: "Deployments",
                column: "TriggeredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Domains_Hostname",
                table: "Domains",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_Nanoid",
                table: "Domains",
                column: "Nanoid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_OnePrimaryPerSite",
                table: "Domains",
                column: "SiteId",
                unique: true,
                filter: "\"IsPrimary\"");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLogins_Provider_ProviderKey",
                table: "ExternalLogins",
                columns: new[] { "Provider", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalLogins_UserId",
                table: "ExternalLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_CreatedByUserId",
                table: "SiteVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_Nanoid",
                table: "SiteVersions",
                column: "Nanoid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_ParentVersionId",
                table: "SiteVersions",
                column: "ParentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_RestoredFromVersionId",
                table: "SiteVersions",
                column: "RestoredFromVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_SiteId_CommitSha",
                table: "SiteVersions",
                columns: new[] { "SiteId", "CommitSha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_SiteId_CreatedAt",
                table: "SiteVersions",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteVersions_SourceMessageId",
                table: "SiteVersions",
                column: "SourceMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_HeadVersionId",
                table: "Sites",
                column: "HeadVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Nanoid",
                table: "Sites",
                column: "Nanoid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_OwnerId",
                table: "Sites",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_PublishedVersionId",
                table: "Sites",
                column: "PublishedVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSecurityTokens_TokenHash",
                table: "UserSecurityTokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserSecurityTokens_UserId",
                table: "UserSecurityTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CurrentSiteId",
                table: "Users",
                column: "CurrentSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Nanoid",
                table: "Users",
                column: "Nanoid",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMessages_Conversations_ConversationId",
                table: "ConversationMessages",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConversationMessages_SiteVersions_ProducedVersionId",
                table: "ConversationMessages",
                column: "ProducedVersionId",
                principalTable: "SiteVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Sites_SiteId",
                table: "Conversations",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Users_UserId",
                table: "Conversations",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_SiteVersions_SiteVersionId",
                table: "Deployments",
                column: "SiteVersionId",
                principalTable: "SiteVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_Sites_SiteId",
                table: "Deployments",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Deployments_Users_TriggeredByUserId",
                table: "Deployments",
                column: "TriggeredByUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Domains_Sites_SiteId",
                table: "Domains",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ExternalLogins_Users_UserId",
                table: "ExternalLogins",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_Users_UserId",
                table: "RefreshTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteVersions_Sites_SiteId",
                table: "SiteVersions",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteVersions_Users_CreatedByUserId",
                table: "SiteVersions",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Sites_Users_OwnerId",
                table: "Sites",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Everything below is the one thing EF cannot express, and it is not optional.
            //
            // Ten foreign keys in this schema are NO ACTION rather than CASCADE, because what they point at must
            // not be deleted out from under them: a version somebody is still publishing, the message that
            // explains a change, the account a change was made for. That is right, and on its own it makes
            // deleting an account impossible.
            //
            // Deleting a user cascades along several paths at once — sites → versions, conversations → messages,
            // deployments — and Postgres checks a NO ACTION constraint after each *triggered action*, not after
            // the whole statement. So whichever cascade runs second finds rows that the first has already
            // orphaned and refuses; an account that had ever published could never be removed. DEFERRABLE
            // INITIALLY DEFERRED moves every one of these checks to COMMIT, by which point all the cascades have
            // run and the graph is consistent again.
            //
            // They still refuse what they exist to refuse: a version another row points at cannot be deleted on
            // its own, because at COMMIT the reference would still be there. The deferral changes *when* the
            // question is asked, not the answer.
            //
            // WeblyDbContextTests.Deleting_a_user_removes_their_sites_and_everything_under_them is the canary.
            foreach (var (table, constraint) in new[]
            {
                ("SiteVersions", "FK_SiteVersions_ConversationMessages_SourceMessageId"),
                ("SiteVersions", "FK_SiteVersions_SiteVersions_ParentVersionId"),
                ("SiteVersions", "FK_SiteVersions_SiteVersions_RestoredFromVersionId"),
                ("SiteVersions", "FK_SiteVersions_Users_CreatedByUserId"),
                ("Sites", "FK_Sites_SiteVersions_HeadVersionId"),
                ("Sites", "FK_Sites_SiteVersions_PublishedVersionId"),
                ("ConversationMessages", "FK_ConversationMessages_SiteVersions_ProducedVersionId"),
                ("Conversations", "FK_Conversations_Users_UserId"),
                ("Deployments", "FK_Deployments_SiteVersions_SiteVersionId"),
                ("Deployments", "FK_Deployments_Users_TriggeredByUserId"),
            })
            {
                migrationBuilder.Sql(
                    $"""ALTER TABLE "{table}" ALTER CONSTRAINT "{constraint}" DEFERRABLE INITIALLY DEFERRED;""");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMessages_Conversations_ConversationId",
                table: "ConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_ConversationMessages_SiteVersions_ProducedVersionId",
                table: "ConversationMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Sites_SiteVersions_HeadVersionId",
                table: "Sites");

            migrationBuilder.DropForeignKey(
                name: "FK_Sites_SiteVersions_PublishedVersionId",
                table: "Sites");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Sites_CurrentSiteId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Domains");

            migrationBuilder.DropTable(
                name: "ExternalLogins");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "UserSecurityTokens");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "SiteVersions");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
