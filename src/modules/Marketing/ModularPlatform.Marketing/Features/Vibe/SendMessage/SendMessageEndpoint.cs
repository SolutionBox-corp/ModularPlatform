using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.SendMessage;

/// <summary>
/// 202 endpoint: accepts a user message, persists it + kicks off the durable agent turn, returns <c>202 Accepted</c>
/// + a <c>Location</c> to the conversation (where the assistant reply will appear). Owner from the token.
/// </summary>
internal static class SendMessageEndpoint
{
    public static void MapSendMessage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/marketing/vibe/conversations/{conversationId:guid}/messages", async (
                Guid conversationId,
                SendMessageRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                var result = await dispatcher.Send(
                    new SendMessageCommand(conversationId, userId, request.Content), ct);

                var location = links.GetPathByName(http, "GetVibeConversation", new { conversationId })
                    ?? $"/marketing/vibe/conversations/{conversationId}";
                return Results.Accepted(location, ApiResponse<SendMessageResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("SendVibeMessage");
    }
}
