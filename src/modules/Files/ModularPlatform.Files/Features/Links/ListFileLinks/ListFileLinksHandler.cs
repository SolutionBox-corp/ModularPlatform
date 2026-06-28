using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Files.Features.Links.ListFileLinks;

internal sealed class ListFileLinksHandler(IReadDbContextFactory<FilesDbContext> readDb)
    : IQueryHandler<ListFileLinksQuery, PagedResponse<FileLinkItem>>
{
    public async Task<PagedResponse<FileLinkItem>> Handle(ListFileLinksQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        return await db.FileLinks
            .Where(l => l.UserId == query.UserId
                && l.OwnerType == query.OwnerType
                && l.OwnerId == query.OwnerId)
            .OrderByDescending(l => l.CreatedAt)
            .Join(
                db.Files.Where(f => f.UserId == query.UserId),
                link => link.FileObjectId,
                file => file.Id,
                (link, file) => new FileLinkItem(
                    link.Id,
                    file.Id,
                    link.OwnerType,
                    link.OwnerId,
                    file.FileName,
                    file.ContentType,
                    file.Size,
                    link.CreatedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
