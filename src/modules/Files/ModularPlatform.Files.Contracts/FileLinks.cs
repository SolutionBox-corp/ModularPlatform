using ModularPlatform.Cqrs;

namespace ModularPlatform.Files.Contracts;

public sealed record FileLinkItem(
    Guid Id,
    Guid FileObjectId,
    string OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset CreatedAt);

public sealed record LinkFileToOwnerCommand(
    Guid FileObjectId,
    Guid UserId,
    string OwnerType,
    Guid OwnerId) : ICommand<FileLinkItem>;

public sealed record ListFileLinksQuery(
    Guid UserId,
    string OwnerType,
    Guid OwnerId,
    PageRequest Page) : IQuery<PagedResponse<FileLinkItem>>;

public sealed record UnlinkFileCommand(Guid LinkId, Guid UserId) : ICommand;
