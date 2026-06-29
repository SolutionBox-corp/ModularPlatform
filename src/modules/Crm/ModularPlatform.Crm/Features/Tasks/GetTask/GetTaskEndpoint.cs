using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.GetTask;

internal static class GetTaskEndpoint
{
    public static void MapGetTask(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/tasks/{taskId:guid}", async (
                Guid taskId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetTaskQuery(userId, taskId), ct);
                return Results.Ok(ApiResponse<TaskResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("GetTask");
    }
}
