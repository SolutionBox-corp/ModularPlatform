using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Deals.MoveDealStage;

public sealed record MoveDealStageCommand(Guid UserId, Guid DealId, string Stage)
    : ICommand<ModularPlatform.Crm.Features.Deals.DealResponse>;

public sealed record MoveDealStageRequest(string Stage);
