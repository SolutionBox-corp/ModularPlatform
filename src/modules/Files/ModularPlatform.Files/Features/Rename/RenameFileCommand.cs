using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.List;

namespace ModularPlatform.Files.Features.Rename;

/// <summary>
/// Renames the display name of the caller's own file. Only <see cref="FileName"/> is mutated; the blob and its
/// <c>StorageKey</c> are untouched. <see cref="UserId"/> is taken from the token — never the body.
/// </summary>
public sealed record RenameFileCommand(Guid FileId, Guid UserId, string FileName) : ICommand<FileListItem>;

/// <summary>Wire body for the PATCH /files/{id} endpoint.</summary>
public sealed record RenameFileRequest(string FileName);
