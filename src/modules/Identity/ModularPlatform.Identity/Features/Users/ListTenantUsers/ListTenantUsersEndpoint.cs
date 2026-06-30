using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.ListTenantUsers;

internal static class ListTenantUsersEndpoint
{
    public static void MapListTenantUsers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/users", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                _ = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var tenantId = tenant.TenantId
                    ?? throw new UnauthorizedException("auth.required", "Tenant context required.");
                var users = await dispatcher.Query(new ListTenantUsersQuery(tenantId, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<TenantUserListItem>>.Ok(users));
            })
            .RequireAuthorization()
            .WithTags("Identity")
            .WithName("ListTenantUsers");
    }
}
