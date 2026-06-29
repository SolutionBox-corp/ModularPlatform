using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.DeleteTask;

internal static class DeleteTaskEndpoint
{
    public static void MapDeleteTask(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/crm/tasks/{taskId:guid}", async (
                Guid taskId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new DeleteTaskCommand(userId, taskId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("DeleteTask");
    }
}
