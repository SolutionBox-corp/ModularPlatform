using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Tasks;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Tasks.ListTasks;

internal static class ListTasksEndpoint
{
    public static void MapListTasks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/tasks", async (
                string? status,
                DateTimeOffset? dueBefore,
                Guid? contactId,
                Guid? dealId,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListTasksQuery(userId, status, dueBefore, contactId, dealId, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<TaskResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListTasks");
    }
}
