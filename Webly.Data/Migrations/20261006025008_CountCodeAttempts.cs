using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <summary>
    /// Renames <c>UserSecurityTokens.FailedAttempts</c> to <c>CodeAttempts</c>, because it now counts every try at
    /// a code, the right one included: a try is claimed before it is compared, which is the only order in which a
    /// burst of guesses cannot all read the same count. The values carry over as they are — every try they record
    /// was a wrong one, which is still a try.
    /// </summary>
    public partial class CountCodeAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FailedAttempts",
                table: "UserSecurityTokens",
                newName: "CodeAttempts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CodeAttempts",
                table: "UserSecurityTokens",
                newName: "FailedAttempts");
        }
    }
}
