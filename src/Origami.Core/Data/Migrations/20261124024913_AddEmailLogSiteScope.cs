using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailLogSiteScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailLogs_CreatedAt",
                table: "EmailLogs");

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "EmailLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"),
                comment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_SiteId_CreatedAt",
                table: "EmailLogs",
                columns: new[] { "SiteId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_EmailLogs_Sites_SiteId",
                table: "EmailLogs",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailLogs_Sites_SiteId",
                table: "EmailLogs");

            migrationBuilder.DropIndex(
                name: "IX_EmailLogs_SiteId_CreatedAt",
                table: "EmailLogs");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "EmailLogs");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_CreatedAt",
                table: "EmailLogs",
                column: "CreatedAt");
        }
    }
}
