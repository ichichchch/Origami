using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Origami.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentReplySubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubscriberEmail",
                table: "Comments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                comment: "回复通知订阅邮箱。");

            migrationBuilder.AddColumn<Guid>(
                name: "UnsubscribeToken",
                table: "Comments",
                type: "uuid",
                nullable: true,
                comment: "退订一次性标识。");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriberEmail",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "UnsubscribeToken",
                table: "Comments");
        }
    }
}
