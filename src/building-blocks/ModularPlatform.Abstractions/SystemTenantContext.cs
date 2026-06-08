namespace ModularPlatform.Abstractions;

/// <summary>
/// Non-HTTP tenant context for the Worker / Jobs / Migration hosts: system context with no tenant or
/// user. System operations run with elevated scope (RLS not narrowed). When a worker handles a message
/// that carries a tenant, set it explicitly per message rather than relying on this default.
/// </summary>
public sealed class SystemTenantContext : ITenantContext
{
    public Guid? TenantId => null;
    public Guid? UserId => null;
    public bool IsSystem => true;
    public string? IpAddress => null;
}
