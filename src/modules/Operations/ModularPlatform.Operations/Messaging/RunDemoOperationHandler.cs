using ModularPlatform.Abstractions;

namespace ModularPlatform.Operations.Messaging;

/// <summary>
/// CANONICAL durable worker for a long-running operation: transitions Pending → Running, does the real work, then
/// → Succeeded (or → Failed via <see cref="IOperationStore.FailAsync"/> on error). Public + parameterless-discovered
/// by Wolverine. Runs under system context (no HttpContext), so its writes bypass RLS to advance the user's op.
/// Other modules' long-running workers follow this exact shape, calling <see cref="IOperationStore"/> on their work.
/// </summary>
public sealed class RunDemoOperationHandler
{
    public async Task Handle(RunDemoOperation message, IOperationStore operations, CancellationToken ct)
    {
        await operations.MarkRunningAsync(message.OperationId, ct);

        // The "work" — trivial here; a real operation would do the slow thing (export, bulk job, external call).
        var result = new { message = "demo complete", completedFor = message.OperationId };

        await operations.CompleteAsync(message.OperationId, result, ct);
    }
}
