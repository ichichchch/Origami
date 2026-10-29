using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentColor",
                table: "Settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardStyle",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReadingWidth",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShowByline",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowCover",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ThemePreset",
                table: "Settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                columns: new[] { "AccentColor", "CardStyle", "ReadingWidth", "ShowByline", "ShowCover", "ThemePreset" },
                values: new object[] { "#2f6f4e", "list", 68, true, true, "reading" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccentColor",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CardStyle",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReadingWidth",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ShowByline",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ShowCover",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ThemePreset",
                table: "Settings");
        }
    }
}
