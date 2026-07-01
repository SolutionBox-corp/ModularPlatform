using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Contracts;

/// <summary>
/// Published when a tenant is provisioned (registry row created). Other modules react by Id — e.g. platform-plane
/// billing can set up the tenant's subscription-to-the-SaaS, or a module can seed tenant defaults. PII-free.
/// </summary>
public sealed record TenantProvisionedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    string Subdomain,
    string Name) : IIntegrationEvent;

/// <summary>
/// Published when platform-admin changes tenant registry display/routing data. Other modules use it to refresh
/// local read-model projections; Tenancy remains the owner of the registry row.
/// </summary>
public sealed record TenantUpdatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    string Subdomain,
    string Name) : IIntegrationEvent;
