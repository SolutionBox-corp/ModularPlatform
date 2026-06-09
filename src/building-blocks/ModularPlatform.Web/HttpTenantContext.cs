using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Web;

/// <summary>
/// <see cref="ITenantContext"/> sourced from the current request's JWT claims. Registered singleton
/// (stateless; reads the accessor live each call) so the audit interceptor and DbContext factory can
/// depend on it. In non-HTTP hosts (worker/jobs) there is no accessor context, so it yields system
/// context (null tenant/user) — system operations bypass RLS by design.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public const string TenantClaim = "tenant_id";

    public Guid? TenantId => ParseGuid(User?.FindFirstValue(TenantClaim));

    public Guid? UserId => ParseGuid(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub"));

    /// <summary>
    /// System ONLY when there is no HTTP context at all — i.e. background work running inside the Api process
    /// (Wolverine outbox/inbox handlers, startup). Such work has no request principal and must bypass the tenant
    /// query filter + RLS to provision per-user data. A real HTTP request (even unauthenticated) has a context,
    /// so it is NEVER system and stays tenant/principal-scoped.
    /// </summary>
    public bool IsSystem => accessor.HttpContext is null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var g) ? g : null;
}
