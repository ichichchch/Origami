using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountEmailAndLoginLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "ContentTemplates",
                comment: "内容模板表：新建内容时套用的 Markdown 骨架。",
                oldComment: "业务表：ContentTemplates。");

            migrationBuilder.AlterTable(
                name: "ContentSnippets",
                comment: "文字片段表：编辑器可重复插入的 Markdown 片段。",
                oldComment: "业务表：ContentSnippets。");

            migrationBuilder.CreateTable(
                name: "EmailLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    Recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "业务字段：Recipient。"),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "业务字段：Subject。"),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false, comment: "业务字段：Succeeded。"),
                    Error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "业务字段：Error。"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "创建时间。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLogs", x => x.Id);
                },
                comment: "寄信纪录表：对外邮件的收件人、主旨与结果。");

            migrationBuilder.CreateTable(
                name: "LoginEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "用户 ID。"),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "业务字段：UserName。"),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false, comment: "业务字段：Succeeded。"),
                    RemoteIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "业务字段：RemoteIp。"),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "业务字段：UserAgent。"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "创建时间。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginEvents", x => x.Id);
                },
                comment: "登录事件表：账号登录成功与失败的摘要。");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_CreatedAt",
                table: "EmailLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginEvents_UserId_CreatedAt",
                table: "LoginEvents",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailLogs");

            migrationBuilder.DropTable(
                name: "LoginEvents");

            migrationBuilder.AlterTable(
                name: "ContentTemplates",
                comment: "业务表：ContentTemplates。",
                oldComment: "内容模板表：新建内容时套用的 Markdown 骨架。");

            migrationBuilder.AlterTable(
                name: "ContentSnippets",
                comment: "业务表：ContentSnippets。",
                oldComment: "文字片段表：编辑器可重复插入的 Markdown 片段。");
        }
    }
}
