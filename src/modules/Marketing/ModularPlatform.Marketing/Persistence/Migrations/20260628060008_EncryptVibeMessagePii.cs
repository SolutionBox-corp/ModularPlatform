using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Marketing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EncryptVibeMessagePii : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ToolCallsJson",
                table: "vibe_messages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ToolCallsJson",
                table: "vibe_messages",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
