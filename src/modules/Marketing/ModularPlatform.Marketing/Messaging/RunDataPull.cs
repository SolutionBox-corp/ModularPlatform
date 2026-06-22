namespace ModularPlatform.Marketing.Messaging;

/// <summary>
/// Durable work message for a marketing data pull (published by the accept handler via the outbox, consumed by the
/// Worker). Intra-module — not a cross-module integration contract.
/// </summary>
public sealed record RunDataPull(Guid DataPullId);
