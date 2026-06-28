namespace ModularPlatform.Files.Features.Links;

public sealed record FileLinkItem(
    Guid Id,
    Guid FileObjectId,
    string OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset CreatedAt);
