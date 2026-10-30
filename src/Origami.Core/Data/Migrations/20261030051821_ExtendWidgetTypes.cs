using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtendWidgetTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets",
                sql: "\"Type\" IN (0, 1, 2, 3, 4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteWidget_Type",
                table: "SiteWidgets",
                sql: "\"Type\" IN (0, 1, 2, 3, 4)");
        }
    }
}
