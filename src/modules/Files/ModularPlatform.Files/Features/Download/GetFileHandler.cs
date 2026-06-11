using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Files.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Files.Features.Download;

/// <summary>
/// Reads the blob coordinates for a file the caller OWNS. Ownership is enforced BOTH at the app layer (the explicit
/// <c>UserId</c> predicate, from the token) AND by RLS — defence in depth, mirroring <c>ListFilesHandler</c>. A
/// foreign id is a 404 even in a deployment that runs with <c>Persistence:Rls:Enabled=false</c>.
/// </summary>
internal sealed class GetFileHandler(IReadDbContextFactory<FilesDbContext> readDb)
    : IQueryHandler<GetFileQuery, FileContentDescriptor>
{
    public async Task<FileContentDescriptor> Handle(GetFileQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var file = await db.Files
            .Where(f => f.Id == query.FileId && f.UserId == query.UserId)
            .Select(f => new FileContentDescriptor(f.StorageKey, f.FileName, f.ContentType))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("file.not_found", "File not found.");

        return file;
    }
}
