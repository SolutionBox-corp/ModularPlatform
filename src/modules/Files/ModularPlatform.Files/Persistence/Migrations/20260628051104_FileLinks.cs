using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Files.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FileLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_links", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_links_UserId",
                table: "file_links",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_file_links_UserId_OwnerType_OwnerId",
                table: "file_links",
                columns: new[] { "UserId", "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_file_links_UserId_OwnerType_OwnerId_FileObjectId",
                table: "file_links",
                columns: new[] { "UserId", "OwnerType", "OwnerId", "FileObjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_links");
        }
    }
}
