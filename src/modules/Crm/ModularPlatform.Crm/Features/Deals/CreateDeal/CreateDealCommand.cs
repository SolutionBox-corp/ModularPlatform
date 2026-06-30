using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Deals.CreateDeal;

/// <summary><paramref name="UserId"/> is the owner, set by the endpoint from the token — NEVER the request body (Law 10).</summary>
public sealed record CreateDealCommand(
    Guid UserId,
    Guid? ContactId,
    Guid? CompanyId,
    string Title,
    long AmountCents,
    string Currency,
    string Stage,
    int? ProbabilityPercent,
    string? LeadSource,
    DateTimeOffset? ExpectedCloseAt,
    string? NextStep,
    string? Notes) : ICommand<CreateDealResponse>;

public sealed record CreateDealResponse(Guid Id);

public sealed record CreateDealRequest(
    Guid? ContactId,
    Guid? CompanyId,
    string Title,
    long AmountCents,
    string? Currency,
    string? Stage,
    int? ProbabilityPercent,
    string? LeadSource,
    DateTimeOffset? ExpectedCloseAt,
    string? NextStep,
    string? Notes);
