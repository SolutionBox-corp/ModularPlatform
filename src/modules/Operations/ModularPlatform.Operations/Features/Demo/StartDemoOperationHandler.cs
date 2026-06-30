using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Entities;
using ModularPlatform.Operations.Messaging;
using ModularPlatform.Operations.Persistence;
using Npgsql;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Operations.Features.Demo;

/// <summary>
/// CANONICAL long-running accept: creates a Pending operation owned by the caller AND publishes the durable work
/// message in ONE transaction (outbox), then returns its id. The endpoint replies 202; the worker does the real
/// work and transitions the operation; the caller polls the status endpoint. Never do the slow work here.
/// </summary>
internal sealed class StartDemoOperationHandler(IDbContextOutbox<OperationsDbContext> outbox)
    : ICommandHandler<StartDemoOperationCommand, StartDemoOperationResponse>
{
    public async Task<StartDemoOperationResponse> Handle(StartDemoOperationCommand command, CancellationToken ct)
    {
        var idempotencyKey = NormalizeIdempotencyKey(command.IdempotencyKey);
        if (idempotencyKey is not null)
        {
            var existing = await FindExistingAsync(command.UserId, idempotencyKey, ct);
            if (existing.HasValue)
            {
                return new StartDemoOperationResponse(existing.Value);
            }
        }

        var operation = new Operation
        {
            UserId = command.UserId,
            Type = "demo",
            IdempotencyKey = idempotencyKey,
            Status = OperationStatus.Pending,
        };
        outbox.DbContext.Operations.Add(operation);

        await outbox.PublishAsync(new RunDemoOperation(operation.Id));
        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            outbox.DbContext.ChangeTracker.Clear();
            var existing = await FindExistingAsync(command.UserId, idempotencyKey, ct)
                ?? throw new InvalidOperationException("The operation idempotency key collided but the existing operation could not be reloaded.");
            return new StartDemoOperationResponse(existing);
        }

        return new StartDemoOperationResponse(operation.Id);
    }

    private Task<Guid?> FindExistingAsync(Guid userId, string idempotencyKey, CancellationToken ct) =>
        outbox.DbContext.Operations
            .AsNoTracking()
            .Where(o => o.UserId == userId && o.Type == "demo" && o.IdempotencyKey == idempotencyKey)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        var normalized = idempotencyKey?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
