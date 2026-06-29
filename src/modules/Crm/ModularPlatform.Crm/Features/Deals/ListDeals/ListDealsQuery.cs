using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Deals.ListDeals;

/// <summary>Owner-scoped, paged deal list with optional stage / contact filters. Newest first.</summary>
public sealed record ListDealsQuery(
    Guid UserId,
    string? Stage,
    Guid? ContactId,
    int? Page,
    int? PageSize) : IQuery<PagedResponse<ModularPlatform.Crm.Features.Deals.DealListItem>>;
