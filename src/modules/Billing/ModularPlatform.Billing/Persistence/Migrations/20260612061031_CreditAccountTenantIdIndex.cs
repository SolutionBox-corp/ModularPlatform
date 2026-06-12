using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditAccountTenantIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_credit_accounts_TenantId",
                table: "credit_accounts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_accounts_TenantId",
                table: "credit_accounts");
        }
    }
}
