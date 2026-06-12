using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Tenancy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantInvitePerTenantUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenant_invites_TenantId",
                table: "tenant_invites");

            migrationBuilder.DropIndex(
                name: "IX_tenant_invites_TokenHash",
                table: "tenant_invites");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_invites_TenantId_TokenHash",
                table: "tenant_invites",
                columns: new[] { "TenantId", "TokenHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tenant_invites_TenantId_TokenHash",
                table: "tenant_invites");

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
    }
}
