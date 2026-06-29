using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Delete;

/// <summary>
/// Deletes the caller's own file (blob + metadata). Returns 204 No Content on success. A foreign file id is a 404.
/// Owner is taken from the token — never the route.
/// </summary>
internal static class DeleteFileEndpoint
{
    public static void MapDeleteFile(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/files/{fileId:guid}", async (
                Guid fileId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                await dispatcher.Send(new DeleteFileCommand(fileId, userId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("DeleteFile");
    }
}
