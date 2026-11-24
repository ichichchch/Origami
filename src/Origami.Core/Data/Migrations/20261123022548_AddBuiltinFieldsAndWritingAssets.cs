using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBuiltinFieldsAndWritingAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomFields",
                type: "uuid",
                nullable: true,
                comment: "业务字段：ContentTypeId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：ContentTypeId。");

            migrationBuilder.AddColumn<int>(
                name: "BuiltinKind",
                table: "CustomFields",
                type: "integer",
                nullable: true,
                comment: "业务字段：BuiltinKind。");

            migrationBuilder.CreateTable(
                name: "ContentSnippets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false, comment: "所属站点 ID（租户隔离键）。"),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "名称。"),
                    Markdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false, comment: "Markdown 正文。"),
                    Position = table.Column<int>(type: "integer", nullable: false, comment: "排序位置（值越小越靠前）。"),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, comment: "并发版本号（乐观并发控制）。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentSnippets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentSnippets_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "业务表：ContentSnippets。");

            migrationBuilder.CreateTable(
                name: "ContentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false, comment: "所属站点 ID（租户隔离键）。"),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "名称。"),
                    Kind = table.Column<int>(type: "integer", nullable: false, comment: "类型枚举值。"),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: true, comment: "业务字段：ContentTypeId。"),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "标题。"),
                    Markdown = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false, comment: "Markdown 正文。"),
                    Position = table.Column<int>(type: "integer", nullable: false, comment: "排序位置（值越小越靠前）。"),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, comment: "并发版本号（乐观并发控制）。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTemplates", x => x.Id);
                    table.CheckConstraint("CK_ContentTemplate_Kind", "\"Kind\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_ContentTemplates_ContentTypes_SiteId_ContentTypeId",
                        columns: x => new { x.SiteId, x.ContentTypeId },
                        principalTable: "ContentTypes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentTemplates_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "业务表：ContentTemplates。");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_SiteId_BuiltinKind_Key",
                table: "CustomFields",
                columns: new[] { "SiteId", "BuiltinKind", "Key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomField_Owner",
                table: "CustomFields",
                sql: "(\"ContentTypeId\" IS NULL) <> (\"BuiltinKind\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields",
                sql: "\"FieldType\" IN ('text', 'longtext', 'number', 'date', 'checkbox', 'url', 'select', 'media', 'email', 'datetime', 'gallery', 'relation')");

            migrationBuilder.CreateIndex(
                name: "IX_ContentSnippets_SiteId",
                table: "ContentSnippets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentTemplates_SiteId_ContentTypeId",
                table: "ContentTemplates",
                columns: new[] { "SiteId", "ContentTypeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 內建文章／頁面的欄位在舊結構沒有對應欄位（所屬內容類型不可為空）：降版前連同其值一併移除。
            migrationBuilder.Sql("DELETE FROM \"ContentFieldValues\" WHERE \"FieldId\" IN (SELECT \"Id\" FROM \"CustomFields\" WHERE \"ContentTypeId\" IS NULL)");
            migrationBuilder.Sql("DELETE FROM \"CustomFields\" WHERE \"ContentTypeId\" IS NULL");

            migrationBuilder.DropTable(
                name: "ContentSnippets");

            migrationBuilder.DropTable(
                name: "ContentTemplates");

            migrationBuilder.DropIndex(
                name: "IX_CustomFields_SiteId_BuiltinKind_Key",
                table: "CustomFields");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomField_Owner",
                table: "CustomFields");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "BuiltinKind",
                table: "CustomFields");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "业务字段：ContentTypeId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "业务字段：ContentTypeId。");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields",
                sql: "\"FieldType\" IN ('text', 'longtext', 'number', 'date', 'checkbox', 'url', 'select', 'media')");
        }
    }
}
