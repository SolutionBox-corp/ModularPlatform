using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Features.Delete;

/// <summary>
/// Deletes the caller's own file: blob first, then the metadata row. <see cref="UserId"/> comes from the token —
/// never the request body. A foreign <see cref="FileId"/> results in 404 (NotFoundException "file.not_found").
/// </summary>
public sealed record DeleteFileCommand(Guid FileId, Guid UserId) : ICommand;
