using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.List;

/// <summary>
/// Paged list of the CALLER's own operations, newest first. Ownership is enforced at the app layer (the explicit
/// <c>UserId</c> predicate, from the token) AND by RLS — defence in depth.
/// </summary>
public sealed record ListMyOperationsQuery(Guid UserId, PageRequest Page) : IQuery<PagedResponse<OperationListItem>>;

public sealed record OperationListItem(
    Guid Id,
    string Type,
    string Status,
    string? ErrorCode,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);
