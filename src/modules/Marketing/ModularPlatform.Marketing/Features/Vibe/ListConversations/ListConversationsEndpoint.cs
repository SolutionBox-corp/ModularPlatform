using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Vibe.ListConversations;

/// <summary>Lists the caller's vibe-chat conversations (owner from the token; RLS-scoped; non-deleted only).</summary>
internal static class ListConversationsEndpoint
{
    public static void MapListConversations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/vibe/conversations", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListConversationsQuery(userId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<ConversationListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("ListVibeConversations");
    }
}
