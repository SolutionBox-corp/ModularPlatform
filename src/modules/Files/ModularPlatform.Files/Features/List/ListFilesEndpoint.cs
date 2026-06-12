using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.List;

/// <summary>Paged list of the caller's own files (metadata only). Owner from the token; RLS-scoped.</summary>
internal static class ListFilesEndpoint
{
    public static void MapListFiles(this IEndpointRouteBuilder app)
    {
        app.MapGet("/files", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListFilesQuery(userId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<FileListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("ListFiles");
    }
}
