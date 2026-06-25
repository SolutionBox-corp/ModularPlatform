using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Users.GetProfile;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.UpdateProfile;

internal static class UpdateProfileEndpoint
{
    public static void MapUpdateProfile(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/identity/users/me", async (
                UpdateProfileRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var profile = await dispatcher.Send(
                    new UpdateProfileCommand(userId, request.DisplayName, request.Locale), ct);
                return Results.Ok(ApiResponse<UserProfileResponse>.Ok(profile));
            })
            .RequireAuthorization()
            .DenyMachinePrincipals()
            .WithTags("Identity")
            .WithName("UpdateMyProfile");
    }
}
