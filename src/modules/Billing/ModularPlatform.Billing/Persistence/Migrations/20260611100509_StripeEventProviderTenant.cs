using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StripeEventProviderTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "stripe_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "stripe");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "stripe_events",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "stripe_events");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "stripe_events");
        }
    }
}
