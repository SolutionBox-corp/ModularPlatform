using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Erasure.RequestErasure;

internal static class RequestErasureEndpoint
{
    public static void MapRequestErasure(this IEndpointRouteBuilder app)
    {
        app.MapPost("/gdpr/users/{id:guid}/erase", async (
                Guid id,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new RequestErasureCommand(id), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("RequestErasure");
    }
}
