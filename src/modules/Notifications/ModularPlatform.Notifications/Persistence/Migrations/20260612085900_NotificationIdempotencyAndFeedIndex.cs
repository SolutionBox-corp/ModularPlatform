using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationIdempotencyAndFeedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId",
                table: "notifications");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "notifications",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_IdempotencyKey",
                table: "notifications",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_IdempotencyKey",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserId_CreatedAt",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId",
                table: "notifications",
                column: "UserId");
        }
    }
}
