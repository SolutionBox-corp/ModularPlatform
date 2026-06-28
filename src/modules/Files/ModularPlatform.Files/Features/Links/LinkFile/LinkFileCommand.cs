using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.Links;

namespace ModularPlatform.Files.Features.Links.LinkFile;

public sealed record LinkFileCommand(
    Guid FileObjectId,
    Guid UserId,
    string OwnerType,
    Guid OwnerId) : ICommand<FileLinkItem>;

public sealed record LinkFileRequest(string OwnerType, Guid OwnerId);
