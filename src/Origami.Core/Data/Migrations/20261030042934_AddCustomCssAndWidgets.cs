using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomCssAndWidgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomCss",
                table: "Settings",
                type: "character varying(50000)",
                maxLength: 50000,
                nullable: false,
                defaultValue: "",
                comment: "自定义 CSS（保存前经危险构造清理）。");

            migrationBuilder.CreateTable(
                name: "SiteWidgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false, comment: "所属站点 ID（租户隔离键）。"),
                    Area = table.Column<int>(type: "integer", nullable: false, comment: "所属区域：侧栏或页尾。"),
                    Type = table.Column<int>(type: "integer", nullable: false, comment: "小工具类型（内置声明式）。"),
                    Title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "显示标题（空时用类型默认标题）。"),
                    ConfigJson = table.Column<string>(type: "character varying(24000)", maxLength: 24000, nullable: false, comment: "类型配置 JSON。"),
                    Position = table.Column<int>(type: "integer", nullable: false, comment: "排序位置（值越小越靠前）。"),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false, comment: "是否显示于前台。"),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, comment: "并发版本号（乐观并发控制）。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteWidgets", x => x.Id);
                    table.CheckConstraint("CK_SiteWidget_Area", "\"Area\" IN (0, 1)");
                    table.CheckConstraint("CK_SiteWidget_Type", "\"Type\" IN (0, 1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_SiteWidgets_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "站点小工具表：侧栏与页尾的声明式内容区块。");

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                column: "CustomCss",
                value: "");

            migrationBuilder.CreateIndex(
                name: "IX_SiteWidgets_SiteId_Area_Position_Id",
                table: "SiteWidgets",
                columns: new[] { "SiteId", "Area", "Position", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteWidgets");

            migrationBuilder.DropColumn(
                name: "CustomCss",
                table: "Settings");
        }
    }
}
