using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MachineTokenIssuances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_token_issuances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_machine_token_issuances", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_machine_token_issuances_CreatedAt",
                table: "machine_token_issuances",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_machine_token_issuances_MachineSubjectId",
                table: "machine_token_issuances",
                column: "MachineSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_machine_token_issuances_TargetTenantId",
                table: "machine_token_issuances",
                column: "TargetTenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "machine_token_issuances");
        }
    }
}
