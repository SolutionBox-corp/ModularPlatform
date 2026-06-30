using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.SetRegistrationMode;

internal static class SetRegistrationModeEndpoint
{
    public static void MapSetRegistrationMode(this IEndpointRouteBuilder app)
    {
        app.MapPut("/tenant/admin/tenants/{tenantId:guid}/registration-mode", async (
                Guid tenantId,
                SetRegistrationModeRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(
                    new SetRegistrationModeCommand(tenantId, request.RegistrationMode), ct);
                return Results.Ok(ApiResponse<SetRegistrationModeResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("SetTenantRegistrationMode");
    }
}
