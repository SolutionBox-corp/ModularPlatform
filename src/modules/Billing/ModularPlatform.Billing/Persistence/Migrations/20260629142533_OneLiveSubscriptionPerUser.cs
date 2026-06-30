using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OneLiveSubscriptionPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscriptions_UserId",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_UserId",
                table: "subscriptions",
                column: "UserId",
                unique: true,
                filter: "\"Status\" <> 'Canceled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subscriptions_UserId",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_UserId",
                table: "subscriptions",
                column: "UserId");
        }
    }
}
