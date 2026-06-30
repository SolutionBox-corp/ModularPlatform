using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Companies.CreateCompany;

/// <summary><paramref name="UserId"/> is the owner, set by the endpoint from the token — NEVER the request body (Law 10).</summary>
public sealed record CreateCompanyCommand(
    Guid UserId,
    string Name,
    string? Domain,
    string? Industry,
    string? IdentificationNumber,
    string? TaxIdentificationNumber,
    string? RegisteredAddress,
    string? City,
    string? PostalCode,
    string? Country,
    string? Notes) : ICommand<CreateCompanyResponse>;

public sealed record CreateCompanyResponse(Guid Id);

public sealed record CreateCompanyRequest(
    string Name,
    string? Domain,
    string? Industry,
    string? IdentificationNumber,
    string? TaxIdentificationNumber,
    string? RegisteredAddress,
    string? City,
    string? PostalCode,
    string? Country,
    string? Notes);
