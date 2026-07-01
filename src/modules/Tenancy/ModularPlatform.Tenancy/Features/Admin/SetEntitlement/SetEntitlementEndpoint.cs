using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.SetEntitlement;

internal static class SetEntitlementEndpoint
{
    public static void MapSetEntitlement(this IEndpointRouteBuilder app)
    {
        app.MapPut("/tenant/admin/tenants/{tenantId:guid}/entitlements/{moduleKey}", async (
                Guid tenantId,
                string moduleKey,
                SetEntitlementRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(
                    new SetEntitlementCommand(tenantId, moduleKey, request.Enabled, request.Tier, request.Limits), ct);
                return Results.Ok(ApiResponse<SetEntitlementResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("SetTenantEntitlement");
    }
}
