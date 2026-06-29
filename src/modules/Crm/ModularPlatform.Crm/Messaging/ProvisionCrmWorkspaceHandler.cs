using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Workspace.ProvisionCrmWorkspace;
using ModularPlatform.Identity.Contracts;

namespace ModularPlatform.Crm.Messaging;

/// <summary>
/// Consumes Identity's <see cref="UserRegisteredIntegrationEvent"/> (the ONLY way CRM learns about new users — never
/// by referencing Identity's Core) and seeds a starter workspace. Thin PUBLIC shell (Wolverine-discovered, runs in
/// the Worker, inbox-deduped) dispatching the internal command, so CRM's Core stays internal.
/// </summary>
public sealed class ProvisionCrmWorkspaceHandler
{
    public Task Handle(UserRegisteredIntegrationEvent message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new ProvisionCrmWorkspaceCommand(message.UserId, message.TenantId), ct);
}
