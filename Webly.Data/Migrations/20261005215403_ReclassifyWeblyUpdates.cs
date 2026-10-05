using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <summary>
    /// The versions Webly committed to keep its own files current — "Updated the editing instructions" and its
    /// siblings — move from <c>Template</c> to the new <c>Webly</c> origin, which is what <c>SyncWeblyOwnedFiles</c>
    /// writes from now on. A site's first commit is the only version without a parent, so "Template with a parent"
    /// is exactly the set that was misfiled; no schema changes, because the origin is stored as its name.
    /// </summary>
    public partial class ReclassifyWeblyUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"SiteVersions\" SET \"Origin\" = 'Webly' WHERE \"Origin\" = 'Template' AND \"ParentVersionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"SiteVersions\" SET \"Origin\" = 'Template' WHERE \"Origin\" = 'Webly'");
        }
    }
}
