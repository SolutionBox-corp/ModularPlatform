using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Companies.UpdateCompany;

/// <summary>Partial patch — a null field is unchanged.</summary>
public sealed record UpdateCompanyCommand(
    Guid UserId,
    Guid CompanyId,
    string? Name,
    string? Domain,
    string? Industry,
    string? Type,
    string? IdentificationNumber,
    string? TaxIdentificationNumber,
    string? RegisteredAddress,
    string? City,
    string? PostalCode,
    string? Country,
    string? Notes) : ICommand<ModularPlatform.Crm.Features.Companies.CompanyResponse>;

public sealed record UpdateCompanyRequest(
    string? Name,
    string? Domain,
    string? Industry,
    string? Type,
    string? IdentificationNumber,
    string? TaxIdentificationNumber,
    string? RegisteredAddress,
    string? City,
    string? PostalCode,
    string? Country,
    string? Notes);
