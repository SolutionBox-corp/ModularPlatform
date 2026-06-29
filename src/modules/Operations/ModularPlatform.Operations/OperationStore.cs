using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Entities;
using ModularPlatform.Operations.Persistence;
using Npgsql;

namespace ModularPlatform.Operations;

/// <summary>
/// <see cref="IOperationStore"/> over the operations table. Creation runs under the caller's principal (so the
/// row is owned by — and RLS-checked against — that user); the terminal transitions run on the durable worker
/// under system context, which bypasses RLS to advance any user's operation.
/// </summary>
internal sealed class OperationStore(OperationsDbContext db, IClock clock) : IOperationStore
{
    public async Task<Guid> CreateAsync(string type, Guid userId, CancellationToken ct, string? idempotencyKey = null)
    {
        idempotencyKey = NormalizeIdempotencyKey(idempotencyKey);

        if (idempotencyKey is not null)
        {
            var existing = await FindByIdempotencyKeyAsync(type, userId, idempotencyKey, ct);
            if (existing.HasValue)
            {
                return existing.Value;
            }
        }

        var operation = new Operation
        {
            UserId = userId,
            Type = type,
            IdempotencyKey = idempotencyKey,
            Status = OperationStatus.Pending,
        };
        db.Operations.Add(operation);

        try
        {
            await db.SaveChangesAsync(ct);
            return operation.Id;
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return await FindByIdempotencyKeyAsync(type, userId, idempotencyKey, ct)
                ?? throw new InvalidOperationException("The operation idempotency key collided but the existing operation could not be reloaded.");
        }
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

    private Task<Guid?> FindByIdempotencyKeyAsync(string type, Guid userId, string idempotencyKey, CancellationToken ct) =>
        db.Operations
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.Type == type && o.IdempotencyKey == idempotencyKey)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        var normalized = idempotencyKey?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
