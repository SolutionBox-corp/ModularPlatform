using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreditPackageTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "credit_packages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_packages_TenantId",
                table: "credit_packages",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_packages_TenantId",
                table: "credit_packages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "credit_packages");
        }
    }
}
