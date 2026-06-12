using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditPackageCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "credit_packages",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                // Back-fill pre-existing rows with a real ISO-4217 default — an empty string is rejected by every
                // payment gateway, which would silently break checkout for packages created before this column existed.
                defaultValue: "EUR");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "credit_packages");
        }
    }
}
