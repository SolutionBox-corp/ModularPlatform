using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Companies;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Companies.UpdateCompany;

/// <summary>Loads the caller's OWN tracked company (foreign/deleted ⇒ 404) and applies a PARTIAL patch (null = unchanged).</summary>
internal sealed class UpdateCompanyHandler(CrmDbContext db)
    : ICommandHandler<UpdateCompanyCommand, CompanyResponse>
{
    public async Task<CompanyResponse> Handle(UpdateCompanyCommand command, CancellationToken ct)
    {
        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == command.CompanyId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.company_not_found", "Company not found.");

        if (command.Name is not null)
        {
            company.Name = command.Name.Trim();
        }

        if (command.Domain is not null)
        {
            company.Domain = string.IsNullOrWhiteSpace(command.Domain) ? null : command.Domain.Trim();
        }

        if (command.Industry is not null)
        {
            company.Industry = string.IsNullOrWhiteSpace(command.Industry) ? null : command.Industry.Trim();
        }

        if (command.Type is not null)
        {
            company.Type = command.Type;
        }

        if (command.IdentificationNumber is not null)
        {
            company.IdentificationNumber = string.IsNullOrWhiteSpace(command.IdentificationNumber) ? null : command.IdentificationNumber.Trim();
        }

        if (command.TaxIdentificationNumber is not null)
        {
            company.TaxIdentificationNumber = string.IsNullOrWhiteSpace(command.TaxIdentificationNumber) ? null : command.TaxIdentificationNumber.Trim();
        }

        if (command.RegisteredAddress is not null)
        {
            company.RegisteredAddress = string.IsNullOrWhiteSpace(command.RegisteredAddress) ? null : command.RegisteredAddress.Trim();
        }

        if (command.City is not null)
        {
            company.City = string.IsNullOrWhiteSpace(command.City) ? null : command.City.Trim();
        }

        if (command.PostalCode is not null)
        {
            company.PostalCode = string.IsNullOrWhiteSpace(command.PostalCode) ? null : command.PostalCode.Trim();
        }

        if (command.Country is not null)
        {
            company.Country = string.IsNullOrWhiteSpace(command.Country) ? null : command.Country.Trim();
        }

        if (command.Notes is not null)
        {
            company.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;
        }

        await db.SaveChangesAsync(ct);

        return new CompanyResponse(
            company.Id, company.Name, company.Domain, company.Industry, company.Type, company.IdentificationNumber,
            company.TaxIdentificationNumber, company.RegisteredAddress, company.City, company.PostalCode,
            company.Country, company.Notes, company.CreatedAt, company.UpdatedAt);
    }
}
