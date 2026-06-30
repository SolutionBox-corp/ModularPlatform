using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Contracts;
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
            .Join(
                db.Files.Where(f => f.UserId == query.UserId),
                link => link.FileObjectId,
                file => file.Id,
                (link, file) => new
                {
                    LinkId = link.Id,
                    FileObjectId = file.Id,
                    link.OwnerType,
                    link.OwnerId,
                    file.FileName,
                    file.ContentType,
                    file.Size,
                    link.CreatedAt
                })
            .OrderByDescending(link => link.CreatedAt)
            .ThenByDescending(link => link.LinkId)
            .Select(link => new FileLinkItem(
                link.LinkId,
                link.FileObjectId,
                link.OwnerType,
                link.OwnerId,
                link.FileName,
                link.ContentType,
                link.Size,
                link.CreatedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
