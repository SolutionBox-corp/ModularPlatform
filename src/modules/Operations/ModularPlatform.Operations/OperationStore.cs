using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Entities;
using ModularPlatform.Operations.Persistence;

namespace ModularPlatform.Operations;

/// <summary>
/// <see cref="IOperationStore"/> over the operations table. Creation runs under the caller's principal (so the
/// row is owned by — and RLS-checked against — that user); the terminal transitions run on the durable worker
/// under system context, which bypasses RLS to advance any user's operation.
/// </summary>
internal sealed class OperationStore(OperationsDbContext db, IClock clock) : IOperationStore
{
    public async Task<Guid> CreateAsync(string type, Guid userId, CancellationToken ct)
    {
        var operation = new Operation { UserId = userId, Type = type, Status = OperationStatus.Pending };
        db.Operations.Add(operation);
        await db.SaveChangesAsync(ct);
        return operation.Id;
    }

    public Task MarkRunningAsync(Guid operationId, CancellationToken ct) =>
        TransitionAsync(operationId, op => op.Status = OperationStatus.Running, ct);

    public Task CompleteAsync(Guid operationId, object? result, CancellationToken ct) =>
        TransitionAsync(operationId, op =>
        {
            op.Status = OperationStatus.Succeeded;
            op.ResultJson = result is null ? null : JsonSerializer.Serialize(result);
            op.CompletedAt = clock.UtcNow;
        }, ct);

    public Task FailAsync(Guid operationId, string errorCode, string? detail, CancellationToken ct) =>
        TransitionAsync(operationId, op =>
        {
            op.Status = OperationStatus.Failed;
            op.ErrorCode = errorCode;
            op.ErrorDetail = detail;
            op.CompletedAt = clock.UtcNow;
        }, ct);

    private async Task TransitionAsync(Guid operationId, Action<Operation> mutate, CancellationToken ct)
    {
        var operation = await db.Operations.FirstOrDefaultAsync(o => o.Id == operationId, ct)
            ?? throw new NotFoundException("operation.not_found", "Operation not found.");

        // Terminal states are FINAL: a redelivered/duplicate worker message must not resurrect a Succeeded/Failed
        // operation back to Running, nor flip one terminal state to the other. Idempotent no-op.
        if (operation.Status is OperationStatus.Succeeded or OperationStatus.Failed)
        {
            return;
        }

        mutate(operation);
        await db.SaveChangesAsync(ct);
    }
}
