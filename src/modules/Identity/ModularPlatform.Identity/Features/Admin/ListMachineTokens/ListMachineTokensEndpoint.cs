using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.ListMachineTokens;

internal static class ListMachineTokensEndpoint
{
    public static void MapListMachineTokens(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/admin/machine-tokens", async (
                Guid tenantId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Query(new ListMachineTokensQuery(tenantId), ct);
                return Results.Ok(ApiResponse<ListMachineTokensResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.MachineTokensIssue)
            .WithTags("Identity")
            .WithName("ListMachineTokens");
    }
}
