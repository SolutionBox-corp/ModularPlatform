using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Workspace.ProvisionCrmWorkspace;

/// <summary>
/// Seeds a fresh CRM workspace for a newly-registered user: one "getting started" task so the module isn't empty on
/// first visit. Idempotent — it no-ops if the user already has any task (UserRegistered can redeliver). Identity is
/// the integration event's UserId/TenantId, never a route/body.
/// </summary>
public sealed record ProvisionCrmWorkspaceCommand(Guid UserId, Guid TenantId) : ICommand<Unit>;
