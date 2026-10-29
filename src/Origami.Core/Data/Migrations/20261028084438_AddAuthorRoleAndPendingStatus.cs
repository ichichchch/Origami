using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorRoleAndPendingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteMembership_Role",
                table: "SiteMemberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents");

            migrationBuilder.AddColumn<string>(
                name: "AuthorSlug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_AuthorSlug_Status",
                table: "Contents",
                columns: new[] { "SiteId", "AuthorSlug", "Status" });

            // 角色重編號：原 Editor=1／Administrator=2 整體 +1，空出 Author=1；
            // 既有 AuthorSlug 由應用程式於後續儲存時補齊，歷史內容以空代稱參與查詢。
            migrationBuilder.Sql(
                "UPDATE \"SiteMemberships\" SET \"Role\" = \"Role\" + 1 WHERE \"Role\" >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteMembership_Role",
                table: "SiteMemberships",
                sql: "\"Role\" IN (0, 1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents",
                sql: "\"Status\" IN (0, 2, 3) OR \"PublishedAt\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents",
                sql: "\"Status\" IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteMembership_Role",
                table: "SiteMemberships");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_AuthorSlug_Status",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "AuthorSlug",
                table: "Contents");

            // 回編號：Author(1) 先退回 Reader(0)，其餘整體 -1 還原舊枚舉值。
            migrationBuilder.Sql(
                "UPDATE \"SiteMemberships\" SET \"Role\" = 0 WHERE \"Role\" = 1;\n" +
                "UPDATE \"SiteMemberships\" SET \"Role\" = \"Role\" - 1 WHERE \"Role\" >= 2;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteMembership_Role",
                table: "SiteMemberships",
                sql: "\"Role\" IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents",
                sql: "\"Status\" IN (0, 2) OR \"PublishedAt\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents",
                sql: "\"Status\" IN (0, 1, 2)");
        }
    }
}
