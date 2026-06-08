using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Gdpr.Features.Erasure.RequestErasure;

internal static class RequestErasureEndpoint
{
    public static void MapRequestErasure(this IEndpointRouteBuilder app)
    {
        // Self-service erasure: the subject is the authenticated user, never a client-supplied id.
        app.MapPost("/gdpr/me/erase", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new RequestErasureCommand(userId), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequireAuthorization()
            .WithTags("Gdpr")
            .WithName("RequestErasure");
    }
}
