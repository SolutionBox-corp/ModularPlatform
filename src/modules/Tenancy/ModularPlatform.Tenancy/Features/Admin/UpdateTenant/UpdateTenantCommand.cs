using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.UpdateTenant;

public sealed record UpdateTenantCommand(Guid TenantId, string Name, string Subdomain)
    : ICommand<UpdateTenantResponse>;

public sealed record UpdateTenantResponse(Guid TenantId, string Name, string Subdomain);

public sealed record UpdateTenantRequest(string Name, string Subdomain);
