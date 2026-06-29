using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Marketing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVibeConversationLastMessageAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vibe_conversations_UserId_CreatedAt",
                table: "vibe_conversations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastMessageAt",
                table: "vibe_conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vibe_conversations_UserId_LastMessageAt_CreatedAt",
                table: "vibe_conversations",
                columns: new[] { "UserId", "LastMessageAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vibe_conversations_UserId_LastMessageAt_CreatedAt",
                table: "vibe_conversations");

            migrationBuilder.DropColumn(
                name: "LastMessageAt",
                table: "vibe_conversations");

            migrationBuilder.CreateIndex(
                name: "IX_vibe_conversations_UserId_CreatedAt",
                table: "vibe_conversations",
                columns: new[] { "UserId", "CreatedAt" });
        }
    }
}
