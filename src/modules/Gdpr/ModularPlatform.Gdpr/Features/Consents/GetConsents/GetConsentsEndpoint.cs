using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Consents.GetConsents;

internal static class GetConsentsEndpoint
{
    public static void MapGetConsents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/gdpr/users/{id:guid}/consents", async (
                Guid id,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var consents = await dispatcher.Query(new GetConsentsQuery(id), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<ConsentResponse>>.Ok(consents));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("GetConsents");
    }
}
