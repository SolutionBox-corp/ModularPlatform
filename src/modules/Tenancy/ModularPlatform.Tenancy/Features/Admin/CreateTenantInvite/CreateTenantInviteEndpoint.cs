using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.CreateTenantInvite;

internal static class CreateTenantInviteEndpoint
{
    public static void MapCreateTenantInvite(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tenant/admin/tenants/{tenantId:guid}/invites", async (
                Guid tenantId,
                CreateTenantInviteRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(
                    new CreateTenantInviteCommand(tenantId, request.ExpiresInDays ?? 7), ct);
                return Results.Ok(ApiResponse<CreateTenantInviteResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("CreateTenantInvite");
    }
}
