using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Gdpr.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsentRecordTenantScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "consent_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_consent_records_TenantId",
                table: "consent_records",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_consent_records_TenantId",
                table: "consent_records");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "consent_records");
        }
    }
}
