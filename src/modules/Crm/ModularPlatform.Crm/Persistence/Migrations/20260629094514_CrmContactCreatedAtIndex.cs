using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmContactCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_UserId_CreatedAt",
                table: "crm_contacts",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_contacts_UserId_CreatedAt",
                table: "crm_contacts");
        }
    }
}
