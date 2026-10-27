using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentCoverImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Caption",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "CoverMediaId",
                table: "Contents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contents_CoverMediaId",
                table: "Contents",
                column: "CoverMediaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_MediaAssets_CoverMediaId",
                table: "Contents",
                column: "CoverMediaId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contents_MediaAssets_CoverMediaId",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_CoverMediaId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "Caption",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "CoverMediaId",
                table: "Contents");
        }
    }
}
