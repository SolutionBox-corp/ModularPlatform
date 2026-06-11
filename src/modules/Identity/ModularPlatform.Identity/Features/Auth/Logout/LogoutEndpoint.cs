using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Auth.Logout;

internal static class LogoutEndpoint
{
    public static void MapLogout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/auth/logout", async (
                LogoutRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new LogoutCommand(userId, request.RefreshToken), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequireAuthorization()
            .WithTags("Identity")
            .WithName("Logout");
    }
}
