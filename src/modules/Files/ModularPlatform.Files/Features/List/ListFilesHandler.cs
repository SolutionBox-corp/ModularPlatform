using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Files.Features.List;

/// <summary>
/// Paged list of the caller's files (metadata only — never the bytes). No-tracking read factory; newest first.
/// Owner-scoped both by the explicit <c>WHERE UserId</c> and by RLS (defence in depth).
/// </summary>
internal sealed class ListFilesHandler(IReadDbContextFactory<FilesDbContext> readDb)
    : IQueryHandler<ListFilesQuery, PagedResponse<FileListItem>>
{
    public async Task<PagedResponse<FileListItem>> Handle(ListFilesQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        return await db.Files
            .Where(f => f.UserId == query.UserId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FileListItem(f.Id, f.FileName, f.ContentType, f.Size, f.CreatedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
