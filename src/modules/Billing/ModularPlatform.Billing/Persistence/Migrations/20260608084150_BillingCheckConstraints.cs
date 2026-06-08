using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_credit_accounts_available_non_negative",
                table: "credit_accounts",
                sql: "\"Available\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_credit_accounts_pending_non_negative",
                table: "credit_accounts",
                sql: "\"Pending\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_credit_accounts_posted_non_negative",
                table: "credit_accounts",
                sql: "\"Posted\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_credit_accounts_available_non_negative",
                table: "credit_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_credit_accounts_pending_non_negative",
                table: "credit_accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_credit_accounts_posted_non_negative",
                table: "credit_accounts");
        }
    }
}
