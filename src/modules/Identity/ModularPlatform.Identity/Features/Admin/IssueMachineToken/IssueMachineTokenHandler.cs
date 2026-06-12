using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.IssueMachineToken;

internal sealed class IssueMachineTokenHandler(ITokenIssuer tokenIssuer, IServiceProvider services)
    : ICommandHandler<IssueMachineTokenCommand, IssueMachineTokenResponse>
{
    /// <summary>The role that marks a token as a machine (service) principal rather than a human user.</summary>
    public const string MachineRole = AuthorizationClaims.MachineRole;

    public async Task<IssueMachineTokenResponse> Handle(IssueMachineTokenCommand command, CancellationToken ct)
    {
        // The target tenant MUST exist — minting a token for a phantom/never-provisioned tenant id would create a token
        // that silently "activates" if that id is ever provisioned later. (ITenantDirectory is null only if the Tenancy
        // module is disabled, in which case there is no registry to validate against.)
        var directory = services.GetService<ITenantDirectory>();
        if (directory is not null && await directory.GetByIdAsync(command.TenantId, ct) is null)
        {
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        // A synthetic, non-user subject. No email PII — a stable label only.
        var machineId = Guid.CreateVersion7();
        var label = $"machine:{command.Name.Trim()}";

        var access = tokenIssuer.IssueAccessToken(
            userId: machineId,
            tenantId: command.TenantId,
            email: label,
            roles: [MachineRole],
            permissions: []);

        return new IssueMachineTokenResponse(access.Value, access.ExpiresAt);
    }
}
