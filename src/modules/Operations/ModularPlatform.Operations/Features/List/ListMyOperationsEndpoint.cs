using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Operations.Features.List;

/// <summary>Paged list of the caller's own operations, newest first. Owner from the token; RLS-scoped.</summary>
internal static class ListMyOperationsEndpoint
{
    public static void MapListMyOperations(this IEndpointRouteBuilder app)
    {
        app.MapGet("/operations", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListMyOperationsQuery(userId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<OperationListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("operations")
            .WithTags("Operations")
            .WithName("ListMyOperations");
    }
}
