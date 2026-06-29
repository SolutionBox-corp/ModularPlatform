using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Deals.DeleteDeal;

public sealed record DeleteDealCommand(Guid UserId, Guid DealId) : ICommand<Unit>;
