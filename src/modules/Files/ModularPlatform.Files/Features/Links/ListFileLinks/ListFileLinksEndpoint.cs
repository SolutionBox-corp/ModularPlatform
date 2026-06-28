using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Contracts;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Links.ListFileLinks;

internal static class ListFileLinksEndpoint
{
    public static void MapListFileLinks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/files/links", async (
                string ownerType,
                Guid ownerId,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListFileLinksQuery(userId, ownerType, ownerId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<FileLinkItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("ListFileLinks");
    }
}
