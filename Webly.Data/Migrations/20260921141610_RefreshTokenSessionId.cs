using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <summary>
    /// Which sign-in a refresh token belongs to, carried across every rotation — see
    /// <c>RefreshToken.SessionId</c>. It is what lets a realtime connection be matched to the session that
    /// opened it, so that signing out can end it.
    /// </summary>
    public partial class RefreshTokenSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "RefreshTokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            // A distinct value per existing row rather than the empty string the column defaults to.
            // Sessions that predate this column cannot be matched to a socket either way — their access
            // tokens carry no `sid` — but leaving them all sharing one id would say something false about
            // them, and the first thing to read the column expecting it to identify something would be
            // wrong rather than unlucky.
            migrationBuilder.Sql(
                "UPDATE \"RefreshTokens\" SET \"SessionId\" = md5(random()::text || clock_timestamp()::text || \"Id\"::text) WHERE \"SessionId\" = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "RefreshTokens");
        }
    }
}
