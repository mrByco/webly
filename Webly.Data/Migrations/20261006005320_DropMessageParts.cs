using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <summary>
    /// Drops <c>ConversationMessages.Parts</c>, which nothing ever wrote or read: documented as what let a reload
    /// redraw a turn's activity, it was null in every row. The data the warning above <c>DropColumn</c> is about is
    /// not there — 121 of 121 rows null in the development database it was checked against.
    /// </summary>
    public partial class DropMessageParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Parts",
                table: "ConversationMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Parts",
                table: "ConversationMessages",
                type: "jsonb",
                nullable: true);
        }
    }
}
