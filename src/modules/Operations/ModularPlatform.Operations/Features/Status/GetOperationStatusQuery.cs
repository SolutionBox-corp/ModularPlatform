using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.Status;

public sealed record GetOperationStatusQuery(Guid OperationId) : IQuery<OperationStatusResponse>;

public sealed record OperationStatusResponse(
    Guid Id,
    string Type,
    string Status,
    string? ResultJson,
    string? ErrorCode,
    string? ErrorDetail,
    DateTimeOffset? CompletedAt);
