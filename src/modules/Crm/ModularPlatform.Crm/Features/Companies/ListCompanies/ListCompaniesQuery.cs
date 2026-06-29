using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Companies.ListCompanies;

/// <summary>Owner-scoped, paged company list with optional industry filter + name contains. Newest first.</summary>
public sealed record ListCompaniesQuery(
    Guid UserId,
    string? Industry,
    string? Name,
    int? Page,
    int? PageSize) : IQuery<PagedResponse<ModularPlatform.Crm.Features.Companies.CompanyListItem>>;
