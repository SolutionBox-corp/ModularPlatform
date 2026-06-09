namespace ModularPlatform.Operations.Messaging;

/// <summary>
/// Durable work message for the demo long-running operation (published by the accept handler via the outbox,
/// consumed by the worker). Intra-module — it is not a cross-module integration contract.
/// </summary>
public sealed record RunDemoOperation(Guid OperationId);
