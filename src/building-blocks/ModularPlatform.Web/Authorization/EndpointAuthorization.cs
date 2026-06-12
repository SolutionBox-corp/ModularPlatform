using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Web;

/// <summary>JWT claim types the platform issues and authorizes on. Both are flat string claims on the access token.</summary>
public static class AuthorizationClaims
{
    /// <summary>One claim per granted permission (value = a <c>PlatformPermissions</c> string).</summary>
    public const string Permission = "permission";

    /// <summary>One claim per assigned role name. JWT bearer is configured with this as the role claim type.</summary>
    public const string Role = "role";

    /// <summary>The role marking a non-human machine/service principal (issued via the machine-token endpoint).</summary>
    public const string MachineRole = "machine";
}

/// <summary>
/// Endpoint authorization helpers. Gate an endpoint by a fine-grained permission with
/// <c>.RequirePermission(PlatformPermissions.X)</c> (the canonical way — features declare a permission and gate on
/// it), or by a coarse role with <c>.RequireRole("admin")</c>. Both require an authenticated principal first.
/// Authorization is claim-based — no DB hit per request; the token carries the user's roles + permissions.
/// </summary>
public static class EndpointAuthorizationExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(AuthorizationClaims.Permission, permission));

    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, params string[] roles)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(roles));

    /// <summary>
    /// Excludes machine/service principals (role <see cref="AuthorizationClaims.MachineRole"/>) from a human-only
    /// endpoint — e.g. billing checkout. A machine token is authenticated but must opt IN to the endpoints it may use;
    /// commerce/billing decisions are a human action. Combine with the relevant permission/role for the human side.
    /// </summary>
    public static TBuilder DenyMachinePrincipals<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => !ctx.User.IsInRole(AuthorizationClaims.MachineRole)));

    /// <summary>
    /// Gates an endpoint on the current tenant having <paramref name="moduleKey"/> entitled. Unlike permissions
    /// (claim-based), entitlements are looked up LIVE per request via <see cref="IEntitlementResolver"/> — a
    /// platform-admin toggle takes effect on the next request, never via a stale JWT claim. Not entitled (or no
    /// tenant) ⇒ <see cref="NotEntitledException"/> → <b>404</b> (route-not-found shape — a disabled module must not
    /// leak its existence). Combine with <c>.RequirePermission</c>/<c>.RequireRole</c> for who-can-do-what within the module.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string moduleKey)
        where TBuilder : IEndpointConventionBuilder
    {
        // Discoverable marker so an enforcement test can assert every gated-module endpoint carries the guard.
        builder.Add(b => b.Metadata.Add(new ModuleEntitlementMetadata(moduleKey)));
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var tenant = services.GetRequiredService<ITenantContext>();
            var resolver = services.GetRequiredService<IEntitlementResolver>();
            var ct = context.HttpContext.RequestAborted;

            if (tenant.TenantId is not { } tenantId
                || !await resolver.IsModuleEnabledAsync(tenantId, moduleKey, ct))
            {
                throw new NotEntitledException(
                    "tenant.module_not_entitled", "This feature is not available for your workspace.");
            }

            return await next(context);
        });
    }
}

/// <summary>Marker added by <c>RequireModule</c> so a test can verify the live entitlement guard is wired on every
/// gated-module endpoint (the guard itself is an endpoint filter, which is otherwise invisible in endpoint metadata).</summary>
public sealed record ModuleEntitlementMetadata(string ModuleKey);
