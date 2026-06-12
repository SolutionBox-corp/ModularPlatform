using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Gdpr.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubjectKeyWrappedDekVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DekKeyVersion",
                table: "subject_keys",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DekKeyVersion",
                table: "subject_keys");
        }
    }
}
