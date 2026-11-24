using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultCategoryId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "业务字段：DefaultCategoryId。");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultTagId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "业务字段：DefaultTagId。");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                columns: new[] { "DefaultCategoryId", "DefaultTagId" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultCategoryId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DefaultTagId",
                table: "Settings");
        }
    }
}
