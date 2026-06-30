using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Companies.CreateCompany;

/// <summary>Write slice WITHOUT an event. Owner is the token; TenantId is stamped by the interceptor. No raw SQL.</summary>
internal sealed class CreateCompanyHandler(CrmDbContext db)
    : ICommandHandler<CreateCompanyCommand, CreateCompanyResponse>
{
    public async Task<CreateCompanyResponse> Handle(CreateCompanyCommand command, CancellationToken ct)
    {
        var company = new Company
        {
            UserId = command.UserId,
            Name = command.Name.Trim(),
            Domain = string.IsNullOrWhiteSpace(command.Domain) ? null : command.Domain.Trim(),
            Industry = string.IsNullOrWhiteSpace(command.Industry) ? null : command.Industry.Trim(),
            Type = command.Type,
            IdentificationNumber = string.IsNullOrWhiteSpace(command.IdentificationNumber) ? null : command.IdentificationNumber.Trim(),
            TaxIdentificationNumber = string.IsNullOrWhiteSpace(command.TaxIdentificationNumber) ? null : command.TaxIdentificationNumber.Trim(),
            RegisteredAddress = string.IsNullOrWhiteSpace(command.RegisteredAddress) ? null : command.RegisteredAddress.Trim(),
            City = string.IsNullOrWhiteSpace(command.City) ? null : command.City.Trim(),
            PostalCode = string.IsNullOrWhiteSpace(command.PostalCode) ? null : command.PostalCode.Trim(),
            Country = string.IsNullOrWhiteSpace(command.Country) ? null : command.Country.Trim(),
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes,
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync(ct);

        return new CreateCompanyResponse(company.Id);
    }
}
