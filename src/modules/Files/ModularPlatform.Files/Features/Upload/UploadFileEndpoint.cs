using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Upload;

/// <summary>Caps the request body so an oversized upload is rejected by Kestrel before it reaches the handler.</summary>
internal sealed class RequestBodySizeLimit(long bytes) : IRequestSizeLimitMetadata
{
    public long? MaxRequestBodySize { get; } = bytes;
}

/// <summary>
/// Multipart upload. The OWNER is taken from the token (never the body). Content-type allowlist + size cap are
/// enforced by <c>UploadFileValidator</c>; the request body size is also capped at the endpoint so an oversized
/// stream is rejected before the handler runs. The storage key is generated server-side in the handler.
/// </summary>
internal static class UploadFileEndpoint
{
    public static void MapUploadFile(this IEndpointRouteBuilder app)
    {
        app.MapPost("/files", async (
                IFormFile file,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                await using var content = file.OpenReadStream();
                var command = new UploadFileCommand(
                    userId, content, file.FileName, file.ContentType ?? string.Empty, file.Length);
                var result = await dispatcher.Send(command, ct);
                // Build the Location from the named download route so it stays correct under the host's /v1 group
                // instead of a hardcoded path that misses the prefix and 404s.
                var location = links.GetPathByName(http, "DownloadFile", new { fileId = result.Id })
                    ?? $"/files/{result.Id}";
                return Results.Created(location, ApiResponse<UploadFileResponse>.Ok(result));
            })
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithMetadata(new RequestBodySizeLimit(FileUploadPolicy.MaxSizeBytes))
            .WithTags("Files")
            .WithName("UploadFile");
    }
}
