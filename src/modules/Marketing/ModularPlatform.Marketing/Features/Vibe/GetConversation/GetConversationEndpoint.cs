using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.GetConversation;

/// <summary>Reads one vibe-chat conversation + its messages (owner from the token; RLS-scoped; 404 for anyone else).</summary>
internal static class GetConversationEndpoint
{
    public static void MapGetConversation(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/vibe/conversations/{conversationId:guid}", async (
                Guid conversationId,
                int? messagePage,
                int? messagePageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new GetConversationQuery(conversationId, userId, new PageRequest(messagePage, messagePageSize)), ct);
                return Results.Ok(ApiResponse<ConversationDetail>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("GetVibeConversation");
    }
}
