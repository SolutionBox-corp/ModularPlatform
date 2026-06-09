using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Operations.Features.Status;

/// <summary>
/// Reads an operation's status. Ownership is enforced by RLS — the read connection only ever returns the caller's
/// own operations, so another user's id simply isn't found (404), with no explicit owner check to forget.
/// </summary>
internal sealed class GetOperationStatusHandler(IReadDbContextFactory<OperationsDbContext> readDb)
    : IQueryHandler<GetOperationStatusQuery, OperationStatusResponse>
{
    public async Task<OperationStatusResponse> Handle(GetOperationStatusQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var operation = await db.Operations
            .Where(o => o.Id == query.OperationId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("operation.not_found", "Operation not found.");

        return new OperationStatusResponse(
            operation.Id, operation.Type, operation.Status.ToString(),
            operation.ResultJson, operation.ErrorCode, operation.ErrorDetail, operation.CompletedAt);
    }
}
