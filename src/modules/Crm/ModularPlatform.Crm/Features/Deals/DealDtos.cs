namespace ModularPlatform.Crm.Features.Deals;

/// <summary>Shared read DTOs for the Deals feature. Records (immutable wire shapes).</summary>
public sealed record DealResponse(
    Guid Id,
    Guid? ContactId,
    Guid? CompanyId,
    string Title,
    long AmountCents,
    string Currency,
    string Stage,
    string? LastStage,
    int ProbabilityPercent,
    string? LeadSource,
    DateTimeOffset? ExpectedCloseAt,
    DateTimeOffset? ClosedAt,
    string? NextStep,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record DealListItem(
    Guid Id,
    Guid? ContactId,
    Guid? CompanyId,
    string Title,
    long AmountCents,
    string Currency,
    string Stage,
    int ProbabilityPercent,
    string? LeadSource,
    string? NextStep,
    DateTimeOffset? ExpectedCloseAt,
    DateTimeOffset CreatedAt);
