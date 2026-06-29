using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmDealsTasksCompaniesKanban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "crm_contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "crm_companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Domain = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Industry = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_deals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpectedCloseAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_deals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_kanban_boards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_kanban_boards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_kanban_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ColumnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    DealId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_kanban_cards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_kanban_columns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_kanban_columns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: true),
                    DealId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_tasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_CompanyId",
                table: "crm_contacts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_UserId_CreatedAt",
                table: "crm_contacts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_TenantId",
                table: "crm_companies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_UserId_CreatedAt",
                table: "crm_companies",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_UserId_Industry",
                table: "crm_companies",
                columns: new[] { "UserId", "Industry" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_CompanyId",
                table: "crm_deals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_ContactId",
                table: "crm_deals",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_TenantId",
                table: "crm_deals",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_UserId_CreatedAt",
                table: "crm_deals",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_UserId_Stage",
                table: "crm_deals",
                columns: new[] { "UserId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_boards_TenantId",
                table: "crm_kanban_boards",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_boards_UserId_CreatedAt",
                table: "crm_kanban_boards",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_BoardId",
                table: "crm_kanban_cards",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_ColumnId_Position",
                table: "crm_kanban_cards",
                columns: new[] { "ColumnId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_TenantId",
                table: "crm_kanban_cards",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_cards_UserId",
                table: "crm_kanban_cards",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_columns_BoardId_Position",
                table: "crm_kanban_columns",
                columns: new[] { "BoardId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_columns_TenantId",
                table: "crm_kanban_columns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_kanban_columns_UserId",
                table: "crm_kanban_columns",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_ContactId",
                table: "crm_tasks",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_DealId",
                table: "crm_tasks",
                column: "DealId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_TenantId",
                table: "crm_tasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_tasks_UserId_Status_DueAt",
                table: "crm_tasks",
                columns: new[] { "UserId", "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_companies");

            migrationBuilder.DropTable(
                name: "crm_deals");

            migrationBuilder.DropTable(
                name: "crm_kanban_boards");

            migrationBuilder.DropTable(
                name: "crm_kanban_cards");

            migrationBuilder.DropTable(
                name: "crm_kanban_columns");

            migrationBuilder.DropTable(
                name: "crm_tasks");

            migrationBuilder.DropIndex(
                name: "IX_crm_contacts_CompanyId",
                table: "crm_contacts");

            migrationBuilder.DropIndex(
                name: "IX_crm_contacts_UserId_CreatedAt",
                table: "crm_contacts");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "crm_contacts");
        }
    }
}
