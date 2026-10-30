using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefineChineseCommentDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "WebhookDeliveries",
                comment: "Webhook 投递记录表：出站事件投递状态与重试。",
                oldComment: "数据表：WebhookDeliveries");

            migrationBuilder.AlterTable(
                name: "Tags",
                comment: "标签表：内容标签体系。",
                oldComment: "数据表：Tags");

            migrationBuilder.AlterTable(
                name: "SlugRedirects",
                comment: "Slug 重定向表：旧路径到内容的永久跳转映射。",
                oldComment: "数据表：SlugRedirects");

            migrationBuilder.AlterTable(
                name: "Sites",
                comment: "站点表：多站点租户的基础信息。",
                oldComment: "数据表：Sites");

            migrationBuilder.AlterTable(
                name: "SiteNavItems",
                comment: "站点导航表：前台自定义导航项。",
                oldComment: "数据表：SiteNavItems");

            migrationBuilder.AlterTable(
                name: "SiteMemberships",
                comment: "站点成员表：用户在站点内的角色与署名。",
                oldComment: "数据表：SiteMemberships");

            migrationBuilder.AlterTable(
                name: "Settings",
                comment: "站点设置表：站点配置与展示选项。",
                oldComment: "数据表：Settings");

            migrationBuilder.AlterTable(
                name: "MediaAssets",
                comment: "媒体资源表：图片/文档/音视频元数据。",
                oldComment: "数据表：MediaAssets");

            migrationBuilder.AlterTable(
                name: "EmbeddingJobs",
                comment: "向量生成任务表：嵌入计算队列。",
                oldComment: "数据表：EmbeddingJobs");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomyTerms",
                comment: "自定义词条表：自定义分类法下的具体词条。",
                oldComment: "数据表：CustomTaxonomyTerms");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomies",
                comment: "自定义分类法表：挂载到内容类型的词汇集。",
                oldComment: "数据表：CustomTaxonomies");

            migrationBuilder.AlterTable(
                name: "CustomFields",
                comment: "自定义字段定义表：内容类型可扩展字段。",
                oldComment: "数据表：CustomFields");

            migrationBuilder.AlterTable(
                name: "ContentViews",
                comment: "内容浏览统计表：按天聚合的浏览次数。",
                oldComment: "数据表：ContentViews");

            migrationBuilder.AlterTable(
                name: "ContentTypes",
                comment: "内容类型表：自定义内容模型定义。",
                oldComment: "数据表：ContentTypes");

            migrationBuilder.AlterTable(
                name: "ContentTags",
                comment: "内容-标签关联表。",
                oldComment: "数据表：ContentTags");

            migrationBuilder.AlterTable(
                name: "Contents",
                comment: "内容主表：文章/页面及其发布状态。",
                oldComment: "数据表：Contents");

            migrationBuilder.AlterTable(
                name: "ContentRevisions",
                comment: "内容修订表：内容历史快照。",
                oldComment: "数据表：ContentRevisions");

            migrationBuilder.AlterTable(
                name: "ContentFieldValues",
                comment: "内容自定义字段值表。",
                oldComment: "数据表：ContentFieldValues");

            migrationBuilder.AlterTable(
                name: "ContentEmbeddings",
                comment: "语义向量表：内容切片与向量。",
                oldComment: "数据表：ContentEmbeddings");

            migrationBuilder.AlterTable(
                name: "ContentCustomTerms",
                comment: "内容-自定义词条关联表。",
                oldComment: "数据表：ContentCustomTerms");

            migrationBuilder.AlterTable(
                name: "ContentCategories",
                comment: "内容-分类关联表。",
                oldComment: "数据表：ContentCategories");

            migrationBuilder.AlterTable(
                name: "Comments",
                comment: "评论表：访客评论与审核状态。",
                oldComment: "数据表：Comments");

            migrationBuilder.AlterTable(
                name: "Categories",
                comment: "分类表：内容分类体系。",
                oldComment: "数据表：Categories");

            migrationBuilder.AlterTable(
                name: "AuditLogs",
                comment: "审计日志表：关键管理操作记录。",
                oldComment: "数据表：AuditLogs");

            migrationBuilder.AlterTable(
                name: "AspNetUserTokens",
                comment: "用户令牌表：Identity 令牌与会话相关数据。",
                oldComment: "数据表：AspNetUserTokens");

            migrationBuilder.AlterTable(
                name: "AspNetUsers",
                comment: "后台账号表：保存管理员/编辑者账号及登录安全信息。",
                oldComment: "数据表：AspNetUsers");

            migrationBuilder.AlterTable(
                name: "AspNetUserRoles",
                comment: "用户角色关联表：Identity 用户与角色多对多关系。",
                oldComment: "数据表：AspNetUserRoles");

            migrationBuilder.AlterTable(
                name: "AspNetUserLogins",
                comment: "外部登录表：Identity 第三方登录绑定。",
                oldComment: "数据表：AspNetUserLogins");

            migrationBuilder.AlterTable(
                name: "AspNetUserClaims",
                comment: "用户声明表：Identity 用户附加声明。",
                oldComment: "数据表：AspNetUserClaims");

            migrationBuilder.AlterTable(
                name: "AspNetRoles",
                comment: "系统角色表：Identity 内置角色定义。",
                oldComment: "数据表：AspNetRoles");

            migrationBuilder.AlterTable(
                name: "AspNetRoleClaims",
                comment: "角色声明表：Identity 角色附加声明。",
                oldComment: "数据表：AspNetRoleClaims");

            migrationBuilder.AlterTable(
                name: "ApiCredentials",
                comment: "API 凭据表：站点级访问密钥（仅存摘要）。",
                oldComment: "数据表：ApiCredentials");

            migrationBuilder.AlterColumn<string>(
                name: "TargetUrl",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "投递目标地址。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：TargetUrl");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                comment: "状态枚举值。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Status");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Signature",
                table: "WebhookDeliveries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "请求签名。",
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
                comment: "投递负载 JSON。",
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536,
                oldComment: "字段：PayloadJson");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "下次重试时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：NextAttemptAt");

            migrationBuilder.AlterColumn<int>(
                name: "LastStatusCode",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: true,
                comment: "最近一次响应状态码。",
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
                comment: "最近一次错误信息。",
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
                comment: "事件名称。",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：Event");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "最终投递成功时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：DeliveredAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                comment: "投递尝试次数。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Attempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tags",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tags",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "名称。",
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
                comment: "描述说明。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "OldSlug",
                table: "SlugRedirects",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "业务字段：OldSlug。",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：OldSlug");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "SlugRedirects",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Sites",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Sites",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
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
                comment: "名称。",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Sites",
                type: "boolean",
                nullable: false,
                comment: "是否启用。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsActive");

            migrationBuilder.AlterColumn<string>(
                name: "HostName",
                table: "Sites",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true,
                comment: "业务字段：HostName。",
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
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Sites",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "SiteNavItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "链接地址。",
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
                comment: "标题。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Title");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "SiteNavItems",
                type: "integer",
                nullable: false,
                comment: "排序位置（值越小越靠前）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Position");

            migrationBuilder.AlterColumn<bool>(
                name: "IsVisible",
                table: "SiteNavItems",
                type: "boolean",
                nullable: false,
                comment: "业务字段：IsVisible。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsVisible");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "SiteMemberships",
                type: "integer",
                nullable: false,
                comment: "业务字段：Role。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Role");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "SiteMemberships",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "业务字段：DisplayName。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：DisplayName");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "SiteMemberships",
                type: "text",
                nullable: false,
                comment: "用户 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookUrl",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "Webhook 接收地址。",
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
                comment: "Webhook 签名密钥。",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "字段：WebhookSecret");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Settings",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "站点时区。",
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
                comment: "时间显示格式。",
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
                comment: "主题预设名称。",
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
                comment: "X/Twitter 社交链接。",
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
                comment: "Mastodon 社交链接。",
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
                comment: "GitHub 社交链接。",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：SocialGithubUrl");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteIconMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "站点图标媒体 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：SiteIconMediaId");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCover",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "是否显示封面图。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：ShowCover");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowByline",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "是否显示作者署名。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：ShowByline");

            migrationBuilder.AlterColumn<int>(
                name: "ReadingWidth",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "阅读宽度（字符列）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：ReadingWidth");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrivacyPageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "隐私政策页面 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：PrivacyPageContentId");

            migrationBuilder.AlterColumn<int>(
                name: "PostsPerPage",
                table: "Settings",
                type: "integer",
                nullable: true,
                comment: "列表每页文章数。",
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
                comment: "固定链接方案。",
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
                comment: "名称。",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "MaintenanceMode",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "是否启用维护模式。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：MaintenanceMode");

            migrationBuilder.AlterColumn<string>(
                name: "MaintenanceMessage",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "维护模式提示语。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：MaintenanceMessage");

            migrationBuilder.AlterColumn<Guid>(
                name: "LogoMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "站点 Logo 媒体 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：LogoMediaId");

            migrationBuilder.AlterColumn<Guid>(
                name: "HomePageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "首页绑定内容 ID。",
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
                comment: "页脚文本。",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：FooterText");

            migrationBuilder.AlterColumn<bool>(
                name: "FeedShowFullText",
                table: "Settings",
                type: "boolean",
                nullable: true,
                comment: "订阅源是否输出全文。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true,
                oldComment: "字段：FeedShowFullText");

            migrationBuilder.AlterColumn<int>(
                name: "FeedItems",
                table: "Settings",
                type: "integer",
                nullable: true,
                comment: "订阅源输出条目数。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "字段：FeedItems");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "是否发送邮件通知。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：EmailNotificationsEnabled");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "描述说明。",
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
                comment: "日期显示格式。",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "字段：DateFormat");

            migrationBuilder.AlterColumn<bool>(
                name: "CustomNavigationEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "是否启用受控导航。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：CustomNavigationEnabled");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "是否启用评论系统。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：CommentsEnabled");

            migrationBuilder.AlterColumn<int>(
                name: "CommentCloseDays",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "发布后自动关闭评论天数。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：CommentCloseDays");

            migrationBuilder.AlterColumn<string>(
                name: "CommentBlockedWords",
                table: "Settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "评论敏感词列表。",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "字段：CommentBlockedWords");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentAutoApprove",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "评论是否自动通过。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：CommentAutoApprove");

            migrationBuilder.AlterColumn<string>(
                name: "ColorScheme",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "颜色模式。",
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
                comment: "卡片样式。",
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
                comment: "前台默认作者显示名。",
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
                comment: "主题强调色。",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldComment: "字段：AccentColor");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "单例主键（固定为 1）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Settings",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Width",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "宽度（像素）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Width");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UploadedAt",
                table: "MediaAssets",
                type: "timestamp with time zone",
                nullable: false,
                comment: "上传时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：UploadedAt");

            migrationBuilder.AlterColumn<long>(
                name: "Size",
                table: "MediaAssets",
                type: "bigint",
                nullable: false,
                comment: "文件大小（字节）。",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "字段：Size");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalName",
                table: "MediaAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "原始上传文件名。",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "字段：OriginalName");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "类型枚举值。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Kind");

            migrationBuilder.AlterColumn<int>(
                name: "Height",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "高度（像素）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Height");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "MediaAssets",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "存储文件名。",
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
                comment: "MIME 类型。",
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
                comment: "媒体说明文字。",
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
                comment: "替代文本（无障碍/SEO）。",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：AltText");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "下次重试时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：NextAttemptAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EnqueuedAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "入队时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：EnqueuedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "EmbeddingJobs",
                type: "integer",
                nullable: false,
                comment: "已重试次数。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Attempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaxonomyId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "业务字段：TaxonomyId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：TaxonomyId");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomyTerms",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: true,
                comment: "父级对象 ID。",
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
                comment: "名称。",
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
                comment: "描述说明。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomies",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "名称。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsHierarchical",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                comment: "业务字段：IsHierarchical。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsHierarchical");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                comment: "是否归档。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsArchived");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "描述说明。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "业务字段：ContentTypeId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentTypeId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "CustomFields",
                type: "integer",
                nullable: false,
                comment: "排序位置（值越小越靠前）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Position");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                table: "CustomFields",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "业务字段：Options。",
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
                comment: "名称。",
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
                comment: "业务字段：Key。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Key");

            migrationBuilder.AlterColumn<bool>(
                name: "IsRequired",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                comment: "业务字段：IsRequired。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsRequired");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                comment: "是否归档。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsArchived");

            migrationBuilder.AlterColumn<string>(
                name: "FieldType",
                table: "CustomFields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "业务字段：FieldType。",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "字段：FieldType");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "业务字段：ContentTypeId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentTypeId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<int>(
                name: "Count",
                table: "ContentViews",
                type: "integer",
                nullable: false,
                comment: "当日浏览次数。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Count");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Date",
                table: "ContentViews",
                type: "date",
                nullable: false,
                comment: "日期。",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "字段：Date");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsTags",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "业务字段：SupportsTags。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：SupportsTags");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsHierarchy",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "业务字段：SupportsHierarchy。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：SupportsHierarchy");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsCategories",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "业务字段：SupportsCategories。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：SupportsCategories");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "名称。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "是否归档。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsArchived");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ContentTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "描述说明。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentTypes",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "TagsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "业务字段：TagsId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：TagsId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "业务字段：ContentsId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentsId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                comment: "更新时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：UpdatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Contents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "标题。",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "字段：Title");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "状态枚举值。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Status");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "发布时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：PublishedAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "计划发布时间。",
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
                comment: "访问密码哈希值。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：ProtectionHash");

            migrationBuilder.AlterColumn<Guid>(
                name: "PreviewToken",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "预览令牌（发布前分享预览）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：PreviewToken");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "父级对象 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：ParentId");

            migrationBuilder.AlterColumn<bool>(
                name: "NoIndex",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "是否禁止搜索引擎索引。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：NoIndex");

            migrationBuilder.AlterColumn<string>(
                name: "MetaDescription",
                table: "Contents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "SEO 元描述。",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "字段：MetaDescription");

            migrationBuilder.AlterColumn<int>(
                name: "MenuOrder",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "页面菜单排序权重。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：MenuOrder");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Contents",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                comment: "Markdown 正文。",
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000,
                oldComment: "字段：Markdown");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "内容类型：文章或页面。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Kind");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSticky",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "是否置顶（文章列表优先显示）。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsSticky");

            migrationBuilder.AlterColumn<bool>(
                name: "IsProtected",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "是否启用访问密码。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsProtected");

            migrationBuilder.AlterColumn<bool>(
                name: "IsPrivate",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "是否私密（仅后台可见）。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsPrivate");

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "内容摘要。",
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
                comment: "当前编辑锁占用者名称。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：EditingByName");

            migrationBuilder.AlterColumn<string>(
                name: "EditingById",
                table: "Contents",
                type: "text",
                nullable: true,
                comment: "当前编辑锁占用者 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：EditingById");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EditingAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "编辑锁最近心跳时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：EditingAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "进入回收站时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：DeletedAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoverMediaId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "封面媒体 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "字段：CoverMediaId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "自定义内容类型 ID。",
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
                comment: "作者 URL 代称。",
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
                comment: "作者显示名快照。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：AuthorName");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorId",
                table: "Contents",
                type: "text",
                nullable: true,
                comment: "作者账号 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：AuthorId");

            migrationBuilder.AlterColumn<bool>(
                name: "AllowComments",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "是否允许评论。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：AllowComments");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "ContentRevisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "标题。",
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
                comment: "结构化快照 JSON（分类/标签/SEO/字段值等）。",
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536,
                oldComment: "字段：SnapshotJson");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "ContentRevisions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                comment: "Markdown 正文。",
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
                comment: "业务字段：Excerpt。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Excerpt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentRevisions",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "ContentFieldValues",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "业务字段：Value。",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "字段：Value");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "FieldId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "业务字段：FieldId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：FieldId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "ContentEmbeddings",
                type: "vector(1536)",
                nullable: false,
                comment: "向量数据。",
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldComment: "字段：Embedding");

            migrationBuilder.AlterColumn<string>(
                name: "ChunkText",
                table: "ContentEmbeddings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "切片文本。",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "字段：ChunkText");

            migrationBuilder.AlterColumn<int>(
                name: "ChunkIndex",
                table: "ContentEmbeddings",
                type: "integer",
                nullable: false,
                comment: "切片序号。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：ChunkIndex");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "TermId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "业务字段：TermId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：TermId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "业务字段：ContentsId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentsId");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoriesId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "业务字段：CategoriesId。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：CategoriesId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Comments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "评论作者网址。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Url");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Comments",
                type: "integer",
                nullable: false,
                comment: "状态枚举值。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Status");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Comments",
                type: "uuid",
                nullable: true,
                comment: "父级对象 ID。",
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
                comment: "评论内容（Markdown）。",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "字段：Markdown");

            migrationBuilder.AlterColumn<bool>(
                name: "IsAdminReply",
                table: "Comments",
                type: "boolean",
                nullable: false,
                comment: "是否官方回复。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsAdminReply");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Comments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "内容 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：ContentId");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Comments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "评论作者显示名。",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "字段：AuthorName");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "并发版本号（乐观并发控制）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Version");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Categories",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "URL 代称（站内唯一或局部唯一）。",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "字段：Slug");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Categories",
                type: "uuid",
                nullable: true,
                comment: "父级对象 ID。",
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
                comment: "名称。",
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
                comment: "描述说明。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Description");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "操作者用户名快照。",
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
                comment: "用户 ID。",
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
                comment: "操作目标。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：Target");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "操作动作。",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：Action");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "AspNetUserTokens",
                type: "text",
                nullable: true,
                comment: "业务字段：Value。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：Value");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "名称。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "业务字段：LoginProvider。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：LoginProvider");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "用户 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "WebsiteUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "个人网站地址。",
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
                comment: "业务字段：UserName。",
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
                comment: "两步验证密钥。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：TwoFactorSecret");

            migrationBuilder.AlterColumn<bool>(
                name: "TwoFactorEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "业务字段：TwoFactorEnabled。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：TwoFactorEnabled");

            migrationBuilder.AlterColumn<int>(
                name: "SingletonKey",
                table: "AspNetUsers",
                type: "integer",
                nullable: true,
                comment: "全局单例管理员标记。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "字段：SingletonKey");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "业务字段：SecurityStamp。",
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
                comment: "恢复码哈希集合。",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldComment: "字段：RecoveryCodeHashes");

            migrationBuilder.AlterColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "业务字段：PhoneNumberConfirmed。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：PhoneNumberConfirmed");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "业务字段：PhoneNumber。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：PhoneNumber");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "业务字段：PasswordHash。",
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
                comment: "业务字段：NormalizedUserName。",
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
                comment: "业务字段：NormalizedEmail。",
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
                comment: "业务字段：LockoutEnd。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "字段：LockoutEnd");

            migrationBuilder.AlterColumn<bool>(
                name: "LockoutEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "业务字段：LockoutEnabled。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：LockoutEnabled");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDisabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "账号是否禁用。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：IsDisabled");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "业务字段：EmailConfirmed。",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "字段：EmailConfirmed");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "AspNetUsers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "业务字段：Email。",
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
                comment: "业务字段：ConcurrencyStamp。",
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
                comment: "用户简介。",
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
                comment: "头像地址。",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "字段：AvatarUrl");

            migrationBuilder.AlterColumn<int>(
                name: "AccessFailedCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                comment: "业务字段：AccessFailedCount。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：AccessFailedCount");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                comment: "业务字段：RoleId。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：RoleId");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                comment: "用户 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "用户 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderDisplayName",
                table: "AspNetUserLogins",
                type: "text",
                nullable: true,
                comment: "业务字段：ProviderDisplayName。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ProviderDisplayName");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "业务字段：ProviderKey。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：ProviderKey");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "业务字段：LoginProvider。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：LoginProvider");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "text",
                nullable: false,
                comment: "用户 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                comment: "业务字段：ClaimValue。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimValue");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                comment: "业务字段：ClaimType。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimType");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetUserClaims",
                type: "integer",
                nullable: false,
                comment: "主键 ID。",
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
                comment: "业务字段：NormalizedName。",
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
                comment: "名称。",
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
                comment: "业务字段：ConcurrencyStamp。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ConcurrencyStamp");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：Id");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: false,
                comment: "业务字段：RoleId。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：RoleId");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                comment: "业务字段：ClaimValue。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimValue");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                comment: "业务字段：ClaimType。",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "字段：ClaimType");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetRoleClaims",
                type: "integer",
                nullable: false,
                comment: "主键 ID。",
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
                comment: "用户 ID。",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "字段：UserId");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                comment: "所属站点 ID（租户隔离键）。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：SiteId");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "ApiCredentials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "安全戳（失效控制）。",
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
                comment: "API 密钥哈希（不存明文）。",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "字段：SecretHash");

            migrationBuilder.AlterColumn<int>(
                name: "Scope",
                table: "ApiCredentials",
                type: "integer",
                nullable: false,
                comment: "权限范围（只读/读写）。",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "字段：Scope");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: true,
                comment: "撤销时间。",
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
                comment: "名称。",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "字段：Name");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                comment: "过期时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：ExpiresAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                comment: "创建时间。",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "字段：CreatedAt");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                comment: "主键 ID。",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "字段：Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "WebhookDeliveries",
                comment: "数据表：WebhookDeliveries",
                oldComment: "Webhook 投递记录表：出站事件投递状态与重试。");

            migrationBuilder.AlterTable(
                name: "Tags",
                comment: "数据表：Tags",
                oldComment: "标签表：内容标签体系。");

            migrationBuilder.AlterTable(
                name: "SlugRedirects",
                comment: "数据表：SlugRedirects",
                oldComment: "Slug 重定向表：旧路径到内容的永久跳转映射。");

            migrationBuilder.AlterTable(
                name: "Sites",
                comment: "数据表：Sites",
                oldComment: "站点表：多站点租户的基础信息。");

            migrationBuilder.AlterTable(
                name: "SiteNavItems",
                comment: "数据表：SiteNavItems",
                oldComment: "站点导航表：前台自定义导航项。");

            migrationBuilder.AlterTable(
                name: "SiteMemberships",
                comment: "数据表：SiteMemberships",
                oldComment: "站点成员表：用户在站点内的角色与署名。");

            migrationBuilder.AlterTable(
                name: "Settings",
                comment: "数据表：Settings",
                oldComment: "站点设置表：站点配置与展示选项。");

            migrationBuilder.AlterTable(
                name: "MediaAssets",
                comment: "数据表：MediaAssets",
                oldComment: "媒体资源表：图片/文档/音视频元数据。");

            migrationBuilder.AlterTable(
                name: "EmbeddingJobs",
                comment: "数据表：EmbeddingJobs",
                oldComment: "向量生成任务表：嵌入计算队列。");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomyTerms",
                comment: "数据表：CustomTaxonomyTerms",
                oldComment: "自定义词条表：自定义分类法下的具体词条。");

            migrationBuilder.AlterTable(
                name: "CustomTaxonomies",
                comment: "数据表：CustomTaxonomies",
                oldComment: "自定义分类法表：挂载到内容类型的词汇集。");

            migrationBuilder.AlterTable(
                name: "CustomFields",
                comment: "数据表：CustomFields",
                oldComment: "自定义字段定义表：内容类型可扩展字段。");

            migrationBuilder.AlterTable(
                name: "ContentViews",
                comment: "数据表：ContentViews",
                oldComment: "内容浏览统计表：按天聚合的浏览次数。");

            migrationBuilder.AlterTable(
                name: "ContentTypes",
                comment: "数据表：ContentTypes",
                oldComment: "内容类型表：自定义内容模型定义。");

            migrationBuilder.AlterTable(
                name: "ContentTags",
                comment: "数据表：ContentTags",
                oldComment: "内容-标签关联表。");

            migrationBuilder.AlterTable(
                name: "Contents",
                comment: "数据表：Contents",
                oldComment: "内容主表：文章/页面及其发布状态。");

            migrationBuilder.AlterTable(
                name: "ContentRevisions",
                comment: "数据表：ContentRevisions",
                oldComment: "内容修订表：内容历史快照。");

            migrationBuilder.AlterTable(
                name: "ContentFieldValues",
                comment: "数据表：ContentFieldValues",
                oldComment: "内容自定义字段值表。");

            migrationBuilder.AlterTable(
                name: "ContentEmbeddings",
                comment: "数据表：ContentEmbeddings",
                oldComment: "语义向量表：内容切片与向量。");

            migrationBuilder.AlterTable(
                name: "ContentCustomTerms",
                comment: "数据表：ContentCustomTerms",
                oldComment: "内容-自定义词条关联表。");

            migrationBuilder.AlterTable(
                name: "ContentCategories",
                comment: "数据表：ContentCategories",
                oldComment: "内容-分类关联表。");

            migrationBuilder.AlterTable(
                name: "Comments",
                comment: "数据表：Comments",
                oldComment: "评论表：访客评论与审核状态。");

            migrationBuilder.AlterTable(
                name: "Categories",
                comment: "数据表：Categories",
                oldComment: "分类表：内容分类体系。");

            migrationBuilder.AlterTable(
                name: "AuditLogs",
                comment: "数据表：AuditLogs",
                oldComment: "审计日志表：关键管理操作记录。");

            migrationBuilder.AlterTable(
                name: "AspNetUserTokens",
                comment: "数据表：AspNetUserTokens",
                oldComment: "用户令牌表：Identity 令牌与会话相关数据。");

            migrationBuilder.AlterTable(
                name: "AspNetUsers",
                comment: "数据表：AspNetUsers",
                oldComment: "后台账号表：保存管理员/编辑者账号及登录安全信息。");

            migrationBuilder.AlterTable(
                name: "AspNetUserRoles",
                comment: "数据表：AspNetUserRoles",
                oldComment: "用户角色关联表：Identity 用户与角色多对多关系。");

            migrationBuilder.AlterTable(
                name: "AspNetUserLogins",
                comment: "数据表：AspNetUserLogins",
                oldComment: "外部登录表：Identity 第三方登录绑定。");

            migrationBuilder.AlterTable(
                name: "AspNetUserClaims",
                comment: "数据表：AspNetUserClaims",
                oldComment: "用户声明表：Identity 用户附加声明。");

            migrationBuilder.AlterTable(
                name: "AspNetRoles",
                comment: "数据表：AspNetRoles",
                oldComment: "系统角色表：Identity 内置角色定义。");

            migrationBuilder.AlterTable(
                name: "AspNetRoleClaims",
                comment: "数据表：AspNetRoleClaims",
                oldComment: "角色声明表：Identity 角色附加声明。");

            migrationBuilder.AlterTable(
                name: "ApiCredentials",
                comment: "数据表：ApiCredentials",
                oldComment: "API 凭据表：站点级访问密钥（仅存摘要）。");

            migrationBuilder.AlterColumn<string>(
                name: "TargetUrl",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：TargetUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "投递目标地址。");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                comment: "字段：Status",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "状态枚举值。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "Signature",
                table: "WebhookDeliveries",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Signature",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "请求签名。");

            migrationBuilder.AlterColumn<string>(
                name: "PayloadJson",
                table: "WebhookDeliveries",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                comment: "字段：PayloadJson",
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536,
                oldComment: "投递负载 JSON。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：NextAttemptAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "下次重试时间。");

            migrationBuilder.AlterColumn<int>(
                name: "LastStatusCode",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: true,
                comment: "字段：LastStatusCode",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "最近一次响应状态码。");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "WebhookDeliveries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：LastError",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "最近一次错误信息。");

            migrationBuilder.AlterColumn<string>(
                name: "Event",
                table: "WebhookDeliveries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：Event",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "事件名称。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：DeliveredAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "最终投递成功时间。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "WebhookDeliveries",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "WebhookDeliveries",
                type: "integer",
                nullable: false,
                comment: "字段：Attempts",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "投递尝试次数。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "WebhookDeliveries",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tags",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "URL 代称（站内唯一或局部唯一）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tags",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "描述说明。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Tags",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "OldSlug",
                table: "SlugRedirects",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：OldSlug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "业务字段：OldSlug。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "SlugRedirects",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Sites",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

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
                oldComment: "URL 代称（站内唯一或局部唯一）。",
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
                oldMaxLength: 100,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Sites",
                type: "boolean",
                nullable: false,
                comment: "字段：IsActive",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否启用。");

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
                oldComment: "业务字段：HostName。",
                oldCollation: "C");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Sites",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Sites",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "SiteNavItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Url",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "链接地址。");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "SiteNavItems",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Title",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "标题。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "SiteNavItems",
                type: "integer",
                nullable: false,
                comment: "字段：Position",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "排序位置（值越小越靠前）。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsVisible",
                table: "SiteNavItems",
                type: "boolean",
                nullable: false,
                comment: "字段：IsVisible",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：IsVisible。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "SiteNavItems",
                type: "uuid",
                nullable: true,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "SiteNavItems",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "SiteMemberships",
                type: "integer",
                nullable: false,
                comment: "字段：Role",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "业务字段：Role。");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "SiteMemberships",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：DisplayName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "业务字段：DisplayName。");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "SiteMemberships",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "SiteMemberships",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookUrl",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：WebhookUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "Webhook 接收地址。");

            migrationBuilder.AlterColumn<string>(
                name: "WebhookSecret",
                table: "Settings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "字段：WebhookSecret",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "Webhook 签名密钥。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Settings",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZoneId",
                table: "Settings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：TimeZoneId",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "站点时区。");

            migrationBuilder.AlterColumn<string>(
                name: "TimeFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "字段：TimeFormat",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "时间显示格式。");

            migrationBuilder.AlterColumn<string>(
                name: "ThemePreset",
                table: "Settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "字段：ThemePreset",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "主题预设名称。");

            migrationBuilder.AlterColumn<string>(
                name: "SocialXUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：SocialXUrl",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "X/Twitter 社交链接。");

            migrationBuilder.AlterColumn<string>(
                name: "SocialMastodonUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：SocialMastodonUrl",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "Mastodon 社交链接。");

            migrationBuilder.AlterColumn<string>(
                name: "SocialGithubUrl",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：SocialGithubUrl",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "GitHub 社交链接。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteIconMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：SiteIconMediaId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "站点图标媒体 ID。");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowCover",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：ShowCover",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否显示封面图。");

            migrationBuilder.AlterColumn<bool>(
                name: "ShowByline",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：ShowByline",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否显示作者署名。");

            migrationBuilder.AlterColumn<int>(
                name: "ReadingWidth",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "字段：ReadingWidth",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "阅读宽度（字符列）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "PrivacyPageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：PrivacyPageContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "隐私政策页面 ID。");

            migrationBuilder.AlterColumn<int>(
                name: "PostsPerPage",
                table: "Settings",
                type: "integer",
                nullable: true,
                comment: "字段：PostsPerPage",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "列表每页文章数。");

            migrationBuilder.AlterColumn<string>(
                name: "PermalinkScheme",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "字段：PermalinkScheme",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "固定链接方案。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<bool>(
                name: "MaintenanceMode",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：MaintenanceMode",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否启用维护模式。");

            migrationBuilder.AlterColumn<string>(
                name: "MaintenanceMessage",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：MaintenanceMessage",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "维护模式提示语。");

            migrationBuilder.AlterColumn<Guid>(
                name: "LogoMediaId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：LogoMediaId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "站点 Logo 媒体 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "HomePageContentId",
                table: "Settings",
                type: "uuid",
                nullable: true,
                comment: "字段：HomePageContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "首页绑定内容 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "FooterText",
                table: "Settings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：FooterText",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "页脚文本。");

            migrationBuilder.AlterColumn<bool>(
                name: "FeedShowFullText",
                table: "Settings",
                type: "boolean",
                nullable: true,
                comment: "字段：FeedShowFullText",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true,
                oldComment: "订阅源是否输出全文。");

            migrationBuilder.AlterColumn<int>(
                name: "FeedItems",
                table: "Settings",
                type: "integer",
                nullable: true,
                comment: "字段：FeedItems",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "订阅源输出条目数。");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：EmailNotificationsEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否发送邮件通知。");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Settings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "描述说明。");

            migrationBuilder.AlterColumn<string>(
                name: "DateFormat",
                table: "Settings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                comment: "字段：DateFormat",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldComment: "日期显示格式。");

            migrationBuilder.AlterColumn<bool>(
                name: "CustomNavigationEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：CustomNavigationEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否启用受控导航。");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentsEnabled",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：CommentsEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否启用评论系统。");

            migrationBuilder.AlterColumn<int>(
                name: "CommentCloseDays",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "字段：CommentCloseDays",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "发布后自动关闭评论天数。");

            migrationBuilder.AlterColumn<string>(
                name: "CommentBlockedWords",
                table: "Settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "字段：CommentBlockedWords",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "评论敏感词列表。");

            migrationBuilder.AlterColumn<bool>(
                name: "CommentAutoApprove",
                table: "Settings",
                type: "boolean",
                nullable: false,
                comment: "字段：CommentAutoApprove",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "评论是否自动通过。");

            migrationBuilder.AlterColumn<string>(
                name: "ColorScheme",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "字段：ColorScheme",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "颜色模式。");

            migrationBuilder.AlterColumn<string>(
                name: "CardStyle",
                table: "Settings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                comment: "字段：CardStyle",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldComment: "卡片样式。");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorDisplayName",
                table: "Settings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：AuthorDisplayName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "前台默认作者显示名。");

            migrationBuilder.AlterColumn<string>(
                name: "AccentColor",
                table: "Settings",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                comment: "字段：AccentColor",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldComment: "主题强调色。");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "单例主键（固定为 1）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Settings",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<int>(
                name: "Width",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "字段：Width",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "宽度（像素）。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UploadedAt",
                table: "MediaAssets",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：UploadedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "上传时间。");

            migrationBuilder.AlterColumn<long>(
                name: "Size",
                table: "MediaAssets",
                type: "bigint",
                nullable: false,
                comment: "字段：Size",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "文件大小（字节）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "OriginalName",
                table: "MediaAssets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "字段：OriginalName",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "原始上传文件名。");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "字段：Kind",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "类型枚举值。");

            migrationBuilder.AlterColumn<int>(
                name: "Height",
                table: "MediaAssets",
                type: "integer",
                nullable: false,
                comment: "字段：Height",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "高度（像素）。");

            migrationBuilder.AlterColumn<string>(
                name: "FileName",
                table: "MediaAssets",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：FileName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "存储文件名。");

            migrationBuilder.AlterColumn<string>(
                name: "ContentType",
                table: "MediaAssets",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                comment: "字段：ContentType",
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldComment: "MIME 类型。");

            migrationBuilder.AlterColumn<string>(
                name: "Caption",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：Caption",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "媒体说明文字。");

            migrationBuilder.AlterColumn<string>(
                name: "AltText",
                table: "MediaAssets",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：AltText",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "替代文本（无障碍/SEO）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：NextAttemptAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "下次重试时间。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EnqueuedAt",
                table: "EmbeddingJobs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：EnqueuedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "入队时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "EmbeddingJobs",
                type: "integer",
                nullable: false,
                comment: "字段：Attempts",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "已重试次数。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaxonomyId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：TaxonomyId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：TaxonomyId。");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomyTerms",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "URL 代称（站内唯一或局部唯一）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "父级对象 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomyTerms",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomyTerms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "描述说明。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomyTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "CustomTaxonomies",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "URL 代称（站内唯一或局部唯一）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomTaxonomies",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsHierarchical",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                comment: "字段：IsHierarchical",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：IsHierarchical。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomTaxonomies",
                type: "boolean",
                nullable: false,
                comment: "字段：IsArchived",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否归档。");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CustomTaxonomies",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "描述说明。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentTypeId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：ContentTypeId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomTaxonomies",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<int>(
                name: "Position",
                table: "CustomFields",
                type: "integer",
                nullable: false,
                comment: "字段：Position",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "排序位置（值越小越靠前）。");

            migrationBuilder.AlterColumn<string>(
                name: "Options",
                table: "CustomFields",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Options",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "业务字段：Options。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "CustomFields",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "CustomFields",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Key",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "业务字段：Key。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsRequired",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                comment: "字段：IsRequired",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：IsRequired。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "CustomFields",
                type: "boolean",
                nullable: false,
                comment: "字段：IsArchived",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否归档。");

            migrationBuilder.AlterColumn<string>(
                name: "FieldType",
                table: "CustomFields",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "字段：FieldType",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "业务字段：FieldType。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentTypeId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：ContentTypeId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "CustomFields",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<int>(
                name: "Count",
                table: "ContentViews",
                type: "integer",
                nullable: false,
                comment: "字段：Count",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "当日浏览次数。");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "Date",
                table: "ContentViews",
                type: "date",
                nullable: false,
                comment: "字段：Date",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "日期。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsTags",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：SupportsTags",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：SupportsTags。");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsHierarchy",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：SupportsHierarchy",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：SupportsHierarchy。");

            migrationBuilder.AlterColumn<bool>(
                name: "SupportsCategories",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：SupportsCategories",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：SupportsCategories。");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "URL 代称（站内唯一或局部唯一）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ContentTypes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsArchived",
                table: "ContentTypes",
                type: "boolean",
                nullable: false,
                comment: "字段：IsArchived",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否归档。");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "ContentTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "描述说明。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentTypes",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentTypes",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "TagsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "字段：TagsId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：TagsId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentsId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：ContentsId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：UpdatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "更新时间。");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Contents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "字段：Title",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "标题。");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "字段：Status",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "状态枚举值。");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "URL 代称（站内唯一或局部唯一）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：PublishedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "发布时间。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：PublishAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "计划发布时间。");

            migrationBuilder.AlterColumn<string>(
                name: "ProtectionHash",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：ProtectionHash",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "访问密码哈希值。");

            migrationBuilder.AlterColumn<Guid>(
                name: "PreviewToken",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：PreviewToken",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "预览令牌（发布前分享预览）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "父级对象 ID。");

            migrationBuilder.AlterColumn<bool>(
                name: "NoIndex",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：NoIndex",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否禁止搜索引擎索引。");

            migrationBuilder.AlterColumn<string>(
                name: "MetaDescription",
                table: "Contents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "字段：MetaDescription",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "SEO 元描述。");

            migrationBuilder.AlterColumn<int>(
                name: "MenuOrder",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "字段：MenuOrder",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "页面菜单排序权重。");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Contents",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                comment: "字段：Markdown",
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000,
                oldComment: "Markdown 正文。");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "Contents",
                type: "integer",
                nullable: false,
                comment: "字段：Kind",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "内容类型：文章或页面。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsSticky",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：IsSticky",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否置顶（文章列表优先显示）。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsProtected",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：IsProtected",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否启用访问密码。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsPrivate",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：IsPrivate",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否私密（仅后台可见）。");

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "Contents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Excerpt",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "内容摘要。");

            migrationBuilder.AlterColumn<string>(
                name: "EditingByName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：EditingByName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "当前编辑锁占用者名称。");

            migrationBuilder.AlterColumn<string>(
                name: "EditingById",
                table: "Contents",
                type: "text",
                nullable: true,
                comment: "字段：EditingById",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "当前编辑锁占用者 ID。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EditingAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：EditingAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "编辑锁最近心跳时间。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：DeletedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "进入回收站时间。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Contents",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "CoverMediaId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：CoverMediaId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "封面媒体 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentTypeId",
                table: "Contents",
                type: "uuid",
                nullable: true,
                comment: "字段：ContentTypeId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "自定义内容类型 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorSlug",
                table: "Contents",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：AuthorSlug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "作者 URL 代称。");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：AuthorName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "作者显示名快照。");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorId",
                table: "Contents",
                type: "text",
                nullable: true,
                comment: "字段：AuthorId",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "作者账号 ID。");

            migrationBuilder.AlterColumn<bool>(
                name: "AllowComments",
                table: "Contents",
                type: "boolean",
                nullable: false,
                comment: "字段：AllowComments",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否允许评论。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Contents",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "ContentRevisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "字段：Title",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "标题。");

            migrationBuilder.AlterColumn<string>(
                name: "SnapshotJson",
                table: "ContentRevisions",
                type: "character varying(65536)",
                maxLength: 65536,
                nullable: false,
                comment: "字段：SnapshotJson",
                oldClrType: typeof(string),
                oldType: "character varying(65536)",
                oldMaxLength: 65536,
                oldComment: "结构化快照 JSON（分类/标签/SEO/字段值等）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "ContentRevisions",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: false,
                comment: "字段：Markdown",
                oldClrType: typeof(string),
                oldType: "character varying(200000)",
                oldMaxLength: 200000,
                oldComment: "Markdown 正文。");

            migrationBuilder.AlterColumn<string>(
                name: "Excerpt",
                table: "ContentRevisions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Excerpt",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "业务字段：Excerpt。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ContentRevisions",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "ContentFieldValues",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "字段：Value",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "业务字段：Value。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "FieldId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：FieldId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：FieldId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentFieldValues",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "ContentEmbeddings",
                type: "vector(1536)",
                nullable: false,
                comment: "字段：Embedding",
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldComment: "向量数据。");

            migrationBuilder.AlterColumn<string>(
                name: "ChunkText",
                table: "ContentEmbeddings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "字段：ChunkText",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "切片文本。");

            migrationBuilder.AlterColumn<int>(
                name: "ChunkIndex",
                table: "ContentEmbeddings",
                type: "integer",
                nullable: false,
                comment: "字段：ChunkIndex",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "切片序号。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "TermId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：TermId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：TermId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ContentCustomTerms",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentsId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentsId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：ContentsId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoriesId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "字段：CategoriesId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "业务字段：CategoriesId。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "Comments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Url",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "评论作者网址。");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Comments",
                type: "integer",
                nullable: false,
                comment: "字段：Status",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "状态枚举值。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Comments",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "父级对象 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Markdown",
                table: "Comments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "字段：Markdown",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "评论内容（Markdown）。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsAdminReply",
                table: "Comments",
                type: "boolean",
                nullable: false,
                comment: "字段：IsAdminReply",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "是否官方回复。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Comments",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ContentId",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：ContentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "内容 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "AuthorName",
                table: "Comments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：AuthorName",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "评论作者显示名。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Comments",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Version",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "字段：Version",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "并发版本号（乐观并发控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Categories",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                comment: "字段：Slug",
                oldClrType: typeof(string),
                oldType: "character varying(160)",
                oldMaxLength: 160,
                oldComment: "URL 代称（站内唯一或局部唯一）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<Guid>(
                name: "ParentId",
                table: "Categories",
                type: "uuid",
                nullable: true,
                comment: "字段：ParentId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "父级对象 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Categories",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Description",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "描述说明。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "Categories",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "字段：UserName",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "操作者用户名快照。");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AuditLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Target",
                table: "AuditLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Target",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "操作目标。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：Action",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "操作动作。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                table: "AspNetUserTokens",
                type: "text",
                nullable: true,
                comment: "字段：Value",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：Value。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "名称。");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "字段：LoginProvider",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "业务字段：LoginProvider。");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserTokens",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "WebsiteUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：WebsiteUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "个人网站地址。");

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
                oldNullable: true,
                oldComment: "业务字段：UserName。");

            migrationBuilder.AlterColumn<string>(
                name: "TwoFactorSecret",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                comment: "字段：TwoFactorSecret",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "两步验证密钥。");

            migrationBuilder.AlterColumn<bool>(
                name: "TwoFactorEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：TwoFactorEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：TwoFactorEnabled。");

            migrationBuilder.AlterColumn<int>(
                name: "SingletonKey",
                table: "AspNetUsers",
                type: "integer",
                nullable: true,
                comment: "字段：SingletonKey",
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldComment: "全局单例管理员标记。");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：SecurityStamp",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：SecurityStamp。");

            migrationBuilder.AlterColumn<string>(
                name: "RecoveryCodeHashes",
                table: "AspNetUsers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                comment: "字段：RecoveryCodeHashes",
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldComment: "恢复码哈希集合。");

            migrationBuilder.AlterColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：PhoneNumberConfirmed",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：PhoneNumberConfirmed。");

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：PhoneNumber",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：PhoneNumber。");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：PasswordHash",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：PasswordHash。");

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
                oldNullable: true,
                oldComment: "业务字段：NormalizedUserName。");

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
                oldNullable: true,
                oldComment: "业务字段：NormalizedEmail。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：LockoutEnd",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "业务字段：LockoutEnd。");

            migrationBuilder.AlterColumn<bool>(
                name: "LockoutEnabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：LockoutEnabled",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：LockoutEnabled。");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDisabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：IsDisabled",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "账号是否禁用。");

            migrationBuilder.AlterColumn<bool>(
                name: "EmailConfirmed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                comment: "字段：EmailConfirmed",
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "业务字段：EmailConfirmed。");

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
                oldNullable: true,
                oldComment: "业务字段：Email。");

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetUsers",
                type: "text",
                nullable: true,
                comment: "字段：ConcurrencyStamp",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ConcurrencyStamp。");

            migrationBuilder.AlterColumn<string>(
                name: "Bio",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：Bio",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "用户简介。");

            migrationBuilder.AlterColumn<string>(
                name: "AvatarUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                comment: "字段：AvatarUrl",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldComment: "头像地址。");

            migrationBuilder.AlterColumn<int>(
                name: "AccessFailedCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                comment: "字段：AccessFailedCount",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "业务字段：AccessFailedCount。");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetUsers",
                type: "text",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                comment: "字段：RoleId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "业务字段：RoleId。");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserRoles",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderDisplayName",
                table: "AspNetUserLogins",
                type: "text",
                nullable: true,
                comment: "字段：ProviderDisplayName",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ProviderDisplayName。");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "字段：ProviderKey",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "业务字段：ProviderKey。");

            migrationBuilder.AlterColumn<string>(
                name: "LoginProvider",
                table: "AspNetUserLogins",
                type: "text",
                nullable: false,
                comment: "字段：LoginProvider",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "业务字段：LoginProvider。");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "AspNetUserClaims",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimValue",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ClaimValue。");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetUserClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimType",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ClaimType。");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetUserClaims",
                type: "integer",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "主键 ID。")
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
                oldNullable: true,
                oldComment: "业务字段：NormalizedName。");

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
                oldNullable: true,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetRoles",
                type: "text",
                nullable: true,
                comment: "字段：ConcurrencyStamp",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ConcurrencyStamp。");

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "AspNetRoles",
                type: "text",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "主键 ID。");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: false,
                comment: "字段：RoleId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "业务字段：RoleId。");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimValue",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimValue",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ClaimValue。");

            migrationBuilder.AlterColumn<string>(
                name: "ClaimType",
                table: "AspNetRoleClaims",
                type: "text",
                nullable: true,
                comment: "字段：ClaimType",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "业务字段：ClaimType。");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "AspNetRoleClaims",
                type: "integer",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "主键 ID。")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "ApiCredentials",
                type: "text",
                nullable: false,
                comment: "字段：UserId",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "用户 ID。");

            migrationBuilder.AlterColumn<Guid>(
                name: "SiteId",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                comment: "字段：SiteId",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "所属站点 ID（租户隔离键）。");

            migrationBuilder.AlterColumn<string>(
                name: "SecurityStamp",
                table: "ApiCredentials",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                comment: "字段：SecurityStamp",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldComment: "安全戳（失效控制）。");

            migrationBuilder.AlterColumn<string>(
                name: "SecretHash",
                table: "ApiCredentials",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "字段：SecretHash",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "API 密钥哈希（不存明文）。");

            migrationBuilder.AlterColumn<int>(
                name: "Scope",
                table: "ApiCredentials",
                type: "integer",
                nullable: false,
                comment: "字段：Scope",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "权限范围（只读/读写）。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: true,
                comment: "字段：RevokedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "撤销时间。");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ApiCredentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "字段：Name",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "名称。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：ExpiresAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "过期时间。");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ApiCredentials",
                type: "timestamp with time zone",
                nullable: false,
                comment: "字段：CreatedAt",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "创建时间。");

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "ApiCredentials",
                type: "uuid",
                nullable: false,
                comment: "字段：Id",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "主键 ID。");
        }
    }
}
