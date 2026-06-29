using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.VerifyEmail;

internal static class VerifyEmailEndpoint
{
    public static void MapVerifyEmail(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/auth/verify-email", async (
                VerifyEmailRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new VerifyEmailCommand(request.Token), ct);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("VerifyEmail");
    }
}
