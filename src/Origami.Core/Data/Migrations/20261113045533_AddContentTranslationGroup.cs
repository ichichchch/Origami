using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentTranslationGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Contents",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                comment: "内容语言键（如 zh-Hant、en）。");

            migrationBuilder.AddColumn<Guid>(
                name: "TranslationGroupId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "平权翻译组标识；同组内容互为译本。");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_TranslationGroupId",
                table: "Contents",
                columns: new[] { "SiteId", "TranslationGroupId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_TranslationGroupId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "TranslationGroupId",
                table: "Contents");
        }
    }
}
