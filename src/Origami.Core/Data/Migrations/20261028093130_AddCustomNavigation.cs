using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomNavigation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CustomNavigationEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SiteNavItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteNavItems", x => x.Id);
                    table.CheckConstraint("CK_SiteNavItem_Target", "(\"ContentId\" IS NOT NULL AND \"Url\" = '') OR (\"ContentId\" IS NULL AND \"Url\" <> '')");
                    table.CheckConstraint("CK_SiteNavItem_Title", "length(btrim(\"Title\")) > 0");
                    table.ForeignKey(
                        name: "FK_SiteNavItems_Contents_SiteId_ContentId",
                        columns: x => new { x.SiteId, x.ContentId },
                        principalTable: "Contents",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteNavItems_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                column: "CustomNavigationEnabled",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_SiteNavItems_SiteId_ContentId",
                table: "SiteNavItems",
                columns: new[] { "SiteId", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteNavItems_SiteId_Position_Id",
                table: "SiteNavItems",
                columns: new[] { "SiteId", "Position", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteNavItems");

            migrationBuilder.DropColumn(
                name: "CustomNavigationEnabled",
                table: "Settings");
        }
    }
}
