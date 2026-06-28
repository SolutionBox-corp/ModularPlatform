using ModularPlatform.Cqrs;
using ModularPlatform.Files.Features.Links;

namespace ModularPlatform.Files.Features.Links.ListFileLinks;

public sealed record ListFileLinksQuery(
    Guid UserId,
    string OwnerType,
    Guid OwnerId,
    PageRequest Page) : IQuery<PagedResponse<FileLinkItem>>;
