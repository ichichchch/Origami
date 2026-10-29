using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFields", x => x.Id);
                    table.UniqueConstraint("AK_CustomFields_SiteId_Id", x => new { x.SiteId, x.Id });
                    table.CheckConstraint("CK_CustomField_Key", "length(btrim(\"Key\")) > 0");
                    table.CheckConstraint("CK_CustomField_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_CustomField_Type", "\"FieldType\" IN ('text', 'longtext', 'number', 'date', 'checkbox')");
                    table.ForeignKey(
                        name: "FK_CustomFields_ContentTypes_SiteId_ContentTypeId",
                        columns: x => new { x.SiteId, x.ContentTypeId },
                        principalTable: "ContentTypes",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomFields_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentFieldValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentFieldValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentFieldValues_Contents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "Contents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentFieldValues_Contents_SiteId_ContentId",
                        columns: x => new { x.SiteId, x.ContentId },
                        principalTable: "Contents",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentFieldValues_CustomFields_SiteId_FieldId",
                        columns: x => new { x.SiteId, x.FieldId },
                        principalTable: "CustomFields",
                        principalColumns: new[] { "SiteId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentFieldValues_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentFieldValues_ContentId",
                table: "ContentFieldValues",
                column: "ContentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentFieldValues_SiteId_ContentId_FieldId",
                table: "ContentFieldValues",
                columns: new[] { "SiteId", "ContentId", "FieldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentFieldValues_SiteId_FieldId",
                table: "ContentFieldValues",
                columns: new[] { "SiteId", "FieldId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_SiteId_ContentTypeId_Key",
                table: "CustomFields",
                columns: new[] { "SiteId", "ContentTypeId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentFieldValues");

            migrationBuilder.DropTable(
                name: "CustomFields");
        }
    }
}
