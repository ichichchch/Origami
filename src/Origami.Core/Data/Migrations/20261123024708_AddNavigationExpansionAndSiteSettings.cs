using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNavigationExpansionAndSiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteNavItem_Target",
                table: "SiteNavItems");

            migrationBuilder.AddColumn<string>(
                name: "AuthorSlug",
                table: "SiteNavItems",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                comment: "业务字段：AuthorSlug。");

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                comment: "业务字段：CategoryId。");

            migrationBuilder.AddColumn<int>(
                name: "Location",
                table: "SiteNavItems",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "业务字段：Location。");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                comment: "父级对象 ID。");

            migrationBuilder.AddColumn<Guid>(
                name: "TagId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                comment: "业务字段：TagId。");

            migrationBuilder.AddColumn<bool>(
                name: "ToArchives",
                table: "SiteNavItems",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "业务字段：ToArchives。");

            migrationBuilder.AddColumn<int>(
                name: "CommentMaxDepth",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 3,
                comment: "业务字段：CommentMaxDepth。");

            migrationBuilder.AddColumn<string>(
                name: "DefaultUiLanguage",
                table: "Settings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                comment: "业务字段：DefaultUiLanguage。");

            migrationBuilder.AddColumn<string>(
                name: "NotFoundBody",
                table: "Settings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "",
                comment: "业务字段：NotFoundBody。");

            migrationBuilder.AddColumn<string>(
                name: "NotFoundTitle",
                table: "Settings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                comment: "业务字段：NotFoundTitle。");

            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "Comments",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "业务字段：IsEdited。");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SiteNavItems_SiteId_Id",
                table: "SiteNavItems",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                columns: new[] { "CommentMaxDepth", "DefaultUiLanguage", "NotFoundBody", "NotFoundTitle" },
                values: new object[] { 3, "", "", "" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets",
                sql: "\"Type\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14)");

            migrationBuilder.CreateIndex(
                name: "IX_SiteNavItems_SiteId_ParentId",
                table: "SiteNavItems",
                columns: new[] { "SiteId", "ParentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteNavItem_Location",
                table: "SiteNavItems",
                sql: "\"Location\" IN (0, 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteNavItem_Target",
                table: "SiteNavItems",
                sql: "(\"ContentId\" IS NULL OR \"Url\" = '') AND ((\"ContentId\" IS NOT NULL)::int + (\"CategoryId\" IS NOT NULL)::int + (\"TagId\" IS NOT NULL)::int + \"ToArchives\"::int + (length(btrim(\"AuthorSlug\")) > 0)::int + (length(\"Url\") > 0)::int) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_SiteNavItems_SiteNavItems_SiteId_ParentId",
                table: "SiteNavItems",
                columns: new[] { "SiteId", "ParentId" },
                principalTable: "SiteNavItems",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteNavItems_SiteNavItems_SiteId_ParentId",
                table: "SiteNavItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SiteNavItems_SiteId_Id",
                table: "SiteNavItems");

            migrationBuilder.DropIndex(
                name: "IX_SiteNavItems_SiteId_ParentId",
                table: "SiteNavItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteNavItem_Location",
                table: "SiteNavItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteNavItem_Target",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "AuthorSlug",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "TagId",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "ToArchives",
                table: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "CommentMaxDepth",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DefaultUiLanguage",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotFoundBody",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotFoundTitle",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsEdited",
                table: "Comments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets",
                sql: "\"Type\" IN (0, 1, 2, 3, 4, 5, 6, 7, 8)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteNavItem_Target",
                table: "SiteNavItems",
                sql: "(\"ContentId\" IS NOT NULL AND \"Url\" = '') OR (\"ContentId\" IS NULL AND \"Url\" <> '')");
        }
    }
}
