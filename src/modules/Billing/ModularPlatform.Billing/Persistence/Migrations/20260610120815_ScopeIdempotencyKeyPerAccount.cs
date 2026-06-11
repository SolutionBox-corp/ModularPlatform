using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyKeyPerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_entries_IdempotencyKey",
                table: "credit_entries");

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_AccountId_IdempotencyKey",
                table: "credit_entries",
                columns: new[] { "AccountId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_entries_AccountId_IdempotencyKey",
                table: "credit_entries");

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_IdempotencyKey",
                table: "credit_entries",
                column: "IdempotencyKey",
                unique: true);
        }
    }
}
