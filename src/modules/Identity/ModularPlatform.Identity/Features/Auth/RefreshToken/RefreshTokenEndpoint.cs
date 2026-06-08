using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Auth.RefreshToken;

internal static class RefreshTokenEndpoint
{
    public static void MapRefreshToken(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/auth/refresh", async (
                RefreshTokenRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var tokens = await dispatcher.Send(new RefreshTokenCommand(request.RefreshToken), ct);
                return Results.Ok(ApiResponse<AuthTokensResponse>.Ok(tokens));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("RefreshToken");
    }
}
