using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.ListTaskComments;

internal static class ListTaskCommentsEndpoint
{
    public static void MapListTaskComments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/tasks/{taskId:guid}/comments", async (
                Guid taskId,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListTaskCommentsQuery(userId, taskId, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<TaskCommentResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListTaskComments");
    }
}
