using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchEngineBlocked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SearchEngineBlocked",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "业务字段：SearchEngineBlocked。");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                column: "SearchEngineBlocked",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchEngineBlocked",
                table: "Settings");
        }
    }
}
