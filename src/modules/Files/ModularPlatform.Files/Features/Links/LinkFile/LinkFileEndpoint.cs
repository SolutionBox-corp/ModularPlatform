using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.Links;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Links.LinkFile;

internal static class LinkFileEndpoint
{
    public static void MapLinkFile(this IEndpointRouteBuilder app)
    {
        app.MapPost("/files/{fileId:guid}/links", async (
                Guid fileId,
                LinkFileRequest request,
                HttpContext http,
                LinkGenerator links,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new LinkFileCommand(fileId, userId, request.OwnerType, request.OwnerId), ct);
                var location = links.GetPathByName(http, "ListFileLinks", new
                    {
                        ownerType = result.OwnerType,
                        ownerId = result.OwnerId,
                    })
                    ?? throw new InvalidOperationException("ListFileLinks route not found.");
                return Results.Created(location, ApiResponse<FileLinkItem>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("LinkFile");
    }
}
