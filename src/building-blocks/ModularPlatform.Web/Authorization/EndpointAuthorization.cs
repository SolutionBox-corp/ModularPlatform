using Microsoft.AspNetCore.Builder;

namespace ModularPlatform.Web;

/// <summary>JWT claim types the platform issues and authorizes on. Both are flat string claims on the access token.</summary>
public static class AuthorizationClaims
{
    /// <summary>One claim per granted permission (value = a <c>PlatformPermissions</c> string).</summary>
    public const string Permission = "permission";

    /// <summary>One claim per assigned role name. JWT bearer is configured with this as the role claim type.</summary>
    public const string Role = "role";
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
}
