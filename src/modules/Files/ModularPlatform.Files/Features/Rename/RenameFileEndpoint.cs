using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.List;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Rename;

/// <summary>
/// Renames the caller's own file. Returns the updated file item. Owner is taken from the token — never the route.
/// </summary>
internal static class RenameFileEndpoint
{
    public static void MapRenameFile(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/files/{fileId:guid}", async (
                Guid fileId,
                RenameFileRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                var result = await dispatcher.Send(new RenameFileCommand(fileId, userId, request.FileName), ct);
                return Results.Ok(ApiResponse<FileListItem>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("RenameFile");
    }
}
