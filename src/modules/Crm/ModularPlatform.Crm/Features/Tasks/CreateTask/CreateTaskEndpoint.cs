using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.CreateTask;

internal static class CreateTaskEndpoint
{
    public static void MapCreateTask(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/tasks", async (
                CreateTaskRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateTaskCommand(
                        userId, request.ContactId, request.DealId, request.Title ?? string.Empty,
                        request.Description, request.DueAt, request.AssigneeUserId,
                        string.IsNullOrWhiteSpace(request.Priority) ? TaskPriorities.Normal : request.Priority.Trim().ToLowerInvariant()),
                    ct);
                var location = links.GetPathByName(http, "GetTask", new { taskId = result.Id })
                    ?? $"/crm/tasks/{result.Id}";
                return Results.Created(location, ApiResponse<CreateTaskResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateTask");
    }
}
