using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Entitlements.GetMyEntitlements;

internal static class GetMyEntitlementsEndpoint
{
    public static void MapGetMyEntitlements(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant/me/entitlements", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var entitlements = await dispatcher.Query(new GetMyEntitlementsQuery(), ct);
                return Results.Ok(ApiResponse<TenantEntitlementsView>.Ok(entitlements));
            })
            .RequireAuthorization()
            .WithTags("Tenancy")
            .WithName("GetMyEntitlements");
    }
}
