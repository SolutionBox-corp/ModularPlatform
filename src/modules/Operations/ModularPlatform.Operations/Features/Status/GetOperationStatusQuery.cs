using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.Status;

/// <summary>Reads an operation the CALLER owns. Ownership is enforced at the app layer (<c>UserId</c> from the
/// token) AND by RLS — defence in depth, so a foreign id is a 404 even if RLS is disabled in a deployment.</summary>
public sealed record GetOperationStatusQuery(Guid OperationId, Guid UserId) : IQuery<OperationStatusResponse>;

public sealed record OperationStatusResponse(
    Guid Id,
    string Type,
    string Status,
    string? ResultJson,
    string? ErrorCode,
    string? ErrorDetail,
    DateTimeOffset? CompletedAt);
