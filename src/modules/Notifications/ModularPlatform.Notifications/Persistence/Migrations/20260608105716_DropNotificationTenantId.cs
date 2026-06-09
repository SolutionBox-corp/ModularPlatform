using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropNotificationTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_TenantId",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "notifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_TenantId",
                table: "notifications",
                column: "TenantId");
        }
    }
}
