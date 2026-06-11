using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Security;

namespace ModularPlatform.Identity.Features.Admin.IssueMachineToken;

internal sealed class IssueMachineTokenHandler(ITokenIssuer tokenIssuer)
    : ICommandHandler<IssueMachineTokenCommand, IssueMachineTokenResponse>
{
    /// <summary>The role that marks a token as a machine (service) principal rather than a human user.</summary>
    public const string MachineRole = "machine";

    public Task<IssueMachineTokenResponse> Handle(IssueMachineTokenCommand command, CancellationToken ct)
    {
        // A synthetic, non-user subject. No email PII — a stable label only.
        var machineId = Guid.CreateVersion7();
        var label = $"machine:{command.Name.Trim()}";

        var access = tokenIssuer.IssueAccessToken(
            userId: machineId,
            tenantId: command.TenantId,
            email: label,
            roles: [MachineRole],
            permissions: []);

        return Task.FromResult(new IssueMachineTokenResponse(access.Value, access.ExpiresAt));
    }
}
