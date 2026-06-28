using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Auth.ForgotPassword;

internal static class ForgotPasswordEndpoint
{
    public static void MapForgotPassword(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/auth/forgot-password", async (
                ForgotPasswordRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var response = await dispatcher.Send(new ForgotPasswordCommand(request.Email), ct);
                return Results.Accepted(value: ApiResponse<ForgotPasswordResponse>.Ok(response));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("ForgotPassword");
    }
}
