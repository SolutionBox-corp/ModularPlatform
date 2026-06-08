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
                return Results.Created($"/identity/users/{result.UserId}", ApiResponse<RegisterUserResponse>.Ok(result));
            })
            .AllowAnonymous()
            .WithTags("Identity")
            .WithName("RegisterUser");
    }
}
