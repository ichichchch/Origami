using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChineseComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "WebhookDeliveries",
                comment: "数据表：WebhookDeliveries");

            migrationBuilder.AlterTable(
                name: "Tags",
                comment: "数据表：Tags");

            migrationBuilder.AlterTable(
                name: "SlugRedirects",
                comment: "数据表：SlugRedirects");

            migrationBuilder.AlterTable(
                name: "Sites",
                comment: "数据表：Sites");

            migrationBuilder.AlterTable(
                name: "SiteNavItems",
                comment: "数据表：SiteNavItems");

            migrationBuilder.AlterTable(
                name: "SiteMemberships",
                comment: "数据表：SiteMemberships");

            migrationBuilder.AlterTable(
                name: "Settings",
                comment: "数据表：Settings");

            migrationBuilder.AlterTable(
                name: "MediaAssets",
                comment: "数据表：MediaAssets");

            migrationBuilder.AlterTable(
                name: "EmbeddingJobs",
                comment: "数据表：EmbeddingJobs");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomyTerms",
                comment: "数据表：CustomTaxonomyTerms");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomies",
                comment: "数据表：CustomTaxonomies");

            migrationBuilder.AlterTable(
                name: "CustomFields",
                comment: "数据表：CustomFields");

            migrationBuilder.AlterTable(
                name: "ContentViews",
                comment: "数据表：ContentViews");

            migrationBuilder.AlterTable(
                name: "ContentTypes",
                comment: "数据表：ContentTypes");

            migrationBuilder.AlterTable(
                name: "ContentTags",
                comment: "数据表：ContentTags");

            migrationBuilder.AlterTable(
                name: "Contents",
                comment: "数据表：Contents");

            migrationBuilder.AlterTable(
                name: "ContentRevisions",
                comment: "数据表：ContentRevisions");

            migrationBuilder.AlterTable(
                name: "ContentFieldValues",
                comment: "数据表：ContentFieldValues");

            migrationBuilder.AlterTable(
                name: "ContentEmbeddings",
                comment: "数据表：ContentEmbeddings");

            migrationBuilder.AlterTable(
                name: "ContentCustomTerms",
                comment: "数据表：ContentCustomTerms");

            migrationBuilder.AlterTable(
                name: "ContentCategories",
                comment: "数据表：ContentCategories");

            migrationBuilder.AlterTable(
                name: "Comments",
                comment: "数据表：Comments");

            migrationBuilder.AlterTable(
                name: "Categories",
                comment: "数据表：Categories");

            migrationBuilder.AlterTable(
                name: "AuditLogs",
                comment: "数据表：AuditLogs");

            migrationBuilder.AlterTable(
                name: "AspNetUserTokens",
                comment: "数据表：AspNetUserTokens");

            migrationBuilder.AlterTable(
                name: "AspNetUsers",
                comment: "数据表：AspNetUsers");

            migrationBuilder.AlterTable(
                name: "AspNetUserRoles",
                comment: "数据表：AspNetUserRoles");

            migrationBuilder.AlterTable(
                name: "AspNetUserLogins",
                comment: "数据表：AspNetUserLogins");

            migrationBuilder.AlterTable(
                name: "AspNetUserClaims",
                comment: "数据表：AspNetUserClaims");

            migrationBuilder.AlterTable(
                name: "AspNetRoles",
                comment: "数据表：AspNetRoles");

            migrationBuilder.AlterTable(
                name: "AspNetRoleClaims",
                comment: "数据表：AspNetRoleClaims");

            migrationBuilder.AlterTable(
                name: "ApiCredentials",
                comment: "数据表：ApiCredentials");

            migrationBuilder.AlterColumn<string>(
                name: "TargetUrl",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：TargetUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                comment: "字段：Status",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Signature",
                table: "WebhookDeliveries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Signature",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "WebhookDeliveries",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                comment: "字段：PayloadJson",
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：NextAttemptAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LastStatusCode",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: true,
                comment: "字段：LastStatusCode",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：LastError",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Event",
                table: "WebhookDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：Event",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：DeliveredAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                comment: "字段：Attempts",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tags",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tags",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "OldSlug",
                table: "SlugRedirects",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：OldSlug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "SlugRedirects",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Sites",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Sites",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Slug",
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Sites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Sites",
                type: "boolean",
                nullable: false,
                comment: "字段：IsActive",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "HostName",
                table: "Sites",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true,
                comment: "字段：HostName",
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(253)",
                oldMaxLength: 253,
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Sites",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Sites",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "SiteNavItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Url",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "SiteNavItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Title",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "SiteNavItems",
                type: "integer",
                nullable: false,
                comment: "字段：Position",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "IsVisible",
                table: "SiteNavItems",
                type: "boolean",
                nullable: false,
                comment: "字段：IsVisible",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "SiteMemberships",
                type: "integer",
                nullable: false,
                comment: "字段：Role",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "SiteMemberships",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：DisplayName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "SiteMemberships",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookUrl",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：WebhookUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "WebhookSecret",
                table: "Settings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "字段：WebhookSecret",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Settings",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：TimeZoneId",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "TimeFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "字段：TimeFormat",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "ThemePreset",
                table: "Settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "字段：ThemePreset",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "SocialXUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：SocialXUrl",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "SocialMastodonUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：SocialMastodonUrl",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "SocialGithubUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：SocialGithubUrl",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteIconMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：SiteIconMediaId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCover",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：ShowCover",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowByline",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：ShowByline",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<int>(
                name: "ReadingWidth",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "字段：ReadingWidth",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrivacyPageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：PrivacyPageContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PostsPerPage",
                table: "Settings",
                type: "integer",
                nullable: true,
                comment: "字段：PostsPerPage",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PermalinkScheme",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "字段：PermalinkScheme",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<bool>(
                name: "MaintenanceMode",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：MaintenanceMode",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "MaintenanceMessage",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：MaintenanceMessage",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "LogoMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：LogoMediaId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "HomePageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：HomePageContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FooterText",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：FooterText",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<bool>(
                name: "FeedShowFullText",
                table: "Settings",
                type: "boolean",
                nullable: true,
                comment: "字段：FeedShowFullText",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FeedItems",
                table: "Settings",
                type: "integer",
                nullable: true,
                comment: "字段：FeedItems",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：EmailNotificationsEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "DateFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "字段：DateFormat",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<bool>(
                name: "CustomNavigationEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：CustomNavigationEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：CommentsEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<int>(
                name: "CommentCloseDays",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "字段：CommentCloseDays",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "CommentBlockedWords",
                table: "Settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "字段：CommentBlockedWords",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<bool>(
                name: "CommentAutoApprove",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：CommentAutoApprove",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "ColorScheme",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "字段：ColorScheme",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "CardStyle",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "字段：CardStyle",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorDisplayName",
                table: "Settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：AuthorDisplayName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "AccentColor",
                table: "Settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                comment: "字段：AccentColor",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Settings",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Width",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "字段：Width",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UploadedAt",
                table: "MediaAssets",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：UploadedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<long>(
                name: "Size",
                table: "MediaAssets",
                type: "bigint",
                nullable: false,
                comment: "字段：Size",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalName",
                table: "MediaAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "字段：OriginalName",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "字段：Kind",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "Height",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "字段：Height",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "MediaAssets",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：FileName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "MediaAssets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "字段：ContentType",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "Caption",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：Caption",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<string>(
                name: "AltText",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：AltText",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：NextAttemptAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EnqueuedAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：EnqueuedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "EmbeddingJobs",
                type: "integer",
                nullable: false,
                comment: "字段：Attempts",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaxonomyId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：TaxonomyId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomyTerms",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomyTerms",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomyTerms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomies",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<bool>(
                name: "IsHierarchical",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                comment: "字段：IsHierarchical",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                comment: "字段：IsArchived",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentTypeId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "CustomFields",
                type: "integer",
                nullable: false,
                comment: "字段：Position",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                table: "CustomFields",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Options",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomFields",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "CustomFields",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Key",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<bool>(
                name: "IsRequired",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                comment: "字段：IsRequired",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                comment: "字段：IsArchived",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "FieldType",
                table: "CustomFields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "字段：FieldType",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentTypeId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<int>(
                name: "Count",
                table: "ContentViews",
                type: "integer",
                nullable: false,
                comment: "字段：Count",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Date",
                table: "ContentViews",
                type: "date",
                nullable: false,
                comment: "字段：Date",
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsTags",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：SupportsTags",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsHierarchy",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：SupportsHierarchy",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsCategories",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：SupportsCategories",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：IsArchived",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ContentTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentTypes",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TagsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "字段：TagsId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentsId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：UpdatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Contents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "字段：Title",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "字段：Status",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：PublishedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：PublishAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProtectionHash",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：ProtectionHash",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "PreviewToken",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：PreviewToken",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "NoIndex",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：NoIndex",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "MetaDescription",
                table: "Contents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：MetaDescription",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);

            migrationBuilder.AlterColumn<int>(
                name: "MenuOrder",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "字段：MenuOrder",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Contents",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                comment: "字段：Markdown",
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000);

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "字段：Kind",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSticky",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：IsSticky",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "IsProtected",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：IsProtected",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "IsPrivate",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：IsPrivate",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Excerpt",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "EditingByName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：EditingByName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "EditingById",
                table: "Contents",
                type: "text",
                nullable: true,
                comment: "字段：EditingById",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EditingAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：EditingAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：DeletedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoverMediaId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：CoverMediaId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：ContentTypeId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorSlug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：AuthorSlug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：AuthorName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "AuthorId",
                table: "Contents",
                type: "text",
                nullable: true,
                comment: "字段：AuthorId",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "AllowComments",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：AllowComments",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "ContentRevisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "字段：Title",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "SnapshotJson",
                table: "ContentRevisions",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                comment: "字段：SnapshotJson",
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "ContentRevisions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                comment: "字段：Markdown",
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000);

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "ContentRevisions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Excerpt",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentRevisions",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "ContentFieldValues",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "字段：Value",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "FieldId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：FieldId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "ContentEmbeddings",
                type: "vector(1536)",
                nullable: false,
                comment: "字段：Embedding",
                oldClrType: typeof(Vector),
                oldType: "vector(1536)");

            migrationBuilder.AlterColumn<string>(
                name: "ChunkText",
                table: "ContentEmbeddings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "字段：ChunkText",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<int>(
                name: "ChunkIndex",
                table: "ContentEmbeddings",
                type: "integer",
                nullable: false,
                comment: "字段：ChunkIndex",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TermId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：TermId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentsId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoriesId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "字段：CategoriesId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Comments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Url",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Comments",
                type: "integer",
                nullable: false,
                comment: "字段：Status",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Comments",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Comments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "字段：Markdown",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<bool>(
                name: "IsAdminReply",
                table: "Comments",
                type: "boolean",
                nullable: false,
                comment: "字段：IsAdminReply",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Comments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Comments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：AuthorName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Categories",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Categories",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "字段：UserName",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AuditLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Target",
                table: "AuditLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Target",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：Action",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "AspNetUserTokens",
                type: "text",
                nullable: true,
                comment: "字段：Value",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "字段：LoginProvider",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "WebsiteUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：WebsiteUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "字段：UserName",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TwoFactorSecret",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                comment: "字段：TwoFactorSecret",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<bool>(
                name: "TwoFactorEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：TwoFactorEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<int>(
                name: "SingletonKey",
                table: "AspNetUsers",
                type: "integer",
                nullable: true,
                comment: "字段：SingletonKey",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：SecurityStamp",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RecoveryCodeHashes",
                table: "AspNetUsers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                comment: "字段：RecoveryCodeHashes",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AlterColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：PhoneNumberConfirmed",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：PhoneNumber",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：PasswordHash",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUserName",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "字段：NormalizedUserName",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "字段：NormalizedEmail",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：LockoutEnd",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "LockoutEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：LockoutEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDisabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：IsDisabled",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：EmailConfirmed",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "字段：Email",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：ConcurrencyStamp",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Bio",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：AvatarUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<int>(
                name: "AccessFailedCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                comment: "字段：AccessFailedCount",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                comment: "字段：RoleId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderDisplayName",
                table: "AspNetUserLogins",
                type: "text",
                nullable: true,
                comment: "字段：ProviderDisplayName",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "字段：ProviderKey",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "字段：LoginProvider",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimValue",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimType",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetUserClaims",
                type: "integer",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "AspNetRoles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "字段：NormalizedName",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetRoles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetRoles",
                type: "text",
                nullable: true,
                comment: "字段：ConcurrencyStamp",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: false,
                comment: "字段：RoleId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimValue",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimType",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetRoleClaims",
                type: "integer",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ApiCredentials",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "ApiCredentials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "字段：SecurityStamp",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                table: "ApiCredentials",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：SecretHash",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<int>(
                name: "Scope",
                table: "ApiCredentials",
                type: "integer",
                nullable: false,
                comment: "字段：Scope",
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：RevokedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ApiCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：ExpiresAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "WebhookDeliveries",
                oldComment: "数据表：WebhookDeliveries");

            migrationBuilder.AlterTable(
                name: "Tags",
                oldComment: "数据表：Tags");

            migrationBuilder.AlterTable(
                name: "SlugRedirects",
                oldComment: "数据表：SlugRedirects");

            migrationBuilder.AlterTable(
                name: "Sites",
                oldComment: "数据表：Sites");

            migrationBuilder.AlterTable(
                name: "SiteNavItems",
                oldComment: "数据表：SiteNavItems");

            migrationBuilder.AlterTable(
                name: "SiteMemberships",
                oldComment: "数据表：SiteMemberships");

            migrationBuilder.AlterTable(
                name: "Settings",
                oldComment: "数据表：Settings");

            migrationBuilder.AlterTable(
                name: "MediaAssets",
                oldComment: "数据表：MediaAssets");

            migrationBuilder.AlterTable(
                name: "EmbeddingJobs",
                oldComment: "数据表：EmbeddingJobs");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomyTerms",
                oldComment: "数据表：CustomTaxonomyTerms");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomies",
                oldComment: "数据表：CustomTaxonomies");

            migrationBuilder.AlterTable(
                name: "CustomFields",
                oldComment: "数据表：CustomFields");

            migrationBuilder.AlterTable(
                name: "ContentViews",
                oldComment: "数据表：ContentViews");

            migrationBuilder.AlterTable(
                name: "ContentTypes",
                oldComment: "数据表：ContentTypes");

            migrationBuilder.AlterTable(
                name: "ContentTags",
                oldComment: "数据表：ContentTags");

            migrationBuilder.AlterTable(
                name: "Contents",
                oldComment: "数据表：Contents");

            migrationBuilder.AlterTable(
                name: "ContentRevisions",
                oldComment: "数据表：ContentRevisions");

            migrationBuilder.AlterTable(
                name: "ContentFieldValues",
                oldComment: "数据表：ContentFieldValues");

            migrationBuilder.AlterTable(
                name: "ContentEmbeddings",
                oldComment: "数据表：ContentEmbeddings");

            migrationBuilder.AlterTable(
                name: "ContentCustomTerms",
                oldComment: "数据表：ContentCustomTerms");

            migrationBuilder.AlterTable(
                name: "ContentCategories",
                oldComment: "数据表：ContentCategories");

            migrationBuilder.AlterTable(
                name: "Comments",
                oldComment: "数据表：Comments");

            migrationBuilder.AlterTable(
                name: "Categories",
                oldComment: "数据表：Categories");

            migrationBuilder.AlterTable(
                name: "AuditLogs",
                oldComment: "数据表：AuditLogs");

            migrationBuilder.AlterTable(
                name: "AspNetUserTokens",
                oldComment: "数据表：AspNetUserTokens");

            migrationBuilder.AlterTable(
                name: "AspNetUsers",
                oldComment: "数据表：AspNetUsers");

            migrationBuilder.AlterTable(
                name: "AspNetUserRoles",
                oldComment: "数据表：AspNetUserRoles");

            migrationBuilder.AlterTable(
                name: "AspNetUserLogins",
                oldComment: "数据表：AspNetUserLogins");

            migrationBuilder.AlterTable(
                name: "AspNetUserClaims",
                oldComment: "数据表：AspNetUserClaims");

            migrationBuilder.AlterTable(
                name: "AspNetRoles",
                oldComment: "数据表：AspNetRoles");

            migrationBuilder.AlterTable(
                name: "AspNetRoleClaims",
                oldComment: "数据表：AspNetRoleClaims");

            migrationBuilder.AlterTable(
                name: "ApiCredentials",
                oldComment: "数据表：ApiCredentials");

            migrationBuilder.AlterColumn<string>(
                name: "TargetUrl",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：TargetUrl");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Status");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Signature",
                table: "WebhookDeliveries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Signature");

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "WebhookDeliveries",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536,
                oldComment: "字段：PayloadJson");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：NextAttemptAt");

            migrationBuilder.AlterColumn<int>(
                name: "LastStatusCode",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "字段：LastStatusCode");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：LastError");

            migrationBuilder.AlterColumn<string>(
                name: "Event",
                table: "WebhookDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：Event");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：DeliveredAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Attempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Tags",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tags",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Tags",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tags",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Tags",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "OldSlug",
                table: "SlugRedirects",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：OldSlug");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "SlugRedirects",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Sites",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Sites",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Slug",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Sites",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Sites",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsActive");

            migrationBuilder.AlterColumn<string>(
                name: "HostName",
                table: "Sites",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "character varying(253)",
                oldMaxLength: 253,
                oldNullable: true,
                oldComment: "字段：HostName",
                oldCollation: "C");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Sites",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Sites",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "SiteNavItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Url");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "SiteNavItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Title");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "SiteNavItems",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Position");

            migrationBuilder.AlterColumn<bool>(
                name: "IsVisible",
                table: "SiteNavItems",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsVisible");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "SiteMemberships",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Role");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "SiteMemberships",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：DisplayName");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "SiteMemberships",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookUrl",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：WebhookUrl");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookSecret",
                table: "Settings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "字段：WebhookSecret");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Settings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：TimeZoneId");

            migrationBuilder.AlterColumn<string>(
                name: "TimeFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "字段：TimeFormat");

            migrationBuilder.AlterColumn<string>(
                name: "ThemePreset",
                table: "Settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "字段：ThemePreset");

            migrationBuilder.AlterColumn<string>(
                name: "SocialXUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：SocialXUrl");

            migrationBuilder.AlterColumn<string>(
                name: "SocialMastodonUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：SocialMastodonUrl");

            migrationBuilder.AlterColumn<string>(
                name: "SocialGithubUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：SocialGithubUrl");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteIconMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：SiteIconMediaId");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCover",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：ShowCover");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowByline",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：ShowByline");

            migrationBuilder.AlterColumn<int>(
                name: "ReadingWidth",
                table: "Settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：ReadingWidth");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrivacyPageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：PrivacyPageContentId");

            migrationBuilder.AlterColumn<int>(
                name: "PostsPerPage",
                table: "Settings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "字段：PostsPerPage");

            migrationBuilder.AlterColumn<string>(
                name: "PermalinkScheme",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "字段：PermalinkScheme");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "MaintenanceMode",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：MaintenanceMode");

            migrationBuilder.AlterColumn<string>(
                name: "MaintenanceMessage",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：MaintenanceMessage");

            migrationBuilder.AlterColumn<Guid>(
                name: "LogoMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：LogoMediaId");

            migrationBuilder.AlterColumn<Guid>(
                name: "HomePageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：HomePageContentId");

            migrationBuilder.AlterColumn<string>(
                name: "FooterText",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：FooterText");

            migrationBuilder.AlterColumn<bool>(
                name: "FeedShowFullText",
                table: "Settings",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true,
                oldComment: "字段：FeedShowFullText");

            migrationBuilder.AlterColumn<int>(
                name: "FeedItems",
                table: "Settings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "字段：FeedItems");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：EmailNotificationsEnabled");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<string>(
                name: "DateFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "字段：DateFormat");

            migrationBuilder.AlterColumn<bool>(
                name: "CustomNavigationEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：CustomNavigationEnabled");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：CommentsEnabled");

            migrationBuilder.AlterColumn<int>(
                name: "CommentCloseDays",
                table: "Settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：CommentCloseDays");

            migrationBuilder.AlterColumn<string>(
                name: "CommentBlockedWords",
                table: "Settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "字段：CommentBlockedWords");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentAutoApprove",
                table: "Settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：CommentAutoApprove");

            migrationBuilder.AlterColumn<string>(
                name: "ColorScheme",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "字段：ColorScheme");

            migrationBuilder.AlterColumn<string>(
                name: "CardStyle",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "字段：CardStyle");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorDisplayName",
                table: "Settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：AuthorDisplayName");

            migrationBuilder.AlterColumn<string>(
                name: "AccentColor",
                table: "Settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldComment: "字段：AccentColor");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Settings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Width",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Width");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UploadedAt",
                table: "MediaAssets",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：UploadedAt");

            migrationBuilder.AlterColumn<long>(
                name: "Size",
                table: "MediaAssets",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "字段：Size");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalName",
                table: "MediaAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "字段：OriginalName");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Kind");

            migrationBuilder.AlterColumn<int>(
                name: "Height",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Height");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "MediaAssets",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：FileName");

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "MediaAssets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "字段：ContentType");

            migrationBuilder.AlterColumn<string>(
                name: "Caption",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：Caption");

            migrationBuilder.AlterColumn<string>(
                name: "AltText",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：AltText");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：NextAttemptAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EnqueuedAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：EnqueuedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "EmbeddingJobs",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Attempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaxonomyId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：TaxonomyId");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomyTerms",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ParentId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomyTerms",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomyTerms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomies",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsHierarchical",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsHierarchical");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsArchived");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentTypeId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "CustomFields",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Position");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                table: "CustomFields",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Options");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomFields",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "CustomFields",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Key");

            migrationBuilder.AlterColumn<bool>(
                name: "IsRequired",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsRequired");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsArchived");

            migrationBuilder.AlterColumn<string>(
                name: "FieldType",
                table: "CustomFields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "字段：FieldType");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentTypeId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Count",
                table: "ContentViews",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Count");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Date",
                table: "ContentViews",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "字段：Date");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsTags",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：SupportsTags");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsHierarchy",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：SupportsHierarchy");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsCategories",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：SupportsCategories");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsArchived");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ContentTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentTypes",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "TagsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：TagsId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentsId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Contents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：UpdatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Contents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "字段：Title");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Contents",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Status");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Contents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：PublishedAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：PublishAt");

            migrationBuilder.AlterColumn<string>(
                name: "ProtectionHash",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：ProtectionHash");

            migrationBuilder.AlterColumn<Guid>(
                name: "PreviewToken",
                table: "Contents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：PreviewToken");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ParentId");

            migrationBuilder.AlterColumn<bool>(
                name: "NoIndex",
                table: "Contents",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：NoIndex");

            migrationBuilder.AlterColumn<string>(
                name: "MetaDescription",
                table: "Contents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：MetaDescription");

            migrationBuilder.AlterColumn<int>(
                name: "MenuOrder",
                table: "Contents",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：MenuOrder");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Contents",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000,
                oldComment: "字段：Markdown");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "Contents",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Kind");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSticky",
                table: "Contents",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsSticky");

            migrationBuilder.AlterColumn<bool>(
                name: "IsProtected",
                table: "Contents",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsProtected");

            migrationBuilder.AlterColumn<bool>(
                name: "IsPrivate",
                table: "Contents",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsPrivate");

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Excerpt");

            migrationBuilder.AlterColumn<string>(
                name: "EditingByName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：EditingByName");

            migrationBuilder.AlterColumn<string>(
                name: "EditingById",
                table: "Contents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：EditingById");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EditingAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：EditingAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：DeletedAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoverMediaId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：CoverMediaId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ContentTypeId");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorSlug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：AuthorSlug");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：AuthorName");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorId",
                table: "Contents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：AuthorId");

            migrationBuilder.AlterColumn<bool>(
                name: "AllowComments",
                table: "Contents",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：AllowComments");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Contents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "ContentRevisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "字段：Title");

            migrationBuilder.AlterColumn<string>(
                name: "SnapshotJson",
                table: "ContentRevisions",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536,
                oldComment: "字段：SnapshotJson");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "ContentRevisions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000,
                oldComment: "字段：Markdown");

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "ContentRevisions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Excerpt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentRevisions",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "ContentFieldValues",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "字段：Value");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "FieldId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：FieldId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "ContentEmbeddings",
                type: "vector(1536)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldComment: "字段：Embedding");

            migrationBuilder.AlterColumn<string>(
                name: "ChunkText",
                table: "ContentEmbeddings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "字段：ChunkText");

            migrationBuilder.AlterColumn<int>(
                name: "ChunkIndex",
                table: "ContentEmbeddings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：ChunkIndex");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "TermId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：TermId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentsId");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoriesId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：CategoriesId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Comments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Comments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Url");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Comments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Status");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Comments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ParentId");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Comments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "字段：Markdown");

            migrationBuilder.AlterColumn<bool>(
                name: "IsAdminReply",
                table: "Comments",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsAdminReply");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Comments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Comments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：AuthorName");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Comments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Categories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Categories",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Categories",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ParentId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Categories",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "字段：UserName");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AuditLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "Target",
                table: "AuditLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Target");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：Action");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "AspNetUserTokens",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：Value");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：LoginProvider");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "WebsiteUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：WebsiteUrl");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldComment: "字段：UserName");

            migrationBuilder.AlterColumn<string>(
                name: "TwoFactorSecret",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：TwoFactorSecret");

            migrationBuilder.AlterColumn<bool>(
                name: "TwoFactorEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：TwoFactorEnabled");

            migrationBuilder.AlterColumn<int>(
                name: "SingletonKey",
                table: "AspNetUsers",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "字段：SingletonKey");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：SecurityStamp");

            migrationBuilder.AlterColumn<string>(
                name: "RecoveryCodeHashes",
                table: "AspNetUsers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldComment: "字段：RecoveryCodeHashes");

            migrationBuilder.AlterColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：PhoneNumberConfirmed");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：PhoneNumber");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：PasswordHash");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUserName",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldComment: "字段：NormalizedUserName");

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedEmail",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldComment: "字段：NormalizedEmail");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：LockoutEnd");

            migrationBuilder.AlterColumn<bool>(
                name: "LockoutEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：LockoutEnabled");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDisabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsDisabled");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：EmailConfirmed");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldComment: "字段：Email");

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ConcurrencyStamp");

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Bio");

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：AvatarUrl");

            migrationBuilder.AlterColumn<int>(
                name: "AccessFailedCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：AccessFailedCount");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：RoleId");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderDisplayName",
                table: "AspNetUserLogins",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ProviderDisplayName");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：ProviderKey");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：LoginProvider");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimValue");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimType");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetUserClaims",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Id")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "AspNetRoles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldComment: "字段：NormalizedName");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetRoles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetRoles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ConcurrencyStamp");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：RoleId");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimValue");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimType");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetRoleClaims",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Id")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ApiCredentials",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "ApiCredentials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "字段：SecurityStamp");

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                table: "ApiCredentials",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：SecretHash");

            migrationBuilder.AlterColumn<int>(
                name: "Scope",
                table: "ApiCredentials",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Scope");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：RevokedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ApiCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：ExpiresAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");
        }
    }
}
