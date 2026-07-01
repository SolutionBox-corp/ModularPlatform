using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MachineTokenRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "machine_token_issuances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenId",
                table: "machine_token_issuances",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_machine_token_issuances_TokenId",
                table: "machine_token_issuances",
                column: "TokenId",
                unique: true,
                filter: "\"TokenId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_machine_token_issuances_TokenId",
                table: "machine_token_issuances");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "machine_token_issuances");

            migrationBuilder.DropColumn(
                name: "TokenId",
                table: "machine_token_issuances");
        }
    }
}
