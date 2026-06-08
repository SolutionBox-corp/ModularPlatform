using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Gdpr.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGdpr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consent_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gdpr_audit_entries",
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
                    table.PrimaryKey("PK_gdpr_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subject_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WrappedDek = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consent_records_TenantId",
                table: "consent_records",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_consent_records_UserId_ConsentType",
                table: "consent_records",
                columns: new[] { "UserId", "ConsentType" });

            migrationBuilder.CreateIndex(
                name: "IX_gdpr_audit_entries_EntityType_EntityId",
                table: "gdpr_audit_entries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_gdpr_audit_entries_Timestamp",
                table: "gdpr_audit_entries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_gdpr_audit_entries_UserId",
                table: "gdpr_audit_entries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_keys_TenantId",
                table: "subject_keys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_keys_UserId",
                table: "subject_keys",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consent_records");

            migrationBuilder.DropTable(
                name: "gdpr_audit_entries");

            migrationBuilder.DropTable(
                name: "subject_keys");
        }
    }
}
