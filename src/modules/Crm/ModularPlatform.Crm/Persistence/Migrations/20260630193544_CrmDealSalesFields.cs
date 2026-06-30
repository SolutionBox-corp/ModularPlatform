using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmDealSalesFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastStage",
                table: "crm_deals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeadSource",
                table: "crm_deals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextStep",
                table: "crm_deals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProbabilityPercent",
                table: "crm_deals",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.Sql("""
                UPDATE crm_deals
                SET "ProbabilityPercent" = CASE "Stage"
                    WHEN 'lead' THEN 10
                    WHEN 'qualified' THEN 25
                    WHEN 'proposal' THEN 50
                    WHEN 'negotiation' THEN 75
                    WHEN 'won' THEN 100
                    WHEN 'lost' THEN 0
                    ELSE 10
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_UserId_LeadSource",
                table: "crm_deals",
                columns: new[] { "UserId", "LeadSource" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_deals_UserId_LeadSource",
                table: "crm_deals");

            migrationBuilder.DropColumn(
                name: "LastStage",
                table: "crm_deals");

            migrationBuilder.DropColumn(
                name: "LeadSource",
                table: "crm_deals");

            migrationBuilder.DropColumn(
                name: "NextStep",
                table: "crm_deals");

            migrationBuilder.DropColumn(
                name: "ProbabilityPercent",
                table: "crm_deals");
        }
    }
}
