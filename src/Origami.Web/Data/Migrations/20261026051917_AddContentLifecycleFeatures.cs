using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentLifecycleFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviewToken",
                table: "Contents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Markdown = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false),
                    Excerpt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentRevisions_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_PreviewToken",
                table: "Contents",
                column: "PreviewToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contents_Status",
                table: "Contents",
                column: "Status");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_DeletedAt",
                table: "Contents",
                sql: "(\"Status\" = 2) = (\"DeletedAt\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_PublishAt",
                table: "Contents",
                sql: "\"Status\" = 0 OR \"PublishAt\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents",
                sql: "\"Status\" IN (0, 2) OR \"PublishedAt\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents",
                sql: "\"Status\" IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ContentRevisions_ContentId_CreatedAt",
                table: "ContentRevisions",
                columns: new[] { "ContentId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentRevisions");

            migrationBuilder.DropIndex(
                name: "IX_Contents_PreviewToken",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_Status",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_DeletedAt",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_PublishAt",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "PreviewToken",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "PublishAt",
                table: "Contents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_PublishedAt",
                table: "Contents",
                sql: "\"Status\" = 0 OR \"PublishedAt\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Content_Status",
                table: "Contents",
                sql: "\"Status\" IN (0, 1)");
        }
    }
}
