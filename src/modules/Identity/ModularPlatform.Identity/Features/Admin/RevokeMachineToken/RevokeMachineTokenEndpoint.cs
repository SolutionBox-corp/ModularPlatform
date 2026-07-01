using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.RevokeMachineToken;

internal static class RevokeMachineTokenEndpoint
{
    public static void MapRevokeMachineToken(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/admin/machine-tokens/{tokenId:guid}/revoke", async (
                Guid tokenId,
                Guid tenantId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new RevokeMachineTokenCommand(tenantId, tokenId), ct);
                return Results.Ok(ApiResponse<RevokeMachineTokenResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.MachineTokensIssue)
            .WithTags("Identity")
            .WithName("RevokeMachineToken");
    }
}
