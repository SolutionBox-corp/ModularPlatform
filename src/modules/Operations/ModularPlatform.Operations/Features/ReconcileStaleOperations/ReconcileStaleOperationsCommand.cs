using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.ReconcileStaleOperations;

/// <summary>
/// Reconciliation sweep for long-running operations whose durable work message never terminalized them.
/// Idempotent; dispatched by the Jobs host and safe to run repeatedly.
/// </summary>
public sealed record ReconcileStaleOperationsCommand(
    int StaleAfterMinutes = ReconcileStaleOperationsCommand.DefaultStaleAfterMinutes,
    int Cap = ReconcileStaleOperationsCommand.DefaultCap) : ICommand<ReconcileStaleOperationsResponse>
{
    public const int DefaultStaleAfterMinutes = 120;
    public const int DefaultCap = 100;
}

public sealed record ReconcileStaleOperationsResponse(int FailedCount, bool CapReached);
