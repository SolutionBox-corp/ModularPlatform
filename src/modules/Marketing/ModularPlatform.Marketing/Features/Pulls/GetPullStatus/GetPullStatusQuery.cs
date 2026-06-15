using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Pulls.GetPullStatus;

public sealed record GetPullStatusQuery(Guid DataPullId, Guid UserId) : IQuery<PullStatusResponse>;

public sealed record PullStatusResponse(
    Guid Id,
    string Source,
    string Status,
    string? ErrorCode,
    DateTimeOffset? CompletedAt);
