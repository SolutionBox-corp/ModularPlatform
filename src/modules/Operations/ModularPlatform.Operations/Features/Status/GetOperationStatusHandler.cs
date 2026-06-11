using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Operations.Features.Status;

/// <summary>
/// Reads an operation's status for the caller who OWNS it. Ownership is enforced BOTH at the app layer (the explicit
/// <c>UserId</c> predicate, from the token) AND by RLS — defence in depth. A foreign id is a 404 even in a deployment
/// that runs with <c>Persistence:Rls:Enabled=false</c>.
/// </summary>
internal sealed class GetOperationStatusHandler(IReadDbContextFactory<OperationsDbContext> readDb)
    : IQueryHandler<GetOperationStatusQuery, OperationStatusResponse>
{
    public async Task<OperationStatusResponse> Handle(GetOperationStatusQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var operation = await db.Operations
            .Where(o => o.Id == query.OperationId && o.UserId == query.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("operation.not_found", "Operation not found.");

        return new OperationStatusResponse(
            operation.Id, operation.Type, operation.Status.ToString(),
            operation.ResultJson, operation.ErrorCode, operation.ErrorDetail, operation.CompletedAt);
    }
}
