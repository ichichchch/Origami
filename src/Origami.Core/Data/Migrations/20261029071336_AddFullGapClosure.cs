using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFullGapClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "yyyy.MM.dd");

            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PermalinkScheme",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "post-name");

            migrationBuilder.AddColumn<Guid>(
                name: "PrivacyPageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "HH:mm");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotJson",
                table: "ContentRevisions",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Event = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PayloadJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    Signature = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Settings",
                keyColumns: new[] { "Id", "SiteId" },
                keyValues: new object[] { 1, new Guid("11111111-1111-1111-1111-111111111111") },
                columns: new[] { "DateFormat", "EmailNotificationsEnabled", "PermalinkScheme", "PrivacyPageContentId", "TimeFormat", "TimeZoneId" },
                values: new object[] { "yyyy.MM.dd", false, "post-name", null, "HH:mm", "UTC" });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_SiteId_PrivacyPageContentId",
                table: "Settings",
                columns: new[] { "SiteId", "PrivacyPageContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_SiteId_CreatedAt_Id",
                table: "WebhookDeliveries",
                columns: new[] { "SiteId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_SiteId_Status_NextAttemptAt",
                table: "WebhookDeliveries",
                columns: new[] { "SiteId", "Status", "NextAttemptAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_Contents_SiteId_PrivacyPageContentId",
                table: "Settings",
                columns: new[] { "SiteId", "PrivacyPageContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Settings_Contents_SiteId_PrivacyPageContentId",
                table: "Settings");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropIndex(
                name: "IX_Settings_SiteId_PrivacyPageContentId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DateFormat",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PermalinkScheme",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PrivacyPageContentId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TimeFormat",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SnapshotJson",
                table: "ContentRevisions");
        }
    }
}
