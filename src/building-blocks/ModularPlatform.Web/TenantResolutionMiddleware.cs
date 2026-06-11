using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Web;

/// <summary>
/// Resolves the request host's subdomain to a tenant (B2B subdomain-per-tenant) and cross-checks it against the JWT.
/// Runs AFTER authentication (the token's <c>tenant_id</c> exists) and BEFORE the rate limiter. The resolved tenant
/// is stashed in <c>HttpContext.Items["tenant"]</c> for downstream (placement, nav). Identity still comes from the
/// TOKEN (Law 10) — the subdomain is a routing/placement key plus a defence-in-depth cross-check:
/// <list type="bullet">
/// <item>apex / <c>localhost</c> / IP / reserved label (admin/www/api) ⇒ no tenant resolution (pass through).</item>
/// <item>unknown or suspended subdomain ⇒ <b>404</b> (no existence leak).</item>
/// <item>a token for tenant A presented on tenant B's subdomain ⇒ <b>401</b> (mismatch).</item>
/// </list>
/// No-op when the Tenancy module is disabled (the directory port is then unregistered).
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> ReservedLabels =
        new(StringComparer.OrdinalIgnoreCase) { "admin", "www", "api" };

    public async Task InvokeAsync(HttpContext context)
    {
        var directory = context.RequestServices.GetService<ITenantDirectory>();
        if (directory is null)
        {
            await next(context);
            return;
        }

        var subdomain = ExtractSubdomain(context.Request.Host.Host);
        if (subdomain is null || ReservedLabels.Contains(subdomain))
        {
            await next(context);
            return;
        }

        var tenant = await directory.FindBySubdomainAsync(subdomain, context.RequestAborted);
        if (tenant is null || string.Equals(tenant.Status, "Suspended", StringComparison.OrdinalIgnoreCase))
        {
            // Route-not-found shape — never disclose whether a workspace exists or is suspended.
            throw new NotFoundException("tenant.not_found", "Workspace not found.");
        }

        // Cross-check: a token must only be used on its OWN tenant's subdomain (defence-in-depth IDOR guard).
        var tokenTenant = context.RequestServices.GetRequiredService<ITenantContext>().TenantId;
        if (tokenTenant is { } claimed && claimed != tenant.Id)
        {
            throw new UnauthorizedException("auth.tenant_mismatch", "This session does not belong to this workspace.");
        }

        context.Items["tenant"] = tenant;
        await next(context);
    }

    /// <summary>
    /// Leftmost label when the host carries a subdomain (<c>acme.lvh.me</c>, <c>acme.example.com</c>); null for an
    /// apex / single-label / loopback host (<c>localhost</c>, <c>lvh.me</c>, <c>example.com</c>, an IP).
    /// </summary>
    internal static string? ExtractSubdomain(string host)
    {
        if (string.IsNullOrEmpty(host) || System.Net.IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        // apex = exactly the root (2 labels like example.com / lvh.me) or a bare single label (localhost).
        return labels.Length >= 3 ? labels[0].ToLowerInvariant() : null;
    }
}
