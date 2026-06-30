using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmCompanyContactRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Company",
                table: "crm_contacts");

            migrationBuilder.RenameColumn(
                name: "FullName",
                table: "crm_contacts",
                newName: "LastName");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "crm_contacts",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "crm_companies",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "crm_companies",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificationNumber",
                table: "crm_companies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "crm_companies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegisteredAddress",
                table: "crm_companies",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxIdentificationNumber",
                table: "crm_companies",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "crm_contacts");

            migrationBuilder.DropColumn(
                name: "City",
                table: "crm_companies");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "crm_companies");

            migrationBuilder.DropColumn(
                name: "IdentificationNumber",
                table: "crm_companies");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "crm_companies");

            migrationBuilder.DropColumn(
                name: "RegisteredAddress",
                table: "crm_companies");

            migrationBuilder.DropColumn(
                name: "TaxIdentificationNumber",
                table: "crm_companies");

            migrationBuilder.RenameColumn(
                name: "LastName",
                table: "crm_contacts",
                newName: "FullName");

            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "crm_contacts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }
    }
}
