using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChangedColumns = table.Column<string>(type: "jsonb", nullable: false),
                    NewValues = table.Column<string>(type: "jsonb", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "credit_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Posted = table.Column<long>(type: "bigint", nullable: false),
                    Pending = table.Column<long>(type: "bigint", nullable: false),
                    Available = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "credit_buckets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Remaining = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_buckets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "credit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BucketId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "credit_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_holds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "credit_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreditAmount = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BucketExpiryDays = table.Column<int>(type: "integer", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stripe_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeEventId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stripe_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_audit_entries_EntityType_EntityId",
                table: "billing_audit_entries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_billing_audit_entries_Timestamp",
                table: "billing_audit_entries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_billing_audit_entries_UserId",
                table: "billing_audit_entries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_accounts_TenantId",
                table: "credit_accounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_accounts_UserId",
                table: "credit_accounts",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_buckets_AccountId",
                table: "credit_buckets",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_buckets_ExpiresAt",
                table: "credit_buckets",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_AccountId",
                table: "credit_entries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_IdempotencyKey",
                table: "credit_entries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_entries_TransactionId",
                table: "credit_entries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_holds_AccountId",
                table: "credit_holds",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_holds_AccountId_Status",
                table: "credit_holds",
                columns: new[] { "AccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_packages_StripePriceId",
                table: "credit_packages",
                column: "StripePriceId");

            migrationBuilder.CreateIndex(
                name: "IX_stripe_events_StripeEventId",
                table: "stripe_events",
                column: "StripeEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_audit_entries");

            migrationBuilder.DropTable(
                name: "credit_accounts");

            migrationBuilder.DropTable(
                name: "credit_buckets");

            migrationBuilder.DropTable(
                name: "credit_entries");

            migrationBuilder.DropTable(
                name: "credit_holds");

            migrationBuilder.DropTable(
                name: "credit_packages");

            migrationBuilder.DropTable(
                name: "stripe_events");
        }
    }
}
