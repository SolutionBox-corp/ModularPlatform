using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.RevokeTenantInvite;

internal static class RevokeTenantInviteEndpoint
{
    public static void MapRevokeTenantInvite(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/tenant/admin/tenants/{tenantId:guid}/invites/{inviteId:guid}", async (
                Guid tenantId,
                Guid inviteId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new RevokeTenantInviteCommand(tenantId, inviteId), ct);
                return Results.Ok(ApiResponse<RevokeTenantInviteResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("RevokeTenantInvite");
    }
}
