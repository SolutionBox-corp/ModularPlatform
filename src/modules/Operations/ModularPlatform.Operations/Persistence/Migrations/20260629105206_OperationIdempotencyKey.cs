using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Operations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "operations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_operations_UserId_Type_IdempotencyKey",
                table: "operations",
                columns: new[] { "UserId", "Type", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_operations_UserId_Type_IdempotencyKey",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "operations");
        }
    }
}
