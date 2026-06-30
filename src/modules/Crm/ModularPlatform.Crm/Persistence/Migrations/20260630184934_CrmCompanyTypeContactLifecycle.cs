using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmCompanyTypeContactLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "crm_companies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "prospect");

            migrationBuilder.Sql("""
                UPDATE crm_contacts
                SET "Status" = CASE "Status"
                    WHEN 'lead' THEN 'new'
                    WHEN 'active' THEN 'engaged'
                    WHEN 'customer' THEN 'qualified'
                    ELSE "Status"
                END
                WHERE "Status" IN ('lead', 'active', 'customer');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_UserId_Type",
                table: "crm_companies",
                columns: new[] { "UserId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE crm_contacts
                SET "Status" = CASE "Status"
                    WHEN 'new' THEN 'lead'
                    WHEN 'engaged' THEN 'active'
                    WHEN 'qualified' THEN 'customer'
                    ELSE "Status"
                END
                WHERE "Status" IN ('new', 'engaged', 'qualified');
                """);

            migrationBuilder.DropIndex(
                name: "IX_crm_companies_UserId_Type",
                table: "crm_companies");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "crm_companies");
        }
    }
}
