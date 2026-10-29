using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContentTypeId",
                table: "Contents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SupportsCategories = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsTags = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsHierarchy = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTypes", x => x.Id);
                    table.UniqueConstraint("AK_ContentTypes_SiteId_Id", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_ContentType_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_ContentType_Slug", "length(btrim(\"Slug\")) > 0");
                    table.ForeignKey(
                        name: "FK_ContentTypes_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomTaxonomies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsHierarchical = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomTaxonomies", x => x.Id);
                    table.UniqueConstraint("AK_CustomTaxonomies_SiteId_Id", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_CustomTaxonomy_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_CustomTaxonomy_Slug", "length(btrim(\"Slug\")) > 0");
                    table.ForeignKey(
                        name: "FK_CustomTaxonomies_ContentTypes_SiteId_ContentTypeId",
                        columns: x => new { x.SiteId, x.ContentTypeId },
                        principalTable: "ContentTypes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomTaxonomies_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomTaxonomyTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomTaxonomyTerms", x => x.Id);
                    table.UniqueConstraint("AK_CustomTaxonomyTerms_SiteId_Id", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_CustomTerm_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_CustomTerm_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
                    table.CheckConstraint("CK_CustomTerm_Slug", "length(btrim(\"Slug\")) > 0");
                    table.ForeignKey(
                        name: "FK_CustomTaxonomyTerms_CustomTaxonomies_SiteId_TaxonomyId",
                        columns: x => new { x.SiteId, x.TaxonomyId },
                        principalTable: "CustomTaxonomies",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomTaxonomyTerms_CustomTaxonomyTerms_SiteId_ParentId",
                        columns: x => new { x.SiteId, x.ParentId },
                        principalTable: "CustomTaxonomyTerms",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomTaxonomyTerms_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentCustomTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCustomTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentCustomTerms_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentCustomTerms_Contents_SiteId_ContentId",
                        columns: x => new { x.SiteId, x.ContentId },
                        principalTable: "Contents",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentCustomTerms_CustomTaxonomyTerms_SiteId_TermId",
                        columns: x => new { x.SiteId, x.TermId },
                        principalTable: "CustomTaxonomyTerms",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentCustomTerms_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contents_SiteId_ContentTypeId_Status_PublishedAt_Id",
                table: "Contents",
                columns: new[] { "SiteId", "ContentTypeId", "Status", "PublishedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentCustomTerms_ContentId",
                table: "ContentCustomTerms",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentCustomTerms_SiteId_ContentId_TermId",
                table: "ContentCustomTerms",
                columns: new[] { "SiteId", "ContentId", "TermId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentCustomTerms_SiteId_TermId",
                table: "ContentCustomTerms",
                columns: new[] { "SiteId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentTypes_SiteId_Slug",
                table: "ContentTypes",
                columns: new[] { "SiteId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomTaxonomies_SiteId_ContentTypeId_Slug",
                table: "CustomTaxonomies",
                columns: new[] { "SiteId", "ContentTypeId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomTaxonomyTerms_SiteId_ParentId",
                table: "CustomTaxonomyTerms",
                columns: new[] { "SiteId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomTaxonomyTerms_SiteId_TaxonomyId_Slug",
                table: "CustomTaxonomyTerms",
                columns: new[] { "SiteId", "TaxonomyId", "Slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_ContentTypes_SiteId_ContentTypeId",
                table: "Contents",
                columns: new[] { "SiteId", "ContentTypeId" },
                principalTable: "ContentTypes",
                principalColumns: new[] { "SiteId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contents_ContentTypes_SiteId_ContentTypeId",
                table: "Contents");

            migrationBuilder.DropTable(
                name: "ContentCustomTerms");

            migrationBuilder.DropTable(
                name: "CustomTaxonomyTerms");

            migrationBuilder.DropTable(
                name: "CustomTaxonomies");

            migrationBuilder.DropTable(
                name: "ContentTypes");

            migrationBuilder.DropIndex(
                name: "IX_Contents_SiteId_ContentTypeId_Status_PublishedAt_Id",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "ContentTypeId",
                table: "Contents");
        }
    }
}
