using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.RequestEmailVerification;

internal static class RequestEmailVerificationEndpoint
{
    public static void MapRequestEmailVerification(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/users/me/email-verification", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var response = await dispatcher.Send(new RequestEmailVerificationCommand(userId), ct);
                return Results.Accepted(value: ApiResponse<RequestEmailVerificationResponse>.Ok(response));
            })
            .RequireAuthorization()
            .DenyMachinePrincipals()
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("RequestEmailVerification");
    }
}
