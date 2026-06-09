namespace ModularPlatform.Files.Features.Upload;

/// <summary>
/// The single source of truth for what may be uploaded. SECURITY: a tight content-type allowlist (deny by default)
/// + a hard size cap. Both are enforced by <c>UploadFileValidator</c> (and again as a request-body limit on the
/// endpoint, so an oversized stream is rejected before it is buffered).
/// </summary>
internal static class FileUploadPolicy
{
    public const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "application/pdf",
        "text/plain",
    };
}
