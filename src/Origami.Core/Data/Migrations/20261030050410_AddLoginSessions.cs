using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "主键 ID。"),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false, comment: "用户 ID。"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "创建时间。"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "业务字段：ExpiresAt。"),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "业务字段：RevokedAt。"),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "业务字段：UserAgent。"),
                    RemoteIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "业务字段：RemoteIp。"),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "业务字段：Provider。")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginSessions", x => x.Id);
                    table.CheckConstraint("CK_LoginSession_Time", "\"CreatedAt\" <= \"ExpiresAt\"");
                },
                comment: "业务表：LoginSessions。");

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_ExpiresAt",
                table: "LoginSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_UserId",
                table: "LoginSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginSessions");
        }
    }
}
