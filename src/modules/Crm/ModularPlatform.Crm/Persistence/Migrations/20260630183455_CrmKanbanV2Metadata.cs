using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmKanbanV2Metadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "crm_kanban_columns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "#94A3B8");

            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "crm_kanban_columns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unstarted");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "crm_kanban_columns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WipLimit",
                table: "crm_kanban_columns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeUserId",
                table: "crm_kanban_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Labels",
                table: "crm_kanban_cards",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "MeetingId",
                table: "crm_kanban_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "crm_kanban_cards",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartAt",
                table: "crm_kanban_cards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskId",
                table: "crm_kanban_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_AssigneeUserId",
                table: "crm_kanban_cards",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_DealId",
                table: "crm_kanban_cards",
                column: "DealId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_MeetingId",
                table: "crm_kanban_cards",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_TaskId",
                table: "crm_kanban_cards",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_kanban_cards_AssigneeUserId",
                table: "crm_kanban_cards");

            migrationBuilder.DropIndex(
                name: "IX_crm_kanban_cards_DealId",
                table: "crm_kanban_cards");

            migrationBuilder.DropIndex(
                name: "IX_crm_kanban_cards_MeetingId",
                table: "crm_kanban_cards");

            migrationBuilder.DropIndex(
                name: "IX_crm_kanban_cards_TaskId",
                table: "crm_kanban_cards");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "crm_kanban_columns");

            migrationBuilder.DropColumn(
                name: "Group",
                table: "crm_kanban_columns");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "crm_kanban_columns");

            migrationBuilder.DropColumn(
                name: "WipLimit",
                table: "crm_kanban_columns");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "crm_kanban_cards");

            migrationBuilder.DropColumn(
                name: "Labels",
                table: "crm_kanban_cards");

            migrationBuilder.DropColumn(
                name: "MeetingId",
                table: "crm_kanban_cards");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "crm_kanban_cards");

            migrationBuilder.DropColumn(
                name: "StartAt",
                table: "crm_kanban_cards");

            migrationBuilder.DropColumn(
                name: "TaskId",
                table: "crm_kanban_cards");
        }
    }
}
