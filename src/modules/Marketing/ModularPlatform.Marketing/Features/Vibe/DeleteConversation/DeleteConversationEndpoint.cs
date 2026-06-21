using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.DeleteConversation;

/// <summary>Soft-deletes one of the caller's vibe-chat conversations (owner from the token; 404 for anyone else).</summary>
internal static class DeleteConversationEndpoint
{
    public static void MapDeleteConversation(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/marketing/vibe/conversations/{conversationId:guid}", async (
                Guid conversationId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new DeleteConversationCommand(conversationId, userId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("DeleteVibeConversation");
    }
}
