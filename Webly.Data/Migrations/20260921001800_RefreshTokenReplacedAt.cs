using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenReplacedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplacedAt",
                table: "RefreshTokens");
        }
    }
}
