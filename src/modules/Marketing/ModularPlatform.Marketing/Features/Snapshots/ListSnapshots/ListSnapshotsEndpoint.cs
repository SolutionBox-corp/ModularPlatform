using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Snapshots.ListSnapshots;

/// <summary>Paged list of the caller's metric snapshots (owner from the token; RLS-scoped). Optional <c>?source=</c>.</summary>
internal static class ListSnapshotsEndpoint
{
    public static void MapListSnapshots(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/snapshots", async (
                string? source,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListSnapshotsQuery(userId, source, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<SnapshotListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("ListSnapshots");
    }
}
