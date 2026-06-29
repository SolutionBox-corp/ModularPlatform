using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.TenantSnapshots.UpsertTenantSnapshot;
using ModularPlatform.Tenancy.Contracts;

namespace ModularPlatform.Marketing.Messaging;

/// <summary>
/// Cross-module projection adapter: Tenancy owns the tenant registry; Marketing stores only a small local read-model.
/// The real work is an internal command so replay/reconcile can reuse the same idempotent upsert logic.
/// </summary>
public sealed class TenantProvisionedForMarketingHandler
{
    public Task Handle(TenantProvisionedIntegrationEvent message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new UpsertTenantSnapshotCommand(
            message.TenantId,
            message.Subdomain,
            message.Name,
            message.EventId,
            message.OccurredAt), ct);
}
