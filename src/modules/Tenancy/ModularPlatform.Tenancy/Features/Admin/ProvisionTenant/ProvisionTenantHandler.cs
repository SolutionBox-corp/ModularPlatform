using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.ProvisionTenant;

internal sealed class ProvisionTenantHandler(ITenantProvisioning provisioning)
    : ICommandHandler<ProvisionTenantCommand, ProvisionTenantResponse>
{
    public async Task<ProvisionTenantResponse> Handle(ProvisionTenantCommand command, CancellationToken ct) =>
        new(await provisioning.CreateAsync(command.Name.Trim(), command.Subdomain.Trim().ToLowerInvariant(), ct));
}
