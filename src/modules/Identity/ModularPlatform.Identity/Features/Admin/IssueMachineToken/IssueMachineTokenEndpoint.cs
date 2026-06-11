using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.IssueMachineToken;

internal static class IssueMachineTokenEndpoint
{
    public static void MapIssueMachineToken(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/admin/machine-tokens", async (
                IssueMachineTokenRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new IssueMachineTokenCommand(request.TenantId, request.Name), ct);
                return Results.Ok(ApiResponse<IssueMachineTokenResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.MachineTokensIssue)
            .WithTags("Identity")
            .WithName("IssueMachineToken");
    }
}
