using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.AddTaskComment;

internal static class AddTaskCommentEndpoint
{
    public static void MapAddTaskComment(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/tasks/{taskId:guid}/comments", async (
                Guid taskId,
                AddTaskCommentRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new AddTaskCommentCommand(userId, taskId, request.Body ?? string.Empty), ct);
                return Results.Created((string?)null, ApiResponse<AddTaskCommentResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("AddTaskComment");
    }
}
