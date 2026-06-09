using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Features.Upload;

/// <summary>
/// Uploads one file. <see cref="UserId"/> is the OWNER, taken from the token by the endpoint — never the body.
/// <see cref="Content"/> is the raw bytes; <see cref="FileName"/> is the original client filename (display only,
/// NEVER used to address storage). Content-type + size are validated by <c>UploadFileValidator</c> before the
/// handler runs.
/// </summary>
public sealed record UploadFileCommand(
    Guid UserId,
    Stream Content,
    string FileName,
    string ContentType,
    long Size) : ICommand<UploadFileResponse>;

public sealed record UploadFileResponse(
    Guid Id,
    string FileName,
    string ContentType,
    long Size);
