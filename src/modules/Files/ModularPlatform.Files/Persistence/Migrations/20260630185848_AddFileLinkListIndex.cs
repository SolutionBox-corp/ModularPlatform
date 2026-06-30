using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileLinkListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_links_UserId_OwnerType_OwnerId",
                table: "file_links");

            migrationBuilder.CreateIndex(
                name: "IX_file_links_UserId_OwnerType_OwnerId_CreatedAt_Id",
                table: "file_links",
                columns: new[] { "UserId", "OwnerType", "OwnerId", "CreatedAt", "Id" },
                descending: new[] { false, false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_links_UserId_OwnerType_OwnerId_CreatedAt_Id",
                table: "file_links");

            migrationBuilder.CreateIndex(
                name: "IX_file_links_UserId_OwnerType_OwnerId",
                table: "file_links",
                columns: new[] { "UserId", "OwnerType", "OwnerId" });
        }
    }
}
