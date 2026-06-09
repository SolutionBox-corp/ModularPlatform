using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Gdpr.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropGdprTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subject_keys_TenantId",
                table: "subject_keys");

            migrationBuilder.DropIndex(
                name: "IX_consent_records_TenantId",
                table: "consent_records");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "subject_keys");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "consent_records");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "subject_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "consent_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subject_keys_TenantId",
                table: "subject_keys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_consent_records_TenantId",
                table: "consent_records",
                column: "TenantId");
        }
    }
}
