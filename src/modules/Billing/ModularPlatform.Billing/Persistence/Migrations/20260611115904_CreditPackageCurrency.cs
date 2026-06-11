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
                defaultValue: "");
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
