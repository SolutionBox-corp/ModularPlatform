using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.ResetPassword;

internal static class ResetPasswordEndpoint
{
    public static void MapResetPassword(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/auth/reset-password", async (
                ResetPasswordRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("ResetPassword");
    }
}
