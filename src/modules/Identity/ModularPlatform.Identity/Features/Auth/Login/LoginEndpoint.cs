using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Auth.Login;

internal static class LoginEndpoint
{
    public static void MapLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/auth/login", async (
                LoginRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var tokens = await dispatcher.Send(new LoginCommand(request.Email, request.Password), ct);
                return Results.Ok(ApiResponse<AuthTokensResponse>.Ok(tokens));
            })
            .AllowAnonymous()
            .WithTags("Identity")
            .WithName("Login");
    }
}
