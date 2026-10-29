using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LogoMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                column: "LogoMediaId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_SiteId_LogoMediaId",
                table: "Settings",
                columns: new[] { "SiteId", "LogoMediaId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_MediaAssets_SiteId_LogoMediaId",
                table: "Settings",
                columns: new[] { "SiteId", "LogoMediaId" },
                principalTable: "MediaAssets",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Settings_MediaAssets_SiteId_LogoMediaId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_SiteId_LogoMediaId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "LogoMediaId",
                table: "Settings");
        }
    }
}
