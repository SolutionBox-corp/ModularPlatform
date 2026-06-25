using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.ChangePassword;

internal static class ChangePasswordEndpoint
{
    public static void MapChangePassword(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/users/me/change-password", async (
                ChangePasswordRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(
                    new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .DenyMachinePrincipals()
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("ChangePassword");
    }
}
