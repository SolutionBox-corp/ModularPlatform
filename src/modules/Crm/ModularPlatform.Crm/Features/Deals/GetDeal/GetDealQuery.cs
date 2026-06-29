using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Deals.GetDeal;

public sealed record GetDealQuery(Guid UserId, Guid DealId)
    : IQuery<ModularPlatform.Crm.Features.Deals.DealResponse>;
