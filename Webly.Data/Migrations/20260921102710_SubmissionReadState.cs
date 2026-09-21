using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Everything that already exists comes out unread, and that is deliberate rather than a default
            // nobody chose: until this migration there was no way for an owner to say they had dealt with a
            // message, so claiming on their behalf that they had would be the one direction of this change
            // that cannot be undone by looking.
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "FormSubmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormSubmissions_Unread",
                table: "FormSubmissions",
                column: "SiteId",
                filter: "\"ReadAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormSubmissions_Unread",
                table: "FormSubmissions");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "FormSubmissions");
        }
    }
}
