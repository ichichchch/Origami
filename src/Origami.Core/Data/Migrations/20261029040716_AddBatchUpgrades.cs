using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchUpgrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowComments",
                table: "Contents",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MenuOrder",
                table: "Contents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdminReply",
                table: "Comments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "Comments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "Categories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Comments_SiteId_Id",
                table: "Comments",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_SiteId_ParentId",
                table: "Comments",
                columns: new[] { "SiteId", "ParentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Comment_Parent",
                table: "Comments",
                sql: "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SiteId_ParentId",
                table: "Categories",
                columns: new[] { "SiteId", "ParentId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Category_Parent",
                table: "Categories",
                sql: "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Categories_SiteId_ParentId",
                table: "Categories",
                columns: new[] { "SiteId", "ParentId" },
                principalTable: "Categories",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Comments_SiteId_ParentId",
                table: "Comments",
                columns: new[] { "SiteId", "ParentId" },
                principalTable: "Comments",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Categories_SiteId_ParentId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Comments_SiteId_ParentId",
                table: "Comments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Comments_SiteId_Id",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_SiteId_ParentId",
                table: "Comments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Comment_Parent",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SiteId_ParentId",
                table: "Categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Category_Parent",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "AllowComments",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "MenuOrder",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "IsAdminReply",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "AspNetUsers");
        }
    }
}
