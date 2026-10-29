using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "SiteMemberships",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AuthorId",
                table: "Contents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorName",
                table: "Contents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SiteMembership_DisplayName",
                table: "SiteMemberships",
                sql: "\"DisplayName\" = btrim(\"DisplayName\")");

            migrationBuilder.CreateIndex(
                name: "IX_Contents_AuthorId",
                table: "Contents",
                column: "AuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contents_AspNetUsers_AuthorId",
                table: "Contents",
                column: "AuthorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contents_AspNetUsers_AuthorId",
                table: "Contents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SiteMembership_DisplayName",
                table: "SiteMemberships");

            migrationBuilder.DropIndex(
                name: "IX_Contents_AuthorId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "SiteMemberships");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Contents");

            migrationBuilder.DropColumn(
                name: "AuthorName",
                table: "Contents");
        }
    }
}
