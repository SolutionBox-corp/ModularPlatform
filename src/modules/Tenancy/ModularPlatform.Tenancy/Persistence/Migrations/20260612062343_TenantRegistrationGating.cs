using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Tenancy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantRegistrationGating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegistrationMode",
                table: "tenants",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                // Secure default — any existing tenant (and any insert that omits the column) is invite-only, not the
                // empty string EF would otherwise back-fill (which is not a valid TenantRegistrationMode value).
                defaultValue: "InviteOnly");

            migrationBuilder.CreateTable(
                name: "tenant_invites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_invites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invites_TenantId",
                table: "tenant_invites",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invites_TokenHash",
                table: "tenant_invites",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_invites");

            migrationBuilder.DropColumn(
                name: "RegistrationMode",
                table: "tenants");
        }
    }
}
