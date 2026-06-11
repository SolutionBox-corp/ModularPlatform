using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.ProvisionTenant;

/// <summary>
/// Platform-admin: provision a new tenant (subdomain + name), seeding the default module entitlements. Gated by
/// <c>platform.tenants.manage</c>. This is how tenants are created in the B2B flow — registration JOINS an existing
/// tenant's subdomain, it does not create one.
/// </summary>
public sealed record ProvisionTenantCommand(string Name, string Subdomain) : ICommand<ProvisionTenantResponse>;

public sealed record ProvisionTenantResponse(Guid TenantId);

public sealed record ProvisionTenantRequest(string Name, string Subdomain);
