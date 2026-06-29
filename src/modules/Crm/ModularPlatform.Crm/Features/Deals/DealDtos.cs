namespace ModularPlatform.Crm.Features.Deals;

/// <summary>Shared read DTOs for the Deals feature. Records (immutable wire shapes).</summary>
public sealed record DealResponse(
    Guid Id,
    Guid? ContactId,
    string Title,
    long AmountCents,
    string Currency,
    string Stage,
    DateTimeOffset? ExpectedCloseAt,
    DateTimeOffset? ClosedAt,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record DealListItem(
    Guid Id,
    Guid? ContactId,
    string Title,
    long AmountCents,
    string Currency,
    string Stage,
    DateTimeOffset? ExpectedCloseAt,
    DateTimeOffset CreatedAt);
