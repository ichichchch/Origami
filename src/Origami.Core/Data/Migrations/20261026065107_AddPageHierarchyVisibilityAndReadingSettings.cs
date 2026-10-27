using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageHierarchyVisibilityAndReadingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Tags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FeedItems",
                table: "Settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HomePageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PostsPerPage",
                table: "Settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteIconMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Contents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "Contents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FeedItems", "HomePageContentId", "PostsPerPage", "SiteIconMediaId" },
                values: new object[] { null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_HomePageContentId",
                table: "Settings",
                column: "HomePageContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_SiteIconMediaId",
                table: "Settings",
                column: "SiteIconMediaId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Settings_FeedItems",
                table: "Settings",
                sql: "\"FeedItems\" IS NULL OR \"FeedItems\" BETWEEN 1 AND 50");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Settings_PostsPerPage",
                table: "Settings",
                sql: "\"PostsPerPage\" IS NULL OR \"PostsPerPage\" BETWEEN 1 AND 50");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_ParentId",
                table: "Contents",
                column: "ParentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_Parent",
                table: "Contents",
                sql: "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_Private",
                table: "Contents",
                sql: "NOT (\"IsProtected\" AND \"IsPrivate\")");

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_Contents_ParentId",
                table: "Contents",
                column: "ParentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_Contents_HomePageContentId",
                table: "Settings",
                column: "HomePageContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_MediaAssets_SiteIconMediaId",
                table: "Settings",
                column: "SiteIconMediaId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contents_Contents_ParentId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_Contents_HomePageContentId",
                table: "Settings");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_MediaAssets_SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_HomePageContentId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Settings_FeedItems",
                table: "Settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Settings_PostsPerPage",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Contents_ParentId",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_Parent",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_Private",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "FeedItems",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HomePageContentId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PostsPerPage",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Categories");
        }
    }
}
