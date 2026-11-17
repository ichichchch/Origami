using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddViewCountsTaxonomyOrderIndexNow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Tags",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "业务字段：SortOrder。");

            migrationBuilder.AddColumn<bool>(
                name: "IndexNowEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "业务字段：IndexNowEnabled。");

            migrationBuilder.AddColumn<string>(
                name: "IndexNowKey",
                table: "Settings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                comment: "业务字段：IndexNowKey。");

            migrationBuilder.AddColumn<bool>(
                name: "ShowViewCounts",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "业务字段：ShowViewCounts。");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Categories",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "业务字段：SortOrder。");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                columns: new[] { "IndexNowEnabled", "IndexNowKey", "ShowViewCounts" },
                values: new object[] { false, "", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "IndexNowEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IndexNowKey",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ShowViewCounts",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Categories");
        }
    }
}
