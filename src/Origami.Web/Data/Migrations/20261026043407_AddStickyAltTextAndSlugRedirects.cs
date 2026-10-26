using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStickyAltTextAndSlugRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsSticky",
                table: "Contents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SlugRedirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OldSlug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlugRedirects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlugRedirects_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlugRedirects_ContentId",
                table: "SlugRedirects",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_SlugRedirects_OldSlug",
                table: "SlugRedirects",
                column: "OldSlug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlugRedirects");

            migrationBuilder.DropColumn(
                name: "AltText",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "IsSticky",
                table: "Contents");
        }
    }
}
