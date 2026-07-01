using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Users.GetProfile;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.AcceptTerms;

internal static class AcceptTermsEndpoint
{
    public static void MapAcceptTerms(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/users/me/terms-acceptance", async (
                AcceptTermsRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var profile = await dispatcher.Send(new AcceptTermsCommand(userId, request.TermsVersion), ct);
                return Results.Ok(ApiResponse<UserProfileResponse>.Ok(profile));
            })
            .RequireAuthorization()
            .DenyMachinePrincipals()
            .WithTags("Identity")
            .WithName("AcceptTerms");
    }
}
