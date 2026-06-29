using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Deals.UpdateDeal;

/// <summary>Partial patch — a null field is unchanged. Stage changes go through MoveDealStage, not here.</summary>
public sealed record UpdateDealCommand(
    Guid UserId,
    Guid DealId,
    string? Title,
    long? AmountCents,
    string? Currency,
    DateTimeOffset? ExpectedCloseAt,
    string? Notes) : ICommand<ModularPlatform.Crm.Features.Deals.DealResponse>;

public sealed record UpdateDealRequest(
    string? Title,
    long? AmountCents,
    string? Currency,
    DateTimeOffset? ExpectedCloseAt,
    string? Notes);
