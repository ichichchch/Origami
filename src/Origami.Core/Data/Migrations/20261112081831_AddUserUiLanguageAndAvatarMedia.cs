using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserUiLanguageAndAvatarMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AvatarMediaId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true,
                comment: "本机头像的媒体资产引用。");

            migrationBuilder.AddColumn<string>(
                name: "UiLanguage",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                comment: "每使用者界面语言偏好。");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarMediaId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UiLanguage",
                table: "AspNetUsers");
        }
    }
}
