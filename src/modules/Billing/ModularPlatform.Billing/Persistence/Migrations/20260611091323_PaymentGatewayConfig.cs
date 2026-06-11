using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentGatewayConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plane = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    WebhookToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GoPayGoid = table.Column<long>(type: "bigint", nullable: true),
                    Sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_configurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_secrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyVersion = table.Column<int>(type: "integer", nullable: false),
                    Ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    WrappedDek = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_secrets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_configurations_TenantId_Plane",
                table: "payment_configurations",
                columns: new[] { "TenantId", "Plane" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenant_secrets_TenantId_Purpose",
                table: "tenant_secrets",
                columns: new[] { "TenantId", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_configurations");

            migrationBuilder.DropTable(
                name: "tenant_secrets");
        }
    }
}
