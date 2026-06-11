using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.RegisterUser;

/// <summary>
/// CANONICAL endpoint: a Minimal API extension method. Maps the wire request to the command, sends it
/// through the dispatcher, wraps the success result in <see cref="ApiResponse{T}"/>. It does NO business
/// logic and NO error handling — validation is a behavior, errors become RFC 9457 in the middleware.
/// </summary>
internal static class RegisterUserEndpoint
{
    public static void MapRegisterUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/users", async (
                RegisterUserRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(
                    new RegisterUserCommand(request.Email, request.Password, request.DisplayName), ct);
                // 201 with NO Location: there is no GET /identity/users/{id} (a user reads their own profile via
                // /me, never by id), so we must not fabricate a Location to a route that does not exist.
                return Results.Created((string?)null, ApiResponse<RegisterUserResponse>.Ok(result));
            })
            .AllowAnonymous()
            // Anonymous signup is an unthrottled Argon2-hash + tenant-INSERT surface and an enumeration vector
            // (409 vs 201) — apply the same per-IP "auth" limit as /login and /refresh.
            .RequireRateLimiting("auth")
            .WithTags("Identity")
            .WithName("RegisterUser");
    }
}
