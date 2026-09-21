using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneDeploymentInFlightPerSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows first, or the index cannot be created on a database where this has already
            // happened — and it has: two clicks on Publish produced two live deployments, which is the whole
            // reason for the index. The newest survives and the rest are cancelled with a reason, because a
            // deployment row is what a person's publishing history shows and deleting one would make a publish
            // they watched disappear.
            migrationBuilder.Sql(
                """
                UPDATE "Deployments" AS d
                SET "Status" = 'Cancelled',
                    "FinishedAt" = NOW(),
                    "Error" = COALESCE(d."Error", 'Superseded by a newer publish.')
                WHERE d."Status" IN ('Queued', 'Preparing', 'Building')
                  AND d."Id" <> (
                      SELECT MAX(newest."Id") FROM "Deployments" AS newest
                      WHERE newest."SiteId" = d."SiteId"
                        AND newest."Status" IN ('Queued', 'Preparing', 'Building'));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_OneInFlightPerSite",
                table: "Deployments",
                column: "SiteId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Preparing', 'Building')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deployments_OneInFlightPerSite",
                table: "Deployments");
        }
    }
}
