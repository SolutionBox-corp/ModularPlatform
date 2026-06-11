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
        try
        {
            // MarkRunning is INSIDE the try: a failure on the Pending → Running transition must also drive the
            // operation to a terminal state, otherwise a caller would poll a stuck Pending operation forever.
            await operations.MarkRunningAsync(message.OperationId, ct);

            // The "work" — trivial here; a real operation would do the slow thing (export, bulk job, external call).
            var result = new { message = "demo complete", completedFor = message.OperationId };

            await operations.CompleteAsync(message.OperationId, result, ct);
        }
        catch (Exception ex)
        {
            // Drive the operation to a TERMINAL state on ANY failure — never leave it stuck Pending/Running with the
            // caller polling forever. The failure IS the user-facing record (the operation row). If FailAsync itself
            // cannot write (e.g. the DB is down), the exception propagates and Wolverine retries the whole handler;
            // a deterministic error terminalizes. Transient infra faults remain the messaging layer's job.
            await operations.FailAsync(message.OperationId, "operation.failed", ex.Message, ct);
        }
    }
}
