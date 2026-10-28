using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteIsolationAndApiCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_Categories_CategoriesId",
                table: "ContentCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_Contents_ContentsId",
                table: "ContentCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentEmbeddings_Contents_ContentId",
                table: "ContentEmbeddings");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentRevisions_Contents_ContentId",
                table: "ContentRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Contents_Contents_ParentId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_Contents_MediaAssets_CoverMediaId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentTags_Contents_ContentsId",
                table: "ContentTags");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentTags_Tags_TagsId",
                table: "ContentTags");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentViews_Contents_ContentId",
                table: "ContentViews");

            migrationBuilder.DropForeignKey(
                name: "FK_EmbeddingJobs_Contents_ContentId",
                table: "EmbeddingJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_Contents_HomePageContentId",
                table: "Settings");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_MediaAssets_SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropForeignKey(
                name: "FK_SlugRedirects_Contents_ContentId",
                table: "SlugRedirects");

            migrationBuilder.DropIndex(
                name: "IX_Tags_Slug",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_SlugRedirects_ContentId",
                table: "SlugRedirects");

            migrationBuilder.DropIndex(
                name: "IX_SlugRedirects_OldSlug",
                table: "SlugRedirects");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_HomePageContentId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_FileName",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_UploadedAt_Id",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_EmbeddingJobs_ContentId",
                table: "EmbeddingJobs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ContentTags",
                table: "ContentTags");

            migrationBuilder.DropIndex(
                name: "IX_ContentTags_TagsId",
                table: "ContentTags");

            migrationBuilder.DropIndex(
                name: "IX_Contents_CoverMediaId",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_Kind_Status_PublishedAt_Id",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_ParentId",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_Slug",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_Status",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_ContentRevisions_ContentId_CreatedAt",
                table: "ContentRevisions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ContentCategories",
                table: "ContentCategories");

            migrationBuilder.DropIndex(
                name: "IX_ContentCategories_ContentsId",
                table: "ContentCategories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SingletonKey",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Admin_Singleton",
                table: "AspNetUsers");

            // Settings 的 PK 改變不代表要重建種子列；保留使用者的名稱、首頁、圖示與版本。
            // 全部既有資料（包括 join）先回填預設站點，再建立同站複合 FK。
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Tags",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "SlugRedirects",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Settings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "MediaAssets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "EmbeddingJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ContentViews",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ContentTags",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Contents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ContentRevisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ContentEmbeddings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "ContentCategories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            // 回填用 DEFAULT 不留在執行期；之後每筆寫入都須明確提供 SiteId。
            migrationBuilder.Sql("""
                ALTER TABLE "Tags" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "SlugRedirects" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "Settings" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "MediaAssets" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "EmbeddingJobs" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "ContentViews" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "ContentTags" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "Contents" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "ContentRevisions" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "ContentEmbeddings" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "ContentCategories" ALTER COLUMN "SiteId" DROP DEFAULT;
                ALTER TABLE "Categories" ALTER COLUMN "SiteId" DROP DEFAULT;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SingletonKey",
                table: "AspNetUsers",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<bool>(
                name: "IsDisabled",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Tags_SiteId_Id",
                table: "Tags",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MediaAssets_SiteId_Id",
                table: "MediaAssets",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContentTags",
                table: "ContentTags",
                columns: new[] { "SiteId", "ContentsId", "TagsId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Contents_SiteId_Id",
                table: "Contents",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContentCategories",
                table: "ContentCategories",
                columns: new[] { "SiteId", "CategoriesId", "ContentsId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Categories_SiteId_Id",
                table: "Categories",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, collation: "C"),
                    HostName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true, collation: "C"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                    table.CheckConstraint("CK_Site_DefaultActive", "\"Id\" <> '11111111-1111-1111-1111-111111111111'::uuid OR \"IsActive\"");
                    table.CheckConstraint("CK_Site_DefaultHost", "\"HostName\" IS NOT NULL OR \"Id\" = '11111111-1111-1111-1111-111111111111'::uuid");
                    table.CheckConstraint("CK_Site_HostName", "\"HostName\" IS NULL OR \"HostName\" ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$'");
                    table.CheckConstraint("CK_Site_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Site_Slug", "length(btrim(\"Slug\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "ApiCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SecurityStamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiCredentials", x => x.Id);
                    table.CheckConstraint("CK_ApiCredential_ExpiresAt", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_ApiCredential_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ApiCredential_Scope", "\"Scope\" IN (0, 1)");
                    table.CheckConstraint("CK_ApiCredential_SecretHash", "\"SecretHash\" ~ '^[0-9a-fA-F]{64}$'");
                    table.CheckConstraint("CK_ApiCredential_SecurityStamp", "length(\"SecurityStamp\") > 0");
                    table.ForeignKey(
                        name: "FK_ApiCredentials_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApiCredentials_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteMemberships",
                columns: table => new
                {
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteMemberships", x => new { x.SiteId, x.UserId });
                    table.CheckConstraint("CK_SiteMembership_Role", "\"Role\" IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_SiteMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteMemberships_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Sites",
                columns: new[] { "Id", "CreatedAt", "HostName", "IsActive", "Name", "Slug", "Version" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), new DateTimeOffset(new DateTime(2026, 10, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Origami", "default", new Guid("134d979d-707b-40b1-bd78-95d96033fd85") });

            // 若種子設定意外缺失才補建，絕不覆寫既有設定。
            migrationBuilder.Sql("""
                INSERT INTO "Settings" ("SiteId", "Id", "Name", "Description", "AuthorDisplayName", "Version")
                VALUES ('11111111-1111-1111-1111-111111111111', 1, 'Origami', '用文字，分享你的世界。', '',
                        '134d979d-707b-40b1-bd78-95d96033fd84')
                ON CONFLICT ("SiteId", "Id") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SiteId_Slug",
                table: "Tags",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlugRedirects_SiteId_ContentId",
                table: "SlugRedirects",
                columns: new[] { "SiteId", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SlugRedirects_SiteId_OldSlug",
                table: "SlugRedirects",
                columns: new[] { "SiteId", "OldSlug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_SiteId_HomePageContentId",
                table: "Settings",
                columns: new[] { "SiteId", "HomePageContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_SiteId_SiteIconMediaId",
                table: "Settings",
                columns: new[] { "SiteId", "SiteIconMediaId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_SiteId_FileName",
                table: "MediaAssets",
                columns: new[] { "SiteId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_SiteId_UploadedAt_Id",
                table: "MediaAssets",
                columns: new[] { "SiteId", "UploadedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingJobs_SiteId_ContentId",
                table: "EmbeddingJobs",
                columns: new[] { "SiteId", "ContentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentViews_SiteId_ContentId",
                table: "ContentViews",
                columns: new[] { "SiteId", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentTags_SiteId_TagsId",
                table: "ContentTags",
                columns: new[] { "SiteId", "TagsId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_CoverMediaId",
                table: "Contents",
                columns: new[] { "SiteId", "CoverMediaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_Kind_Status_PublishedAt_Id",
                table: "Contents",
                columns: new[] { "SiteId", "Kind", "Status", "PublishedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_ParentId",
                table: "Contents",
                columns: new[] { "SiteId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_Slug",
                table: "Contents",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_Status",
                table: "Contents",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentRevisions_SiteId_ContentId_CreatedAt",
                table: "ContentRevisions",
                columns: new[] { "SiteId", "ContentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEmbeddings_SiteId_ContentId",
                table: "ContentEmbeddings",
                columns: new[] { "SiteId", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentCategories_SiteId_ContentsId",
                table: "ContentCategories",
                columns: new[] { "SiteId", "ContentsId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SiteId_Slug",
                table: "Categories",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SingletonKey",
                table: "AspNetUsers",
                column: "SingletonKey",
                unique: true,
                filter: "\"SingletonKey\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Admin_Singleton",
                table: "AspNetUsers",
                sql: "\"SingletonKey\" IS NULL OR \"SingletonKey\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_SiteId_UserId_CreatedAt",
                table: "ApiCredentials",
                columns: new[] { "SiteId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiCredentials_UserId",
                table: "ApiCredentials",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteMemberships_UserId",
                table: "SiteMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_HostName",
                table: "Sites",
                column: "HostName",
                unique: true,
                filter: "\"HostName\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Slug",
                table: "Sites",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Sites_SiteId",
                table: "Categories",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_Categories_SiteId_CategoriesId",
                table: "ContentCategories",
                columns: new[] { "SiteId", "CategoriesId" },
                principalTable: "Categories",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_Contents_SiteId_ContentsId",
                table: "ContentCategories",
                columns: new[] { "SiteId", "ContentsId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_Sites_SiteId",
                table: "ContentCategories",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentEmbeddings_Contents_SiteId_ContentId",
                table: "ContentEmbeddings",
                columns: new[] { "SiteId", "ContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentEmbeddings_Sites_SiteId",
                table: "ContentEmbeddings",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentRevisions_Contents_SiteId_ContentId",
                table: "ContentRevisions",
                columns: new[] { "SiteId", "ContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentRevisions_Sites_SiteId",
                table: "ContentRevisions",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_Contents_SiteId_ParentId",
                table: "Contents",
                columns: new[] { "SiteId", "ParentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_MediaAssets_SiteId_CoverMediaId",
                table: "Contents",
                columns: new[] { "SiteId", "CoverMediaId" },
                principalTable: "MediaAssets",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_Sites_SiteId",
                table: "Contents",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentTags_Contents_SiteId_ContentsId",
                table: "ContentTags",
                columns: new[] { "SiteId", "ContentsId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentTags_Sites_SiteId",
                table: "ContentTags",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentTags_Tags_SiteId_TagsId",
                table: "ContentTags",
                columns: new[] { "SiteId", "TagsId" },
                principalTable: "Tags",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentViews_Contents_SiteId_ContentId",
                table: "ContentViews",
                columns: new[] { "SiteId", "ContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentViews_Sites_SiteId",
                table: "ContentViews",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmbeddingJobs_Contents_SiteId_ContentId",
                table: "EmbeddingJobs",
                columns: new[] { "SiteId", "ContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmbeddingJobs_Sites_SiteId",
                table: "EmbeddingJobs",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaAssets_Sites_SiteId",
                table: "MediaAssets",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_Contents_SiteId_HomePageContentId",
                table: "Settings",
                columns: new[] { "SiteId", "HomePageContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_MediaAssets_SiteId_SiteIconMediaId",
                table: "Settings",
                columns: new[] { "SiteId", "SiteIconMediaId" },
                principalTable: "MediaAssets",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_Sites_SiteId",
                table: "Settings",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SlugRedirects_Contents_SiteId_ContentId",
                table: "SlugRedirects",
                columns: new[] { "SiteId", "ContentId" },
                principalTable: "Contents",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SlugRedirects_Sites_SiteId",
                table: "SlugRedirects",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_Sites_SiteId",
                table: "Tags",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 多站點／帳號／憑證已投入使用後，不可用降版合併隔離資料或刪掉權限資料。
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Sites" WHERE "Id" <> '11111111-1111-1111-1111-111111111111'::uuid)
                       OR EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "SingletonKey" IS NULL OR "IsDisabled")
                       OR EXISTS (SELECT 1 FROM "SiteMemberships")
                       OR EXISTS (SELECT 1 FROM "ApiCredentials") THEN
                        RAISE EXCEPTION 'Cannot downgrade tenancy after sites, users, memberships or credentials have been created; preserve them before a planned rollback.';
                    END IF;
                END $migration$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Sites_SiteId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_Categories_SiteId_CategoriesId",
                table: "ContentCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_Contents_SiteId_ContentsId",
                table: "ContentCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentCategories_Sites_SiteId",
                table: "ContentCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentEmbeddings_Contents_SiteId_ContentId",
                table: "ContentEmbeddings");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentEmbeddings_Sites_SiteId",
                table: "ContentEmbeddings");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentRevisions_Contents_SiteId_ContentId",
                table: "ContentRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentRevisions_Sites_SiteId",
                table: "ContentRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Contents_Contents_SiteId_ParentId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_Contents_MediaAssets_SiteId_CoverMediaId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_Contents_Sites_SiteId",
                table: "Contents");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentTags_Contents_SiteId_ContentsId",
                table: "ContentTags");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentTags_Sites_SiteId",
                table: "ContentTags");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentTags_Tags_SiteId_TagsId",
                table: "ContentTags");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentViews_Contents_SiteId_ContentId",
                table: "ContentViews");

            migrationBuilder.DropForeignKey(
                name: "FK_ContentViews_Sites_SiteId",
                table: "ContentViews");

            migrationBuilder.DropForeignKey(
                name: "FK_EmbeddingJobs_Contents_SiteId_ContentId",
                table: "EmbeddingJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_EmbeddingJobs_Sites_SiteId",
                table: "EmbeddingJobs");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaAssets_Sites_SiteId",
                table: "MediaAssets");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_Contents_SiteId_HomePageContentId",
                table: "Settings");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_MediaAssets_SiteId_SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropForeignKey(
                name: "FK_Settings_Sites_SiteId",
                table: "Settings");

            migrationBuilder.DropForeignKey(
                name: "FK_SlugRedirects_Contents_SiteId_ContentId",
                table: "SlugRedirects");

            migrationBuilder.DropForeignKey(
                name: "FK_SlugRedirects_Sites_SiteId",
                table: "SlugRedirects");

            migrationBuilder.DropForeignKey(
                name: "FK_Tags_Sites_SiteId",
                table: "Tags");

            migrationBuilder.DropTable(
                name: "ApiCredentials");

            migrationBuilder.DropTable(
                name: "SiteMemberships");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Tags_SiteId_Id",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_SiteId_Slug",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_SlugRedirects_SiteId_ContentId",
                table: "SlugRedirects");

            migrationBuilder.DropIndex(
                name: "IX_SlugRedirects_SiteId_OldSlug",
                table: "SlugRedirects");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_SiteId_HomePageContentId",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_Settings_SiteId_SiteIconMediaId",
                table: "Settings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MediaAssets_SiteId_Id",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_SiteId_FileName",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_SiteId_UploadedAt_Id",
                table: "MediaAssets");

            migrationBuilder.DropIndex(
                name: "IX_EmbeddingJobs_SiteId_ContentId",
                table: "EmbeddingJobs");

            migrationBuilder.DropIndex(
                name: "IX_ContentViews_SiteId_ContentId",
                table: "ContentViews");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ContentTags",
                table: "ContentTags");

            migrationBuilder.DropIndex(
                name: "IX_ContentTags_SiteId_TagsId",
                table: "ContentTags");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Contents_SiteId_Id",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_CoverMediaId",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_Kind_Status_PublishedAt_Id",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_ParentId",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_Slug",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_Status",
                table: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_ContentRevisions_SiteId_ContentId_CreatedAt",
                table: "ContentRevisions");

            migrationBuilder.DropIndex(
                name: "IX_ContentEmbeddings_SiteId_ContentId",
                table: "ContentEmbeddings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ContentCategories",
                table: "ContentCategories");

            migrationBuilder.DropIndex(
                name: "IX_ContentCategories_SiteId_ContentsId",
                table: "ContentCategories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Categories_SiteId_Id",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SiteId_Slug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SingletonKey",
                table: "AspNetUsers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Admin_Singleton",
                table: "AspNetUsers");

            // 降版同樣保留設定列；前置檢查已確保只有預設站點。
            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "SlugRedirects");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "EmbeddingJobs");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContentViews");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContentTags");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContentRevisions");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContentEmbeddings");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContentCategories");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsDisabled",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Settings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "SingletonKey",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContentTags",
                table: "ContentTags",
                columns: new[] { "ContentsId", "TagsId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContentCategories",
                table: "ContentCategories",
                columns: new[] { "CategoriesId", "ContentsId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlugRedirects_ContentId",
                table: "SlugRedirects",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_SlugRedirects_OldSlug",
                table: "SlugRedirects",
                column: "OldSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Settings_HomePageContentId",
                table: "Settings",
                column: "HomePageContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_SiteIconMediaId",
                table: "Settings",
                column: "SiteIconMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_FileName",
                table: "MediaAssets",
                column: "FileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_UploadedAt_Id",
                table: "MediaAssets",
                columns: new[] { "UploadedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_EmbeddingJobs_ContentId",
                table: "EmbeddingJobs",
                column: "ContentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentTags_TagsId",
                table: "ContentTags",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_CoverMediaId",
                table: "Contents",
                column: "CoverMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_Kind_Status_PublishedAt_Id",
                table: "Contents",
                columns: new[] { "Kind", "Status", "PublishedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_ParentId",
                table: "Contents",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_Slug",
                table: "Contents",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contents_Status",
                table: "Contents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ContentRevisions_ContentId_CreatedAt",
                table: "ContentRevisions",
                columns: new[] { "ContentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentCategories_ContentsId",
                table: "ContentCategories",
                column: "ContentsId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SingletonKey",
                table: "AspNetUsers",
                column: "SingletonKey",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Admin_Singleton",
                table: "AspNetUsers",
                sql: "\"SingletonKey\" = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_Categories_CategoriesId",
                table: "ContentCategories",
                column: "CategoriesId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentCategories_Contents_ContentsId",
                table: "ContentCategories",
                column: "ContentsId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentEmbeddings_Contents_ContentId",
                table: "ContentEmbeddings",
                column: "ContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentRevisions_Contents_ContentId",
                table: "ContentRevisions",
                column: "ContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_Contents_ParentId",
                table: "Contents",
                column: "ParentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_MediaAssets_CoverMediaId",
                table: "Contents",
                column: "CoverMediaId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentTags_Contents_ContentsId",
                table: "ContentTags",
                column: "ContentsId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentTags_Tags_TagsId",
                table: "ContentTags",
                column: "TagsId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ContentViews_Contents_ContentId",
                table: "ContentViews",
                column: "ContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EmbeddingJobs_Contents_ContentId",
                table: "EmbeddingJobs",
                column: "ContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_Contents_HomePageContentId",
                table: "Settings",
                column: "HomePageContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_MediaAssets_SiteIconMediaId",
                table: "Settings",
                column: "SiteIconMediaId",
                principalTable: "MediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SlugRedirects_Contents_ContentId",
                table: "SlugRedirects",
                column: "ContentId",
                principalTable: "Contents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
