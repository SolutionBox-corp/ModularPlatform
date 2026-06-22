using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Gdpr.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GdprConsentPolicyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PolicyVersion",
                table: "consent_records",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PolicyVersion",
                table: "consent_records");
        }
    }
}
