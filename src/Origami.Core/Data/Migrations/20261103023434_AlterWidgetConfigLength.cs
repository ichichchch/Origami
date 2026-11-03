using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlterWidgetConfigLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ConfigJson",
                table: "SiteWidgets",
                type: "character varying(64000)",
                maxLength: 64000,
                nullable: false,
                comment: "类型配置 JSON。",
                oldClrType: typeof(string),
                oldType: "character varying(24000)",
                oldMaxLength: 24000,
                oldComment: "类型配置 JSON。");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ConfigJson",
                table: "SiteWidgets",
                type: "character varying(24000)",
                maxLength: 24000,
                nullable: false,
                comment: "类型配置 JSON。",
                oldClrType: typeof(string),
                oldType: "character varying(64000)",
                oldMaxLength: 64000,
                oldComment: "类型配置 JSON。");
        }
    }
}
