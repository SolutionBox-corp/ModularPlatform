using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Entities;
using ModularPlatform.Operations.Persistence;

namespace ModularPlatform.Operations.Features.ReconcileStaleOperations;

internal sealed class ReconcileStaleOperationsHandler(
    OperationsDbContext db,
    IClock clock,
    ILogger<ReconcileStaleOperationsHandler> logger)
    : ICommandHandler<ReconcileStaleOperationsCommand, ReconcileStaleOperationsResponse>
{
    public async Task<ReconcileStaleOperationsResponse> Handle(
        ReconcileStaleOperationsCommand command,
        CancellationToken ct)
    {
        var staleAfterMinutes = Math.Clamp(command.StaleAfterMinutes, 1, 7 * 24 * 60);
        var cap = Math.Clamp(command.Cap, 1, 1_000);
        var cutoff = clock.UtcNow.AddMinutes(-staleAfterMinutes);

        var stale = await db.Operations
            .Where(o => (o.Status == OperationStatus.Pending || o.Status == OperationStatus.Running)
                && (o.UpdatedAt ?? o.CreatedAt) < cutoff)
            .OrderBy(o => o.UpdatedAt ?? o.CreatedAt)
            .Take(cap)
            .ToListAsync(ct);

        if (stale.Count == cap)
        {
            logger.LogWarning(
                "Operations reconcile: stale-operation cap ({Cap}) reached; there may be more stuck operations",
                cap);
        }

        var now = clock.UtcNow;
        foreach (var operation in stale)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorCode = "operation.stale_reconciled";
            operation.ErrorDetail = "The operation did not finish in time and was marked failed by reconciliation.";
            operation.CompletedAt = now;
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new ReconcileStaleOperationsResponse(stale.Count, stale.Count == cap);
    }
}
