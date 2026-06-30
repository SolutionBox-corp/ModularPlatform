using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.UpdateTask;

internal static class UpdateTaskEndpoint
{
    public static void MapUpdateTask(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/crm/tasks/{taskId:guid}", async (
                Guid taskId,
                UpdateTaskRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var priority = request.Priority is null ? null : request.Priority.Trim().ToLowerInvariant();
                var result = await dispatcher.Send(
                    new UpdateTaskCommand(userId, taskId, request.Title, request.Description, request.DueAt, priority, request.AssigneeUserId), ct);
                return Results.Ok(ApiResponse<TaskResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("UpdateTask");
    }
}
