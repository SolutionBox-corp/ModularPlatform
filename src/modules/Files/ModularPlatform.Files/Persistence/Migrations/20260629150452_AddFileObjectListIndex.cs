using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileObjectListIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_objects_UserId",
                table: "file_objects");

            migrationBuilder.CreateIndex(
                name: "IX_file_objects_UserId_CreatedAt_Id",
                table: "file_objects",
                columns: new[] { "UserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_file_objects_UserId_CreatedAt_Id",
                table: "file_objects");

            migrationBuilder.CreateIndex(
                name: "IX_file_objects_UserId",
                table: "file_objects",
                column: "UserId");
        }
    }
}
