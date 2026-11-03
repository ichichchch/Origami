using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplacePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PackageId",
                table: "SiteWidgets",
                type: "uuid",
                nullable: true,
                comment: "来源受控包 ID（空为手动创建）。");

            migrationBuilder.AlterColumn<string>(
                name: "ThemePreset",
                table: "Settings",
                type: "character varying(46)",
                maxLength: 46,
                nullable: false,
                comment: "主题预设名称。",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "主题预设名称。");

            migrationBuilder.CreateTable(
                name: "SitePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false, comment: "所属站点 ID（租户隔离键）。"),
                    Kind = table.Column<int>(type: "integer", nullable: false, comment: "包种类：主题包或小组件包。"),
                    Slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, comment: "包识别代称（站内唯一）。"),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, comment: "名称。"),
                    ManifestJson = table.Column<string>(type: "character varying(200000)", maxLength: 200000, nullable: false, comment: "验证后标准化的声明 JSON。"),
                    Source = table.Column<int>(type: "integer", nullable: false, comment: "安装来源：内置目录或上传。"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, comment: "启用状态（主题包为现行版型镜像）。"),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "安装时间。"),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, comment: "并发版本号（乐观并发控制）。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SitePackages", x => x.Id);
                    table.UniqueConstraint("AK_SitePackages_SiteId_Id", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_SitePackage_Kind", "\"Kind\" IN (0, 1)");
                    table.CheckConstraint("CK_SitePackage_Slug", "length(btrim(\"Slug\")) > 0");
                    table.CheckConstraint("CK_SitePackage_Source", "\"Source\" IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_SitePackages_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "站点受控包表：主题包与小组件包的安装记录与声明。");

            migrationBuilder.CreateIndex(
                name: "IX_SiteWidgets_SiteId_PackageId",
                table: "SiteWidgets",
                columns: new[] { "SiteId", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_SitePackages_SiteId_Slug",
                table: "SitePackages",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SiteWidgets_SitePackages_SiteId_PackageId",
                table: "SiteWidgets",
                columns: new[] { "SiteId", "PackageId" },
                principalTable: "SitePackages",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SiteWidgets_SitePackages_SiteId_PackageId",
                table: "SiteWidgets");

            migrationBuilder.DropTable(
                name: "SitePackages");

            migrationBuilder.DropIndex(
                name: "IX_SiteWidgets_SiteId_PackageId",
                table: "SiteWidgets");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "SiteWidgets");

            migrationBuilder.AlterColumn<string>(
                name: "ThemePreset",
                table: "Settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "主题预设名称。",
                oldClrType: typeof(string),
                oldType: "character varying(46)",
                oldMaxLength: 46,
                oldComment: "主题预设名称。");
        }
    }
}
