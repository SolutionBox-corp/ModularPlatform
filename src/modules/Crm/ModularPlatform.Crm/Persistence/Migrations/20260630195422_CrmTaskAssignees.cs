using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmTaskAssignees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId",
                table: "crm_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_AssigneeUserId",
                table: "crm_tasks",
                column: "AssigneeUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_tasks_AssigneeUserId",
                table: "crm_tasks");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "crm_tasks");
        }
    }
}
