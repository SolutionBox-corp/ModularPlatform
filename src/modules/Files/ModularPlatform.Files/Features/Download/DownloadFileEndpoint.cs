using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Download;

/// <summary>
/// Streams a file's bytes back with its stored content-type. Owner-scoped by RLS (the metadata query returns 404
/// for anyone but the owner), so there is no explicit ownership check here. The bytes are streamed from
/// <see cref="IFileStorage"/> — never wrapped in the JSON ApiResponse envelope.
/// </summary>
internal static class DownloadFileEndpoint
{
    public static void MapDownloadFile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/files/{fileId:guid}", async (
                Guid fileId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                IFileStorage storage,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var descriptor = await dispatcher.Query(new GetFileQuery(fileId, userId), ct);
                var stream = await storage.GetAsync(descriptor.StorageKey, ct);
                return Results.Stream(stream, descriptor.ContentType, descriptor.FileName);
            })
            .RequireAuthorization()
            .RequireModule("files")
            .WithTags("Files")
            .WithName("DownloadFile");
    }
}
