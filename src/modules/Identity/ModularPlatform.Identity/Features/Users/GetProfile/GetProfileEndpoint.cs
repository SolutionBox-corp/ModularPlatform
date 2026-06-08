using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.GetProfile;

internal static class GetProfileEndpoint
{
    public static void MapGetProfile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/users/me", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var profile = await dispatcher.Query(new GetProfileQuery(userId), ct);
                return Results.Ok(ApiResponse<UserProfileResponse>.Ok(profile));
            })
            .RequireAuthorization()
            .WithTags("Identity")
            .WithName("GetMyProfile");
    }
}
