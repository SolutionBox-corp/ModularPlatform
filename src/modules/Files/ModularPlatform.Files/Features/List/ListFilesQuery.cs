using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Features.List;

public sealed record ListFilesQuery(Guid UserId, PageRequest Page) : IQuery<PagedResponse<FileListItem>>;

public sealed record FileListItem(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset CreatedAt);
