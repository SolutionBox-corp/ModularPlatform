using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Download;

/// <summary>
/// Reads the blob coordinates for a file. Ownership is enforced by RLS — the read connection only ever returns the
/// caller's own files, so another user's id simply isn't found (404), with no explicit owner check to forget.
/// </summary>
internal sealed class GetFileHandler(IReadDbContextFactory<FilesDbContext> readDb)
    : IQueryHandler<GetFileQuery, FileContentDescriptor>
{
    public async Task<FileContentDescriptor> Handle(GetFileQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var file = await db.Files
            .Where(f => f.Id == query.FileId)
            .Select(f => new FileContentDescriptor(f.StorageKey, f.FileName, f.ContentType))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("file.not_found", "File not found.");

        return file;
    }
}
