using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Consents.GetConsents;

internal static class GetConsentsEndpoint
{
    public static void MapGetConsents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/gdpr/me/consents", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var consents = await dispatcher.Query(new GetConsentsQuery(userId), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<ConsentResponse>>.Ok(consents));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("GetConsents");
    }
}
