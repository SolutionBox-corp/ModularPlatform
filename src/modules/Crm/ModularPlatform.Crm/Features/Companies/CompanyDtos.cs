namespace ModularPlatform.Crm.Features.Companies;

/// <summary>Shared read DTOs for the Companies feature.</summary>
public sealed record CompanyResponse(
    Guid Id,
    string Name,
    string? Domain,
    string? Industry,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CompanyListItem(
    Guid Id,
    string Name,
    string? Domain,
    string? Industry,
    DateTimeOffset CreatedAt);
