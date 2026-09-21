using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webly.Data.Migrations
{
    /// <inheritdoc />
    public partial class SiteAddressReadyAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddressReadyAt",
                table: "Sites",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressReadyAt",
                table: "Sites");
        }
    }
}
