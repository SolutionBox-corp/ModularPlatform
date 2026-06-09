namespace ModularPlatform.Abstractions;

/// <summary>Lifecycle of a long-running operation tracked behind a 202 + status-polling endpoint.</summary>
public enum OperationStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>
/// Port for the long-running-operation pattern: a command that can't finish within the request creates an
/// operation, returns 202 + its id, and does the real work on a durable worker that transitions the operation to
/// a terminal state; the caller polls the status endpoint. Implemented by the Operations module. Any module can
/// inject this to create an operation from its HTTP handler and complete it from its worker handler.
/// Operations are owned by a user (RLS-isolated) — the owner is supplied at creation.
/// </summary>
public interface IOperationStore
{
    /// <summary>Creates a <see cref="OperationStatus.Pending"/> operation owned by <paramref name="userId"/>; returns its id.</summary>
    Task<Guid> CreateAsync(string type, Guid userId, CancellationToken ct);

    Task MarkRunningAsync(Guid operationId, CancellationToken ct);

    /// <summary>Marks the operation <see cref="OperationStatus.Succeeded"/>, storing an optional JSON-serialisable result.</summary>
    Task CompleteAsync(Guid operationId, object? result, CancellationToken ct);

    Task FailAsync(Guid operationId, string errorCode, string? detail, CancellationToken ct);
}
