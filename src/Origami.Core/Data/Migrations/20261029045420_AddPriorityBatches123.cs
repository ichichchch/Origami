using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPriorityBatches123 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields");

            migrationBuilder.AddColumn<string>(
                name: "ColorScheme",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CommentBlockedWords",
                table: "Settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CommentCloseDays",
                table: "Settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FooterText",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MaintenanceMessage",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "MaintenanceMode",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SocialGithubUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SocialMastodonUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SocialXUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "Settings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Options",
                table: "CustomFields",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecoveryCodeHashes",
                table: "AspNetUsers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Target = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                columns: new[] { "ColorScheme", "CommentBlockedWords", "CommentCloseDays", "FooterText", "MaintenanceMessage", "MaintenanceMode", "SocialGithubUrl", "SocialMastodonUrl", "SocialXUrl", "WebhookSecret", "WebhookUrl" },
                values: new object[] { "auto", "", 0, "", "", false, "", "", "", "", "" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields",
                sql: "\"FieldType\" IN ('text', 'longtext', 'number', 'date', 'checkbox', 'url', 'select', 'media')");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_SiteId_CreatedAt_Id",
                table: "AuditLogs",
                columns: new[] { "SiteId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "ColorScheme",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CommentBlockedWords",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CommentCloseDays",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "FooterText",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MaintenanceMessage",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MaintenanceMode",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SocialGithubUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SocialMastodonUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SocialXUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WebhookSecret",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Options",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeHashes",
                table: "AspNetUsers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomField_Type",
                table: "CustomFields",
                sql: "\"FieldType\" IN ('text', 'longtext', 'number', 'date', 'checkbox')");
        }
    }
}
