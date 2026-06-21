using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.StartConversation;

/// <summary>Creates a vibe-chat conversation (owner from the token). Returns the new id + a Location to the thread.</summary>
internal static class StartConversationEndpoint
{
    public static void MapStartConversation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/marketing/vibe/conversations", async (
                StartConversationRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                var result = await dispatcher.Send(new StartConversationCommand(userId, request.Title), ct);

                var location = links.GetPathByName(http, "GetVibeConversation", new { conversationId = result.ConversationId })
                    ?? $"/marketing/vibe/conversations/{result.ConversationId}";
                return Results.Created(location, ApiResponse<StartConversationResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("StartVibeConversation");
    }
}
