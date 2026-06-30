using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmDealInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DealId",
                table: "crm_contact_interactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_contact_interactions_DealId_OccurredAt",
                table: "crm_contact_interactions",
                columns: new[] { "DealId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_contact_interactions_DealId_OccurredAt",
                table: "crm_contact_interactions");

            migrationBuilder.DropColumn(
                name: "DealId",
                table: "crm_contact_interactions");
        }
    }
}
